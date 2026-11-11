using System.Data;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using PcbInspectionViewer.Core.Data;
using PcbInspectionViewer.Core.Data.Entities;

namespace PcbInspectionViewer.Core.Sql;

public sealed class SqlGuard : ISqlGuard
{
    private readonly string _dbPath;
    private readonly Func<InspectionDbContext> _auditDbContextFactory;

    private static readonly HashSet<string> AllowedViews = new(StringComparer.OrdinalIgnoreCase)
    {
        "v_inspections",
        "v_defects",
        "v_batches"
    };

    // Disallowed keywords & functions
    private static readonly Regex DisallowedKeywordsRegex = new(
        @"\b(attach|detach|pragma|insert|update|delete|drop|alter|create|replace|load_extension|reindex|vacuum|transaction|commit|rollback|savepoint|release)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Comment indicators -- or /* */
    private static readonly Regex CommentRegex = new(
        @"(--|/\*|\*/)",
        RegexOptions.Compiled);

    // Matches FROM or JOIN followed by an identifier
    private static readonly Regex TableOrViewReferenceRegex = new(
        @"\b(?:from|join)\s+([a-zA-Z0-9_]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public SqlGuard(string dbPath, Func<InspectionDbContext> auditDbContextFactory)
    {
        if (string.IsNullOrWhiteSpace(dbPath))
        {
            throw new ArgumentException("Database path must be provided", nameof(dbPath));
        }

        _dbPath = dbPath;
        _auditDbContextFactory = auditDbContextFactory ?? throw new ArgumentNullException(nameof(auditDbContextFactory));
    }

    public SqlValidationResult ValidateQuery(string sql, IReadOnlyDictionary<string, object?>? parameters = null)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return new SqlValidationResult(false, null, "Query cannot be empty.");
        }

        string trimmed = sql.Trim();

        // 1. Reject SQL comments (-- or /* */)
        if (CommentRegex.IsMatch(trimmed))
        {
            return new SqlValidationResult(false, null, "SQL comments are strictly forbidden.");
        }

        // 2. Must start with SELECT or WITH ... SELECT
        bool startsWithSelect = trimmed.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase);
        bool startsWithWith = trimmed.StartsWith("WITH", StringComparison.OrdinalIgnoreCase);

        if (!startsWithSelect && !startsWithWith)
        {
            return new SqlValidationResult(false, null, "Query must start with SELECT or WITH ... SELECT.");
        }

        if (startsWithWith && !Regex.IsMatch(trimmed, @"\bSELECT\b", RegexOptions.IgnoreCase))
        {
            return new SqlValidationResult(false, null, "WITH CTE must conclude with a SELECT statement.");
        }

        // 3. Single statement only: reject semicolons followed by non-whitespace characters
        int firstSemicolon = trimmed.IndexOf(';');
        if (firstSemicolon >= 0)
        {
            string after = trimmed[(firstSemicolon + 1)..].Trim();
            if (!string.IsNullOrEmpty(after))
            {
                return new SqlValidationResult(false, null, "Multiple SQL statements are strictly forbidden.");
            }
            // Strip trailing semicolon
            trimmed = trimmed[..firstSemicolon].Trim();
        }

        // 4. Reject disallowed keywords (DDL/DML/Extensions/PRAGMA)
        Match keywordMatch = DisallowedKeywordsRegex.Match(trimmed);
        if (keywordMatch.Success)
        {
            return new SqlValidationResult(false, null, $"Disallowed keyword detected: '{keywordMatch.Value.ToUpperInvariant()}'.");
        }

        // 5. Allowlisted views only: verify table/view references
        // If WITH clause is used, collect declared CTE names as allowed references
        var allowedInQuery = new HashSet<string>(AllowedViews, StringComparer.OrdinalIgnoreCase);
        if (startsWithWith)
        {
            var cteMatches = Regex.Matches(trimmed, @"\b(?:with|,)\s+([a-zA-Z0-9_]+)\s+as\b", RegexOptions.IgnoreCase);
            foreach (Match cm in cteMatches)
            {
                allowedInQuery.Add(cm.Groups[1].Value);
            }
        }

        MatchCollection viewMatches = TableOrViewReferenceRegex.Matches(trimmed);
        if (viewMatches.Count > 0)
        {
            foreach (Match match in viewMatches)
            {
                string target = match.Groups[1].Value;
                if (!allowedInQuery.Contains(target))
                {
                    return new SqlValidationResult(false, null, $"Access to table or view '{target}' is not allowed. Only allowlisted views ({string.Join(", ", AllowedViews)}) may be queried.");
                }
            }
        }

        // 6. Parameter limits: max 10, string length <= 100
        if (parameters is not null)
        {
            if (parameters.Count > 10)
            {
                return new SqlValidationResult(false, null, "Exceeded maximum allowed parameters (max 10).");
            }

            foreach (var kvp in parameters)
            {
                if (kvp.Value is string s && s.Length > 100)
                {
                    return new SqlValidationResult(false, null, $"Parameter '{kvp.Key}' exceeds maximum string length (100 characters).");
                }
            }
        }

        // Wrap query inside LIMIT 500
        string sanitizedSql = $"SELECT * FROM ({trimmed}) LIMIT 500";
        return new SqlValidationResult(true, sanitizedSql, null);
    }

    public async Task<QueryExecutionResult> ExecuteGuardedQueryAsync(
        string rawSql,
        IReadOnlyDictionary<string, object?>? parameters = null,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        SqlValidationResult validation = ValidateQuery(rawSql, parameters);

        if (!validation.IsAllowed || string.IsNullOrWhiteSpace(validation.SanitizedSql))
        {
            sw.Stop();
            await AuditLogAsync(rawSql, validation.SanitizedSql ?? string.Empty, isSuccess: false, validation.RejectionReason, sw.Elapsed.TotalMilliseconds).ConfigureAwait(false);
            return new QueryExecutionResult(
                IsSuccess: false,
                ColumnNames: Array.Empty<string>(),
                Rows: Array.Empty<IReadOnlyList<object?>>(),
                RowCount: 0,
                ExecutionTimeMs: sw.Elapsed.TotalMilliseconds,
                ErrorMessage: validation.RejectionReason);
        }

        // Open read-only SQLite connection with query_only = ON
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _dbPath,
            Mode = SqliteOpenMode.ReadOnly,
            DefaultTimeout = 2 // 2-second timeout
        };

        try
        {
            await using var conn = new SqliteConnection(builder.ConnectionString);
            await conn.OpenAsync(ct).ConfigureAwait(false);

            // Enforce SQLite engine-level read-only pragma
            await using (var pragmaCmd = conn.CreateCommand())
            {
                pragmaCmd.CommandText = "PRAGMA query_only = ON;";
                await pragmaCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = validation.SanitizedSql;
            cmd.CommandTimeout = 2; // 2 seconds execution timeout

            if (parameters is not null)
            {
                foreach (var kvp in parameters)
                {
                    cmd.Parameters.AddWithValue(kvp.Key.StartsWith('@') ? kvp.Key : $"@{kvp.Key}", kvp.Value ?? DBNull.Value);
                }
            }

            // Execute with linked CTS for 2s timeout
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            await using var reader = await cmd.ExecuteReaderAsync(linkedCts.Token).ConfigureAwait(false);

            var columns = new List<string>();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                columns.Add(reader.GetName(i));
            }

            var rows = new List<IReadOnlyList<object?>>();
            while (await reader.ReadAsync(linkedCts.Token).ConfigureAwait(false))
            {
                var row = new object?[reader.FieldCount];
                reader.GetValues(row);
                rows.Add(row);
            }

            sw.Stop();
            await AuditLogAsync(rawSql, validation.SanitizedSql, isSuccess: true, null, sw.Elapsed.TotalMilliseconds).ConfigureAwait(false);

            return new QueryExecutionResult(
                IsSuccess: true,
                ColumnNames: columns,
                Rows: rows,
                RowCount: rows.Count,
                ExecutionTimeMs: sw.Elapsed.TotalMilliseconds,
                ErrorMessage: null);
        }
        catch (Exception ex)
        {
            sw.Stop();
            string error = ex is OperationCanceledException ? "Query execution timed out after 2 seconds." : ex.Message;
            await AuditLogAsync(rawSql, validation.SanitizedSql, isSuccess: false, error, sw.Elapsed.TotalMilliseconds).ConfigureAwait(false);

            return new QueryExecutionResult(
                IsSuccess: false,
                ColumnNames: Array.Empty<string>(),
                Rows: Array.Empty<IReadOnlyList<object?>>(),
                RowCount: 0,
                ExecutionTimeMs: sw.Elapsed.TotalMilliseconds,
                ErrorMessage: error);
        }
    }

    private async Task AuditLogAsync(string promptOrRawSql, string executedSql, bool isSuccess, string? errorMessage, double latencyMs)
    {
        try
        {
            await using InspectionDbContext db = _auditDbContextFactory();
            db.QueryLogs.Add(new QueryLogEntity
            {
                UserPrompt = promptOrRawSql,
                GeneratedSql = executedSql,
                IsSuccess = isSuccess,
                ErrorMessage = errorMessage,
                ExecutionLatencyMs = Math.Round(latencyMs, 2),
                QueriedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync().ConfigureAwait(false);
        }
        catch
        {
            // Logging failure should never take down query pipeline
        }
    }
}
