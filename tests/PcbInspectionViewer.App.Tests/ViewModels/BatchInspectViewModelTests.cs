using FluentAssertions;
using NSubstitute;
using PcbInspectionViewer.Core.Batch;
using PcbInspectionViewer.Core.Configuration;
using PcbInspectionViewer.Core.ViewModels;
using Xunit;

namespace PcbInspectionViewer.App.Tests.ViewModels;

public sealed class BatchInspectViewModelTests : IDisposable
{
    private readonly string _tempDir;

    public BatchInspectViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pcb-batch-vm-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public async Task StartBatchCommand_WhenFolderHasDeepPcbPairs_ExecutesBatchAndUpdatesUiProperties()
    {
        string test1 = Path.Combine(_tempDir, "20085000_test.jpg");
        string temp1 = Path.Combine(_tempDir, "20085000_temp.jpg");
        File.WriteAllBytes(test1, [1, 2]);
        File.WriteAllBytes(temp1, [1, 2]);

        var batchRunner = Substitute.For<IBatchInspectionRunner>();
        var settingsService = Substitute.For<ISettingsService>();

        batchRunner.RunBatchAsync(
            Arg.Any<IReadOnlyList<BatchInspectionWorkItem>>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<bool>(),
            Arg.Any<int>(),
            Arg.Any<string?>(),
            Arg.Any<IProgress<BatchRunProgress>>(),
            Arg.Any<CancellationToken>())
            .Returns(new BatchRunProgress(
                TotalItems: 1,
                CompletedCount: 1,
                FailedCount: 0,
                CancelledCount: 0,
                PercentComplete: 100.0,
                Elapsed: TimeSpan.FromSeconds(2),
                EstimatedRemaining: TimeSpan.Zero,
                Items: [new BatchItemProgress(1, "20085000_test.jpg", BatchItemStatus.Completed, "PASS", 0, 30.0)]));

        var sut = new BatchInspectViewModel(batchRunner, settingsService)
        {
            FolderPath = _tempDir,
            UseDeepPcbAutoPairing = true,
            BatchId = "BATCH-UI-01",
            Concurrency = 2
        };

        await sut.StartBatchCommand.ExecuteAsync(null);

        sut.IsRunning.Should().BeFalse();
        sut.Items.Should().HaveCount(1);
        sut.Items[0].Status.Should().Be(BatchItemStatus.Completed);
        sut.Items[0].Verdict.Should().Be("PASS");
        sut.StatusMessage.Should().Contain("Batch finished");
    }

    [Fact]
    public void ApplySettingsDefaults_SetsBatchDefaultsFromStationSettings()
    {
        var batchRunner = Substitute.For<IBatchInspectionRunner>();
        var settingsService = Substitute.For<ISettingsService>();
        var sut = new BatchInspectViewModel(batchRunner, settingsService);

        var settings = new StationSettings
        {
            Batch = new BatchOptions
            {
                Concurrency = 4,
                DefaultBatchId = "BATCH-DEFAULT-99",
                DefaultIpcClass = "Class 3",
                ColorizePcb = true
            }
        };

        sut.ApplySettingsDefaults(settings);

        sut.BatchId.Should().Be("BATCH-DEFAULT-99");
        sut.Concurrency.Should().Be(4);
        sut.IpcClass.Should().Be("Class 3");
        sut.ColorizePcb.Should().BeTrue();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }
}
