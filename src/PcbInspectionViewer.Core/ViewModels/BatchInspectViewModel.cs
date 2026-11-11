using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcbInspectionViewer.Core.Batch;
using PcbInspectionViewer.Core.Configuration;

namespace PcbInspectionViewer.Core.ViewModels;

public sealed partial class BatchInspectViewModel : ObservableObject, IDisposable
{
    private readonly IBatchInspectionRunner _batchRunner;
    private readonly ISettingsService _settingsService;
    private CancellationTokenSource? _batchCts;
    private string? _baseUrlOverride;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartBatchCommand))]
    private string _folderPath = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartBatchCommand))]
    private string _globalTemplatePath = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartBatchCommand))]
    private bool _useDeepPcbAutoPairing = true;

    [ObservableProperty]
    private string _batchId = "BATCH-RUN";

    [ObservableProperty]
    private string _ipcClass = "Class 2";

    [ObservableProperty]
    private int _concurrency = 2;

    [ObservableProperty]
    private bool _colorizePcb;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartBatchCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelBatchCommand))]
    private bool _isRunning;

    [ObservableProperty]
    private int _totalItems;

    [ObservableProperty]
    private int _completedCount;

    [ObservableProperty]
    private int _failedCount;

    [ObservableProperty]
    private int _cancelledCount;

    [ObservableProperty]
    private double _percentComplete;

    [ObservableProperty]
    private string _elapsedText = "00:00";

    [ObservableProperty]
    private string _etaText = "--:--";

    [ObservableProperty]
    private string _statusMessage = "Select a folder to begin batch inspection.";

    public ObservableCollection<BatchItemProgress> Items { get; } = new();
    public IReadOnlyList<string> AllowedIpcClasses => BatchOptions.AllowedIpcClasses;

    public BatchInspectViewModel(IBatchInspectionRunner batchRunner, ISettingsService settingsService)
    {
        _batchRunner = batchRunner ?? throw new ArgumentNullException(nameof(batchRunner));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
    }

    public void ApplySettingsDefaults(StationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _baseUrlOverride = settings.Api.BaseUrl;

        if (string.IsNullOrWhiteSpace(BatchId) || BatchId == "BATCH-RUN")
        {
            BatchId = settings.Batch.DefaultBatchId;
        }

        IpcClass = settings.Batch.DefaultIpcClass;
        Concurrency = Math.Clamp(settings.Batch.Concurrency, 1, 8);
        ColorizePcb = settings.Batch.ColorizePcb;
    }

    public async Task InitializeFromSettingsAsync(CancellationToken ct = default)
    {
        StationSettings settings = await _settingsService.LoadAsync(ct);
        ApplySettingsDefaults(settings);
    }

    private bool CanStartBatch()
    {
        if (IsRunning || string.IsNullOrWhiteSpace(FolderPath) || !Directory.Exists(FolderPath))
        {
            return false;
        }

        if (UseDeepPcbAutoPairing)
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(GlobalTemplatePath) && File.Exists(GlobalTemplatePath);
    }

    private bool CanCancelBatch() => IsRunning;

    [RelayCommand(CanExecute = nameof(CanStartBatch))]
    public async Task StartBatchAsync()
    {
        IReadOnlyList<BatchInspectionWorkItem> workItems = BatchFolderScanner.ScanFolder(
            FolderPath,
            GlobalTemplatePath,
            UseDeepPcbAutoPairing);

        if (workItems.Count == 0)
        {
            StatusMessage = UseDeepPcbAutoPairing
                ? "No valid DeepPCB pairs (<id>_test.jpg ↔ <id>_temp.jpg) found in folder."
                : "No valid PCB test images found in selected folder.";
            return;
        }

        IsRunning = true;
        StatusMessage = $"Running batch of {workItems.Count} boards (Concurrency: {Concurrency})...";

        _batchCts?.Dispose();
        _batchCts = new CancellationTokenSource();

        Items.Clear();
        foreach (BatchInspectionWorkItem w in workItems)
        {
            Items.Add(new BatchItemProgress(w.Index, w.DisplayFileName, BatchItemStatus.Queued));
        }

        var progressHandler = new Progress<BatchRunProgress>(p =>
        {
            TotalItems = p.TotalItems;
            CompletedCount = p.CompletedCount;
            FailedCount = p.FailedCount;
            CancelledCount = p.CancelledCount;
            PercentComplete = p.PercentComplete;

            ElapsedText = p.Elapsed.ToString(@"mm\:ss");
            EtaText = p.EstimatedRemaining.HasValue ? p.EstimatedRemaining.Value.ToString(@"mm\:ss") : "--:--";

            // Update item row in-place
            for (int i = 0; i < p.Items.Count && i < Items.Count; i++)
            {
                if (Items[i] != p.Items[i])
                {
                    Items[i] = p.Items[i];
                }
            }
        });

        try
        {
            BatchRunProgress final = await _batchRunner.RunBatchAsync(
                workItems,
                BatchId,
                IpcClass,
                ColorizePcb,
                Concurrency,
                _baseUrlOverride,
                progressHandler,
                _batchCts.Token);

            TotalItems = final.TotalItems;
            CompletedCount = final.CompletedCount;
            FailedCount = final.FailedCount;
            CancelledCount = final.CancelledCount;
            PercentComplete = final.PercentComplete;

            for (int i = 0; i < final.Items.Count && i < Items.Count; i++)
            {
                if (Items[i] != final.Items[i])
                {
                    Items[i] = final.Items[i];
                }
            }

            StatusMessage = _batchCts.IsCancellationRequested
                ? $"Batch cancelled by operator. Completed: {final.CompletedCount}, Failed: {final.FailedCount}, Cancelled: {final.CancelledCount}."
                : $"Batch finished. Completed: {final.CompletedCount}, Failed: {final.FailedCount}.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Batch cancelled by operator.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Batch error: {ex.Message}";
        }
        finally
        {
            _batchCts?.Dispose();
            _batchCts = null;
            IsRunning = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancelBatch))]
    public void CancelBatch()
    {
        if (_batchCts is not null && !_batchCts.IsCancellationRequested)
        {
            _batchCts.Cancel();
            StatusMessage = "Cancelling batch inspection...";
        }
    }

    public void Dispose()
    {
        _batchCts?.Cancel();
        _batchCts?.Dispose();
        _batchCts = null;
    }
}
