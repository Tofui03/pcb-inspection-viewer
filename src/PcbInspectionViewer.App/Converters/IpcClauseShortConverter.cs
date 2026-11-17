using System.Globalization;
using System.Windows.Data;
using PcbInspectionViewer.Core.ViewModels;

namespace PcbInspectionViewer.App.Converters;

public sealed class IpcClauseShortConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        InspectAlarm.ShortIpcClause(value as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
