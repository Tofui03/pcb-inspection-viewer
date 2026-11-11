namespace PcbInspectionViewer.Core.Services;

public sealed record DefectClassDistribution(
    string DefectClass,
    int Count,
    double Percentage);

public sealed record HourlyThroughputPoint(
    int HourOfDay,
    string HourLabel,
    int InspectedCount,
    int PassCount,
    int ReworkCount,
    int ScrapCount);

public sealed record DailyDashboardMetrics(
    DateTime TargetDateLocal,
    int TotalInspected,
    int PassCount,
    int ReworkCount,
    int ScrapCount,
    double YieldPercentage,
    int HaltedBatchesCount,
    IReadOnlyList<string> HaltedBatchIds,
    IReadOnlyList<DefectClassDistribution> TopDefectClasses,
    IReadOnlyList<HourlyThroughputPoint> HourlyThroughput);

public interface IDashboardService
{
    Task<DailyDashboardMetrics> GetDailyMetricsAsync(DateTime targetDateLocal, CancellationToken ct = default);
}
