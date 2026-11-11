using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PcbInspectionViewer.Core.Api;
using PcbInspectionViewer.Core.Batch;
using PcbInspectionViewer.Core.Configuration;
using PcbInspectionViewer.Core.Data;
using PcbInspectionViewer.Core.Storage;
using PcbInspectionViewer.Core.ViewModels;
using Serilog;
using Serilog.Events;

namespace PcbInspectionViewer.App;

public partial class App : Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        string dataDir = JsonSettingsService.GetDefaultDataDirectory();
        string logDir = Path.Combine(dataDir, "logs");
        Directory.CreateDirectory(logDir);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System.Net.Http", LogEventLevel.Warning)
            .WriteTo.Debug()
            .WriteTo.File(
                path: Path.Combine(logDir, "pcb-viewer-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        _host = Host.CreateDefaultBuilder(e.Args)
            .UseSerilog()
            .ConfigureAppConfiguration((_, config) =>
            {
                config.SetBasePath(AppContext.BaseDirectory);
                config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
                config.AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: false);
                config.AddEnvironmentVariables();
            })
            .ConfigureServices((_, services) =>
            {
                services.AddSingleton<ISettingsService>(_ => new JsonSettingsService());
                services.AddSingleton<IImageCache, ImageCache>();

                services.AddSingleton<IInspectionRepository>(sp =>
                {
                    var settingsService = sp.GetRequiredService<ISettingsService>();
                    var imageCache = sp.GetRequiredService<IImageCache>();
                    string dbPath = settingsService.DatabaseFilePath;

                    // Ensure directory and database with views exist
                    string? dir = Path.GetDirectoryName(dbPath);
                    if (!string.IsNullOrWhiteSpace(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    using var db = new InspectionDbContext(dbPath);
                    db.EnsureDatabaseAndViewsCreatedAsync().GetAwaiter().GetResult();

                    return new InspectionRepository(() => new InspectionDbContext(dbPath), imageCache);
                });

                services.AddHttpClient<IBackendHealthMonitor, BackendHealthMonitor>(client =>
                {
                    client.BaseAddress = new Uri("http://localhost:8000");
                    client.Timeout = TimeSpan.FromSeconds(5);
                });

                services.AddHttpClient<IInspectionApiClient, InspectionApiClient>(client =>
                {
                    client.BaseAddress = new Uri("http://localhost:8000");
                    client.Timeout = TimeSpan.FromSeconds(30);
                });

                services.AddSingleton<IBatchInspectionRunner, BatchInspectionRunner>();

                services.AddSingleton<SettingsViewModel>();
                services.AddSingleton<InspectViewModel>();
                services.AddSingleton<BatchInspectViewModel>();
                services.AddSingleton<HistoryViewModel>();
                services.AddSingleton<MainShellViewModel>();
                services.AddSingleton<MainWindow>();
            })
            .Build();

        await _host.StartAsync();

        var shellVm = _host.Services.GetRequiredService<MainShellViewModel>();
        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.DataContext = shellVm;
        mainWindow.Show();

        await shellVm.InitializeAsync();
        shellVm.StartPeriodicHealthPolling(TimeSpan.FromSeconds(30));
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync(TimeSpan.FromSeconds(3));
            _host.Dispose();
        }

        await Log.CloseAndFlushAsync();
        base.OnExit(e);
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unhandled UI dispatcher exception.");
        MessageBox.Show(
            $"An unexpected error occurred:\n{e.Exception.Message}",
            "PCB Inspection Viewer — Error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unobserved background task exception.");
        e.SetObserved();
    }
}
