namespace PcbInspectionViewer.Core.Configuration;

public sealed record ApiOptions
{
    public const string SectionName = "Api";

    public string BaseUrl { get; init; } = "http://localhost:8000";
    public int TimeoutSeconds { get; init; } = 10;
}

public sealed record BatchOptions
{
    public const string SectionName = "Batch";

    public int Concurrency { get; init; } = 2;
    public string DefaultIpcClass { get; init; } = "Class 2";
    public string DefaultBatchId { get; init; } = "BATCH-UPLOAD";
    public bool ColorizePcb { get; init; } = false;
}

public sealed record LlmOptions
{
    public const string SectionName = "Llm";

    public string Provider { get; init; } = "None";
    public string ModelName { get; init; } = "gemini-2.5-flash";
    public string SummaryLanguage { get; init; } = "en";
}

public sealed record StationSettings
{
    public ApiOptions Api { get; init; } = new();
    public BatchOptions Batch { get; init; } = new();
    public LlmOptions Llm { get; init; } = new();
}

public sealed record SettingsValidationResult(
    bool IsValid,
    IReadOnlyList<string> Errors,
    bool RequiresHttpsWarning);

public interface ISettingsService
{
    string SettingsFilePath { get; }
    string DataDirectoryPath { get; }
    string DatabaseFilePath { get; }
    Task<StationSettings> LoadAsync(CancellationToken ct = default);
    Task<SettingsValidationResult> SaveAsync(StationSettings settings, CancellationToken ct = default);
    SettingsValidationResult Validate(StationSettings settings);
}
