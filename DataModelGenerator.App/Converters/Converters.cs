using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using DataModelGenerator.Core.Validation;

namespace DataModelGenerator.App.Converters;

[ValueConversion(typeof(bool), typeof(Visibility))]
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Visibility.Visible;
}

[ValueConversion(typeof(bool), typeof(Visibility))]
public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Visibility.Collapsed;
}

[ValueConversion(typeof(bool), typeof(bool))]
public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is not true;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is not true;
}

[ValueConversion(typeof(string), typeof(Visibility))]
public class NullOrEmptyToCollapsedConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

[ValueConversion(typeof(IEnumerable), typeof(Visibility))]
public class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is null) return Visibility.Collapsed;
        if (value is ICollection collection) return collection.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (value is int count) return count > 0 ? Visibility.Visible : Visibility.Collapsed;
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

[ValueConversion(typeof(double), typeof(Brush))]
public class ConfidenceToBrushConverter : IValueConverter
{
    private static readonly Brush Low = new SolidColorBrush(Color.FromRgb(192, 0, 0));
    private static readonly Brush Medium = new SolidColorBrush(Color.FromRgb(191, 128, 0));
    private static readonly Brush High = new SolidColorBrush(Color.FromRgb(55, 86, 35));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var confidence = value is double d ? d : 1.0;
        return confidence < 0.5 ? Low : confidence < 0.75 ? Medium : High;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

[ValueConversion(typeof(ValidationSeverity), typeof(Brush))]
public class SeverityToBrushConverter : IValueConverter
{
    private static readonly Brush Error = new SolidColorBrush(Color.FromRgb(192, 0, 0));
    private static readonly Brush Warning = new SolidColorBrush(Color.FromRgb(191, 128, 0));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is ValidationSeverity.Error ? Error : Warning;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
