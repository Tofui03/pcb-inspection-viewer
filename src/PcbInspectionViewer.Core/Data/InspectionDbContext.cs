using Microsoft.EntityFrameworkCore;
using PcbInspectionViewer.Core.Data.Entities;

namespace PcbInspectionViewer.Core.Data;

public sealed class InspectionDbContext : DbContext
{
    private readonly string? _dbPath;

    public InspectionDbContext(DbContextOptions<InspectionDbContext> options)
        : base(options)
    {
    }

    public InspectionDbContext(string dbPath)
    {
        _dbPath = dbPath;
    }

    public DbSet<BatchEntity> Batches => Set<BatchEntity>();
    public DbSet<InspectionEntity> Inspections => Set<InspectionEntity>();
    public DbSet<DefectEntity> Defects => Set<DefectEntity>();
    public DbSet<DecisionEntity> Decisions => Set<DecisionEntity>();
    public DbSet<LlmSummaryEntity> LlmSummaries => Set<LlmSummaryEntity>();
    public DbSet<QueryLogEntity> QueryLogs => Set<QueryLogEntity>();
    public DbSet<KbChunkEntity> KbChunks => Set<KbChunkEntity>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured && !string.IsNullOrWhiteSpace(_dbPath))
        {
            optionsBuilder.UseSqlite($"Data Source={_dbPath};Cache=Shared;Default Timeout=5");
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<InspectionEntity>()
            .HasIndex(i => i.InspectedAtUtc);

        modelBuilder.Entity<InspectionEntity>()
            .HasIndex(i => i.BatchId);

        modelBuilder.Entity<InspectionEntity>()
            .HasIndex(i => i.BoardDisposition);

        modelBuilder.Entity<DefectEntity>()
            .HasIndex(d => d.DefectClass);

        modelBuilder.Entity<DefectEntity>()
            .HasIndex(d => d.InspectionId);

        modelBuilder.Entity<DecisionEntity>()
            .HasIndex(d => d.InspectionId);

        modelBuilder.Entity<InspectionEntity>()
            .HasOne(i => i.Batch)
            .WithMany(b => b.Inspections)
            .HasForeignKey(i => i.BatchId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<DefectEntity>()
            .HasOne(d => d.Inspection)
            .WithMany(i => i.Defects)
            .HasForeignKey(d => d.InspectionId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<DecisionEntity>()
            .HasOne(d => d.Inspection)
            .WithMany(i => i.Decisions)
            .HasForeignKey(d => d.InspectionId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<LlmSummaryEntity>()
            .HasOne(l => l.Inspection)
            .WithOne(i => i.LlmSummary)
            .HasForeignKey<LlmSummaryEntity>(l => l.InspectionId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    public async Task EnsureDatabaseAndViewsCreatedAsync(CancellationToken ct = default)
    {
        await Database.EnsureCreatedAsync(ct).ConfigureAwait(false);

        // Create read-only views excluding raw LLM payloads and local file paths per Acceptance Criteria
        const string createViewsSql = """
            CREATE VIEW IF NOT EXISTS v_inspections AS
            SELECT
                Id,
                BatchId,
                FileName,
                BoardDisposition,
                IpcClass,
                LineHalted,
                DefectCount,
                DefectClassesSummary,
                Stage1LatencyMs,
                RoundTripMs,
                InspectedAtUtc
            FROM Inspections;

            CREATE VIEW IF NOT EXISTS v_defects AS
            SELECT
                d.Id,
                d.InspectionId,
                d.DetectionIndex,
                d.DefectClass,
                d.Confidence,
                d.X1,
                d.Y1,
                d.X2,
                d.Y2,
                d.AreaPx,
                d.Severity,
                d.Disposition,
                d.IpcClause,
                i.BoardDisposition,
                i.BatchId,
                i.InspectedAtUtc
            FROM Defects d
            JOIN Inspections i ON d.InspectionId = i.Id;

            CREATE VIEW IF NOT EXISTS v_batches AS
            SELECT
                BatchId,
                CreatedAtUtc,
                TotalBoards,
                PassCount,
                ReworkCount,
                ScrapCount,
                LineHalted
            FROM Batches;
            """;

        await Database.ExecuteSqlRawAsync(createViewsSql, ct).ConfigureAwait(false);
    }
}
