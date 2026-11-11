using System.Text;
using PcbInspectionViewer.Core.Data.Entities;
using PcbInspectionViewer.Core.Export;
using PcbInspectionViewer.Core.Services;
using Xunit;

namespace PcbInspectionViewer.Core.Tests.Export;

public sealed class ExportServiceTests
{
    [Fact]
    public async Task ExportInspectionsCsvAsync_EmitsUtf8BomAndCorrectColumns()
    {
        var exporter = new ExportService();
        using var ms = new MemoryStream();

        var inspections = new List<InspectionEntity>
        {
            new InspectionEntity
            {
                Id = 101,
                InspectedAtUtc = new DateTime(2026, 11, 10, 14, 30, 0, DateTimeKind.Utc),
                FileName = "board_测试_01.jpg",
                BatchId = "BATCH-CHINESE-批次",
                BoardDisposition = "PASS",
                IpcClass = "Class 2",
                LineHalted = false,
                DefectCount = 0,
                DefectClassesSummary = "",
                Stage1LatencyMs = 12.5,
                RoundTripMs = 35.0
            }
        };

        await exporter.ExportInspectionsCsvAsync(inspections, ms);

        byte[] bytes = ms.ToArray();
        // UTF-8 BOM is 0xEF, 0xBB, 0xBF
        Assert.True(bytes.Length >= 3);
        Assert.Equal(0xEF, bytes[0]);
        Assert.Equal(0xBB, bytes[1]);
        Assert.Equal(0xBF, bytes[2]);

        string text = Encoding.UTF8.GetString(bytes);
        Assert.Contains("Id,InspectedAtLocal,FileName,BatchId,BoardDisposition", text);
        Assert.Contains("board_测试_01.jpg", text);
        Assert.Contains("BATCH-CHINESE-批次", text);
        Assert.Contains("PASS", text);
    }

    [Fact]
    public async Task ExportDefectsCsvAsync_EmitsDefectCoordinatesAndIpcClause()
    {
        var exporter = new ExportService();
        using var ms = new MemoryStream();

        var inspection = new InspectionEntity
        {
            Id = 202,
            InspectedAtUtc = DateTime.UtcNow,
            FileName = "board02.jpg",
            BatchId = "BATCH-02",
            BoardDisposition = "REWORK",
            Defects = new List<DefectEntity>
            {
                new DefectEntity
                {
                    DetectionIndex = 1,
                    DefectClass = "short",
                    Confidence = 0.9421,
                    X1 = 12.0,
                    Y1 = 34.0,
                    X2 = 56.0,
                    Y2 = 78.0,
                    AreaPx = 1936.0,
                    Severity = "MAJOR",
                    Disposition = "REWORK",
                    IpcClause = "IPC-A-600 3.1"
                }
            }
        };

        await exporter.ExportDefectsCsvAsync(new[] { inspection }, ms);

        string text = Encoding.UTF8.GetString(ms.ToArray());
        Assert.Contains("InspectionId,InspectedAtLocal,FileName,BatchId,BoardDisposition,DetectionIndex,DefectClass", text);
        Assert.Contains("short", text);
        Assert.Contains("0.9421", text);
        Assert.Contains("IPC-A-600 3.1", text);
    }

    [Fact]
    public async Task GeneratePdfReportAsync_ProducesValidPdfBytes()
    {
        var exporter = new ExportService();
        using var ms = new MemoryStream();

        var reportData = new PdfReportData(
            ReportTitle: "Daily Quality Inspection Report",
            BatchOrDateScope: "BATCH-TEST-20261110",
            GeneratedAtLocal: DateTime.Now,
            TotalInspected: 10,
            PassCount: 8,
            ReworkCount: 2,
            ScrapCount: 0,
            YieldPercentage: 80.0,
            LineHalted: false,
            Inspections: new List<InspectionEntity>
            {
                new InspectionEntity
                {
                    Id = 1,
                    FileName = "board1.jpg",
                    BoardDisposition = "PASS",
                    DefectCount = 0,
                    DefectClassesSummary = "",
                    InspectedAtUtc = DateTime.UtcNow
                }
            },
            TopDefects: new List<DefectClassDistribution>
            {
                new DefectClassDistribution("short", 2, 100.0)
            },
            LlmSummaryHeadline: "High yield observed across batch test.",
            LlmSummaryBody: "2 minor reworkable shorts were detected in circuit traces.",
            IsAiGenerated: true
        );

        await exporter.GeneratePdfReportAsync(reportData, ms);

        byte[] pdfBytes = ms.ToArray();
        Assert.True(pdfBytes.Length > 1000);
        // Valid PDF file starts with %PDF-
        string header = Encoding.ASCII.GetString(pdfBytes, 0, 5);
        Assert.Equal("%PDF-", header);
    }
}
