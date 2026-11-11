using Microsoft.EntityFrameworkCore;
using PcbInspectionViewer.Core.Data;
using PcbInspectionViewer.Core.Data.Entities;

namespace PcbInspectionViewer.Core.Services;

public sealed class DashboardService : IDashboardService
{
    private readonly Func<InspectionDbContext> _contextFactory;

    public DashboardService(Func<InspectionDbContext> contextFactory)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
    }

    public async Task<DailyDashboardMetrics> GetDailyMetricsAsync(DateTime targetDateLocal, CancellationToken ct = default)
    {
        // Compute date range in UTC corresponding to the chosen local day (midnight to midnight local)
        DateTime dayStartLocal = targetDateLocal.Date;
        DateTime dayEndLocal = dayStartLocal.AddDays(1);

        DateTime dayStartUtc = dayStartLocal.ToUniversalTime();
        DateTime dayEndUtc = dayEndLocal.ToUniversalTime();

        await using InspectionDbContext db = _contextFactory();

        // 1. Fetch inspections for this time window
        List<InspectionEntity> inspections = await db.Inspections
            .AsNoTracking()
            .Where(i => i.InspectedAtUtc >= dayStartUtc && i.InspectedAtUtc < dayEndUtc)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        int total = inspections.Count;
        int pass = inspections.Count(i => i.BoardDisposition == "PASS");
        int rework = inspections.Count(i => i.BoardDisposition == "REWORK");
        int scrap = inspections.Count(i => i.BoardDisposition == "SCRAP");
        double yieldPct = total > 0 ? (double)pass / total * 100.0 : 0.0;

        // 2. Batches halted on this day
        HashSet<string> batchIdsToday = inspections
            .Select(i => i.BatchId)
            .Where(b => !string.IsNullOrEmpty(b))
            .ToHashSet();

        List<string> haltedBatchIds = await db.Batches
            .AsNoTracking()
            .Where(b => batchIdsToday.Contains(b.BatchId) && b.LineHalted)
            .Select(b => b.BatchId)
            .Distinct()
            .ToListAsync(ct)
            .ConfigureAwait(false);

        // 3. Defects distribution for inspections today
        List<long> inspectionIds = inspections.Select(i => i.Id).ToList();
        List<DefectEntity> defects = inspectionIds.Count > 0
            ? await db.Defects
                .AsNoTracking()
                .Where(d => inspectionIds.Contains(d.InspectionId))
                .ToListAsync(ct)
                .ConfigureAwait(false)
            : new List<DefectEntity>();

        int totalDefects = defects.Count;
        List<DefectClassDistribution> topDefects = defects
            .GroupBy(d => string.IsNullOrWhiteSpace(d.DefectClass) ? "Unknown" : d.DefectClass)
            .Select(g => new DefectClassDistribution(
                DefectClass: g.Key,
                Count: g.Count(),
                Percentage: totalDefects > 0 ? Math.Round((double)g.Count() / totalDefects * 100.0, 1) : 0.0))
            .OrderByDescending(d => d.Count)
            .Take(3)
            .ToList();

        // 4. Hourly throughput for the 24 hours of local day
        var hourly = new List<HourlyThroughputPoint>(24);
        for (int h = 0; h < 24; h++)
        {
            DateTime slotStartLocal = dayStartLocal.AddHours(h);
            DateTime slotEndLocal = slotStartLocal.AddHours(1);
            DateTime slotStartUtc = slotStartLocal.ToUniversalTime();
            DateTime slotEndUtc = slotEndLocal.ToUniversalTime();

            var slotInspections = inspections
                .Where(i => i.InspectedAtUtc >= slotStartUtc && i.InspectedAtUtc < slotEndUtc)
                .ToList();

            hourly.Add(new HourlyThroughputPoint(
                HourOfDay: h,
                HourLabel: $"{h:00}:00",
                InspectedCount: slotInspections.Count,
                PassCount: slotInspections.Count(i => i.BoardDisposition == "PASS"),
                ReworkCount: slotInspections.Count(i => i.BoardDisposition == "REWORK"),
                ScrapCount: slotInspections.Count(i => i.BoardDisposition == "SCRAP")));
        }

        return new DailyDashboardMetrics(
            TargetDateLocal: dayStartLocal,
            TotalInspected: total,
            PassCount: pass,
            ReworkCount: rework,
            ScrapCount: scrap,
            YieldPercentage: Math.Round(yieldPct, 2),
            HaltedBatchesCount: haltedBatchIds.Count,
            HaltedBatchIds: haltedBatchIds,
            TopDefectClasses: topDefects,
            HourlyThroughput: hourly);
    }
}
