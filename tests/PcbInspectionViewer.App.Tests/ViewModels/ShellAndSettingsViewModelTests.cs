using FluentAssertions;
using NSubstitute;
using PcbInspectionViewer.Core.Api;
using PcbInspectionViewer.Core.Configuration;
using PcbInspectionViewer.Core.ViewModels;
using Xunit;

namespace PcbInspectionViewer.App.Tests.ViewModels;

public sealed class ShellAndSettingsViewModelTests
{
    [Fact]
    public async Task InitializeAsync_WhenBackendIsOnline_UpdatesStatusPillToOnlineAndPopulatesMetadata()
    {
        var settingsService = Substitute.For<ISettingsService>();
        settingsService.DatabaseFilePath.Returns(@"C:\AppData\Local\PcbInspectionViewer\inspections.db");
        settingsService.LoadAsync(Arg.Any<CancellationToken>()).Returns(new StationSettings
        {
            Api = new ApiOptions { BaseUrl = "http://localhost:8000", TimeoutSeconds = 10 },
            Batch = new BatchOptions { Concurrency = 2, DefaultIpcClass = "Class 2", DefaultBatchId = "BATCH-01" },
            Llm = new LlmOptions { Provider = "Gemini", ModelName = "gemini-2.5-flash", SummaryLanguage = "en+zh" }
        });

        var healthMonitor = Substitute.For<IBackendHealthMonitor>();
        healthMonitor.CheckHealthAsync("http://localhost:8000", Arg.Any<CancellationToken>())
            .Returns(new BackendHealthStatus(
                IsOnline: true,
                Version: "1.2.0",
                DetectorBackend: "yolov8",
                ExecutionProvider: "CPUExecutionProvider",
                DatasetLoaded: false,
                ErrorMessage: null));

        var settingsVm = new SettingsViewModel(settingsService, healthMonitor);
        using var shellVm = new MainShellViewModel(settingsService, healthMonitor, settingsVm);

        await shellVm.InitializeAsync();

        shellVm.IsBackendOnline.Should().BeTrue();
        shellVm.BackendStatusText.Should().Be("Backend online");
        shellVm.DetectorAndVersionText.Should().Be("yolov8 · v1.2.0");
        shellVm.DatabasePathText.Should().Be(@"C:\AppData\Local\PcbInspectionViewer\inspections.db");
        shellVm.LlmProviderStatusText.Should().Contain("Gemini");
    }

    [Fact]
    public async Task RefreshHealthAsync_WhenBackendIsOffline_UpdatesStatusPillToRedOffline()
    {
        var settingsService = Substitute.For<ISettingsService>();
        settingsService.DatabaseFilePath.Returns("/tmp/inspections.db");
        settingsService.LoadAsync(Arg.Any<CancellationToken>()).Returns(new StationSettings());

        var healthMonitor = Substitute.For<IBackendHealthMonitor>();
        healthMonitor.CheckHealthAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(BackendHealthStatus.Offline("Connection refused"));

        var settingsVm = new SettingsViewModel(settingsService, healthMonitor);
        using var shellVm = new MainShellViewModel(settingsService, healthMonitor, settingsVm);

        await shellVm.InitializeAsync();

        shellVm.IsBackendOnline.Should().BeFalse();
        shellVm.BackendStatusText.Should().Be("Backend offline");
        shellVm.BackendStatusTooltip.Should().Contain("Connection refused");
    }

    [Fact]
    public async Task SaveSettingsCommand_WhenValidWithNonLocalHttp_PersistsAndShowsHttpsWarning()
    {
        var settingsService = Substitute.For<ISettingsService>();
        settingsService.Validate(Arg.Any<StationSettings>())
            .Returns(new SettingsValidationResult(true, [], RequiresHttpsWarning: true));
        settingsService.SaveAsync(Arg.Any<StationSettings>(), Arg.Any<CancellationToken>())
            .Returns(new SettingsValidationResult(true, [], RequiresHttpsWarning: true));

        var healthMonitor = Substitute.For<IBackendHealthMonitor>();
        healthMonitor.CheckHealthAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new BackendHealthStatus(true, "1.2.0", "yolov8", "CPU", false, null));

        var sut = new SettingsViewModel(settingsService, healthMonitor)
        {
            BaseUrl = "http://192.168.10.25:8000",
            TimeoutSeconds = 15,
            Concurrency = 4,
            DefaultIpcClass = "Class 3",
            DefaultBatchId = "BATCH-LINE-02",
            ColorizePcb = true,
            LlmProvider = "OpenAI",
            ModelName = "gpt-4o-mini",
            SummaryLanguage = "zh"
        };

        await sut.SaveSettingsCommand.ExecuteAsync(null);

        await settingsService.Received(1).SaveAsync(
            Arg.Is<StationSettings>(s =>
                s.Api.BaseUrl == "http://192.168.10.25:8000" &&
                s.Batch.Concurrency == 4 &&
                s.Batch.DefaultIpcClass == "Class 3" &&
                s.Llm.Provider == "OpenAI"),
            Arg.Any<CancellationToken>());
        sut.HasHttpsWarning.Should().BeTrue();
        sut.StatusMessage.Should().Contain("Saved");
    }

    [Fact]
    public async Task SaveSettingsCommand_WhenConcurrencyInvalid_DoesNotPersistAndSetsValidationError()
    {
        var settingsService = Substitute.For<ISettingsService>();
        settingsService.SaveAsync(Arg.Any<StationSettings>(), Arg.Any<CancellationToken>())
            .Returns(new SettingsValidationResult(false, ["Batch Concurrency must be between 1 and 8."], false));

        var healthMonitor = Substitute.For<IBackendHealthMonitor>();
        var sut = new SettingsViewModel(settingsService, healthMonitor)
        {
            Concurrency = 99
        };

        await sut.SaveSettingsCommand.ExecuteAsync(null);

        sut.HasValidationErrors.Should().BeTrue();
        sut.StatusMessage.Should().Contain("Batch Concurrency must be between 1 and 8.");
    }
}
