using System.Text.Json;

namespace PcbInspectionViewer.Core.ViewModels;

public enum AlarmKind
{
    None,
    Offline,
    InspectError,
    SqlGuard,
    LineHalted,
    Fallback
}

public readonly record struct AlarmDecision(AlarmKind Kind, string Text, bool IsAmber)
{
    public static AlarmDecision None { get; } = new(AlarmKind.None, string.Empty, false);
}

public static class InspectAlarm
{
    public const string OfflineText = "Backend offline. The request failed after 3 attempts.";
    public const string FallbackText = "Template fallback.";
    public const string LineHaltedText = "LINE HALTED";
    public const string SqlGuardPrefix = "Execution rejected by SqlGuard";

    public static AlarmDecision Decide(
        bool isBackendOnline,
        bool hasError,
        string? statusOrErrorMessage,
        string? askStatusMessage,
        bool isLineHalted,
        bool hasResult,
        bool usedTemplateFallback)
    {
        if (!isBackendOnline)
        {
            return new AlarmDecision(AlarmKind.Offline, OfflineText, false);
        }

        if (hasError && !string.IsNullOrWhiteSpace(statusOrErrorMessage))
        {
            return new AlarmDecision(AlarmKind.InspectError, statusOrErrorMessage, false);
        }

        if (!string.IsNullOrWhiteSpace(askStatusMessage) &&
            askStatusMessage.StartsWith(SqlGuardPrefix, StringComparison.Ordinal))
        {
            return new AlarmDecision(AlarmKind.SqlGuard, askStatusMessage, false);
        }

        if (isLineHalted && hasResult)
        {
            return new AlarmDecision(AlarmKind.LineHalted, LineHaltedText, false);
        }

        if (usedTemplateFallback && hasResult)
        {
            return new AlarmDecision(AlarmKind.Fallback, FallbackText, true);
        }

        return AlarmDecision.None;
    }

    public static bool ShouldShowTemplateFallback(string? llmProvider, bool traceUsedFallback)
    {
        if (!traceUsedFallback || string.IsNullOrWhiteSpace(llmProvider))
        {
            return false;
        }

        return !string.Equals(llmProvider, "None", StringComparison.OrdinalIgnoreCase);
    }

    public static bool TraceUsedFallback(JsonElement? trace)
    {
        if (trace is not { ValueKind: JsonValueKind.Array } array)
        {
            return false;
        }

        foreach (JsonElement item in array.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object &&
                item.TryGetProperty("used_fallback", out JsonElement flag) &&
                flag.ValueKind == JsonValueKind.True)
            {
                return true;
            }
        }

        return false;
    }

    public static string ShortIpcClause(string? clause)
    {
        if (string.IsNullOrWhiteSpace(clause))
        {
            return string.Empty;
        }

        const string marker = "IPC-A-600 Section ";
        int start = clause.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return clause;
        }

        int numberStart = start + marker.Length;
        int numberEnd = numberStart;
        while (numberEnd < clause.Length && (char.IsDigit(clause[numberEnd]) || clause[numberEnd] == '.'))
        {
            numberEnd++;
        }

        if (numberEnd == numberStart)
        {
            return clause;
        }

        return "IPC-A-600 " + clause[numberStart..numberEnd];
    }
}
