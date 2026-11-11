using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace PcbInspectionViewer.App.Converters;

public sealed class OnlineToStatusBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush OnlineBrush = CreateFrozenBrush(16, 185, 129);   // Emerald green
    private static readonly SolidColorBrush OfflineBrush = CreateFrozenBrush(239, 68, 68);   // Red

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? OnlineBrush : OfflineBrush;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;

    private static SolidColorBrush CreateFrozenBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
