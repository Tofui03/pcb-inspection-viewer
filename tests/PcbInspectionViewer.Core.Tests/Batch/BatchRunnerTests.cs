using FluentAssertions;
using NSubstitute;
using PcbInspectionViewer.Core.Api;
using PcbInspectionViewer.Core.Api.Dtos;
using PcbInspectionViewer.Core.Batch;
using Xunit;

namespace PcbInspectionViewer.Core.Tests.Batch;

public sealed class BatchRunnerTests : IDisposable
{
    private readonly string _tempDir;

    public BatchRunnerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pcb-batch-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void BatchFolderScanner_InDefaultMode_SkipsTemplateFileAndTempFiles()
    {
        string tpl = Path.Combine(_tempDir, "golden_template.jpg");
        string test1 = Path.Combine(_tempDir, "board_01.jpg");
        string test2 = Path.Combine(_tempDir, "board_02.png");
        string tempOther = Path.Combine(_tempDir, "board_02_temp.png");
        string nonImage = Path.Combine(_tempDir, "notes.txt");

        File.WriteAllBytes(tpl, [1, 2]);
        File.WriteAllBytes(test1, [1, 2]);
        File.WriteAllBytes(test2, [1, 2]);
        File.WriteAllBytes(tempOther, [1, 2]);
        File.WriteAllText(nonImage, "ignored");

        IReadOnlyList<BatchInspectionWorkItem> items = BatchFolderScanner.ScanFolder(
            folderPath: _tempDir,
            globalTemplatePath: tpl,
            useDeepPcbAutoPairing: false);

        items.Should().HaveCount(2);
        items.Select(i => i.DisplayFileName).Should().BeEquivalentTo(["board_01.jpg", "board_02.png"]);
        items.Should().OnlyContain(i => i.TemplateImagePath == Path.GetFullPath(tpl));
    }

    [Fact]
    public void BatchFolderScanner_InDeepPcbAutoPairingMode_PairsTestWithTempFilesCorrectly()
    {
        string test1 = Path.Combine(_tempDir, "20085000_test.jpg");
        string temp1 = Path.Combine(_tempDir, "20085000_temp.jpg");
        string test2 = Path.Combine(_tempDir, "20085001_test.png");
        string temp2 = Path.Combine(_tempDir, "20085001_temp.png");
        string orphanTest = Path.Combine(_tempDir, "20085002_test.jpg");

        File.WriteAllBytes(test1, [1, 2]);
        File.WriteAllBytes(temp1, [1, 2]);
        File.WriteAllBytes(test2, [1, 2]);
        File.WriteAllBytes(temp2, [1, 2]);
        File.WriteAllBytes(orphanTest, [1, 2]);

        IReadOnlyList<BatchInspectionWorkItem> items = BatchFolderScanner.ScanFolder(
            folderPath: _tempDir,
            globalTemplatePath: null,
            useDeepPcbAutoPairing: true);

        items.Should().HaveCount(3);
        items[0].DisplayFileName.Should().Be("20085000_test.jpg");
        items[0].TemplateImagePath.Should().Be(temp1);
        items[1].DisplayFileName.Should().Be("20085001_test.png");
        items[1].TemplateImagePath.Should().Be(temp2);
        items[2].DisplayFileName.Should().Be("20085002_test.jpg");
        items[2].TemplateImagePath.Should().Contain("20085002_temp.jpg");
    }

    [Fact]
    public async Task RunBatchAsync_EnforcesBoundedConcurrency()
    {
        string tpl = Path.Combine(_tempDir, "tpl.jpg");
        File.WriteAllBytes(tpl, [1, 2]);

        var workItems = Enumerable.Range(1, 8).Select(i =>
        {
            string p = Path.Combine(_tempDir, $"board_concurrency_{i}.jpg");
            File.WriteAllBytes(p, [1, 2]);
            return new BatchInspectionWorkItem(i, p, tpl, Path.GetFileName(p));
        }).ToList();

        var apiClient = Substitute.For<IInspectionApiClient>();
        int activeInFlight = 0;
        int maxObservedInFlight = 0;
        var inFlightLock = new object();

        apiClient.UploadTriageAsync(Arg.Any<UploadTriageRequest>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                lock (inFlightLock)
                {
                    activeInFlight++;
                    if (activeInFlight > maxObservedInFlight)
                    {
                        maxObservedInFlight = activeInFlight;
                    }
                }

                await Task.Delay(30);

                lock (inFlightLock)
                {
                    activeInFlight--;
                }

                return new UploadTriageEnvelope(new UploadTriageResponseDto { BoardDisposition = "PASS" }, 50.0);
            });

        var sut = new BatchInspectionRunner(apiClient);

        BatchRunProgress result = await sut.RunBatchAsync(
            workItems,
            batchId: "BATCH-BOUNDS",
            ipcClass: "Class 2",
            colorizePcb: false,
            concurrency: 2);

        result.CompletedCount.Should().Be(8);
        maxObservedInFlight.Should().BeLessOrEqualTo(2);
    }

    [Fact]
    public async Task RunBatchAsync_WithConcurrency_ExecutesAndIsolatesErrorsWithoutAbortingBatch()
    {
        string tpl = Path.Combine(_tempDir, "tpl.jpg");
        string goodTest = Path.Combine(_tempDir, "good.jpg");
        string badTest = Path.Combine(_tempDir, "bad.jpg");

        File.WriteAllBytes(tpl, [1, 2]);
        File.WriteAllBytes(goodTest, [1, 2]);
        File.WriteAllBytes(badTest, [1, 2]);

        var apiClient = Substitute.For<IInspectionApiClient>();

        // Return success for good.jpg, throw for bad.jpg
        apiClient.UploadTriageAsync(
            Arg.Is<UploadTriageRequest>(r => r.TestFileName == "good.jpg"),
            Arg.Any<CancellationToken>())
            .Returns(new UploadTriageEnvelope(
                new UploadTriageResponseDto
                {
                    BoardDisposition = "PASS",
                    Report = new BoardInspectionReportDto { Stage1LatencyMs = 25.0 }
                },
                RoundTripMs: 60.0));

        apiClient.UploadTriageAsync(
            Arg.Is<UploadTriageRequest>(r => r.TestFileName == "bad.jpg"),
            Arg.Any<CancellationToken>())
            .Returns<UploadTriageEnvelope>(_ => throw new InspectionApiException(System.Net.HttpStatusCode.BadRequest, "Bad image decode"));

        var workItems = new[]
        {
            new BatchInspectionWorkItem(1, goodTest, tpl, "good.jpg"),
            new BatchInspectionWorkItem(2, badTest, tpl, "bad.jpg")
        };

        var sut = new BatchInspectionRunner(apiClient);

        BatchRunProgress result = await sut.RunBatchAsync(
            workItems,
            batchId: "BATCH-TEST",
            ipcClass: "Class 2",
            colorizePcb: false,
            concurrency: 2);

        result.TotalItems.Should().Be(2);
        result.CompletedCount.Should().Be(1);
        result.FailedCount.Should().Be(1);
        result.Items[0].Status.Should().Be(BatchItemStatus.Completed);
        result.Items[0].Verdict.Should().Be("PASS");
        result.Items[1].Status.Should().Be(BatchItemStatus.Failed);
        result.Items[1].ErrorMessage.Should().Contain("Bad image decode");
    }

    [Fact]
    public async Task RunBatchAsync_WhenCancelled_MarksRemainingQueuedItemsAsCancelled()
    {
        string tpl = Path.Combine(_tempDir, "tpl.jpg");
        File.WriteAllBytes(tpl, [1, 2]);

        var workItems = Enumerable.Range(1, 10).Select(i =>
        {
            string p = Path.Combine(_tempDir, $"board_{i}.jpg");
            File.WriteAllBytes(p, [1, 2]);
            return new BatchInspectionWorkItem(i, p, tpl, $"board_{i}.jpg");
        }).ToList();

        using var cts = new CancellationTokenSource();
        var apiClient = Substitute.For<IInspectionApiClient>();
        int executed = 0;

        apiClient.UploadTriageAsync(Arg.Any<UploadTriageRequest>(), Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                executed++;
                if (executed >= 2)
                {
                    await cts.CancelAsync();
                }
                await Task.Delay(20);
                return new UploadTriageEnvelope(new UploadTriageResponseDto { BoardDisposition = "PASS" }, 50.0);
            });

        var sut = new BatchInspectionRunner(apiClient);

        BatchRunProgress result = await sut.RunBatchAsync(
            workItems,
            batchId: "BATCH-CANCEL",
            ipcClass: "Class 2",
            colorizePcb: false,
            concurrency: 1,
            ct: cts.Token);

        result.CancelledCount.Should().BeGreaterThan(0);
        result.Items.Should().Contain(i => i.Status == BatchItemStatus.Cancelled);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }
}
