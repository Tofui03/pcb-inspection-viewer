using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcbInspectionViewer.Core.Api;
using PcbInspectionViewer.Core.Configuration;

namespace PcbInspectionViewer.Core.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IBackendHealthMonitor _healthMonitor;

    [ObservableProperty]
    private string _baseUrl = "http://localhost:8000";

    [ObservableProperty]
    private int _timeoutSeconds = 10;

    [ObservableProperty]
    private int _concurrency = 2;

    [ObservableProperty]
    private string _defaultIpcClass = "Class 2";

    [ObservableProperty]
    private string _defaultBatchId = "BATCH-UPLOAD";

    [ObservableProperty]
    private bool _colorizePcb;

    [ObservableProperty]
    private string _llmProvider = "None";

    [ObservableProperty]
    private string _modelName = "gemini-2.5-flash";

    [ObservableProperty]
    private string _summaryLanguage = "en";

    [ObservableProperty]
    private bool _hasValidationErrors;

    [ObservableProperty]
    private bool _hasHttpsWarning;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _healthCheckResultText = "Not checked yet.";

    public SettingsViewModel(
        ISettingsService settingsService,
        IBackendHealthMonitor healthMonitor)
    {
        ArgumentNullException.ThrowIfNull(settingsService);
        ArgumentNullException.ThrowIfNull(healthMonitor);

        _settingsService = settingsService;
        _healthMonitor = healthMonitor;
    }

    public event EventHandler<StationSettings>? SettingsSaved;
    public event EventHandler<BackendHealthStatus>? HealthChecked;

    public IReadOnlyList<string> AvailableIpcClasses { get; } = ["Class 2", "Class 3"];
    public IReadOnlyList<string> AvailableLlmProviders { get; } = ["None", "Gemini", "OpenAI"];
    public IReadOnlyList<string> AvailableSummaryLanguages { get; } = ["en", "zh", "en+zh"];

    public void ApplyFrom(StationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        BaseUrl = settings.Api.BaseUrl;
        TimeoutSeconds = settings.Api.TimeoutSeconds;
        Concurrency = settings.Batch.Concurrency;
        DefaultIpcClass = settings.Batch.DefaultIpcClass;
        DefaultBatchId = settings.Batch.DefaultBatchId;
        ColorizePcb = settings.Batch.ColorizePcb;
        LlmProvider = settings.Llm.Provider;
        ModelName = settings.Llm.ModelName;
        SummaryLanguage = settings.Llm.SummaryLanguage;
    }

    public StationSettings BuildSettings() =>
        new()
        {
            Api = new ApiOptions
            {
                BaseUrl = BaseUrl.Trim(),
                TimeoutSeconds = TimeoutSeconds
            },
            Batch = new BatchOptions
            {
                Concurrency = Concurrency,
                DefaultIpcClass = DefaultIpcClass,
                DefaultBatchId = DefaultBatchId.Trim(),
                ColorizePcb = ColorizePcb
            },
            Llm = new LlmOptions
            {
                Provider = LlmProvider,
                ModelName = ModelName.Trim(),
                SummaryLanguage = SummaryLanguage
            }
        };

    [RelayCommand]
    private async Task SaveSettingsAsync(CancellationToken ct)
    {
        StationSettings candidate = BuildSettings();
        SettingsValidationResult result = await _settingsService.SaveAsync(candidate, ct).ConfigureAwait(false);

        HasValidationErrors = !result.IsValid;
        HasHttpsWarning = result.RequiresHttpsWarning;

        if (!result.IsValid)
        {
            StatusMessage = string.Join(" ", result.Errors);
            return;
        }

        StatusMessage = result.RequiresHttpsWarning
            ? "Saved with warning: plain HTTP is used on a non-localhost endpoint."
            : "Saved settings.";

        SettingsSaved?.Invoke(this, candidate);
    }

    [RelayCommand]
    private async Task CheckHealthNowAsync(CancellationToken ct)
    {
        BackendHealthStatus health = await _healthMonitor.CheckHealthAsync(BaseUrl.Trim(), ct).ConfigureAwait(false);
        HealthCheckResultText = health.IsOnline
            ? $"Online ({health.DetectorBackend} · v{health.Version})"
            : $"Offline ({health.ErrorMessage ?? "unreachable"})";

        HealthChecked?.Invoke(this, health);
    }
}
