using PcbInspectionViewer.Core.Data.Entities;
using PcbInspectionViewer.Core.Services;

namespace PcbInspectionViewer.Core.Export;

public sealed record InspectionCsvRecord(
    long Id,
    string InspectedAtLocal,
    string FileName,
    string BatchId,
    string BoardDisposition,
    string IpcClass,
    bool LineHalted,
    int DefectCount,
    string DefectClassesSummary,
    double Stage1LatencyMs,
    double RoundTripMs);

public sealed record DefectCsvRecord(
    long InspectionId,
    string InspectedAtLocal,
    string FileName,
    string BatchId,
    string BoardDisposition,
    int DetectionIndex,
    string DefectClass,
    double Confidence,
    double X1,
    double Y1,
    double X2,
    double Y2,
    double AreaPx,
    string? Severity,
    string? Disposition,
    string? IpcClause);

public sealed record PdfReportData(
    string ReportTitle,
    string BatchOrDateScope,
    DateTime GeneratedAtLocal,
    int TotalInspected,
    int PassCount,
    int ReworkCount,
    int ScrapCount,
    double YieldPercentage,
    bool LineHalted,
    IReadOnlyList<InspectionEntity> Inspections,
    IReadOnlyList<DefectClassDistribution> TopDefects,
    string? LlmSummaryHeadline,
    string? LlmSummaryBody,
    bool IsAiGenerated);

public interface IExportService
{
    Task ExportInspectionsCsvAsync(IEnumerable<InspectionEntity> inspections, Stream destinationStream, CancellationToken ct = default);
    Task ExportDefectsCsvAsync(IEnumerable<InspectionEntity> inspectionsWithDefects, Stream destinationStream, CancellationToken ct = default);
    Task GeneratePdfReportAsync(PdfReportData reportData, Stream destinationStream, CancellationToken ct = default);
}
