using System.Globalization;
using PcbInspectionViewer.Core.Api.Dtos;

namespace PcbInspectionViewer.Core.Geometry;

public sealed record OverlayBoxProjection(
    double Left,
    double Top,
    double Width,
    double Height,
    string DefectClass,
    double Confidence,
    string LabelText,
    string ColorHex);

public sealed record AlignedDefectDecision(
    int Index,
    BoundingBoxDetectionDto Detection,
    InspectionDecisionDto? Decision);

public static class OverlayGeometryCalculator
{
    public const double BoardDimensionPx = 640.0;

    public static OverlayBoxProjection ProjectBox(
        BoundingBoxDetectionDto box,
        double viewportWidth,
        double viewportHeight,
        double zoomFactor = 1.0,
        double panX = 0.0,
        double panY = 0.0)
    {
        ArgumentNullException.ThrowIfNull(box);

        double safeViewportWidth = Math.Max(1.0, viewportWidth);
        double safeViewportHeight = Math.Max(1.0, viewportHeight);
        double safeZoom = Math.Clamp(zoomFactor, 0.25, 16.0);

        double uniformScale = Math.Min(
            safeViewportWidth / BoardDimensionPx,
            safeViewportHeight / BoardDimensionPx);

        double displayedBoardWidth = BoardDimensionPx * uniformScale;
        double displayedBoardHeight = BoardDimensionPx * uniformScale;

        double letterboxOffsetX = (safeViewportWidth - displayedBoardWidth) / 2.0;
        double letterboxOffsetY = (safeViewportHeight - displayedBoardHeight) / 2.0;

        double effectiveScale = uniformScale * safeZoom;

        double left = (letterboxOffsetX * safeZoom) + (box.X1 * effectiveScale) + panX;
        double top = (letterboxOffsetY * safeZoom) + (box.Y1 * effectiveScale) + panY;
        double width = Math.Max(0.0, (box.X2 - box.X1) * effectiveScale);
        double height = Math.Max(0.0, (box.Y2 - box.Y1) * effectiveScale);

        string label = string.Create(
            CultureInfo.InvariantCulture,
            $"{box.DefectClass} {box.Confidence:0.00}");

        return new OverlayBoxProjection(
            Left: left,
            Top: top,
            Width: width,
            Height: height,
            DefectClass: box.DefectClass,
            Confidence: box.Confidence,
            LabelText: label,
            ColorHex: GetClassColorHex(box.DefectClass));
    }

    public static string GetClassColorHex(string? defectClass) =>
        defectClass switch
        {
            "Short" => "#EF4444",           // Crimson red
            "Open" => "#F97316",            // Orange
            "Mousebite" => "#EAB308",       // Amber yellow
            "Spur" => "#A855F7",            // Purple
            "Spurious Copper" => "#06B6D4", // Cyan
            "Pin-hole" => "#EC4899",        // Pink
            _ => "#3B82F6"                  // Default blue
        };

    public static IReadOnlyList<AlignedDefectDecision> PairDetectionsWithDecisions(
        IReadOnlyList<BoundingBoxDetectionDto> detections,
        IReadOnlyList<InspectionDecisionDto> decisions)
    {
        ArgumentNullException.ThrowIfNull(detections);
        ArgumentNullException.ThrowIfNull(decisions);

        if (detections.Count == 0)
        {
            return [];
        }

        var explicitIndexMap = new Dictionary<int, InspectionDecisionDto>();
        foreach (InspectionDecisionDto decision in decisions)
        {
            if (decision.DetectionIndex is int idx && idx >= 0 && idx < detections.Count)
            {
                explicitIndexMap[idx] = decision;
            }
        }

        bool canJoinByOrdinalPosition =
            explicitIndexMap.Count == 0 &&
            decisions.Count == detections.Count &&
            decisions.All(d => d.DetectionIndex is null);

        var result = new List<AlignedDefectDecision>(detections.Count);
        for (int i = 0; i < detections.Count; i++)
        {
            InspectionDecisionDto? matched = null;
            if (explicitIndexMap.TryGetValue(i, out InspectionDecisionDto? byKey))
            {
                matched = byKey;
            }
            else if (canJoinByOrdinalPosition)
            {
                matched = decisions[i];
            }

            result.Add(new AlignedDefectDecision(i, detections[i], matched));
        }

        return result;
    }
}
