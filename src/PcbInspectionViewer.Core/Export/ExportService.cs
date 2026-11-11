using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using PcbInspectionViewer.Core.Data.Entities;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PcbInspectionViewer.Core.Export;

public sealed class ExportService : IExportService
{
    static ExportService()
    {
        // QuestPDF Community License Setup
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public async Task ExportInspectionsCsvAsync(IEnumerable<InspectionEntity> inspections, Stream destinationStream, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(inspections);
        ArgumentNullException.ThrowIfNull(destinationStream);

        // UTF-8 with BOM for Excel compatibility (supports English + Chinese characters)
        var utf8WithBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        await using var writer = new StreamWriter(destinationStream, utf8WithBom, bufferSize: 4096, leaveOpen: true);
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true
        };
        await using var csv = new CsvWriter(writer, config);

        var records = inspections.Select(i => new InspectionCsvRecord(
            Id: i.Id,
            InspectedAtLocal: i.InspectedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
            FileName: i.FileName,
            BatchId: i.BatchId,
            BoardDisposition: i.BoardDisposition,
            IpcClass: i.IpcClass,
            LineHalted: i.LineHalted,
            DefectCount: i.DefectCount,
            DefectClassesSummary: i.DefectClassesSummary,
            Stage1LatencyMs: i.Stage1LatencyMs,
            RoundTripMs: i.RoundTripMs
        ));

        await csv.WriteRecordsAsync(records, ct).ConfigureAwait(false);
        await writer.FlushAsync(ct).ConfigureAwait(false);
    }

    public async Task ExportDefectsCsvAsync(IEnumerable<InspectionEntity> inspectionsWithDefects, Stream destinationStream, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(inspectionsWithDefects);
        ArgumentNullException.ThrowIfNull(destinationStream);

        var utf8WithBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        await using var writer = new StreamWriter(destinationStream, utf8WithBom, bufferSize: 4096, leaveOpen: true);
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true
        };
        await using var csv = new CsvWriter(writer, config);

        var defectRecords = new List<DefectCsvRecord>();
        foreach (InspectionEntity insp in inspectionsWithDefects)
        {
            string timeStr = insp.InspectedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            if (insp.Defects is not null)
            {
                foreach (DefectEntity d in insp.Defects)
                {
                    defectRecords.Add(new DefectCsvRecord(
                        InspectionId: insp.Id,
                        InspectedAtLocal: timeStr,
                        FileName: insp.FileName,
                        BatchId: insp.BatchId,
                        BoardDisposition: insp.BoardDisposition,
                        DetectionIndex: d.DetectionIndex,
                        DefectClass: d.DefectClass,
                        Confidence: Math.Round(d.Confidence, 4),
                        X1: Math.Round(d.X1, 1),
                        Y1: Math.Round(d.Y1, 1),
                        X2: Math.Round(d.X2, 1),
                        Y2: Math.Round(d.Y2, 1),
                        AreaPx: Math.Round(d.AreaPx, 1),
                        Severity: d.Severity,
                        Disposition: d.Disposition,
                        IpcClause: d.IpcClause
                    ));
                }
            }
        }

        await csv.WriteRecordsAsync(defectRecords, ct).ConfigureAwait(false);
        await writer.FlushAsync(ct).ConfigureAwait(false);
    }

    public Task GeneratePdfReportAsync(PdfReportData reportData, Stream destinationStream, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(reportData);
        ArgumentNullException.ThrowIfNull(destinationStream);

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(1.5f, Unit.Centimetre);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Segoe UI", "Arial", "sans-serif"));

                // Header
                page.Header().Column(col =>
                {
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Column(titleCol =>
                        {
                            titleCol.Item().Text(reportData.ReportTitle).FontSize(18).Bold().FontColor(Colors.BlueGrey.Darken3);
                            titleCol.Item().Text($"Scope: {reportData.BatchOrDateScope}").FontSize(10).FontColor(Colors.Grey.Darken1);
                        });

                        row.ConstantItem(180).AlignRight().Column(metaCol =>
                        {
                            metaCol.Item().Text($"Generated: {reportData.GeneratedAtLocal:yyyy-MM-dd HH:mm:ss}").FontSize(9).FontColor(Colors.Grey.Darken1);
                            metaCol.Item().Text("DeepPCB-AOI System").FontSize(9).Bold().FontColor(Colors.BlueGrey.Medium);
                        });
                    });

                    col.Item().PaddingVertical(8).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
                });

                // Content
                page.Content().Column(col =>
                {
                    // 1. KPI Cards
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(8).Column(c =>
                        {
                            c.Item().Text("TOTAL INSPECTED").FontSize(8).Bold().FontColor(Colors.Grey.Darken1);
                            c.Item().Text(reportData.TotalInspected.ToString()).FontSize(16).Bold().FontColor(Colors.BlueGrey.Darken4);
                        });
                        row.Spacing(8);

                        row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(8).Column(c =>
                        {
                            c.Item().Text("PASS COUNT").FontSize(8).Bold().FontColor(Colors.Green.Darken2);
                            c.Item().Text(reportData.PassCount.ToString()).FontSize(16).Bold().FontColor(Colors.Green.Darken2);
                        });
                        row.Spacing(8);

                        row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(8).Column(c =>
                        {
                            c.Item().Text("REWORK").FontSize(8).Bold().FontColor(Colors.Amber.Darken3);
                            c.Item().Text(reportData.ReworkCount.ToString()).FontSize(16).Bold().FontColor(Colors.Amber.Darken3);
                        });
                        row.Spacing(8);

                        row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(8).Column(c =>
                        {
                            c.Item().Text("SCRAP").FontSize(8).Bold().FontColor(Colors.Red.Darken2);
                            c.Item().Text(reportData.ScrapCount.ToString()).FontSize(16).Bold().FontColor(Colors.Red.Darken2);
                        });
                        row.Spacing(8);

                        row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(8).Column(c =>
                        {
                            c.Item().Text("YIELD %").FontSize(8).Bold().FontColor(Colors.Blue.Darken2);
                            c.Item().Text($"{reportData.YieldPercentage:F1}%").FontSize(16).Bold().FontColor(Colors.Blue.Darken2);
                        });
                    });

                    // Line Halted Alert if triggered
                    if (reportData.LineHalted)
                    {
                        col.Item().PaddingTop(10).Background(Colors.Red.Lighten4).Padding(8).Row(r =>
                        {
                            r.RelativeItem().Text("⚠️ LINE HALT EVENT DETECTED: Quality guardrail triggered stopping the batch run.")
                                .Bold().FontColor(Colors.Red.Darken3);
                        });
                    }

                    // 2. LLM / Executive Summary section if present
                    if (!string.IsNullOrWhiteSpace(reportData.LlmSummaryHeadline))
                    {
                        col.Item().PaddingTop(12).Border(1).BorderColor(Colors.Blue.Lighten3).Background(Colors.Blue.Lighten5).Padding(10).Column(sCol =>
                        {
                            sCol.Item().Row(r =>
                            {
                                r.RelativeItem().Text(reportData.LlmSummaryHeadline).Bold().FontSize(11).FontColor(Colors.Blue.Darken3);
                                if (reportData.IsAiGenerated)
                                {
                                    r.ConstantItem(120).AlignRight().Text("AI-generated explanation; verdicts are rule-based")
                                        .FontSize(7).Italic().FontColor(Colors.Grey.Darken2);
                                }
                            });

                            if (!string.IsNullOrWhiteSpace(reportData.LlmSummaryBody))
                            {
                                sCol.Item().PaddingTop(4).Text(reportData.LlmSummaryBody).FontSize(9).FontColor(Colors.Grey.Darken3);
                            }
                        });
                    }

                    // 3. Top Defect Classes Table
                    if (reportData.TopDefects is not null && reportData.TopDefects.Count > 0)
                    {
                        col.Item().PaddingTop(12).Text("Top Defect Classes").Bold().FontSize(11);
                        col.Item().PaddingTop(4).Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(3);
                                columns.RelativeColumn(1);
                                columns.RelativeColumn(1);
                            });

                            table.Header(h =>
                            {
                                h.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Defect Class").Bold();
                                h.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Occurrences").Bold().AlignRight();
                                h.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Share %").Bold().AlignRight();
                            });

                            foreach (var item in reportData.TopDefects)
                            {
                                table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(4).Text(item.DefectClass);
                                table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(4).Text(item.Count.ToString()).AlignRight();
                                table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(4).Text($"{item.Percentage:F1}%").AlignRight();
                            }
                        });
                    }

                    // 4. Inspection Table Sample
                    col.Item().PaddingTop(14).Text($"Inspections Log ({reportData.Inspections.Count} boards)").Bold().FontSize(11);
                    col.Item().PaddingTop(4).Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(40);
                            columns.RelativeColumn(2);
                            columns.RelativeColumn(2);
                            columns.ConstantColumn(60);
                            columns.ConstantColumn(50);
                            columns.RelativeColumn(3);
                        });

                        table.Header(h =>
                        {
                            h.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("ID").Bold();
                            h.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Time").Bold();
                            h.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("File").Bold();
                            h.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Verdict").Bold().AlignCenter();
                            h.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Defects").Bold().AlignCenter();
                            h.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Defect Types").Bold();
                        });

                        foreach (InspectionEntity item in reportData.Inspections.Take(30))
                        {
                            string verdictColor = item.BoardDisposition switch
                            {
                                "PASS" => Colors.Green.Darken2,
                                "REWORK" => Colors.Amber.Darken3,
                                "SCRAP" => Colors.Red.Darken2,
                                _ => Colors.Grey.Darken2
                            };

                            table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(3).Text(item.Id.ToString()).FontSize(8);
                            table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(3).Text(item.InspectedAtUtc.ToLocalTime().ToString("MM-dd HH:mm")).FontSize(8);
                            table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(3).Text(item.FileName).FontSize(8);
                            table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(3).Text(item.BoardDisposition).Bold().FontColor(verdictColor).FontSize(8).AlignCenter();
                            table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(3).Text(item.DefectCount.ToString()).FontSize(8).AlignCenter();
                            table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(3).Text(item.DefectClassesSummary).FontSize(8);
                        }
                    });
                });

                // Footer
                page.Footer().Row(row =>
                {
                    row.RelativeItem().Text("DeepPCB-AOI QA Automated Report · Confidential").FontSize(8).FontColor(Colors.Grey.Darken1);
                    row.ConstantItem(80).AlignRight().Text(x =>
                    {
                        x.CurrentPageNumber();
                        x.Span(" / ");
                        x.TotalPages();
                    });
                });
            });
        }).GeneratePdf(destinationStream);

        return Task.CompletedTask;
    }
}
