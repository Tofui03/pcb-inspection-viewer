using System.Diagnostics;
using PcbInspectionViewer.Core.Api;
using PcbInspectionViewer.Core.Api.Dtos;
using PcbInspectionViewer.Core.Data;

namespace PcbInspectionViewer.Core.Batch;

public enum BatchItemStatus
{
    Queued,
    Running,
    Completed,
    Failed,
    Cancelled
}

public sealed record BatchItemProgress(
    int Index,
    string FileName,
    BatchItemStatus Status,
    string? Verdict = null,
    int DefectCount = 0,
    double Stage1LatencyMs = 0.0,
    string? ErrorMessage = null);

public sealed record BatchRunProgress(
    int TotalItems,
    int CompletedCount,
    int FailedCount,
    int CancelledCount,
    double PercentComplete,
    TimeSpan Elapsed,
    TimeSpan? EstimatedRemaining,
    IReadOnlyList<BatchItemProgress> Items);

public interface IBatchInspectionRunner
{
    Task<BatchRunProgress> RunBatchAsync(
        IReadOnlyList<BatchInspectionWorkItem> workItems,
        string batchId,
        string ipcClass,
        bool colorizePcb,
        int concurrency,
        string? baseUrlOverride = null,
        IProgress<BatchRunProgress>? progress = null,
        CancellationToken ct = default);
}

public sealed class BatchInspectionRunner : IBatchInspectionRunner
{
    private readonly IInspectionApiClient _apiClient;
    private readonly IInspectionRepository? _repository;

    public BatchInspectionRunner(IInspectionApiClient apiClient, IInspectionRepository? repository = null)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _repository = repository;
    }

    public async Task<BatchRunProgress> RunBatchAsync(
        IReadOnlyList<BatchInspectionWorkItem> workItems,
        string batchId,
        string ipcClass,
        bool colorizePcb,
        int concurrency,
        string? baseUrlOverride = null,
        IProgress<BatchRunProgress>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(workItems);

        int safeConcurrency = Math.Clamp(concurrency, 1, 8);
        using var throttler = new SemaphoreSlim(safeConcurrency, safeConcurrency);

        var itemStates = workItems
            .Select(w => new BatchItemProgress(w.Index, w.DisplayFileName, BatchItemStatus.Queued))
            .ToArray();

        var stateLock = new object();
        int completedCount = 0;
        int failedCount = 0;
        int cancelledCount = 0;

        var stopwatch = Stopwatch.StartNew();

        void ReportCurrentProgress()
        {
            if (progress is null)
            {
                return;
            }

            lock (stateLock)
            {
                int processed = completedCount + failedCount + cancelledCount;
                double pct = workItems.Count > 0 ? (processed * 100.0) / workItems.Count : 100.0;
                TimeSpan elapsed = stopwatch.Elapsed;
                TimeSpan? eta = null;

                if (processed > 0 && processed < workItems.Count)
                {
                    double avgMsPerItem = elapsed.TotalMilliseconds / processed;
                    int remainingItems = workItems.Count - processed;
                    eta = TimeSpan.FromMilliseconds(avgMsPerItem * remainingItems);
                }

                progress.Report(new BatchRunProgress(
                    TotalItems: workItems.Count,
                    CompletedCount: completedCount,
                    FailedCount: failedCount,
                    CancelledCount: cancelledCount,
                    PercentComplete: pct,
                    Elapsed: elapsed,
                    EstimatedRemaining: eta,
                    Items: itemStates.ToArray()));
            }
        }

        ReportCurrentProgress();

        // 200 ms heartbeat timer to guarantee updates <= 250 ms during slow network calls (Spec Gap 4)
        using var timerCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task heartbeatTask = Task.Run(async () =>
        {
            try
            {
                using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(200));
                while (await timer.WaitForNextTickAsync(timerCts.Token).ConfigureAwait(false))
                {
                    ReportCurrentProgress();
                }
            }
            catch (OperationCanceledException)
            {
            }
        }, CancellationToken.None);

        var tasks = workItems.Select(async item =>
        {
            if (ct.IsCancellationRequested)
            {
                lock (stateLock)
                {
                    itemStates[item.Index - 1] = itemStates[item.Index - 1] with { Status = BatchItemStatus.Cancelled };
                    cancelledCount++;
                }
                ReportCurrentProgress();
                return;
            }

            try
            {
                await throttler.WaitAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                lock (stateLock)
                {
                    itemStates[item.Index - 1] = itemStates[item.Index - 1] with { Status = BatchItemStatus.Cancelled };
                    cancelledCount++;
                }
                ReportCurrentProgress();
                return;
            }

            try
            {
                if (ct.IsCancellationRequested)
                {
                    lock (stateLock)
                    {
                        itemStates[item.Index - 1] = itemStates[item.Index - 1] with { Status = BatchItemStatus.Cancelled };
                        cancelledCount++;
                    }
                    ReportCurrentProgress();
                    return;
                }

                lock (stateLock)
                {
                    itemStates[item.Index - 1] = itemStates[item.Index - 1] with { Status = BatchItemStatus.Running };
                }
                ReportCurrentProgress();

                // Validate files exist locally
                if (!File.Exists(item.TemplateImagePath))
                {
                    lock (stateLock)
                    {
                        itemStates[item.Index - 1] = itemStates[item.Index - 1] with
                        {
                            Status = BatchItemStatus.Failed,
                            ErrorMessage = "Template image not found."
                        };
                        failedCount++;
                    }
                    ReportCurrentProgress();
                    return;
                }

                if (!File.Exists(item.TestImagePath))
                {
                    lock (stateLock)
                    {
                        itemStates[item.Index - 1] = itemStates[item.Index - 1] with
                        {
                            Status = BatchItemStatus.Failed,
                            ErrorMessage = "Test image not found."
                        };
                        failedCount++;
                    }
                    ReportCurrentProgress();
                    return;
                }

                byte[] templateBytes = await File.ReadAllBytesAsync(item.TemplateImagePath, ct).ConfigureAwait(false);
                byte[] testBytes = await File.ReadAllBytesAsync(item.TestImagePath, ct).ConfigureAwait(false);

                var request = new UploadTriageRequest(
                    TemplateBytes: templateBytes,
                    TemplateFileName: Path.GetFileName(item.TemplateImagePath),
                    TestBytes: testBytes,
                    TestFileName: Path.GetFileName(item.TestImagePath),
                    BatchId: batchId,
                    IpcClass: ipcClass,
                    SimulateRogueVlm: false,
                    ColorizePcb: colorizePcb,
                    ReturnImages: _repository is not null,
                    BaseUrlOverride: baseUrlOverride);

                UploadTriageEnvelope envelope = await _apiClient.UploadTriageAsync(request, ct).ConfigureAwait(false);

                if (_repository is not null)
                {
                    try
                    {
                        await _repository.SaveInspectionResultAsync(new SaveInspectionCommand(
                            BatchId: batchId,
                            FileName: item.DisplayFileName,
                            TemplateImagePath: item.TemplateImagePath,
                            TestImagePath: item.TestImagePath,
                            IpcClass: ipcClass,
                            Envelope: envelope), ct).ConfigureAwait(false);
                    }
                    catch
                    {
                        // Database error should not mark inspection failed
                    }
                }

                lock (stateLock)
                {
                    itemStates[item.Index - 1] = itemStates[item.Index - 1] with
                    {
                        Status = BatchItemStatus.Completed,
                        Verdict = envelope.Response.BoardDisposition.ToUpperInvariant(),
                        DefectCount = envelope.Response.Report?.Detections?.Count ?? 0,
                        Stage1LatencyMs = envelope.Response.Report?.Stage1LatencyMs ?? 0.0
                    };
                    completedCount++;
                }
                ReportCurrentProgress();
            }
            catch (OperationCanceledException)
            {
                lock (stateLock)
                {
                    itemStates[item.Index - 1] = itemStates[item.Index - 1] with { Status = BatchItemStatus.Cancelled };
                    cancelledCount++;
                }
                ReportCurrentProgress();
            }
            catch (Exception ex)
            {
                lock (stateLock)
                {
                    itemStates[item.Index - 1] = itemStates[item.Index - 1] with
                    {
                        Status = BatchItemStatus.Failed,
                        ErrorMessage = ex.Message
                    };
                    failedCount++;
                }
                ReportCurrentProgress();
            }
            finally
            {
                throttler.Release();
            }
        });

        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Catch WhenAll cancellation
        }
        finally
        {
            timerCts.Cancel();
            try { await heartbeatTask.ConfigureAwait(false); } catch { }
        }

        stopwatch.Stop();
        ReportCurrentProgress();

        lock (stateLock)
        {
            int totalProcessed = completedCount + failedCount + cancelledCount;
            double finalPct = workItems.Count > 0 ? (totalProcessed * 100.0) / workItems.Count : 100.0;

            return new BatchRunProgress(
                TotalItems: workItems.Count,
                CompletedCount: completedCount,
                FailedCount: failedCount,
                CancelledCount: cancelledCount,
                PercentComplete: finalPct,
                Elapsed: stopwatch.Elapsed,
                EstimatedRemaining: TimeSpan.Zero,
                Items: itemStates.ToArray());
        }
    }
}
