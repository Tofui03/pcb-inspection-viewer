using Microsoft.EntityFrameworkCore;
using PcbInspectionViewer.Core.Data;
using PcbInspectionViewer.Core.Data.Entities;
using PcbInspectionViewer.Core.Services;
using Xunit;

namespace PcbInspectionViewer.Core.Tests.Services;

public sealed class DashboardServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;
    private readonly DbContextOptions<InspectionDbContext> _dbOptions;

    public DashboardServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pcb-dash-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "dash_test.db");

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
        catch
        {
            // best effort cleanup
        }
    }

    [Fact]
    public async Task GetDailyMetricsAsync_CalculatesAccurateYieldAndTopDefects()
    {
        using (var db = new InspectionDbContext(_dbOptions))
        {
            DateTime localTargetDate = new DateTime(2026, 11, 10, 0, 0, 0, DateTimeKind.Local);
            DateTime utcMidday = localTargetDate.AddHours(12).ToUniversalTime();

            var batch1 = new BatchEntity
            {
                BatchId = "BATCH-A",
                CreatedAtUtc = utcMidday,
                LineHalted = true
            };
            var batch2 = new BatchEntity
            {
                BatchId = "BATCH-B",
                CreatedAtUtc = utcMidday,
                LineHalted = false
            };
            var batchOld = new BatchEntity
            {
                BatchId = "BATCH-OLD",
                CreatedAtUtc = utcMidday.AddDays(-2),
                LineHalted = false
            };
            db.Batches.AddRange(batch1, batch2, batchOld);
            await db.SaveChangesAsync();

            var insp1 = new InspectionEntity
            {
                Id = 1,
                BatchId = "BATCH-A",
                FileName = "board1.jpg",
                BoardDisposition = "PASS",
                InspectedAtUtc = utcMidday
            };
            var insp2 = new InspectionEntity
            {
                Id = 2,
                BatchId = "BATCH-A",
                FileName = "board2.jpg",
                BoardDisposition = "REWORK",
                InspectedAtUtc = utcMidday
            };
            var insp3 = new InspectionEntity
            {
                Id = 3,
                BatchId = "BATCH-B",
                FileName = "board3.jpg",
                BoardDisposition = "PASS",
                InspectedAtUtc = utcMidday
            };
            var insp4 = new InspectionEntity
            {
                Id = 4,
                BatchId = "BATCH-B",
                FileName = "board4.jpg",
                BoardDisposition = "SCRAP",
                InspectedAtUtc = utcMidday
            };
            // Another board from 2 days ago (should not be counted in today's daily metrics)
            var inspOld = new InspectionEntity
            {
                Id = 5,
                BatchId = "BATCH-OLD",
                FileName = "old.jpg",
                BoardDisposition = "PASS",
                InspectedAtUtc = utcMidday.AddDays(-2)
            };

            db.Inspections.AddRange(insp1, insp2, insp3, insp4, inspOld);

            // Add defects
            db.Defects.AddRange(
                new DefectEntity { InspectionId = 2, DefectClass = "short" },
                new DefectEntity { InspectionId = 2, DefectClass = "mousebite" },
                new DefectEntity { InspectionId = 4, DefectClass = "short" },
                new DefectEntity { InspectionId = 4, DefectClass = "open" },
                new DefectEntity { InspectionId = 4, DefectClass = "short" },
                new DefectEntity { InspectionId = 5, DefectClass = "spur" } // yesterday defect
            );

            await db.SaveChangesAsync();
        }

        var service = new DashboardService(() => new InspectionDbContext(_dbOptions));
        DateTime target = new DateTime(2026, 11, 10, 0, 0, 0, DateTimeKind.Local);

        DailyDashboardMetrics metrics = await service.GetDailyMetricsAsync(target);

        Assert.Equal(4, metrics.TotalInspected);
        Assert.Equal(2, metrics.PassCount);
        Assert.Equal(1, metrics.ReworkCount);
        Assert.Equal(1, metrics.ScrapCount);
        Assert.Equal(50.0, metrics.YieldPercentage);
        Assert.Equal(1, metrics.HaltedBatchesCount);
        Assert.Contains("BATCH-A", metrics.HaltedBatchIds);

        // Top defect classes: short (3), mousebite (1), open (1)
        Assert.Equal(3, metrics.TopDefectClasses.Count);
        Assert.Equal("short", metrics.TopDefectClasses[0].DefectClass);
        Assert.Equal(3, metrics.TopDefectClasses[0].Count);
        Assert.Equal(60.0, metrics.TopDefectClasses[0].Percentage); // 3 out of 5 = 60.0%

        // Hourly throughput verification
        int targetHour = target.AddHours(12).Hour;
        HourlyThroughputPoint middayPoint = metrics.HourlyThroughput.Single(h => h.HourOfDay == targetHour);
        Assert.Equal(4, middayPoint.InspectedCount);
        Assert.Equal(2, middayPoint.PassCount);
    }
}
