using System.Text.Json;

namespace PcbInspectionViewer.Core.Configuration;

public sealed class JsonSettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null
    };

    private static readonly HashSet<string> AllowedIpcClasses = new(StringComparer.Ordinal)
    {
        "Class 2",
        "Class 3"
    };

    private static readonly HashSet<string> AllowedLlmProviders = new(StringComparer.Ordinal)
    {
        "None",
        "Gemini",
        "OpenAI"
    };

    private static readonly HashSet<string> AllowedSummaryLanguages = new(StringComparer.Ordinal)
    {
        "en",
        "zh",
        "en+zh"
    };

    public JsonSettingsService(string? settingsFilePath = null)
    {
        if (!string.IsNullOrWhiteSpace(settingsFilePath))
        {
            SettingsFilePath = Path.GetFullPath(settingsFilePath);
            DataDirectoryPath = Path.GetDirectoryName(SettingsFilePath)
                ?? GetDefaultDataDirectory();
        }
        else
        {
            DataDirectoryPath = GetDefaultDataDirectory();
            SettingsFilePath = Path.Combine(DataDirectoryPath, "settings.json");
        }

        DatabaseFilePath = Path.Combine(DataDirectoryPath, "inspections.db");
    }

    public string SettingsFilePath { get; }
    public string DataDirectoryPath { get; }
    public string DatabaseFilePath { get; }

    public static string GetDefaultDataDirectory()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            localAppData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        }

        return Path.Combine(localAppData, "PcbInspectionViewer");
    }

    public async Task<StationSettings> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(SettingsFilePath))
        {
            return new StationSettings();
        }

        await using FileStream stream = File.OpenRead(SettingsFilePath);
        StationSettings? loaded = await JsonSerializer.DeserializeAsync<StationSettings>(stream, SerializerOptions, ct)
            .ConfigureAwait(false);

        return loaded ?? new StationSettings();
    }

    public async Task<SettingsValidationResult> SaveAsync(StationSettings settings, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        SettingsValidationResult validation = Validate(settings);
        if (!validation.IsValid)
        {
            return validation;
        }

        string? directory = Path.GetDirectoryName(SettingsFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using FileStream stream = File.Create(SettingsFilePath);
        await JsonSerializer.SerializeAsync(stream, settings, SerializerOptions, ct)
            .ConfigureAwait(false);

        return validation;
    }

    public SettingsValidationResult Validate(StationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var errors = new List<string>();
        bool requiresHttpsWarning = false;

        if (string.IsNullOrWhiteSpace(settings.Api.BaseUrl) ||
            !Uri.TryCreate(settings.Api.BaseUrl, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            errors.Add("API Base URL must be a valid absolute http:// or https:// URL.");
        }
        else if (uri.Scheme == Uri.UriSchemeHttp && !IsLocalhost(uri.Host))
        {
            requiresHttpsWarning = true;
        }

        if (settings.Api.TimeoutSeconds is < 1 or > 300)
        {
            errors.Add("API TimeoutSeconds must be between 1 and 300 seconds.");
        }

        if (settings.Batch.Concurrency is < 1 or > 8)
        {
            errors.Add("Batch Concurrency must be between 1 and 8.");
        }

        if (!AllowedIpcClasses.Contains(settings.Batch.DefaultIpcClass))
        {
            errors.Add("Default IPC Class must be 'Class 2' or 'Class 3'.");
        }

        if (string.IsNullOrWhiteSpace(settings.Batch.DefaultBatchId))
        {
            errors.Add("Default Batch ID cannot be empty.");
        }

        if (!AllowedLlmProviders.Contains(settings.Llm.Provider))
        {
            errors.Add("LLM Provider must be 'None', 'Gemini', or 'OpenAI'.");
        }

        if (!AllowedSummaryLanguages.Contains(settings.Llm.SummaryLanguage))
        {
            errors.Add("Summary Language must be 'en', 'zh', or 'en+zh'.");
        }

        return new SettingsValidationResult(
            IsValid: errors.Count == 0,
            Errors: errors,
            RequiresHttpsWarning: requiresHttpsWarning);
    }

    private static bool IsLocalhost(string host) =>
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(host, "::1", StringComparison.OrdinalIgnoreCase);
}
