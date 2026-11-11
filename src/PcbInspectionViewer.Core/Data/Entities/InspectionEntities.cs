using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PcbInspectionViewer.Core.Data.Entities;

[Table("Batches")]
public sealed class BatchEntity
{
    [Key]
    [MaxLength(128)]
    public string BatchId { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public int TotalBoards { get; set; }

    public int PassCount { get; set; }

    public int ReworkCount { get; set; }

    public int ScrapCount { get; set; }

    public bool LineHalted { get; set; }

    public List<InspectionEntity> Inspections { get; set; } = new();
}

[Table("Inspections")]
public sealed class InspectionEntity
{
    [Key]
    public long Id { get; set; }

    [MaxLength(128)]
    public string BatchId { get; set; } = string.Empty;

    public BatchEntity? Batch { get; set; }

    [MaxLength(260)]
    public string FileName { get; set; } = string.Empty;

    public string? TemplateImagePath { get; set; }

    public string? TestImagePath { get; set; }

    public string? CachedTemplateImagePath { get; set; }

    public string? CachedTestImagePath { get; set; }

    public string? CachedDiffImagePath { get; set; }

    [MaxLength(32)]
    public string BoardDisposition { get; set; } = "UNKNOWN";

    [MaxLength(32)]
    public string IpcClass { get; set; } = "Class 2";

    public bool LineHalted { get; set; }

    public int DefectCount { get; set; }

    [MaxLength(512)]
    public string DefectClassesSummary { get; set; } = string.Empty;

    public double Stage1LatencyMs { get; set; }

    public double RoundTripMs { get; set; }

    public DateTime InspectedAtUtc { get; set; } = DateTime.UtcNow;

    public List<DefectEntity> Defects { get; set; } = new();

    public List<DecisionEntity> Decisions { get; set; } = new();

    public LlmSummaryEntity? LlmSummary { get; set; }
}

[Table("Defects")]
public sealed class DefectEntity
{
    [Key]
    public long Id { get; set; }

    public long InspectionId { get; set; }

    public InspectionEntity? Inspection { get; set; }

    public int DetectionIndex { get; set; }

    [MaxLength(64)]
    public string DefectClass { get; set; } = string.Empty;

    public double Confidence { get; set; }

    public double X1 { get; set; }

    public double Y1 { get; set; }

    public double X2 { get; set; }

    public double Y2 { get; set; }

    public double AreaPx { get; set; }

    [MaxLength(32)]
    public string? Severity { get; set; }

    [MaxLength(32)]
    public string? Disposition { get; set; }

    [MaxLength(256)]
    public string? IpcClause { get; set; }
}

[Table("Decisions")]
public sealed class DecisionEntity
{
    [Key]
    public long Id { get; set; }

    public long InspectionId { get; set; }

    public InspectionEntity? Inspection { get; set; }

    public int? DetectionIndex { get; set; }

    [MaxLength(64)]
    public string DefectClass { get; set; } = string.Empty;

    [MaxLength(32)]
    public string Severity { get; set; } = string.Empty;

    [MaxLength(32)]
    public string Disposition { get; set; } = string.Empty;

    [MaxLength(256)]
    public string IpcStandardClause { get; set; } = string.Empty;

    [MaxLength(512)]
    public string ElectricalFailureRisk { get; set; } = string.Empty;

    [MaxLength(512)]
    public string RootCauseHypothesis { get; set; } = string.Empty;

    [MaxLength(512)]
    public string RecommendedCorrectiveAction { get; set; } = string.Empty;

    public bool GuardrailOverridden { get; set; }

    public string? OverrideReason { get; set; }
}

[Table("LlmSummaries")]
public sealed class LlmSummaryEntity
{
    [Key]
    public long Id { get; set; }

    public long? InspectionId { get; set; }

    public InspectionEntity? Inspection { get; set; }

    [MaxLength(128)]
    public string? BatchId { get; set; }

    [MaxLength(64)]
    public string Provider { get; set; } = "None";

    [MaxLength(64)]
    public string ModelName { get; set; } = "none";

    public string Headline { get; set; } = string.Empty;

    public string YieldStatement { get; set; } = string.Empty;

    public string DefectSummary { get; set; } = string.Empty;

    public string EngineeringAdvice { get; set; } = string.Empty;

    public string RawJson { get; set; } = string.Empty;

    public bool IsAiGenerated { get; set; }

    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
}

[Table("QueryLogs")]
public sealed class QueryLogEntity
{
    [Key]
    public long Id { get; set; }

    public string UserPrompt { get; set; } = string.Empty;

    public string GeneratedSql { get; set; } = string.Empty;

    public bool IsSuccess { get; set; }

    public string? ErrorMessage { get; set; }

    public double ExecutionLatencyMs { get; set; }

    public DateTime QueriedAtUtc { get; set; } = DateTime.UtcNow;
}

[Table("KbChunks")]
public sealed class KbChunkEntity
{
    [Key]
    public long Id { get; set; }

    [MaxLength(128)]
    public string DocumentSource { get; set; } = string.Empty;

    [MaxLength(128)]
    public string SectionTitle { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public DateTime IndexedAtUtc { get; set; } = DateTime.UtcNow;
}
