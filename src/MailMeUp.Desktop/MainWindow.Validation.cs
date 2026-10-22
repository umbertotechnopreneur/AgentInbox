// SPDX-License-Identifier: MIT

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace MailMeUp.Desktop;

public sealed partial class MainWindow
{
    /// <summary>Renders model errors with the native theme resources; validation belongs to the model.</summary>
    private void RenderAccountErrors()
    {
        foreach (var (property, field) in _accountFields)
        {
            var error = _configuration.Editor.Errors.FirstOrDefault(item => item.Property == property);
            string[] resources = ["TextControlBorderBrush", "TextControlBorderBrushPointerOver", "TextControlBorderBrushFocused"];
            if (error is null)
            {
                foreach (var key in resources) field.Resources.Remove(key);
                field.ClearValue(Control.BorderBrushProperty);
                field.ClearValue(AutomationProperties.HelpTextProperty);
                if (field is TextBox text) text.Description = null;
                if (field is PasswordBox password) password.Description = null;
                continue;
            }
            var brush = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
            foreach (var key in resources) field.Resources[key] = brush;
            field.BorderBrush = brush;
            var description = new TextBlock { Text = error.Message, Foreground = brush, TextWrapping = TextWrapping.Wrap };
            if (field is TextBox textBox) textBox.Description = description;
            if (field is PasswordBox passwordBox) passwordBox.Description = description;
            AutomationProperties.SetHelpText(field, error.Message);
        }
    }

    private void FocusAccountError()
    {
        var first = _configuration.Editor.Errors.FirstOrDefault();
        if (first is null || !_accountFields.TryGetValue(first.Property, out var field)) return;
        _configuration.EditorTab = first.Tab;
        DispatcherQueue.TryEnqueue(() => { field.StartBringIntoView(); field.Focus(FocusState.Programmatic); });
    }
}