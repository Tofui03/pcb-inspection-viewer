using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using PcbInspectionViewer.Core.Configuration;
using PcbInspectionViewer.Core.ViewModels;

namespace PcbInspectionViewer.App;

public partial class MainWindow : Window
{
    private const string ImageDialogFilter = "PCB Images (*.jpg;*.jpeg;*.png;*.bmp)|*.jpg;*.jpeg;*.png;*.bmp";

    public MainWindow()
    {
        InitializeComponent();
    }

    private InspectViewModel? InspectVm => (DataContext as MainShellViewModel)?.Inspect;
    private BatchInspectViewModel? BatchVm => (DataContext as MainShellViewModel)?.Batch;
    private HistoryViewModel? HistoryVm => (DataContext as MainShellViewModel)?.History;

    private void OnBrowseBatchFolderClick(object sender, RoutedEventArgs e)
    {
        if (BatchVm is null)
        {
            return;
        }

        var dialog = new OpenFolderDialog
        {
            Title = "Select Folder Containing PCB Board Images"
        };

        if (dialog.ShowDialog(this) == true)
        {
            BatchVm.FolderPath = dialog.FolderName;
        }
    }

    private void OnBrowseBatchTemplateClick(object sender, RoutedEventArgs e)
    {
        if (BatchVm is null)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Select Global Golden Template PCB Image",
            Filter = ImageDialogFilter,
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) == true)
        {
            BatchVm.GlobalTemplatePath = dialog.FileName;
        }
    }

    private void OnHistoryDataGridMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (HistoryVm?.SelectedItem is not null)
        {
            _ = HistoryVm.DrillDownCommand.ExecuteAsync(HistoryVm.SelectedItem);
        }
    }

    private void OnOpenDataFolderClick(object sender, RoutedEventArgs e)
    {
        string dataDir = JsonSettingsService.GetDefaultDataDirectory();
        if (!Directory.Exists(dataDir))
        {
            Directory.CreateDirectory(dataDir);
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = dataDir,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Unable to open data directory:\n{ex.Message}", "PCB Inspection Viewer", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.O && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            BrowseAndAssignImage(isTemplate: false);
            e.Handled = true;
        }
    }

    private void OnBrowseTemplateClick(object sender, RoutedEventArgs e) =>
        BrowseAndAssignImage(isTemplate: true);

    private void OnBrowseTestClick(object sender, RoutedEventArgs e) =>
        BrowseAndAssignImage(isTemplate: false);

    private void BrowseAndAssignImage(bool isTemplate)
    {
        if (InspectVm is null)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = isTemplate ? "Select Golden Template PCB Image" : "Select Test PCB Image",
            Filter = ImageDialogFilter,
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) == true)
        {
            InspectVm.TrySetImagePathFromDrop(dialog.FileName, isTemplate: isTemplate);
        }
    }

    private void OnImageDropZoneDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnTemplateImageDrop(object sender, DragEventArgs e)
    {
        if (InspectVm is null || !e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
        {
            InspectVm.TrySetImagePathFromDrop(files[0], isTemplate: true);
            if (files.Length > 1)
            {
                InspectVm.TrySetImagePathFromDrop(files[1], isTemplate: false);
            }
        }

        e.Handled = true;
    }

    private void OnTestImageDrop(object sender, DragEventArgs e)
    {
        if (InspectVm is null || !e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
        {
            if (files.Length >= 2)
            {
                InspectVm.TrySetImagePathFromDrop(files[0], isTemplate: true);
                InspectVm.TrySetImagePathFromDrop(files[1], isTemplate: false);
            }
            else if (string.IsNullOrWhiteSpace(InspectVm.TemplateImagePath))
            {
                InspectVm.TrySetImagePathFromDrop(files[0], isTemplate: true);
            }
            else
            {
                InspectVm.TrySetImagePathFromDrop(files[0], isTemplate: false);
            }
        }

        e.Handled = true;
    }
}
