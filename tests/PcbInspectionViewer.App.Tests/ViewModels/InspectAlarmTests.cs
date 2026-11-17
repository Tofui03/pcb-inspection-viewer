using System.Text.Json;
using FluentAssertions;
using NSubstitute;
using PcbInspectionViewer.Core.Api;
using PcbInspectionViewer.Core.Api.Dtos;
using PcbInspectionViewer.Core.Configuration;
using PcbInspectionViewer.Core.Sql;
using PcbInspectionViewer.Core.ViewModels;
using Xunit;

namespace PcbInspectionViewer.App.Tests.ViewModels;

public sealed class InspectAlarmTests
{
    [Fact]
    public void Decide_OfflineWinsOverLineHaltedAndFallback()
    {
        AlarmDecision decision = InspectAlarm.Decide(
            isBackendOnline: false,
            hasError: false,
            statusOrErrorMessage: null,
            askStatusMessage: null,
            isLineHalted: true,
            hasResult: true,
            usedTemplateFallback: true);

        decision.Kind.Should().Be(AlarmKind.Offline);
        decision.Text.Should().Be(InspectAlarm.OfflineText);
        decision.IsAmber.Should().BeFalse();
    }

    [Fact]
    public void Decide_InspectErrorWinsOverSqlGuard()
    {
        AlarmDecision decision = InspectAlarm.Decide(
            isBackendOnline: true,
            hasError: true,
            statusOrErrorMessage: "Inspection API error (400): template and test images differ in size.",
            askStatusMessage: "Execution rejected by SqlGuard: no",
            isLineHalted: false,
            hasResult: false,
            usedTemplateFallback: false);

        decision.Kind.Should().Be(AlarmKind.InspectError);
        decision.Text.Should().Contain("differ in size");
        decision.IsAmber.Should().BeFalse();
    }

    [Fact]
    public void Decide_SqlGuardThenLineHaltedThenAmberFallbackThenNone()
    {
        InspectAlarm.Decide(true, false, null, "Execution rejected by SqlGuard: Access to table or view 'Inspections' is not allowed.", false, false, false)
            .Kind.Should().Be(AlarmKind.SqlGuard);

        AlarmDecision halted = InspectAlarm.Decide(true, false, null, "Ready.", true, true, false);
        halted.Kind.Should().Be(AlarmKind.LineHalted);
        halted.Text.Should().Be("LINE HALTED");
        halted.IsAmber.Should().BeFalse();

        AlarmDecision fallback = InspectAlarm.Decide(true, false, null, null, false, true, true);
        fallback.Kind.Should().Be(AlarmKind.Fallback);
        fallback.Text.Should().Be(InspectAlarm.FallbackText);
        fallback.IsAmber.Should().BeTrue();

        InspectAlarm.Decide(true, false, null, "Ready.", false, true, false).Kind.Should().Be(AlarmKind.None);
        InspectAlarm.Decide(true, false, null, null, true, false, true).Kind.Should().Be(AlarmKind.None);
    }

    [Theory]
    [InlineData(null, true, false)]
    [InlineData("None", true, false)]
    [InlineData("none", true, false)]
    [InlineData("Gemini", false, false)]
    [InlineData("Gemini", true, true)]
    [InlineData("OpenAI", true, true)]
    public void ShouldShowTemplateFallback_OnlyWhenAConfiguredProviderFailed(string? provider, bool traceFallback, bool expected)
    {
        InspectAlarm.ShouldShowTemplateFallback(provider, traceFallback).Should().Be(expected);
    }

    [Fact]
    public void ShortIpcClause_KeepsTheSectionNumber()
    {
        InspectAlarm.ShortIpcClause("IPC-A-600 Section 2.2.2 (Short / Bridging)")
            .Should().Be("IPC-A-600 2.2.2");
        InspectAlarm.ShortIpcClause("IPC-A-600 Section 2.2.1 (Open / Break)")
            .Should().Be("IPC-A-600 2.2.1");
    }

    [Fact]
    public void ShowRecordedResult_WhenProviderIsNone_KeepsDispositionAndHidesFallback()
    {
        UploadTriageResponseDto dto = LoadRealFixtureDto();
        dto.Trace.Should().NotBeNull();
        InspectAlarm.TraceUsedFallback(dto.Trace).Should().BeTrue();

        var sut = NewInspect();
        sut.IsBackendOffline = false;
        sut.ShowRecordedResult(new UploadTriageEnvelope(dto, 0), dto.BatchId, dto.IpcClass);

        sut.VerdictText.Should().Be(dto.BoardDisposition.ToUpperInvariant());
        sut.UsedTemplateFallback.Should().BeFalse();
        sut.PrimaryActionLabel.Should().Be("Next");
        sut.SelectedDefect.Should().BeNull();
    }

    [Fact]
    public void ShowRecordedResult_WhenConfiguredProviderFellBack_ShowsAmberWithoutChangingVerdict()
    {
        UploadTriageResponseDto dto = LoadRealFixtureDto();
        var sut = NewInspect();
        sut.UseLlmProvider("Gemini");
        sut.IsBackendOffline = false;
        sut.ShowRecordedResult(new UploadTriageEnvelope(dto, 0), dto.BatchId, dto.IpcClass);

        sut.VerdictText.Should().Be("SCRAP");
        sut.UsedTemplateFallback.Should().BeTrue();
        sut.SelectedDefectIndex.Should().BeNull();
    }

    [Fact]
    public void ShowMismatchedPair_KeepsTheErrorAndClearsTheDisposition()
    {
        UploadTriageResponseDto dto = LoadRealFixtureDto();
        var sut = NewInspect();
        sut.IsBackendOffline = false;
        sut.ShowMismatchedPair(dto, "Inspection API error (400): template and test images differ in size.");

        sut.HasError.Should().BeTrue();
        sut.HasResult.Should().BeFalse();
        sut.VerdictText.Should().BeEmpty();
        sut.Detections.Should().BeEmpty();
        sut.StatusOrErrorMessage.Should().Contain("differ in size");
        sut.PrimaryActionLabel.Should().Be("Inspect");
        sut.ShowWorkspace.Should().BeTrue();
    }

    [Fact]
    public void ClearResult_DropsTheDispositionAndReturnsToLoadImages()
    {
        UploadTriageResponseDto dto = LoadRealFixtureDto();
        var sut = NewInspect();
        sut.IsBackendOffline = false;
        sut.ShowRecordedResult(new UploadTriageEnvelope(dto, 0));
        sut.ClearResultCommand.Execute(null);

        sut.HasResult.Should().BeFalse();
        sut.VerdictText.Should().BeEmpty();
        sut.UsedTemplateFallback.Should().BeFalse();
        sut.PrimaryActionLabel.Should().Be("Load images");
    }

    [Fact]
    public void SelectDefect_TogglesTheDetailAndExposesTheIpcClause()
    {
        UploadTriageResponseDto dto = LoadRealFixtureDto();
        var sut = NewInspect();
        sut.ShowRecordedResult(new UploadTriageEnvelope(dto, 0));

        sut.SelectDefectCommand.Execute(0);
        sut.ShowDefectDetail.Should().BeTrue();
        sut.SelectedDefect!.Decision!.IpcStandardClause.Should().Be("IPC-A-600 Section 2.2.2 (Short / Bridging)");
        sut.SelectedDefectText.Should().Contain("IPC-A-600 Section 2.2.2 (Short / Bridging)");

        sut.SelectDefectCommand.Execute(0);
        sut.SelectedDefect.Should().BeNull();
        sut.ShowDefectDetail.Should().BeFalse();
    }

    [Fact]
    public void ShellAlarm_FollowsOfflineSqlGuardAndConfiguredFallback()
    {
        var settingsService = Substitute.For<ISettingsService>();
        settingsService.DatabaseFilePath.Returns("/tmp/inspections.db");
        var health = Substitute.For<IBackendHealthMonitor>();
        var settings = new SettingsViewModel(settingsService, health);
        var inspect = NewInspect();
        var ask = new AskViewModel(Substitute.For<INlToSqlService>(), Substitute.For<ISqlGuard>());
        using var shell = new MainShellViewModel(settingsService, health, settings, inspect, askViewModel: ask);

        shell.HasAlarm.Should().BeTrue();
        shell.AlarmText.Should().Be(InspectAlarm.OfflineText);

        UploadTriageResponseDto dto = LoadRealFixtureDto();
        shell.IsBackendOnline = true;
        inspect.ShowRecordedResult(new UploadTriageEnvelope(dto, 0));
        shell.HasAlarm.Should().BeFalse();

        inspect.IsLineHalted = true;
        shell.AlarmText.Should().Be("LINE HALTED");
        shell.AlarmIsAmber.Should().BeFalse();

        shell.IsBackendOnline = false;
        shell.AlarmText.Should().Be(InspectAlarm.OfflineText);

        shell.IsBackendOnline = true;
        inspect.IsLineHalted = false;
        inspect.UseLlmProvider("Gemini");
        inspect.ShowRecordedResult(new UploadTriageEnvelope(dto, 0));
        shell.HasAlarm.Should().BeTrue();
        shell.AlarmIsAmber.Should().BeTrue();
        shell.AlarmText.Should().Be(InspectAlarm.FallbackText);
        inspect.VerdictText.Should().Be("SCRAP");

        ask.StatusMessage = "Execution rejected by SqlGuard: Access to table or view 'Inspections' is not allowed. Only allowlisted views (v_inspections, v_defects, v_batches) may be queried.";
        inspect.HasResult = false;
        shell.AlarmIsAmber.Should().BeFalse();
        shell.AlarmText.Should().StartWith("Execution rejected by SqlGuard");
    }

    private static InspectViewModel NewInspect()
    {
        var api = Substitute.For<IInspectionApiClient>();
        var settings = Substitute.For<ISettingsService>();
        return new InspectViewModel(api, settings);
    }

    private static UploadTriageResponseDto LoadRealFixtureDto()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            string candidate = Path.Combine(dir, "tests", "fixtures", "upload_triage_sample.json");
            if (File.Exists(candidate))
            {
                return JsonSerializer.Deserialize<UploadTriageResponseDto>(File.ReadAllText(candidate))!;
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new FileNotFoundException("Missing upload_triage_sample.json fixture.");
    }
}
