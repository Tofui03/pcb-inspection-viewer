using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using PcbInspectionViewer.Core.Batch;

namespace PcbInspectionViewer.App.Converters;

public sealed class BatchStatusToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush CompletedBrush = CreateFrozenBrush(16, 185, 129);  // Green
    private static readonly SolidColorBrush RunningBrush = CreateFrozenBrush(59, 130, 246);     // Blue
    private static readonly SolidColorBrush FailedBrush = CreateFrozenBrush(239, 68, 68);       // Red
    private static readonly SolidColorBrush CancelledBrush = CreateFrozenBrush(100, 116, 139);  // Slate
    private static readonly SolidColorBrush QueuedBrush = CreateFrozenBrush(148, 163, 184);     // Light slate

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is BatchItemStatus status)
        {
            return status switch
            {
                BatchItemStatus.Completed => CompletedBrush,
                BatchItemStatus.Running => RunningBrush,
                BatchItemStatus.Failed => FailedBrush,
                BatchItemStatus.Cancelled => CancelledBrush,
                _ => QueuedBrush
            };
        }

        return QueuedBrush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;

    private static SolidColorBrush CreateFrozenBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
