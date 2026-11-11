using System.Text.Json;
using System.Text.Json.Serialization;

namespace PcbInspectionViewer.Core.Api.Dtos;

public sealed record BoundingBoxDetectionDto
{
    [JsonPropertyName("x1")]
    public int X1 { get; init; }

    [JsonPropertyName("y1")]
    public int Y1 { get; init; }

    [JsonPropertyName("x2")]
    public int X2 { get; init; }

    [JsonPropertyName("y2")]
    public int Y2 { get; init; }

    [JsonPropertyName("defect_class")]
    public string DefectClass { get; init; } = string.Empty;

    [JsonPropertyName("confidence")]
    public double Confidence { get; init; }

    [JsonPropertyName("area_px")]
    public int? AreaPx { get; init; }

    [JsonPropertyName("relative_area_pct")]
    public double? RelativeAreaPct { get; init; }

    public int ComputedAreaPx => AreaPx ?? Math.Max(0, (X2 - X1) * (Y2 - Y1));
}

public sealed record InspectionDecisionDto
{
    [JsonPropertyName("detection_index")]
    public int? DetectionIndex { get; init; }

    [JsonPropertyName("defect_class")]
    public string? DefectClass { get; init; }

    [JsonPropertyName("disposition")]
    public string Disposition { get; init; } = "PASS";

    [JsonPropertyName("severity")]
    public string Severity { get; init; } = "LOW";

    [JsonPropertyName("ipc_standard_clause")]
    public string IpcStandardClause { get; init; } = string.Empty;

    [JsonPropertyName("electrical_failure_risk")]
    public string ElectricalFailureRisk { get; init; } = string.Empty;

    [JsonPropertyName("root_cause_hypothesis")]
    public string RootCauseHypothesis { get; init; } = string.Empty;

    [JsonPropertyName("recommended_corrective_action")]
    public string RecommendedCorrectiveAction { get; init; } = string.Empty;

    [JsonPropertyName("halt_production_line")]
    public bool HaltProductionLine { get; init; }

    [JsonPropertyName("guardrail_overridden")]
    public bool GuardrailOverridden { get; init; }

    [JsonPropertyName("override_reason")]
    public string? OverrideReason { get; init; }
}

public sealed record BoardInspectionReportDto
{
    [JsonPropertyName("stage1_latency_ms")]
    public double Stage1LatencyMs { get; init; }

    [JsonPropertyName("detections")]
    public IReadOnlyList<BoundingBoxDetectionDto> Detections { get; init; } = [];

    [JsonPropertyName("fast_path_pass")]
    public bool FastPathPass { get; init; }

    [JsonPropertyName("decisions")]
    public IReadOnlyList<InspectionDecisionDto> Decisions { get; init; } = [];
}

public sealed record BatchTrendResultDto
{
    [JsonPropertyName("batch_id")]
    public string BatchId { get; init; } = string.Empty;

    [JsonPropertyName("defect_class")]
    public string DefectClass { get; init; } = string.Empty;

    [JsonPropertyName("occurrence_count")]
    public int OccurrenceCount { get; init; }

    [JsonPropertyName("threshold_exceeded")]
    public bool ThresholdExceeded { get; init; }
}

public sealed record UploadTriageResponseDto
{
    [JsonPropertyName("sample_id")]
    public string SampleId { get; init; } = string.Empty;

    [JsonPropertyName("batch_id")]
    public string BatchId { get; init; } = string.Empty;

    [JsonPropertyName("ipc_class")]
    public string IpcClass { get; init; } = "Class 2";

    [JsonPropertyName("clean_board_mode")]
    public bool CleanBoardMode { get; init; }

    [JsonPropertyName("simulate_rogue_vlm")]
    public bool SimulateRogueVlm { get; init; }

    [JsonPropertyName("colorize_pcb")]
    public bool ColorizePcb { get; init; }

    [JsonPropertyName("board_disposition")]
    public string BoardDisposition { get; init; } = "PASS";

    [JsonPropertyName("line_halted")]
    public bool LineHalted { get; init; }

    [JsonPropertyName("gt_defect_count")]
    public int GtDefectCount { get; init; }

    [JsonPropertyName("report")]
    public BoardInspectionReportDto Report { get; init; } = new();

    [JsonPropertyName("trace")]
    public JsonElement? Trace { get; init; }

    [JsonPropertyName("batch_summary")]
    public IReadOnlyList<BatchTrendResultDto> BatchSummary { get; init; } = [];

    [JsonPropertyName("template_b64")]
    public string? TemplateB64 { get; init; }

    [JsonPropertyName("test_b64")]
    public string? TestB64 { get; init; }

    [JsonPropertyName("diff_b64")]
    public string? DiffB64 { get; init; }
}

public sealed record UploadTriageRequest(
    byte[] TemplateBytes,
    string TemplateFileName,
    byte[] TestBytes,
    string TestFileName,
    string BatchId = "BATCH-UPLOAD",
    string IpcClass = "Class 2",
    bool SimulateRogueVlm = false,
    bool ColorizePcb = false,
    bool ReturnImages = true,
    string? BaseUrlOverride = null);

public sealed record UploadTriageEnvelope(
    UploadTriageResponseDto Response,
    double RoundTripMs);
