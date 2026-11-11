using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcbInspectionViewer.Core.Api;
using PcbInspectionViewer.Core.Api.Dtos;
using PcbInspectionViewer.Core.Configuration;
using PcbInspectionViewer.Core.Geometry;

namespace PcbInspectionViewer.Core.ViewModels;

public sealed partial class InspectViewModel : ObservableObject, IDisposable
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp"
    };

    private readonly IInspectionApiClient _apiClient;
    private readonly ISettingsService _settingsService;
    private CancellationTokenSource? _inspectCts;
    private string? _baseUrlOverride;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InspectCommand))]
    private string _templateImagePath = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InspectCommand))]
    private string _testImagePath = string.Empty;

    [ObservableProperty]
    private string _batchId = "BATCH-UPLOAD";

    [ObservableProperty]
    private string _ipcClass = "Class 2";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InspectCommand))]
    private bool _cleanBoardMode;

    [ObservableProperty]
    private bool _simulateRogueVlm;

    [ObservableProperty]
    private bool _colorizePcb;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InspectCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelInspectCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private bool _hasResult;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private string _statusOrErrorMessage = string.Empty;

    [ObservableProperty]
    private string _verdictText = string.Empty;

    [ObservableProperty]
    private bool _isLineHalted;

    [ObservableProperty]
    private double _stage1LatencyMs;

    [ObservableProperty]
    private double _roundTripMs;

    [ObservableProperty]
    private string _activeImageLayer = "Test";

    [ObservableProperty]
    private bool _showBoundingBoxes = true;

    [ObservableProperty]
    private double _zoomFactor = 1.0;

    [ObservableProperty]
    private double _panX;

    [ObservableProperty]
    private double _panY;

    [ObservableProperty]
    private string? _activeImageDataUri;

    [ObservableProperty]
    private string? _templateImageB64;

    [ObservableProperty]
    private string? _testImageB64;

    [ObservableProperty]
    private string? _diffImageB64;

    public ObservableCollection<BoundingBoxDetectionDto> Detections { get; } = new();
    public ObservableCollection<InspectionDecisionDto> Decisions { get; } = new();
    public ObservableCollection<AlignedDefectDecision> AlignedDefects { get; } = new();

    public IReadOnlyList<string> AllowedIpcClasses => BatchOptions.AllowedIpcClasses;

    public InspectViewModel(IInspectionApiClient apiClient, ISettingsService settingsService)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
    }

    partial void OnTemplateImagePathChanged(string value)
    {
        if (CleanBoardMode && !string.IsNullOrWhiteSpace(value))
        {
            TestImagePath = value;
        }
    }

    partial void OnCleanBoardModeChanged(bool value)
    {
        if (value && !string.IsNullOrWhiteSpace(TemplateImagePath))
        {
            TestImagePath = TemplateImagePath;
        }
    }

    public static bool IsSupportedImageFile(string? pathOrFileName)
    {
        if (string.IsNullOrWhiteSpace(pathOrFileName))
        {
            return false;
        }

        string ext = Path.GetExtension(pathOrFileName);
        return SupportedExtensions.Contains(ext);
    }

    public bool TrySetImagePathFromDrop(string? filePath, bool isTemplate = false)
    {
        if (!IsSupportedImageFile(filePath) || string.IsNullOrWhiteSpace(filePath))
        {
            HasError = true;
            StatusOrErrorMessage = "Unsupported image format. Please select a .jpg, .jpeg, .png, or .bmp file.";
            return false;
        }

        HasError = false;
        if (isTemplate)
        {
            TemplateImagePath = filePath;
            StatusOrErrorMessage = $"Golden template set: {Path.GetFileName(filePath)}";
        }
        else
        {
            TestImagePath = filePath;
            StatusOrErrorMessage = $"Test image set: {Path.GetFileName(filePath)}";
        }

        return true;
    }

    public void ApplySettingsDefaults(StationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _baseUrlOverride = settings.Api.BaseUrl;

        if (string.IsNullOrWhiteSpace(BatchId) || BatchId == "BATCH-UPLOAD")
        {
            BatchId = settings.Batch.DefaultBatchId;
        }

        IpcClass = settings.Batch.DefaultIpcClass;
        ColorizePcb = settings.Batch.ColorizePcb;
    }

    public async Task InitializeFromSettingsAsync(CancellationToken cancellationToken = default)
    {
        StationSettings settings = await _settingsService.LoadAsync(cancellationToken);
        ApplySettingsDefaults(settings);
    }

    private bool CanInspect()
    {
        string effectiveTestPath = CleanBoardMode ? TemplateImagePath : TestImagePath;
        return !IsBusy &&
               !string.IsNullOrWhiteSpace(TemplateImagePath) &&
               File.Exists(TemplateImagePath) &&
               !string.IsNullOrWhiteSpace(effectiveTestPath) &&
               File.Exists(effectiveTestPath);
    }

    private bool CanCancelInspect() => IsBusy;

    [RelayCommand(CanExecute = nameof(CanInspect))]
    private async Task InspectAsync()
    {
        IsBusy = true;
        HasError = false;
        StatusOrErrorMessage = "Inspecting board...";

        _inspectCts?.Dispose();
        _inspectCts = new CancellationTokenSource();

        try
        {
            string effectiveTestPath = CleanBoardMode ? TemplateImagePath : TestImagePath;

            byte[] templateBytes = await File.ReadAllBytesAsync(TemplateImagePath, _inspectCts.Token);
            byte[] testBytes = await File.ReadAllBytesAsync(effectiveTestPath, _inspectCts.Token);

            var request = new UploadTriageRequest(
                TemplateBytes: templateBytes,
                TemplateFileName: Path.GetFileName(TemplateImagePath),
                TestBytes: testBytes,
                TestFileName: Path.GetFileName(effectiveTestPath),
                BatchId: BatchId,
                IpcClass: IpcClass,
                SimulateRogueVlm: SimulateRogueVlm,
                ColorizePcb: ColorizePcb,
                ReturnImages: true,
                BaseUrlOverride: _baseUrlOverride);

            UploadTriageEnvelope envelope = await _apiClient.UploadTriageAsync(request, _inspectCts.Token);
            ApplyInspectionResult(envelope);
            HasResult = true;
            StatusOrErrorMessage = "Inspection completed successfully.";
        }
        catch (OperationCanceledException)
        {
            StatusOrErrorMessage = "Inspection Cancelled by operator.";
        }
        catch (InspectionApiException ex)
        {
            HasError = true;
            StatusOrErrorMessage = $"Inspection API error ({(int)ex.StatusCode}): {ex.Message}";
        }
        catch (Exception ex)
        {
            HasError = true;
            StatusOrErrorMessage = $"Inspection error: {ex.Message}";
        }
        finally
        {
            _inspectCts?.Dispose();
            _inspectCts = null;
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancelInspect))]
    private void CancelInspect()
    {
        if (_inspectCts is not null && !_inspectCts.IsCancellationRequested)
        {
            _inspectCts.Cancel();
        }
    }

    [RelayCommand]
    private void SelectLayer(string? layerName)
    {
        if (string.IsNullOrWhiteSpace(layerName))
        {
            return;
        }

        ActiveImageLayer = layerName;
        ActiveImageDataUri = layerName switch
        {
            "Template" => TemplateImageB64,
            "Diff" => DiffImageB64,
            _ => TestImageB64
        };
    }

    [RelayCommand]
    private void ResetZoomAndPan()
    {
        ZoomFactor = 1.0;
        PanX = 0.0;
        PanY = 0.0;
    }

    private void ApplyInspectionResult(UploadTriageEnvelope envelope)
    {
        UploadTriageResponseDto dto = envelope.Response;

        // Deterministic rule: display server board_disposition verbatim; never recompute or override.
        VerdictText = dto.BoardDisposition.ToUpperInvariant();
        IsLineHalted = dto.LineHalted;
        Stage1LatencyMs = dto.Report?.Stage1LatencyMs ?? 0;
        RoundTripMs = envelope.RoundTripMs;

        TemplateImageB64 = dto.TemplateB64;
        TestImageB64 = dto.TestB64;
        DiffImageB64 = dto.DiffB64;

        SelectLayer(ActiveImageLayer);

        Detections.Clear();
        IReadOnlyList<BoundingBoxDetectionDto> rawDetections = dto.Report?.Detections ?? [];
        foreach (BoundingBoxDetectionDto detection in rawDetections)
        {
            Detections.Add(detection);
        }

        Decisions.Clear();
        IReadOnlyList<InspectionDecisionDto> rawDecisions = dto.Report?.Decisions ?? [];
        foreach (InspectionDecisionDto decision in rawDecisions)
        {
            Decisions.Add(decision);
        }

        AlignedDefects.Clear();
        foreach (AlignedDefectDecision aligned in OverlayGeometryCalculator.PairDetectionsWithDecisions(rawDetections, rawDecisions))
        {
            AlignedDefects.Add(aligned);
        }
    }

    public void Dispose()
    {
        _inspectCts?.Cancel();
        _inspectCts?.Dispose();
        _inspectCts = null;
    }
}
