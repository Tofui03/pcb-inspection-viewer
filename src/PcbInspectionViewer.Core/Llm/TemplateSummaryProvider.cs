using System.Text.Json;

namespace PcbInspectionViewer.Core.Llm;

public sealed class TemplateSummaryProvider
{
    public static InspectionSummary GenerateDeterministicSummary(InspectionFacts facts, string language = "en")
    {
        ArgumentNullException.ThrowIfNull(facts);

        bool isZh = language.StartsWith("zh", StringComparison.OrdinalIgnoreCase);

        string headline;
        string yieldStatement;
        string defectSummary;
        string engineeringAdvice;

        if (isZh)
        {
            if (facts.LineHalted)
            {
                headline = $"批次质量警报：产线已暂停 (废品 {facts.ScrapCount} / 返工 {facts.ReworkCount})";
            }
            else if (facts.ScrapCount == 0 && facts.ReworkCount == 0)
            {
                headline = $"批次检测完成：共检测 {facts.TotalInspected} 块电路板，全部合格";
            }
            else
            {
                headline = $"批次检测完成：检测 {facts.TotalInspected} 块电路板，发现 {facts.ReworkCount} 块返工，{facts.ScrapCount} 块报废";
            }

            yieldStatement = $"最终良率为 {facts.YieldPercentage:F1}%。合格数: {facts.PassCount}，返工数: {facts.ReworkCount}，报废数: {facts.ScrapCount}。";

            if (facts.DefectCountsByClass.Count > 0)
            {
                var defectStrings = facts.DefectCountsByClass
                    .OrderByDescending(kv => kv.Value)
                    .Select(kv => $"{kv.Key}: {kv.Value}处");
                defectSummary = $"检测到主要缺陷分布：{string.Join("，", defectStrings)}。";
            }
            else
            {
                defectSummary = "未检出明显表面外观缺陷。";
            }

            if (facts.TopCorrectiveActions.Count > 0)
            {
                engineeringAdvice = $"建议工程处理措施：{string.Join("；", facts.TopCorrectiveActions.Take(2))}。";
            }
            else
            {
                engineeringAdvice = "保持当前工艺参数与回流焊工位监控。";
            }
        }
        else
        {
            if (facts.LineHalted)
            {
                headline = $"CRITICAL ALERT: Inspection line halted ({facts.ScrapCount} scrap, {facts.ReworkCount} rework)";
            }
            else if (facts.ScrapCount == 0 && facts.ReworkCount == 0)
            {
                headline = $"Batch run completed: {facts.TotalInspected} boards inspected with 0 defects";
            }
            else
            {
                headline = $"Batch run completed: {facts.TotalInspected} inspected with {facts.ReworkCount} rework and {facts.ScrapCount} scrap";
            }

            yieldStatement = $"Overall batch yield stands at {facts.YieldPercentage:F1}%. Pass: {facts.PassCount}, Rework: {facts.ReworkCount}, Scrap: {facts.ScrapCount}.";

            if (facts.DefectCountsByClass.Count > 0)
            {
                var defectStrings = facts.DefectCountsByClass
                    .OrderByDescending(kv => kv.Value)
                    .Select(kv => $"{kv.Key} ({kv.Value})");
                defectSummary = $"Observed defect distribution: {string.Join(", ", defectStrings)}.";
            }
            else
            {
                defectSummary = "No anomalous defects proposing corrective actions.";
            }

            if (facts.TopCorrectiveActions.Count > 0)
            {
                engineeringAdvice = $"Recommended actions: {string.Join("; ", facts.TopCorrectiveActions.Take(2))}.";
            }
            else
            {
                engineeringAdvice = "Maintain current line calibration and standard operating parameters.";
            }
        }

        return new InspectionSummary(
            Headline: headline,
            YieldStatement: yieldStatement,
            DefectSummary: defectSummary,
            EngineeringAdvice: engineeringAdvice,
            IsAiGenerated: false);
    }
}
