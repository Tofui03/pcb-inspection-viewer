using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcbInspectionViewer.Core.Services;

namespace PcbInspectionViewer.Core.ViewModels;

public sealed partial class DashboardViewModel : ObservableObject
{
    private readonly IDashboardService _dashboardService;

    [ObservableProperty]
    private DateTime _selectedDate = DateTime.Today;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private int _totalInspected;

    [ObservableProperty]
    private int _passCount;

    [ObservableProperty]
    private int _reworkCount;

    [ObservableProperty]
    private int _scrapCount;

    [ObservableProperty]
    private double _yieldPercentage;

    [ObservableProperty]
    private int _haltedBatchesCount;

    [ObservableProperty]
    private string _haltedBatchesSummary = "None";

    [ObservableProperty]
    private IReadOnlyList<DefectClassDistribution> _topDefectClasses = Array.Empty<DefectClassDistribution>();

    [ObservableProperty]
    private IReadOnlyList<HourlyThroughputPoint> _hourlyThroughput = Array.Empty<HourlyThroughputPoint>();

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public DashboardViewModel(IDashboardService dashboardService)
    {
        _dashboardService = dashboardService ?? throw new ArgumentNullException(nameof(dashboardService));
    }

    partial void OnSelectedDateChanged(DateTime value)
    {
        _ = RefreshDashboardAsync();
    }

    [RelayCommand]
    public async Task RefreshDashboardAsync(CancellationToken ct = default)
    {
        if (IsLoading) return;

        IsLoading = true;
        StatusMessage = "Calculating metrics...";
        try
        {
            DailyDashboardMetrics metrics = await _dashboardService.GetDailyMetricsAsync(SelectedDate, ct);

            TotalInspected = metrics.TotalInspected;
            PassCount = metrics.PassCount;
            ReworkCount = metrics.ReworkCount;
            ScrapCount = metrics.ScrapCount;
            YieldPercentage = metrics.YieldPercentage;
            HaltedBatchesCount = metrics.HaltedBatchesCount;
            HaltedBatchesSummary = metrics.HaltedBatchIds.Count > 0
                ? string.Join(", ", metrics.HaltedBatchIds)
                : "None";

            TopDefectClasses = metrics.TopDefectClasses;
            HourlyThroughput = metrics.HourlyThroughput;

            StatusMessage = $"Metrics updated for {SelectedDate:yyyy-MM-dd}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load dashboard: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
}
