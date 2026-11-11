using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcbInspectionViewer.Core.Data;
using PcbInspectionViewer.Core.Data.Entities;
using PcbInspectionViewer.Core.Storage;

namespace PcbInspectionViewer.Core.ViewModels;

public sealed record HistoryItemRow(
    long Id,
    DateTime InspectedAtLocal,
    string FileName,
    string BatchId,
    string BoardDisposition,
    int DefectCount,
    string DefectClassesSummary,
    double Stage1LatencyMs,
    double RoundTripMs);

public sealed partial class HistoryViewModel : ObservableObject
{
    private readonly IInspectionRepository _repository;
    private readonly IImageCache _imageCache;

    [ObservableProperty]
    private string _selectedVerdict = "ALL";

    [ObservableProperty]
    private string _selectedBatch = "ALL";

    [ObservableProperty]
    private string _fileNameSearch = string.Empty;

    [ObservableProperty]
    private DateTime? _startDate;

    [ObservableProperty]
    private DateTime? _endDate;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private HistoryItemRow? _selectedItem;

    [ObservableProperty]
    private int _totalLoadedCount;

    public ObservableCollection<HistoryItemRow> Items { get; } = new();
    public ObservableCollection<string> AvailableBatches { get; } = new() { "ALL" };

    public IReadOnlyList<string> AvailableVerdicts { get; } = new[] { "ALL", "PASS", "REWORK", "SCRAP" };
    public IReadOnlyList<string> AvailableDefectClasses { get; } = new[]
    {
        "Short", "Open", "Mousebite", "Spur", "Spurious Copper", "Pin-hole"
    };

    public ObservableCollection<string> SelectedDefectClasses { get; } = new();

    public event EventHandler<InspectionEntity>? DrillDownRequested;
    private readonly Export.IExportService? _exportService;

    public HistoryViewModel(
        IInspectionRepository repository,
        IImageCache imageCache,
        Export.IExportService? exportService = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _imageCache = imageCache ?? throw new ArgumentNullException(nameof(imageCache));
        _exportService = exportService;
    }

    [RelayCommand]
    public async Task ExportCurrentViewCsvAsync(Stream destinationStream, CancellationToken ct = default)
    {
        if (_exportService is null) return;
        var filter = BuildCurrentFilter();
        List<InspectionEntity> inspections = await _repository.QueryInspectionsAsync(filter, ct);
        await _exportService.ExportInspectionsCsvAsync(inspections, destinationStream, ct);
        StatusMessage = $"Exported {inspections.Count} records to CSV.";
    }

    [RelayCommand]
    public async Task ExportCurrentViewPdfAsync(Stream destinationStream, CancellationToken ct = default)
    {
        if (_exportService is null) return;
        var filter = BuildCurrentFilter();
        List<InspectionEntity> inspections = await _repository.QueryInspectionsAsync(filter, ct);
        
        int total = inspections.Count;
        int pass = inspections.Count(i => i.BoardDisposition == "PASS");
        int rework = inspections.Count(i => i.BoardDisposition == "REWORK");
        int scrap = inspections.Count(i => i.BoardDisposition == "SCRAP");
        double yieldPct = total > 0 ? (double)pass / total * 100.0 : 0.0;
        bool halted = inspections.Any(i => i.LineHalted);

        var reportData = new Export.PdfReportData(
            ReportTitle: "PCB Inspection History Report",
            BatchOrDateScope: $"Batch: {SelectedBatch} | Verdict: {SelectedVerdict}",
            GeneratedAtLocal: DateTime.Now,
            TotalInspected: total,
            PassCount: pass,
            ReworkCount: rework,
            ScrapCount: scrap,
            YieldPercentage: Math.Round(yieldPct, 2),
            LineHalted: halted,
            Inspections: inspections,
            TopDefects: Array.Empty<Services.DefectClassDistribution>(),
            LlmSummaryHeadline: null,
            LlmSummaryBody: null,
            IsAiGenerated: false
        );

        await _exportService.GeneratePdfReportAsync(reportData, destinationStream, ct);
        StatusMessage = $"Exported PDF report for {total} boards.";
    }

    private InspectionQueryFilter BuildCurrentFilter()
    {
        return new InspectionQueryFilter(
            Verdict: SelectedVerdict,
            BatchId: SelectedBatch,
            FileNameSubstring: FileNameSearch,
            StartDateUtc: StartDate?.ToUniversalTime(),
            EndDateUtc: EndDate?.ToUniversalTime(),
            DefectClasses: SelectedDefectClasses.ToList(),
            Limit: 1000);
    }

    [RelayCommand]
    public async Task LoadHistoryAsync(CancellationToken ct = default)
    {
        IsBusy = true;
        StatusMessage = "Querying inspection history...";

        try
        {
            var filter = new InspectionQueryFilter(
                Verdict: SelectedVerdict,
                DefectClasses: SelectedDefectClasses.Count > 0 ? SelectedDefectClasses.ToList() : null,
                BatchId: SelectedBatch,
                StartDateUtc: StartDate?.ToUniversalTime(),
                EndDateUtc: EndDate?.ToUniversalTime().AddDays(1).AddTicks(-1),
                FileNameSubstring: FileNameSearch,
                Limit: 1000);

            List<InspectionEntity> entities = await _repository.QueryInspectionsAsync(filter, ct);

            Items.Clear();
            foreach (InspectionEntity e in entities)
            {
                Items.Add(new HistoryItemRow(
                    Id: e.Id,
                    InspectedAtLocal: e.InspectedAtUtc.ToLocalTime(),
                    FileName: e.FileName,
                    BatchId: e.BatchId,
                    BoardDisposition: e.BoardDisposition,
                    DefectCount: e.DefectCount,
                    DefectClassesSummary: e.DefectClassesSummary,
                    Stage1LatencyMs: e.Stage1LatencyMs,
                    RoundTripMs: e.RoundTripMs));
            }

            TotalLoadedCount = Items.Count;
            StatusMessage = $"Loaded {TotalLoadedCount} inspection record(s).";

            // Refresh batch filter list while preserving selection (Minor 4)
            string previousBatch = SelectedBatch;
            List<string> batches = await _repository.GetAllBatchIdsAsync(ct);
            AvailableBatches.Clear();
            AvailableBatches.Add("ALL");
            foreach (string b in batches)
            {
                AvailableBatches.Add(b);
            }

            SelectedBatch = AvailableBatches.Contains(previousBatch) ? previousBatch : "ALL";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load history: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task DrillDownAsync(HistoryItemRow? row, CancellationToken ct = default)
    {
        if (row is null)
        {
            return;
        }

        InspectionEntity? entity = await _repository.GetInspectionDetailsAsync(row.Id, ct);
        if (entity is not null)
        {
            DrillDownRequested?.Invoke(this, entity);
        }
    }

    [RelayCommand]
    public void ToggleDefectClassFilter(string? defectClass)
    {
        if (string.IsNullOrWhiteSpace(defectClass))
        {
            return;
        }

        if (SelectedDefectClasses.Contains(defectClass))
        {
            SelectedDefectClasses.Remove(defectClass);
        }
        else
        {
            SelectedDefectClasses.Add(defectClass);
        }

        _ = LoadHistoryAsync();
    }

    [RelayCommand]
    public void ClearFilters()
    {
        SelectedVerdict = "ALL";
        SelectedBatch = "ALL";
        FileNameSearch = string.Empty;
        StartDate = null;
        EndDate = null;
        SelectedDefectClasses.Clear();

        _ = LoadHistoryAsync();
    }
}
