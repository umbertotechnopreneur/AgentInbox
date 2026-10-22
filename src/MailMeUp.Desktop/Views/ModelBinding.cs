// SPDX-License-Identifier: MIT

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace MailMeUp.Desktop.Views;

/// <summary>Adapts observable model properties to native WinUI dependency properties.</summary>
internal static class ModelBinding
{
    internal static void Bind(FrameworkElement target, DependencyProperty property, object source, string path,
        BindingMode mode = BindingMode.OneWay, IValueConverter? converter = null, object? parameter = null)
    {
        target.ClearValue(property);
        target.SetBinding(property, new Binding
        {
            Source = source, Path = new PropertyPath(path), Mode = mode, Converter = converter,
            ConverterParameter = parameter, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
        });
    }

    internal static void Visible(FrameworkElement target, object source, string path, bool invert = false) =>
        Bind(target, UIElement.VisibilityProperty, source, path, converter: VisibilityConverter.Instance, parameter: invert);

    internal sealed class VisibilityConverter : IValueConverter
    {
        internal static readonly VisibilityConverter Instance = new();
        public object Convert(object value, Type targetType, object parameter, string language) =>
            (value is true) != (parameter is true) ? Visibility.Visible : Visibility.Collapsed;
        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
    }

    internal sealed class SeverityConverter : IValueConverter
    {
        internal static readonly SeverityConverter Instance = new();
        public object Convert(object value, Type targetType, object parameter, string language) => (InfoBarSeverity)(int)value;
        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
    }

    internal sealed class ToneConverter : IValueConverter
    {
        internal static readonly ToneConverter Instance = new();
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            var rgb = (int)value switch { 1 => 0x16856A, 2 => 0xC77616, 3 => 0xD83B35, _ => 0x2477C8 };
            return new SolidColorBrush(Windows.UI.Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
        }
        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
    }
}
