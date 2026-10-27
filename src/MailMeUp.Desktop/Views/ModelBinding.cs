/* VBWR B
 *
 * Project: AgentInbox
 * Repository: https://github.com/umbertotechnopreneur/AgentInbox
 * Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
 *
 * VibeWare: Human intent, AI execution, and plenty of tokens
 * Manifesto: https://umbertogiacobbi.biz/vibeware/manifesto
 *
 * File: One-way presentation converters and native model-binding helpers.
 * Modified with AI: OpenAI Codex; code readability and documentation, 2026-10-27.
 * Human guidance: Umberto Giacobbi; document parameters and simplify maintenance.
 * Evidence: docs/VALIDATION.md (source review; execution checks pending).
 *
 * Copyright (c) 2026 Umberto Giacobbi
 * License: MIT - see LICENSE
 * SPDX-License-Identifier: MIT
 *
 * VBWR E */

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace MailMeUp.Desktop.Views;

/// <summary>Adapts observable model properties to native WinUI dependency properties.</summary>
internal static class ModelBinding
{
    /// <summary>Bind one observable model property to a native dependency property.</summary>
    /// <param name="target">Native control receiving the dependency-property binding.</param>
    /// <param name="property">Observable property name or native dependency property being updated.</param>
    /// <param name="source">Observable model that owns the binding value.</param>
    /// <param name="path">Configuration file path or observable property path consumed by the adapter.</param>
    /// <param name="mode">Binding direction; field edits use the explicitly supplied two-way mode.</param>
    /// <param name="converter">Optional presentation converter, independent of account policy.</param>
    /// <param name="parameter">Optional presentation-converter argument, such as visibility inversion.</param>
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

    /// <summary>Bind a model flag to native visibility with optional inversion.</summary>
    /// <param name="target">Native control receiving the dependency-property binding.</param>
    /// <param name="source">Observable model that owns the binding value.</param>
    /// <param name="path">Configuration file path or observable property path consumed by the adapter.</param>
    /// <param name="invert">Whether a true model flag should collapse rather than show the control.</param>
    internal static void Visible(FrameworkElement target, object source, string path, bool invert = false) =>
        Bind(target, UIElement.VisibilityProperty, source, path, converter: VisibilityConverter.Instance, parameter: invert);

    internal sealed class VisibilityConverter : IValueConverter
    {
        internal static readonly VisibilityConverter Instance = new();
        /// <summary>Convert model presentation state for this one-way native binding.</summary>
        /// <param name="value">Model value or text consumed by this control or binding.</param>
        /// <param name="targetType">Native target type supplied by the binding engine.</param>
        /// <param name="parameter">Optional presentation-converter argument, such as visibility inversion.</param>
        /// <param name="language">Binding-engine language tag; these converters do not parse localized input.</param>
        public object Convert(object value, Type targetType, object parameter, string language) =>
            (value is true) != (parameter is true) ? Visibility.Visible : Visibility.Collapsed;
        /// <summary>Reject reverse conversion for a one-way presentation binding.</summary>
        /// <param name="value">Model value or text consumed by this control or binding.</param>
        /// <param name="targetType">Native target type supplied by the binding engine.</param>
        /// <param name="parameter">Optional presentation-converter argument, such as visibility inversion.</param>
        /// <param name="language">Binding-engine language tag; these converters do not parse localized input.</param>
        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
    }

    internal sealed class SeverityConverter : IValueConverter
    {
        internal static readonly SeverityConverter Instance = new();
        /// <summary>Convert model presentation state for this one-way native binding.</summary>
        /// <param name="value">Model value or text consumed by this control or binding.</param>
        /// <param name="targetType">Native target type supplied by the binding engine.</param>
        /// <param name="parameter">Optional presentation-converter argument, such as visibility inversion.</param>
        /// <param name="language">Binding-engine language tag; these converters do not parse localized input.</param>
        public object Convert(object value, Type targetType, object parameter, string language) => (InfoBarSeverity)(int)value;
        /// <summary>Reject reverse conversion for a one-way presentation binding.</summary>
        /// <param name="value">Model value or text consumed by this control or binding.</param>
        /// <param name="targetType">Native target type supplied by the binding engine.</param>
        /// <param name="parameter">Optional presentation-converter argument, such as visibility inversion.</param>
        /// <param name="language">Binding-engine language tag; these converters do not parse localized input.</param>
        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
    }

    internal sealed class ToneConverter : IValueConverter
    {
        internal static readonly ToneConverter Instance = new();
        /// <summary>Convert model presentation state for this one-way native binding.</summary>
        /// <param name="value">Model value or text consumed by this control or binding.</param>
        /// <param name="targetType">Native target type supplied by the binding engine.</param>
        /// <param name="parameter">Optional presentation-converter argument, such as visibility inversion.</param>
        /// <param name="language">Binding-engine language tag; these converters do not parse localized input.</param>
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            var rgb = (int)value switch { 1 => 0x16856A, 2 => 0xC77616, 3 => 0xD83B35, _ => 0x2477C8 };
            return new SolidColorBrush(Windows.UI.Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
        }
        /// <summary>Reject reverse conversion for a one-way presentation binding.</summary>
        /// <param name="value">Model value or text consumed by this control or binding.</param>
        /// <param name="targetType">Native target type supplied by the binding engine.</param>
        /// <param name="parameter">Optional presentation-converter argument, such as visibility inversion.</param>
        /// <param name="language">Binding-engine language tag; these converters do not parse localized input.</param>
        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
    }
}
