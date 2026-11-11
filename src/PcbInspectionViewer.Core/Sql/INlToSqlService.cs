using System.Text.RegularExpressions;
using PcbInspectionViewer.Core.Llm;

namespace PcbInspectionViewer.Core.Sql;

public sealed record NlToSqlResult(
    bool IsSuccess,
    string? GeneratedSql,
    string? Explanation,
    string? ErrorMessage);

public interface INlToSqlService
{
    Task<NlToSqlResult> TranslateQuestionToSqlAsync(string userQuestion, CancellationToken ct = default);
}

public sealed class NlToSqlService : INlToSqlService
{
    private readonly ILlmProvider _llmProvider;
    private static readonly Regex SqlBlockRegex = new(@"```(?:sql)?\s*([\s\S]*?)\s*```", RegexOptions.Compiled);

    public NlToSqlService(ILlmProvider llmProvider)
    {
        _llmProvider = llmProvider ?? throw new ArgumentNullException(nameof(llmProvider));
    }

    public async Task<NlToSqlResult> TranslateQuestionToSqlAsync(string userQuestion, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userQuestion))
        {
            return new NlToSqlResult(false, null, null, "Question cannot be empty.");
        }

        if (_llmProvider is NullLlmProvider || _llmProvider.ProviderName == "None")
        {
            // Deterministic rules / heuristics for common offline questions
            string lower = userQuestion.ToLowerInvariant();
            if (lower.Contains("yield") || lower.Contains("良率") || lower.Contains("pass rate"))
            {
                return new NlToSqlResult(
                    true,
                    "SELECT BatchId, TotalBoards, PassCount, ROUND(CAST(PassCount AS FLOAT) / TotalBoards * 100, 2) AS YieldPct FROM v_batches ORDER BY CreatedAtUtc DESC LIMIT 10",
                    "Calculates batch yield from v_batches.",
                    null);
            }
            if (lower.Contains("defect") || lower.Contains("top") || lower.Contains("缺陷") || lower.Contains("最多"))
            {
                return new NlToSqlResult(
                    true,
                    "SELECT DefectClass, COUNT(*) AS TotalOccurrences FROM v_defects GROUP BY DefectClass ORDER BY TotalOccurrences DESC",
                    "Groups and ranks defects by occurrence count from v_defects.",
                    null);
            }
            if (lower.Contains("halt") || lower.Contains("停线") || lower.Contains("pause"))
            {
                return new NlToSqlResult(
                    true,
                    "SELECT BatchId, CreatedAtUtc, TotalBoards, ScrapCount FROM v_batches WHERE LineHalted = 1 ORDER BY CreatedAtUtc DESC",
                    "Finds all batches where the production line was halted.",
                    null);
            }
            if (lower.Contains("scrap") || lower.Contains("报废"))
            {
                return new NlToSqlResult(
                    true,
                    "SELECT Id, FileName, BatchId, DefectCount, DefectClassesSummary FROM v_inspections WHERE BoardDisposition = 'SCRAP' ORDER BY InspectedAtUtc DESC LIMIT 50",
                    "Selects scrap inspections from v_inspections.",
                    null);
            }

            return new NlToSqlResult(
                true,
                "SELECT Id, FileName, BatchId, BoardDisposition, DefectCount FROM v_inspections ORDER BY InspectedAtUtc DESC LIMIT 20",
                "Default recent inspections query from v_inspections.",
                null);
        }

        string prompt = $@"You are a precision SQL generator for an industrial PCB Inspection SQLite database.
Convert the following natural language user question into a safe, valid SQLite read-only query.

DATABASE SCHEMA:
- v_inspections(Id, BatchId, FileName, BoardDisposition, IpcClass, LineHalted, DefectCount, DefectClassesSummary, Stage1LatencyMs, RoundTripMs, InspectedAtUtc)
- v_defects(Id, InspectionId, DetectionIndex, DefectClass, Confidence, X1, Y1, X2, Y2, AreaPx, Severity, Disposition, IpcClause, BoardDisposition, BatchId, InspectedAtUtc)
- v_batches(BatchId, CreatedAtUtc, TotalBoards, PassCount, ReworkCount, ScrapCount, LineHalted)

CRITICAL RULES:
1. ONLY query from the allowed views: v_inspections, v_defects, v_batches. NEVER query underlying tables.
2. Return ONLY a single SELECT query. Do NOT use comments (-- or /* */), PRAGMA, ATTACH, or data modification statements.
3. Wrap your SQL in a ```sql markdown code block.
4. Below the code block, provide a brief 1-sentence plain text explanation of what the query calculates.

USER QUESTION:
{userQuestion}";

        try
        {
            string response = await _llmProvider.GenerateTextAsync(prompt, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(response))
            {
                return new NlToSqlResult(false, null, null, "LLM returned empty response.");
            }

            Match match = SqlBlockRegex.Match(response);
            string sql;
            string explanation = string.Empty;

            if (match.Success)
            {
                sql = match.Groups[1].Value.Trim();
                explanation = response[(match.Index + match.Length)..].Trim();
            }
            else
            {
                sql = response.Trim();
            }

            return new NlToSqlResult(true, sql, string.IsNullOrWhiteSpace(explanation) ? "Query generated by AI." : explanation, null);
        }
        catch (Exception ex)
        {
            return new NlToSqlResult(false, null, null, ex.Message);
        }
    }
}
