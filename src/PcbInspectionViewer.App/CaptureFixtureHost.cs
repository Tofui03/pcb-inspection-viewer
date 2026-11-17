using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using PcbInspectionViewer.Core.Api.Dtos;
using PcbInspectionViewer.Core.ViewModels;

namespace PcbInspectionViewer.App;

/// <summary>
/// Loads the bundled upload-triage fixture onto the Inspect tab so a Windows
/// runner can photograph the real WPF window without a live backend.
/// </summary>
internal static class CaptureFixtureHost
{
    private static DispatcherTimer? _layerCycleTimer;

    public static async Task ApplyIfRequestedAsync(
        MainWindow window,
        MainShellViewModel shell,
        string[] args,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetOption(args, "--fixture", out string fixturePath))
        {
            return;
        }

        if (shell.Inspect is not InspectViewModel inspect)
        {
            throw new InvalidOperationException("Inspect view model is not available.");
        }

        if (TryGetOption(args, "--window-size", out string sizeText) &&
            TryParseSize(sizeText, out int width, out int height))
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.WindowState = WindowState.Normal;
            window.MinWidth = 640;
            window.MinHeight = 480;
            window.Left = 0;
            window.Top = 0;
            window.Width = width;
            window.Height = height;
            window.Topmost = true;
            window.Activate();
        }

        await using FileStream stream = File.OpenRead(fixturePath);
        UploadTriageResponseDto? dto = await JsonSerializer.DeserializeAsync<UploadTriageResponseDto>(
            stream,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (dto is null || string.IsNullOrWhiteSpace(dto.BoardDisposition))
        {
            throw new InvalidOperationException($"Fixture '{fixturePath}' did not contain an inspection result.");
        }

        await window.Dispatcher.InvokeAsync(() =>
        {
            inspect.ShowRecordedResult(
                new UploadTriageEnvelope(dto, RoundTripMs: 0),
                dto.BatchId,
                dto.IpcClass);
            shell.SelectedTabIndex = 0;
            window.UpdateLayout();
            if (HasFlag(args, "--cycle-layers"))
            {
                StartLayerCycle(inspect);
            }
        });
        await window.Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);

        if (TryGetOption(args, "--ready-file", out string readyFile))
        {
            string? directory = Path.GetDirectoryName(readyFile);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(readyFile, DateTime.UtcNow.ToString("o"), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static void StartLayerCycle(InspectViewModel inspect)
    {
        string[] layers = ["Test", "Template", "Diff"];
        int index = 0;
        _layerCycleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
        _layerCycleTimer.Tick += (_, _) =>
        {
            index = (index + 1) % layers.Length;
            inspect.SelectLayerCommand.Execute(layers[index]);
        };
        _layerCycleTimer.Start();
    }

    private static bool HasFlag(string[] args, string name) =>
        args.Any(arg => string.Equals(arg, name, StringComparison.OrdinalIgnoreCase));

    private static bool TryGetOption(string[] args, string name, out string value)
    {
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
            {
                value = arg[(name.Length + 1)..].Trim('"');
                return value.Length > 0;
            }

            if (string.Equals(arg, name, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                value = args[i + 1].Trim('"');
                return value.Length > 0;
            }
        }

        value = string.Empty;
        return false;
    }

    private static bool TryParseSize(string text, out int width, out int height)
    {
        width = 0;
        height = 0;
        string[] parts = text.Split(['x', 'X', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 2
            && int.TryParse(parts[0], out width)
            && int.TryParse(parts[1], out height)
            && width is >= 640 and <= 3840
            && height is >= 480 and <= 2160;
    }
}
