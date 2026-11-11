using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PcbInspectionViewer.Core.Llm;

public static class SummaryValidator
{
    private static readonly Regex NumberRegex = new(@"\b\d+(?:\.\d+)?%?\b", RegexOptions.Compiled);
    private static readonly Regex CodeFenceRegex = new(@"```(?:json)?\s*([\s\S]*?)\s*```", RegexOptions.Compiled);

    public static SummaryValidationResult Validate(string rawResponse, InspectionFacts facts)
    {
        ArgumentNullException.ThrowIfNull(rawResponse);
        ArgumentNullException.ThrowIfNull(facts);

        var errors = new List<string>();

        // 1. Strip markdown code fences if present
        string cleaned = rawResponse.Trim();
        Match fenceMatch = CodeFenceRegex.Match(cleaned);
        if (fenceMatch.Success)
        {
            cleaned = fenceMatch.Groups[1].Value.Trim();
        }

        // 2. Parse JSON
        InspectionSummary? summary;
        try
        {
            summary = JsonSerializer.Deserialize<InspectionSummary>(cleaned, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (summary is null ||
                string.IsNullOrWhiteSpace(summary.Headline) ||
                string.IsNullOrWhiteSpace(summary.YieldStatement))
            {
                errors.Add("Summary JSON is missing required fields (headline, yield_statement).");
                return new SummaryValidationResult(false, errors, null);
            }
        }
        catch (JsonException ex)
        {
            errors.Add($"Malformed JSON payload: {ex.Message}");
            return new SummaryValidationResult(false, errors, null);
        }

        // 3. Verdict Contradiction Check
        // If SCRAP > 0 or REWORK > 0, headline/yield cannot say "all passed" or "100% pass"
        string combinedText = $"{summary.Headline} {summary.YieldStatement}".ToLowerInvariant();
        if (facts.ScrapCount > 0 || facts.ReworkCount > 0)
        {
            if (combinedText.Contains("all boards passed") ||
                combinedText.Contains("all passed") ||
                combinedText.Contains("100% pass") ||
                combinedText.Contains("零缺陷") ||
                combinedText.Contains("全部合格"))
            {
                errors.Add($"Contradiction: Claims all boards passed, but facts show {facts.ReworkCount} rework and {facts.ScrapCount} scrap.");
            }
        }

        if (facts.LineHalted && !combinedText.Contains("halt") && !combinedText.Contains("stop") && !combinedText.Contains("停线") && !combinedText.Contains("暂停"))
        {
            errors.Add("Contradiction: Production line was halted according to facts, but headline/yield statement failed to mention line halt.");
        }

        // 4. Numeric Consistency Check
        // Check that any numbers in headline and yield_statement map to an input fact
        // Permitted numbers:
        // TotalInspected, PassCount, ReworkCount, ScrapCount, YieldPercentage (with +/- 0.1 tolerance), Defect counts
        var allowedNumbers = new HashSet<double>
        {
            facts.TotalInspected,
            facts.PassCount,
            facts.ReworkCount,
            facts.ScrapCount
        };

        foreach (int count in facts.DefectCountsByClass.Values)
        {
            allowedNumbers.Add(count);
        }

        // Extract numbers from headline and yield_statement
        string numbersToCheck = $"{summary.Headline} {summary.YieldStatement}";
        MatchCollection matches = NumberRegex.Matches(numbersToCheck);

        foreach (Match m in matches)
        {
            string token = m.Value;
            bool isPercentage = token.EndsWith('%');
            string bare = isPercentage ? token.TrimEnd('%') : token;

            if (!double.TryParse(bare, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedVal))
            {
                continue;
            }

            // Check if matches yield percentage (+/- 0.1)
            if (Math.Abs(parsedVal - facts.YieldPercentage) <= 0.15)
            {
                continue;
            }

            // Check integer facts
            bool foundFact = false;
            foreach (double allowed in allowedNumbers)
            {
                if (Math.Abs(parsedVal - allowed) < 0.001)
                {
                    foundFact = true;
                    break;
                }
            }

            if (!foundFact)
            {
                errors.Add($"Numeric consistency error: Number '{token}' in headline/yield does not match any input fact (total: {facts.TotalInspected}, pass: {facts.PassCount}, rework: {facts.ReworkCount}, scrap: {facts.ScrapCount}, yield: {facts.YieldPercentage}%).");
            }
        }

        bool isValid = errors.Count == 0;
        return new SummaryValidationResult(isValid, errors, isValid ? summary : null);
    }
}
