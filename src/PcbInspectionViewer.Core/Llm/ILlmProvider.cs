using System.Text.Json.Serialization;

namespace PcbInspectionViewer.Core.Llm;

public sealed record InspectionFacts(
    string BatchId,
    string IpcClass,
    DateTime StartTimeUtc,
    DateTime EndTimeUtc,
    int TotalInspected,
    int PassCount,
    int ReworkCount,
    int ScrapCount,
    double YieldPercentage,
    bool LineHalted,
    IReadOnlyDictionary<string, int> DefectCountsByClass,
    IReadOnlyDictionary<string, int> GridCentroidDistribution, // e.g. "TL": 2, "C": 5, "BR": 1 (3x3 grid)
    IReadOnlyList<string> TopCorrectiveActions);

public sealed record InspectionSummary(
    [property: JsonPropertyName("headline")] string Headline,
    [property: JsonPropertyName("yield_statement")] string YieldStatement,
    [property: JsonPropertyName("defect_summary")] string DefectSummary,
    [property: JsonPropertyName("engineering_advice")] string EngineeringAdvice,
    [property: JsonPropertyName("is_ai_generated")] bool IsAiGenerated = true);

public sealed record SummaryValidationResult(
    bool IsValid,
    IReadOnlyList<string> Errors,
    InspectionSummary? ValidatedSummary);

public interface ILlmProvider
{
    string ProviderName { get; }
    Task<string> GenerateTextAsync(string prompt, CancellationToken ct = default);
}

public interface ISummaryCoordinator
{
    Task<InspectionSummary> GenerateSummaryAsync(InspectionFacts facts, string language = "en", CancellationToken ct = default);
}
