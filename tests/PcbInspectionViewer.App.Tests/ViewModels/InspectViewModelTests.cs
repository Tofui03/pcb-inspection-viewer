using System.Net;
using System.Text.Json;
using FluentAssertions;
using NSubstitute;
using PcbInspectionViewer.Core.Api;
using PcbInspectionViewer.Core.Api.Dtos;
using PcbInspectionViewer.Core.Configuration;
using PcbInspectionViewer.Core.ViewModels;
using Xunit;

namespace PcbInspectionViewer.App.Tests.ViewModels;

public sealed class InspectViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _templatePath;
    private readonly string _testPath;

    public InspectViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pcb-inspect-vm-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _templatePath = Path.Combine(_tempDir, "20085000_temp.jpg");
        _testPath = Path.Combine(_tempDir, "20085000_test.jpg");
        File.WriteAllBytes(_templatePath, [0xFF, 0xD8, 0xFF, 0xE0]);
        File.WriteAllBytes(_testPath, [0xFF, 0xD8, 0xFF, 0xE0]);
    }

    private static UploadTriageResponseDto LoadRealFixtureDto()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            string candidate = Path.Combine(dir, "tests", "fixtures", "upload_triage_sample.json");
            if (File.Exists(candidate))
            {
                string json = File.ReadAllText(candidate);
                return JsonSerializer.Deserialize<UploadTriageResponseDto>(json)!;
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new FileNotFoundException("Missing upload_triage_sample.json fixture.");
    }

    [Fact]
    public async Task InspectCommand_WhenFixtureReturnsScrap_MapsServerDispositionDecisionsAndLayersExactly()
    {
        UploadTriageResponseDto dto = LoadRealFixtureDto();
        var apiClient = Substitute.For<IInspectionApiClient>();
        apiClient.UploadTriageAsync(Arg.Any<UploadTriageRequest>(), Arg.Any<CancellationToken>())
            .Returns(new UploadTriageEnvelope(dto, RoundTripMs: 112.4));

        var settingsService = Substitute.For<ISettingsService>();
        settingsService.LoadAsync(Arg.Any<CancellationToken>()).Returns(new StationSettings());

        var sut = new InspectViewModel(apiClient, settingsService)
        {
            TemplateImagePath = _templatePath,
            TestImagePath = _testPath,
            BatchId = "BATCH-SMT-01",
            IpcClass = "Class 2"
        };

        await sut.InspectCommand.ExecuteAsync(null);

        sut.HasResult.Should().BeTrue();
        sut.VerdictText.Should().Be("SCRAP");
        sut.IsLineHalted.Should().BeFalse();
        sut.Stage1LatencyMs.Should().BeApproximately(86.3474, 0.01);
        sut.RoundTripMs.Should().BeApproximately(112.4, 0.01);
        sut.Detections.Should().HaveCount(3);
        sut.Decisions.Should().HaveCount(3);
        sut.Decisions[0].DefectClass.Should().Be("Short");
        sut.Decisions[0].Disposition.Should().Be("SCRAP");
        sut.Decisions[0].Severity.Should().Be("CRITICAL");

        // Layer switching: Test -> Template -> Diff
        sut.ActiveImageLayer.Should().Be("Test");
        sut.ActiveImageDataUri.Should().Be(dto.TestB64);

        sut.SelectLayerCommand.Execute("Template");
        sut.ActiveImageLayer.Should().Be("Template");
        sut.ActiveImageDataUri.Should().Be(dto.TemplateB64);

        sut.SelectLayerCommand.Execute("Diff");
        sut.ActiveImageLayer.Should().Be("Diff");
        sut.ActiveImageDataUri.Should().Be(dto.DiffB64);
    }

    [Fact]
    public async Task InspectCommand_WhileRunning_DisablesInspectAndCancelCancelsInFlightRequest()
    {
        var tcs = new TaskCompletionSource<UploadTriageEnvelope>();
        var apiClient = Substitute.For<IInspectionApiClient>();
        apiClient.UploadTriageAsync(Arg.Any<UploadTriageRequest>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var ct = callInfo.Arg<CancellationToken>();
                ct.Register(() => tcs.TrySetCanceled(ct));
                return tcs.Task;
            });

        var settingsService = Substitute.For<ISettingsService>();
        var sut = new InspectViewModel(apiClient, settingsService)
        {
            TemplateImagePath = _templatePath,
            TestImagePath = _testPath
        };

        Task runTask = sut.InspectCommand.ExecuteAsync(null);

        sut.IsBusy.Should().BeTrue();
        sut.InspectCommand.CanExecute(null).Should().BeFalse();
        sut.CancelInspectCommand.CanExecute(null).Should().BeTrue();

        sut.CancelInspectCommand.Execute(null);
        await runTask;

        sut.IsBusy.Should().BeFalse();
        sut.StatusOrErrorMessage.Should().Contain("Cancelled");
    }

    [Fact]
    public async Task InspectCommand_WhenApiThrowsError_SetsFriendlyErrorWithoutCrashing()
    {
        var apiClient = Substitute.For<IInspectionApiClient>();
        apiClient.UploadTriageAsync(Arg.Any<UploadTriageRequest>(), Arg.Any<CancellationToken>())
            .Returns<UploadTriageEnvelope>(_ => throw new InspectionApiException(
                HttpStatusCode.BadRequest,
                "Failed to decode uploaded image files."));

        var settingsService = Substitute.For<ISettingsService>();
        var sut = new InspectViewModel(apiClient, settingsService)
        {
            TemplateImagePath = _templatePath,
            TestImagePath = _testPath
        };

        await sut.InspectCommand.ExecuteAsync(null);

        sut.IsBusy.Should().BeFalse();
        sut.HasError.Should().BeTrue();
        sut.StatusOrErrorMessage.Should().Contain("Failed to decode uploaded image files.");
    }

    [Theory]
    [InlineData("board.jpg", true)]
    [InlineData("board.JPEG", true)]
    [InlineData("board.png", true)]
    [InlineData("board.bmp", true)]
    [InlineData("board.txt", false)]
    [InlineData("board.pdf", false)]
    public void IsSupportedImageFile_ChecksAllowedExtensionsPerF1(string fileName, bool expected)
    {
        InspectViewModel.IsSupportedImageFile(fileName).Should().Be(expected);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }
}
