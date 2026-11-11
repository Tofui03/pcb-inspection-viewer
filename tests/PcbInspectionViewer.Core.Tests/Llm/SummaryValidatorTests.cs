using PcbInspectionViewer.Core.Llm;
using Xunit;

namespace PcbInspectionViewer.Core.Tests.Llm;

public sealed class SummaryValidatorTests
{
    private static InspectionFacts CreateSampleFacts(
        int total = 10,
        int pass = 8,
        int rework = 1,
        int scrap = 1,
        double yield = 80.0,
        bool halted = false)
    {
        return new InspectionFacts(
            BatchId: "BATCH-TEST",
            IpcClass: "Class 2",
            StartTimeUtc: DateTime.UtcNow.AddHours(-1),
            EndTimeUtc: DateTime.UtcNow,
            TotalInspected: total,
            PassCount: pass,
            ReworkCount: rework,
            ScrapCount: scrap,
            YieldPercentage: yield,
            LineHalted: halted,
            DefectCountsByClass: new Dictionary<string, int>
            {
                ["short"] = 2,
                ["mousebite"] = 1
            },
            GridCentroidDistribution: new Dictionary<string, int>
            {
                ["C"] = 2,
                ["TL"] = 1
            },
            TopCorrectiveActions: new List<string>
            {
                "Inspect etching bath temperature"
            });
    }

    [Fact]
    public void Validate_AcceptsAccurateJsonWithCodeFences()
    {
        var facts = CreateSampleFacts();
        string rawJson = @"```json
{
  ""headline"": ""Completed batch with 8 pass and 2 defectives"",
  ""yield_statement"": ""Batch yielded 80.0% with 8 passed out of 10 boards"",
  ""defect_summary"": ""2 short defects and 1 mousebite observed"",
  ""engineering_advice"": ""Inspect etching bath temperature""
}
```";

        SummaryValidationResult result = SummaryValidator.Validate(rawJson, facts);

        Assert.True(result.IsValid);
        Assert.NotNull(result.ValidatedSummary);
        Assert.Empty(result.Errors);
        Assert.Equal("Completed batch with 8 pass and 2 defectives", result.ValidatedSummary.Headline);
    }

    [Fact]
    public void Validate_RejectsVerdictContradiction_WhenScrapExistsButClaimsAllPassed()
    {
        var facts = CreateSampleFacts(rework: 1, scrap: 1); // Not 100% pass
        string rawJson = @"{
  ""headline"": ""All boards passed with zero issues"",
  ""yield_statement"": ""100% pass rate achieved on 10 boards"",
  ""defect_summary"": ""None"",
  ""engineering_advice"": ""Continue""
}";

        SummaryValidationResult result = SummaryValidator.Validate(rawJson, facts);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Contradiction: Claims all boards passed"));
    }

    [Fact]
    public void Validate_RejectsHallucinatedNumbersInHeadlineOrYield()
    {
        var facts = CreateSampleFacts(total: 10, pass: 8, rework: 1, scrap: 1, yield: 80.0);
        string rawJson = @"{
  ""headline"": ""Inspected 99 boards"",
  ""yield_statement"": ""Yield was 80.0% across all items"",
  ""defect_summary"": ""Normal"",
  ""engineering_advice"": ""Check line""
}";

        SummaryValidationResult result = SummaryValidator.Validate(rawJson, facts);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Numeric consistency error") && e.Contains("99"));
    }

    [Fact]
    public void Validate_RejectsMissingLineHaltNoticeWhenHalted()
    {
        var facts = CreateSampleFacts(halted: true);
        string rawJson = @"{
  ""headline"": ""Inspection batch finished with standard checks"",
  ""yield_statement"": ""Batch yielded 80.0% on 10 boards"",
  ""defect_summary"": ""2 shorts"",
  ""engineering_advice"": ""Check etching""
}";

        SummaryValidationResult result = SummaryValidator.Validate(rawJson, facts);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Production line was halted"));
    }

    [Fact]
    public void TemplateSummaryProvider_GeneratesConsistentFallbackInBothLanguages()
    {
        var facts = CreateSampleFacts(halted: true);

        InspectionSummary en = TemplateSummaryProvider.GenerateDeterministicSummary(facts, "en");
        Assert.Contains("halted", en.Headline, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("80.0%", en.YieldStatement);
        Assert.False(en.IsAiGenerated);

        InspectionSummary zh = TemplateSummaryProvider.GenerateDeterministicSummary(facts, "zh");
        Assert.Contains("暂停", zh.Headline);
        Assert.Contains("80.0%", zh.YieldStatement);
        Assert.Contains("short: 2处", zh.DefectSummary);
        Assert.False(zh.IsAiGenerated);
    }
}
