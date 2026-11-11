using FluentAssertions;
using NSubstitute;
using PcbInspectionViewer.Core.Data;
using PcbInspectionViewer.Core.Data.Entities;
using PcbInspectionViewer.Core.Storage;
using PcbInspectionViewer.Core.ViewModels;
using Xunit;

namespace PcbInspectionViewer.App.Tests.ViewModels;

public sealed class HistoryViewModelTests
{
    [Fact]
    public async Task LoadHistoryCommand_PopulatesItemsAndBatchesFromRepository()
    {
        var repository = Substitute.For<IInspectionRepository>();
        var imageCache = Substitute.For<IImageCache>();

        var sampleEntities = new List<InspectionEntity>
        {
            new()
            {
                Id = 1,
                FileName = "board1.jpg",
                BatchId = "BATCH-01",
                BoardDisposition = "PASS",
                DefectCount = 0,
                DefectClassesSummary = "",
                Stage1LatencyMs = 30.5,
                RoundTripMs = 70.0,
                InspectedAtUtc = DateTime.UtcNow
            },
            new()
            {
                Id = 2,
                FileName = "board2.jpg",
                BatchId = "BATCH-02",
                BoardDisposition = "SCRAP",
                DefectCount = 2,
                DefectClassesSummary = "Short, Open",
                Stage1LatencyMs = 45.0,
                RoundTripMs = 95.0,
                InspectedAtUtc = DateTime.UtcNow
            }
        };

        repository.QueryInspectionsAsync(Arg.Any<InspectionQueryFilter>(), Arg.Any<CancellationToken>())
            .Returns(sampleEntities);
        repository.GetAllBatchIdsAsync(Arg.Any<CancellationToken>())
            .Returns(["BATCH-01", "BATCH-02"]);

        var sut = new HistoryViewModel(repository, imageCache);

        await sut.LoadHistoryCommand.ExecuteAsync(null);

        sut.Items.Should().HaveCount(2);
        sut.TotalLoadedCount.Should().Be(2);
        sut.Items[0].FileName.Should().Be("board1.jpg");
        sut.Items[0].BoardDisposition.Should().Be("PASS");
        sut.Items[1].BoardDisposition.Should().Be("SCRAP");
        sut.AvailableBatches.Should().Contain(["ALL", "BATCH-01", "BATCH-02"]);
    }

    [Fact]
    public async Task DrillDownCommand_RaisesDrillDownRequestedEventWithCompleteDetails()
    {
        var repository = Substitute.For<IInspectionRepository>();
        var imageCache = Substitute.For<IImageCache>();

        var targetEntity = new InspectionEntity
        {
            Id = 42,
            FileName = "drilldown_board.jpg",
            BoardDisposition = "REWORK",
            Defects = [new DefectEntity { DefectClass = "Spur", Confidence = 0.88 }]
        };

        repository.GetInspectionDetailsAsync(42, Arg.Any<CancellationToken>())
            .Returns(targetEntity);

        var sut = new HistoryViewModel(repository, imageCache);
        InspectionEntity? firedEntity = null;
        sut.DrillDownRequested += (_, e) => firedEntity = e;

        var row = new HistoryItemRow(
            Id: 42,
            InspectedAtLocal: DateTime.Now,
            FileName: "drilldown_board.jpg",
            BatchId: "BATCH-1",
            BoardDisposition: "REWORK",
            DefectCount: 1,
            DefectClassesSummary: "Spur",
            Stage1LatencyMs: 35.0,
            RoundTripMs: 60.0);

        await sut.DrillDownCommand.ExecuteAsync(row);

        firedEntity.Should().NotBeNull();
        firedEntity!.Id.Should().Be(42);
        firedEntity.FileName.Should().Be("drilldown_board.jpg");
        firedEntity.BoardDisposition.Should().Be("REWORK");
    }

    [Fact]
    public void ToggleDefectClassFilter_AddsAndRemovesPillFilter()
    {
        var repository = Substitute.For<IInspectionRepository>();
        var imageCache = Substitute.For<IImageCache>();
        var sut = new HistoryViewModel(repository, imageCache);

        sut.ToggleDefectClassFilter("Short");
        sut.SelectedDefectClasses.Should().Contain("Short");

        sut.ToggleDefectClassFilter("Short");
        sut.SelectedDefectClasses.Should().NotContain("Short");
    }
}
