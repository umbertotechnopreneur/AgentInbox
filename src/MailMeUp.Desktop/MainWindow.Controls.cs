/* VBWR B
 *
 * Project: AgentInbox
 * Repository: https://github.com/umbertotechnopreneur/AgentInbox
 * Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
 *
 * VibeWare: Human intent, AI execution, and plenty of tokens
 * Manifesto: https://umbertogiacobbi.biz/vibeware/manifesto
 *
 * File: Shared native form controls, accessible actions and responsive layout helpers.
 * Created with AI: OpenAI Codex; extracted native control helpers, 2026-10-27.
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
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace MailMeUp.Desktop;

/// <summary>Configures email accounts and assistant access on Windows.</summary>
public sealed partial class MainWindow
{
    /// <summary>Create an owned confirmation dialog with descriptive action tooltips.</summary>
    /// <param name="title">User-facing heading for this page, dialog or notification.</param>
    /// <param name="content">Native content placed in the section, tab, dialog or expander.</param>
    /// <param name="primary">Label of the explicit primary confirmation action.</param>
    private ContentDialog Dialog(string title, UIElement content, string primary) => new()
    {
        XamlRoot = Root.XamlRoot, Title = title, Content = content, PrimaryButtonText = primary,
        CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close,
        PrimaryButtonStyle = DialogButtonStyle(primary),
        CloseButtonStyle = DialogButtonStyle("Cancel and close this dialog.")
    };

    /// <summary>Attach a descriptive tooltip to a stock dialog button style.</summary>
    /// <param name="tooltip">Descriptive action tooltip; null uses the button caption or name.</param>
    private static Style DialogButtonStyle(string tooltip)
    {
        var style = new Style(typeof(Button))
        {
            BasedOn = (Style)Microsoft.UI.Xaml.Application.Current.Resources["DefaultButtonStyle"]
        };
        style.Setters.Add(new Setter(ToolTipService.ToolTipProperty, tooltip));
        return style;
    }

    /// <summary>Create selectable wrapping text using the requested font size.</summary>
    /// <param name="value">Model value or text consumed by this control or binding.</param>
    /// <param name="size">Requested text size in logical pixels.</param>
    private static TextBlock Text(string value, double size = 14) => new()
    { Text = value, TextWrapping = TextWrapping.Wrap, FontSize = size, IsTextSelectionEnabled = true };

    /// <summary>Create wrapping supporting text with the native subtle-text style.</summary>
    /// <param name="value">Model value or text consumed by this control or binding.</param>
    private static TextBlock Hint(string value) => new()
    { Text = value, TextWrapping = TextWrapping.Wrap, Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["SubtleText"] };

    /// <summary>Compose a section title and optional supporting text.</summary>
    /// <param name="title">User-facing heading for this page, dialog or notification.</param>
    /// <param name="subtitle">Supporting heading text; an empty string omits it.</param>
    private static StackPanel Heading(string title, string subtitle)
    {
        var heading = Stack(new TextBlock { Text = title, Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["SectionTitle"] });
        if (subtitle.Length > 0) heading.Children.Add(Hint(subtitle));
        return heading;
    }

    /// <summary>Keep a page heading and its action usable at narrow widths.</summary>
    /// <param name="title">User-facing heading for this page, dialog or notification.</param>
    /// <param name="subtitle">Supporting heading text; an empty string omits it.</param>
    /// <param name="action">Action callback or native action control placed with the content.</param>
    private static Grid PageHeading(string title, string subtitle, FrameworkElement action)
    {
        var header = new Grid { ColumnSpacing = 16, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Top };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var heading = Heading(title, subtitle);
        header.Children.Add(heading);
        action.HorizontalAlignment = HorizontalAlignment.Right;
        action.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(action, 1);
        header.Children.Add(action);
        header.SizeChanged += (_, args) =>
        {
            var compact = args.NewSize.Width < 620;
            Grid.SetColumnSpan(heading, compact ? 2 : 1);
            Grid.SetRow(action, compact ? 1 : 0);
            Grid.SetColumn(action, compact ? 0 : 1);
            Grid.SetColumnSpan(action, compact ? 2 : 1);
            action.HorizontalAlignment = compact ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            action.Margin = compact ? new Thickness(0, 12, 0, 0) : new Thickness(0);
        };
        return header;
    }

    /// <summary>Convert a packed RGB value into an opaque WinUI brush.</summary>
    /// <param name="rgb">Packed 24-bit RGB color; the resulting brush is opaque.</param>
    private static SolidColorBrush IconBrush(uint rgb) => new(Windows.UI.Color.FromArgb(
        255, (byte)((rgb >> 16) & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF)));

    /// <summary>Create a native font icon with the requested color.</summary>
    /// <param name="glyph">Symbol glyph rendered using the native icon font.</param>
    /// <param name="color">Packed 24-bit RGB color for the leading icon.</param>
    private static FontIcon ColoredIcon(string glyph, uint color) => new()
    {
        Glyph = glyph, FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 18,
        Foreground = IconBrush(color), Width = 20, Height = 20, VerticalAlignment = VerticalAlignment.Center
    };

    /// <summary>Create an accessible icon button with optional visible caption.</summary>
    /// <param name="name">Accessible action name or visible choice label.</param>
    /// <param name="glyph">Symbol glyph rendered using the native icon font.</param>
    /// <param name="color">Packed 24-bit RGB color for the leading icon.</param>
    /// <param name="handler">Callback invoked by the explicit native button click.</param>
    /// <param name="tooltip">Descriptive action tooltip; null uses the button caption or name.</param>
    /// <param name="caption">Optional visible button caption in addition to its icon.</param>
    private static Button IconAction(string name, string glyph, uint color, Action handler, string? tooltip = null, string? caption = null)
    {
        var button = new Button
        {
            Content = ColoredIcon(glyph, color), MinWidth = 36, Height = 36,
            Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["QuietButton"],
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        if (caption is null) button.Width = 36;
        else
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            content.Children.Add(ColoredIcon(glyph, color));
            content.Children.Add(new TextBlock { Text = caption, VerticalAlignment = VerticalAlignment.Center });
            button.Content = content;
            button.Padding = new Thickness(6, 0, 6, 0);
        }
        AutomationProperties.SetName(button, name);
        ToolTipService.SetToolTip(button, tooltip ?? name);
        button.Click += (_, _) => handler();
        return button;
    }

    /// <summary>Centers a compact mailbox prompt in the available space without adding a card.</summary>
    /// <param name="title">User-facing heading for this page, dialog or notification.</param>
    /// <param name="description">Supporting prompt text explaining the available action.</param>
    /// <param name="action">Action callback or native action control placed with the content.</param>
    private static Grid AccountPlaceholder(string title, string description, Button? action = null)
    {
        var icon = new FontIcon
        {
            Glyph = "\uE715", FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 48,
            Foreground = IconBrush(0xB58ADA), HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8)
        };
        AutomationProperties.SetAccessibilityView(icon, AccessibilityView.Raw);
        var heading = Text(title, 24);
        heading.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        heading.TextAlignment = TextAlignment.Center;
        var guidance = Hint(description);
        guidance.FontSize = 14;
        guidance.LineHeight = 22;
        guidance.TextAlignment = TextAlignment.Center;
        var content = Stack(icon, heading, guidance);
        content.MaxWidth = 320;
        content.Margin = new Thickness(20);
        content.HorizontalAlignment = HorizontalAlignment.Center;
        content.VerticalAlignment = VerticalAlignment.Center;
        if (action is not null)
        {
            action.HorizontalAlignment = HorizontalAlignment.Center;
            action.Margin = new Thickness(0, 8, 0, 0);
            content.Children.Add(action);
        }
        var placeholder = new Grid { MinHeight = 320 };
        placeholder.Children.Add(content);
        return placeholder;
    }

    /// <summary>Compose a provider choice with its leading native icon.</summary>
    /// <param name="name">Accessible action name or visible choice label.</param>
    /// <param name="glyph">Symbol glyph rendered using the native icon font.</param>
    /// <param name="color">Packed 24-bit RGB color for the leading icon.</param>
    private static ComboBoxItem PresetItem(string name, string glyph, uint color)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        content.Children.Add(ColoredIcon(glyph, color));
        content.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center });
        var item = new ComboBoxItem { Content = content };
        AutomationProperties.SetName(item, name);
        return item;
    }

    /// <summary>Centers each tab icon and label without changing the stock TabViewItem template.</summary>
    /// <param name="name">Accessible action name or visible choice label.</param>
    /// <param name="glyph">Symbol glyph rendered using the native icon font.</param>
    /// <param name="color">Packed 24-bit RGB color for the leading icon.</param>
    /// <param name="content">Native content placed in the section, tab, dialog or expander.</param>
    private static TabViewItem AccountTab(string name, string glyph, uint color, FrameworkElement content)
    {
        content.Margin = new Thickness(0, 20, 0, 0);
        content.VerticalAlignment = VerticalAlignment.Top;
        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };
        header.Children.Add(new FontIcon
        {
            Glyph = glyph, FontFamily = new FontFamily("Segoe Fluent Icons"),
            FontSize = 16, Foreground = IconBrush(color), VerticalAlignment = VerticalAlignment.Center
        });
        header.Children.Add(new TextBlock
        {
            Text = name, TextWrapping = TextWrapping.NoWrap,
            VerticalAlignment = VerticalAlignment.Center
        });
        var tab = new TabViewItem
        {
            Header = header, IsClosable = false, Content = content,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Top
        };
        AutomationProperties.SetName(tab, name);
        return tab;
    }

    /// <summary>Arrange supplied controls vertically with the shared form spacing.</summary>
    /// <param name="children">Controls placed in their supplied order.</param>
    private static StackPanel Stack(params UIElement[] children)
    {
        var panel = new StackPanel { Spacing = 12, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Top };
        foreach (var child in children) panel.Children.Add(child);
        return panel;
    }

    /// <summary>Group form content with an optional native heading.</summary>
    /// <param name="title">User-facing heading for this page, dialog or notification.</param>
    /// <param name="children">Controls placed in their supplied order.</param>
    private static StackPanel Section(string title, params UIElement[] children)
    {
        var content = Stack();
        if (title.Length > 0) content.Children.Add(new TextBlock { Text = title, FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        foreach (var child in children) content.Children.Add(child);
        return content;
    }

    /// <summary>Arrange two fields side by side and stack them on narrow layouts.</summary>
    /// <param name="left">First field, kept before the second field when stacked.</param>
    /// <param name="right">Second field, moved below the first field on narrow layouts.</param>
    private static Grid Pair(FrameworkElement left, FrameworkElement right)
    {
        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.Children.Add(left);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        grid.SizeChanged += (_, args) =>
        {
            var compact = args.NewSize.Width < 560;
            Grid.SetColumnSpan(left, compact ? 2 : 1);
            Grid.SetRow(right, compact ? 1 : 0);
            Grid.SetColumn(right, compact ? 0 : 1);
            Grid.SetColumnSpan(right, compact ? 2 : 1);
            grid.RowSpacing = compact ? 12 : 0;
        };
        return grid;
    }

    /// <summary>Create a bounded text-entry field with a descriptive header.</summary>
    /// <param name="label">Visible field or operation-stage label.</param>
    /// <param name="placeholder">Example input displayed while the text field is empty.</param>
    private static TextBox Field(string label, string placeholder) => new()
    { Header = label, PlaceholderText = placeholder, HorizontalAlignment = HorizontalAlignment.Stretch, MaxLength = 1024 };

    /// <summary>Create a bounded password field with explicit reveal behavior.</summary>
    /// <param name="label">Visible field or operation-stage label.</param>
    private static PasswordBox Password(string label) => new()
    { Header = label, PasswordRevealMode = PasswordRevealMode.Peek, MaxLength = 1024, HorizontalAlignment = HorizontalAlignment.Stretch };

    /// <summary>Offer only implicit TLS and mandatory STARTTLS connection modes.</summary>
    private static ComboBox TlsField()
    {
        var field = new ComboBox { Header = "Connection security", HorizontalAlignment = HorizontalAlignment.Stretch };
        field.Items.Add("TLS — encrypted from connection start");
        field.Items.Add("STARTTLS — require encryption before signing in");
        field.SelectedIndex = 0;
        return field;
    }

    /// <summary>Create an accessible text button and forward its click to the supplied action.</summary>
    /// <param name="caption">Optional visible button caption in addition to its icon.</param>
    /// <param name="handler">Callback invoked by the explicit native button click.</param>
    /// <param name="accent">Whether to use the native accent-button style.</param>
    /// <param name="tooltip">Descriptive action tooltip; null uses the button caption or name.</param>
    private static Button Action(string caption, Action handler, bool accent = false, string? tooltip = null)
    {
        var button = new Button { Content = caption };
        if (accent) button.Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["AccentButtonStyle"];
        AutomationProperties.SetName(button, caption);
        ToolTipService.SetToolTip(button, tooltip ?? caption);
        button.Click += (_, _) => handler();
        return button;
    }
}
