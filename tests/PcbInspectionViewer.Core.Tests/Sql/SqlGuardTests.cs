using Microsoft.EntityFrameworkCore;
using PcbInspectionViewer.Core.Data;
using PcbInspectionViewer.Core.Sql;
using Xunit;

namespace PcbInspectionViewer.Core.Tests.Sql;

public sealed class SqlGuardTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;
    private readonly DbContextOptions<InspectionDbContext> _dbOptions;

    public SqlGuardTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pcb-sqlguard-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "sqlguard_test.db");

        _dbOptions = new DbContextOptionsBuilder<InspectionDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;

        using var db = new InspectionDbContext(_dbOptions);
        db.EnsureDatabaseAndViewsCreatedAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch {}
    }

    private SqlGuard CreateSut()
    {
        return new SqlGuard(_dbPath, () => new InspectionDbContext(_dbOptions));
    }

    [Theory]
    [InlineData("SELECT * FROM v_inspections")]
    [InlineData("SELECT Id, FileName, BoardDisposition FROM v_inspections WHERE BoardDisposition = 'PASS'")]
    [InlineData("SELECT COUNT(*) FROM v_defects")]
    [InlineData("SELECT DefectClass, COUNT(*) AS c FROM v_defects GROUP BY DefectClass ORDER BY c DESC")]
    [InlineData("SELECT BatchId, TotalBoards, PassCount FROM v_batches WHERE LineHalted = 1")]
    [InlineData("SELECT i.Id, d.DefectClass FROM v_inspections i JOIN v_defects d ON i.Id = d.InspectionId")]
    [InlineData("WITH stats AS (SELECT BatchId, COUNT(*) AS cnt FROM v_inspections GROUP BY BatchId) SELECT * FROM stats")]
    [InlineData("SELECT * FROM v_inspections WHERE InspectedAtUtc > '2026-01-01'")]
    [InlineData("SELECT AVG(Stage1LatencyMs) FROM v_inspections")]
    [InlineData("SELECT DISTINCT BoardDisposition FROM v_inspections;")]
    public void ValidateQuery_PermittedQueries_PassValidation(string sql)
    {
        var sut = CreateSut();
        SqlValidationResult result = sut.ValidateQuery(sql);

        Assert.True(result.IsAllowed, $"Expected allowed for query: {sql}, but got error: {result.RejectionReason}");
        Assert.NotNull(result.SanitizedSql);
        Assert.StartsWith("SELECT * FROM (", result.SanitizedSql);
        Assert.EndsWith(") LIMIT 500", result.SanitizedSql);
    }

    [Theory]
    // DDL & Destruction
    [InlineData("DROP TABLE Inspections")]
    [InlineData("DROP VIEW v_inspections")]
    [InlineData("ALTER TABLE Inspections ADD COLUMN hacked TEXT")]
    [InlineData("CREATE TABLE evil (id INT)")]
    // DML Writes
    [InlineData("DELETE FROM Inspections")]
    [InlineData("UPDATE Inspections SET BoardDisposition = 'PASS'")]
    [InlineData("INSERT INTO Inspections (FileName) VALUES ('hacked.jpg')")]
    [InlineData("REPLACE INTO Batches (BatchId) VALUES ('B')")]
    // Multiple Statements / Injection attempts
    [InlineData("SELECT * FROM v_inspections; DROP TABLE Inspections;")]
    [InlineData("SELECT * FROM v_inspections; DELETE FROM Batches")]
    // Comments
    [InlineData("SELECT * FROM v_inspections -- comment")]
    [InlineData("SELECT * FROM v_inspections /* block comment */")]
    // System / Attach / Pragma / Extensions
    [InlineData("PRAGMA table_info(Inspections)")]
    [InlineData("ATTACH DATABASE 'malicious.db' AS evil")]
    [InlineData("SELECT load_extension('evil.so')")]
    // Access to underlying physical tables (not views)
    [InlineData("SELECT * FROM Inspections")]
    [InlineData("SELECT * FROM Defects")]
    [InlineData("SELECT * FROM Batches")]
    [InlineData("SELECT * FROM QueryLogs")]
    [InlineData("SELECT * FROM sqlite_master")]
    [InlineData("SELECT * FROM v_inspections i JOIN Inspections raw ON i.Id = raw.Id")]
    // Empty & Whitespace
    [InlineData("")]
    [InlineData("   ")]
    // Non-SELECT statements
    [InlineData("VACUUM")]
    [InlineData("REINDEX")]
    [InlineData("EXPLAIN SELECT 1")]
    public void ValidateQuery_DisallowedQueries_RejectedBySqlGuard(string sql)
    {
        var sut = CreateSut();
        SqlValidationResult result = sut.ValidateQuery(sql);

        Assert.False(result.IsAllowed, $"Query should have been rejected: {sql}");
        Assert.NotNull(result.RejectionReason);
    }

    [Fact]
    public void ValidateQuery_ParameterLimit_EnforcedStrictly()
    {
        var sut = CreateSut();
        // 11 parameters (max allowed is 10)
        var dict = Enumerable.Range(1, 11).ToDictionary(i => $"p{i}", i => (object?)$"val{i}");
        SqlValidationResult result = sut.ValidateQuery("SELECT * FROM v_inspections", dict);

        Assert.False(result.IsAllowed);
        Assert.Contains("maximum allowed parameters", result.RejectionReason);
    }

    [Fact]
    public void ValidateQuery_ParameterStringLengthLimit_EnforcedStrictly()
    {
        var sut = CreateSut();
        var dict = new Dictionary<string, object?>
        {
            ["longParam"] = new string('A', 101) // 101 chars > 100 limit
        };
        SqlValidationResult result = sut.ValidateQuery("SELECT * FROM v_inspections", dict);

        Assert.False(result.IsAllowed);
        Assert.Contains("maximum string length", result.RejectionReason);
    }

    [Fact]
    public async Task ExecuteGuardedQueryAsync_WriteAttemptsFailAndAuditedToQueryLogs()
    {
        var sut = CreateSut();

        // Attempting to execute an injection
        QueryExecutionResult res = await sut.ExecuteGuardedQueryAsync("DELETE FROM Inspections WHERE 1=1");

        Assert.False(res.IsSuccess);
        Assert.NotNull(res.ErrorMessage);

        // Verify QueryLogs audit entry
        using var db = new InspectionDbContext(_dbOptions);
        var logs = await db.QueryLogs.ToListAsync();
        Assert.Contains(logs, l => l.UserPrompt.Contains("DELETE FROM Inspections") && !l.IsSuccess);
    }

    [Fact]
    public async Task ExecuteGuardedQueryAsync_ValidQuery_ExecutesSuccessfullyAndAudits()
    {
        var sut = CreateSut();

        QueryExecutionResult res = await sut.ExecuteGuardedQueryAsync("SELECT * FROM v_inspections");

        Assert.True(res.IsSuccess);
        Assert.NotNull(res.ColumnNames);
        Assert.Contains("BoardDisposition", res.ColumnNames);

        using var db = new InspectionDbContext(_dbOptions);
        var logs = await db.QueryLogs.ToListAsync();
        Assert.Contains(logs, l => l.GeneratedSql.Contains("v_inspections") && l.IsSuccess);
    }
}
