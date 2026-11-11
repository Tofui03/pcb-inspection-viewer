using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcbInspectionViewer.Core.Api;
using PcbInspectionViewer.Core.Configuration;

namespace PcbInspectionViewer.Core.ViewModels;

public sealed partial class MainShellViewModel : ObservableObject, IDisposable
{
    private readonly ISettingsService _settingsService;
    private readonly IBackendHealthMonitor _healthMonitor;
    private readonly CancellationTokenSource _pollingCts = new();
    private Task? _pollingTask;

    [ObservableProperty]
    private int _selectedTabIndex;

    [ObservableProperty]
    private bool _isBackendOnline;

    [ObservableProperty]
    private string _backendStatusText = "Backend offline";

    [ObservableProperty]
    private string _backendStatusTooltip = "Health check pending.";

    [ObservableProperty]
    private string _detectorAndVersionText = "unknown · vunknown";

    [ObservableProperty]
    private string _databasePathText = string.Empty;

    [ObservableProperty]
    private string _llmProviderStatusText = "LLM: None";

    public MainShellViewModel(
        ISettingsService settingsService,
        IBackendHealthMonitor healthMonitor,
        SettingsViewModel settingsViewModel)
    {
        ArgumentNullException.ThrowIfNull(settingsService);
        ArgumentNullException.ThrowIfNull(healthMonitor);
        ArgumentNullException.ThrowIfNull(settingsViewModel);

        _settingsService = settingsService;
        _healthMonitor = healthMonitor;
        Settings = settingsViewModel;

        DatabasePathText = _settingsService.DatabaseFilePath;

        Settings.SettingsSaved += OnSettingsSaved;
        Settings.HealthChecked += OnHealthChecked;
    }

    public SettingsViewModel Settings { get; }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        StationSettings loaded = await _settingsService.LoadAsync(ct).ConfigureAwait(false);
        Settings.ApplyFrom(loaded);
        UpdateLlmStatusText(loaded.Llm);

        await RefreshHealthAsync(ct).ConfigureAwait(false);
    }

    public void StartPeriodicHealthPolling(TimeSpan interval)
    {
        if (_pollingTask is not null)
        {
            return;
        }

        _pollingTask = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(interval);
            try
            {
                while (await timer.WaitForNextTickAsync(_pollingCts.Token).ConfigureAwait(false))
                {
                    await RefreshHealthAsync(_pollingCts.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // Clean shutdown.
            }
        });
    }

    [RelayCommand]
    public async Task RefreshHealthAsync(CancellationToken ct = default)
    {
        BackendHealthStatus status = await _healthMonitor.CheckHealthAsync(Settings.BaseUrl, ct).ConfigureAwait(false);
        ApplyHealthStatus(status);
    }

    private void OnSettingsSaved(object? sender, StationSettings updated)
    {
        UpdateLlmStatusText(updated.Llm);
        _ = RefreshHealthAsync();
    }

    private void OnHealthChecked(object? sender, BackendHealthStatus status) =>
        ApplyHealthStatus(status);

    private void ApplyHealthStatus(BackendHealthStatus status)
    {
        IsBackendOnline = status.IsOnline;
        if (status.IsOnline)
        {
            BackendStatusText = "Backend online";
            BackendStatusTooltip = $"Connected to {Settings.BaseUrl} ({status.ExecutionProvider})";
            DetectorAndVersionText = $"{status.DetectorBackend} · v{status.Version}";
        }
        else
        {
            BackendStatusText = "Backend offline";
            BackendStatusTooltip = status.ErrorMessage ?? "Backend unreachable";
            DetectorAndVersionText = "unknown · offline";
        }
    }

    private void UpdateLlmStatusText(LlmOptions llm)
    {
        if (string.Equals(llm.Provider, "None", StringComparison.OrdinalIgnoreCase))
        {
            LlmProviderStatusText = "LLM: None (Template fallback)";
            return;
        }

        string envKeyName = string.Equals(llm.Provider, "Gemini", StringComparison.OrdinalIgnoreCase)
            ? "GEMINI_API_KEY"
            : "OPENAI_API_KEY";
        bool hasKey = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(envKeyName));

        LlmProviderStatusText = hasKey
            ? $"LLM: {llm.Provider} ({llm.ModelName})"
            : $"LLM: {llm.Provider} (No API key → Template fallback)";
    }

    public void Dispose()
    {
        Settings.SettingsSaved -= OnSettingsSaved;
        Settings.HealthChecked -= OnHealthChecked;
        _pollingCts.Cancel();
        _pollingCts.Dispose();
    }
}
