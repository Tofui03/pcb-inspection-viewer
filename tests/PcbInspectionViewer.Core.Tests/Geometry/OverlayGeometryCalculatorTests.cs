using FluentAssertions;
using PcbInspectionViewer.Core.Api.Dtos;
using PcbInspectionViewer.Core.Geometry;
using Xunit;

namespace PcbInspectionViewer.Core.Tests.Geometry;

public sealed class OverlayGeometryCalculatorTests
{
    [Fact]
    public void ProjectBox_WhenViewportIsWide1000x800_AppliesLetterboxOffsetAndScaleAccurately()
    {
        var box = new BoundingBoxDetectionDto
        {
            X1 = 160,
            Y1 = 320,
            X2 = 320,
            Y2 = 480,
            DefectClass = "Short",
            Confidence = 0.9164
        };

        // 640x640 uniform scale inside 1000x800 -> scale = min(1000/640, 800/640) = 1.25
        // Displayed board size = 800x800, offsetX = (1000 - 800) / 2 = 100, offsetY = 0
        OverlayBoxProjection proj = OverlayGeometryCalculator.ProjectBox(
            box,
            viewportWidth: 1000,
            viewportHeight: 800,
            zoomFactor: 1.0,
            panX: 0,
            panY: 0);

        proj.Left.Should().BeApproximately(100 + (160 * 1.25), 0.001);
        proj.Top.Should().BeApproximately(0 + (320 * 1.25), 0.001);
        proj.Width.Should().BeApproximately((320 - 160) * 1.25, 0.001);
        proj.Height.Should().BeApproximately((480 - 320) * 1.25, 0.001);
        proj.LabelText.Should().Be("Short 0.92");
        proj.ColorHex.Should().Be("#EF4444");
    }

    [Fact]
    public void ProjectBox_WithZoomAndPan_ScalesAndTranslatesCoordinates()
    {
        var box = new BoundingBoxDetectionDto
        {
            X1 = 100,
            Y1 = 50,
            X2 = 200,
            Y2 = 150,
            DefectClass = "Open",
            Confidence = 0.8241
        };

        // 640x640 viewport -> base scale 1.0, zoom 2.0, pan (+15, -10)
        OverlayBoxProjection proj = OverlayGeometryCalculator.ProjectBox(
            box,
            viewportWidth: 640,
            viewportHeight: 640,
            zoomFactor: 2.0,
            panX: 15,
            panY: -10);

        proj.Left.Should().BeApproximately((100 * 2.0) + 15, 0.001);
        proj.Top.Should().BeApproximately((50 * 2.0) - 10, 0.001);
        proj.Width.Should().BeApproximately(100 * 2.0, 0.001);
        proj.Height.Should().BeApproximately(100 * 2.0, 0.001);
        proj.LabelText.Should().Be("Open 0.82");
    }

    [Theory]
    [InlineData("Short", "#EF4444")]
    [InlineData("Open", "#F97316")]
    [InlineData("Mousebite", "#EAB308")]
    [InlineData("Spur", "#A855F7")]
    [InlineData("Spurious Copper", "#06B6D4")]
    [InlineData("Pin-hole", "#EC4899")]
    public void GetClassColorHex_ForAllSixDeepPcbDefectClasses_ReturnsDistinctPaletteColors(
        string defectClass,
        string expectedHex)
    {
        OverlayGeometryCalculator.GetClassColorHex(defectClass).Should().Be(expectedHex);
    }

    [Fact]
    public void PairDetectionsWithDecisions_WhenDetectionIndexPresent_PairsByExplicitIndex()
    {
        var detections = new[]
        {
            new BoundingBoxDetectionDto { X1 = 10, Y1 = 10, X2 = 20, Y2 = 20, DefectClass = "Short", Confidence = 0.9 },
            new BoundingBoxDetectionDto { X1 = 30, Y1 = 30, X2 = 50, Y2 = 50, DefectClass = "Mousebite", Confidence = 0.8 }
        };

        var decisions = new[]
        {
            new InspectionDecisionDto { DetectionIndex = 1, Disposition = "REWORK", Severity = "MEDIUM", IpcStandardClause = "Clause B" },
            new InspectionDecisionDto { DetectionIndex = 0, Disposition = "SCRAP", Severity = "CRITICAL", IpcStandardClause = "Clause A" }
        };

        IReadOnlyList<AlignedDefectDecision> aligned = OverlayGeometryCalculator.PairDetectionsWithDecisions(detections, decisions);

        aligned.Should().HaveCount(2);
        aligned[0].Detection.DefectClass.Should().Be("Short");
        aligned[0].Decision!.Disposition.Should().Be("SCRAP");
        aligned[1].Detection.DefectClass.Should().Be("Mousebite");
        aligned[1].Decision!.Disposition.Should().Be("REWORK");
    }

    [Fact]
    public void PairDetectionsWithDecisions_WhenCountMismatchedAndNoExplicitIndex_LeavesDecisionNullPerBc2()
    {
        var detections = new[]
        {
            new BoundingBoxDetectionDto { X1 = 10, Y1 = 10, X2 = 20, Y2 = 20, DefectClass = "Short", Confidence = 0.9 },
            new BoundingBoxDetectionDto { X1 = 30, Y1 = 30, X2 = 50, Y2 = 50, DefectClass = "Mousebite", Confidence = 0.8 }
        };

        var decisions = new[]
        {
            new InspectionDecisionDto { DetectionIndex = null, Disposition = "SCRAP", Severity = "CRITICAL" }
        };

        IReadOnlyList<AlignedDefectDecision> aligned = OverlayGeometryCalculator.PairDetectionsWithDecisions(detections, decisions);

        aligned.Should().HaveCount(2);
        aligned.Should().OnlyContain(item => item.Decision == null);
    }
}
