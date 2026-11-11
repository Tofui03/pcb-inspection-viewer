using System.Text.Json;

namespace PcbInspectionViewer.Core.Llm;

public sealed class SummaryCoordinator : ISummaryCoordinator
{
    private readonly ILlmProvider _provider;

    public SummaryCoordinator(ILlmProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public async Task<InspectionSummary> GenerateSummaryAsync(InspectionFacts facts, string language = "en", CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(facts);

        if (_provider is NullLlmProvider || _provider.ProviderName == "None")
        {
            return TemplateSummaryProvider.GenerateDeterministicSummary(facts, language);
        }

        string initialPrompt = BuildPrompt(facts, language);
        string currentPrompt = initialPrompt;

        // Up to 3 technical retries with backoff
        for (int technicalAttempt = 1; technicalAttempt <= 3; technicalAttempt++)
        {
            try
            {
                // Up to 2 semantic re-prompts if validation fails
                for (int semanticAttempt = 1; semanticAttempt <= 2; semanticAttempt++)
                {
                    string rawResponse = await _provider.GenerateTextAsync(currentPrompt, ct).ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(rawResponse))
                    {
                        break; // fallback
                    }

                    SummaryValidationResult validation = SummaryValidator.Validate(rawResponse, facts);
                    if (validation.IsValid && validation.ValidatedSummary is not null)
                    {
                        return validation.ValidatedSummary with { IsAiGenerated = true };
                    }

                    // Semantic re-prompt with explicit error description
                    string joinedErrors = string.Join("; ", validation.Errors);
                    currentPrompt = $"{initialPrompt}\n\nIMPORTANT: Your previous output had validation errors: {joinedErrors}.\nPlease correct the JSON output strictly according to the facts without contradictions or hallucinated numbers.";
                }

                // If semantic attempts exhausted, fallback to deterministic template
                return TemplateSummaryProvider.GenerateDeterministicSummary(facts, language);
            }
            catch (Exception) when (technicalAttempt < 3)
            {
                // Technical retry (e.g. 5xx, timeout, 429) with exponential backoff
                await Task.Delay(TimeSpan.FromMilliseconds(500 * Math.Pow(2, technicalAttempt - 1)), ct).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Retries exhausted, fallback cleanly
                break;
            }
        }

        return TemplateSummaryProvider.GenerateDeterministicSummary(facts, language);
    }

    private static string BuildPrompt(InspectionFacts facts, string language)
    {
        string factsJson = JsonSerializer.Serialize(new
        {
            batch_id = facts.BatchId,
            ipc_class = facts.IpcClass,
            total_inspected = facts.TotalInspected,
            pass_count = facts.PassCount,
            rework_count = facts.ReworkCount,
            scrap_count = facts.ScrapCount,
            yield_percentage = facts.YieldPercentage,
            line_halted = facts.LineHalted,
            defect_counts_by_class = facts.DefectCountsByClass,
            centroid_distribution_3x3 = facts.GridCentroidDistribution,
            top_corrective_actions = facts.TopCorrectiveActions
        }, new JsonSerializerOptions { WriteIndented = true });

        return $@"You are a precision quality engineer assistant for an automated PCB optical inspection system (AOI).
Produce a concise, factual JSON summary based exclusively on the provided inspection facts.

STRICT CONSTRAINTS:
1. Return ONLY a valid JSON object matching the schema below. No markdown fences, no extra text outside the JSON.
2. Every number in ""headline"" and ""yield_statement"" MUST be strictly grounded in the input facts. Do not invent, round, or extrapolate any numbers.
3. If rework_count > 0 or scrap_count > 0, NEVER claim that all boards passed or that yield was 100%.
4. If line_halted is true, the headline MUST explicitly mention that the production line was halted.
5. Language: {(language.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? "Simplified Chinese" : "English")}.

JSON SCHEMA:
{{
  ""headline"": ""High-level 1-line batch assessment"",
  ""yield_statement"": ""Clear statement quoting total, pass, rework, scrap, and yield percentage"",
  ""defect_summary"": ""Description of defect classes and their spatial distribution across boards"",
  ""engineering_advice"": ""Actionable corrective guidance referencing top actions""
}}

FACTS:
{factsJson}";
    }
}
