using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace PcbInspectionViewer.App.Converters;

public sealed class VerdictToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush PassBrush = CreateFrozenBrush(16, 185, 129);    // #10B981 Emerald
    private static readonly SolidColorBrush ReworkBrush = CreateFrozenBrush(245, 158, 11);  // #F59E0B Amber
    private static readonly SolidColorBrush ScrapBrush = CreateFrozenBrush(239, 68, 68);    // #EF4444 Red
    private static readonly SolidColorBrush NeutralBrush = CreateFrozenBrush(100, 116, 139); // #64748B Slate

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string verdict = (value as string)?.Trim().ToUpperInvariant() ?? string.Empty;
        return verdict switch
        {
            "PASS" => PassBrush,
            "REWORK" => ReworkBrush,
            "SCRAP" => ScrapBrush,
            _ => NeutralBrush
        };
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

public sealed class VerdictToDisplayLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string verdict = (value as string)?.Trim().ToUpperInvariant() ?? string.Empty;
        return verdict switch
        {
            "PASS" => "✓ PASS",
            "REWORK" => "⚠ REWORK",
            "SCRAP" => "✕ SCRAP",
            "" => "AWAITING INSPECTION",
            _ => verdict
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
