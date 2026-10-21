// SPDX-License-Identifier: MIT

using System.Text.RegularExpressions;
using MailMeUp.Desktop.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace MailMeUp.Desktop;

public sealed partial class MainWindow
{
    private readonly HashSet<Control> _invalidAccountFields = [];
    private bool _accountValidationShown;
    private string _accountValidationSummary = "";
    private static readonly string[] ErrorBorderResources =
        ["TextControlBorderBrush", "TextControlBorderBrushPointerOver", "TextControlBorderBrushFocused"];

    /// <summary>Uses native field descriptions and theme brushes; keeps the stock control templates.</summary>
    private void SetAccountFieldError(Control field, string? message)
    {
        if (message is null)
        {
            if (!_invalidAccountFields.Remove(field)) return;
            foreach (var key in ErrorBorderResources) field.Resources.Remove(key);
            field.ClearValue(Control.BorderBrushProperty);
            if (field is TextBox text) text.Description = null;
            if (field is PasswordBox password) password.Description = null;
            field.ClearValue(AutomationProperties.HelpTextProperty);
            return;
        }
        _invalidAccountFields.Add(field);
        var brush = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
        foreach (var key in ErrorBorderResources) field.Resources[key] = brush;
        field.BorderBrush = brush;
        var description = new TextBlock { Text = message, Foreground = brush, TextWrapping = TextWrapping.Wrap };
        if (field is TextBox textBox) textBox.Description = description;
        if (field is PasswordBox passwordBox) passwordBox.Description = description;
        AutomationProperties.SetHelpText(field, message);
    }

    private void ClearAccountValidation()
    {
        foreach (var field in _invalidAccountFields.ToArray()) SetAccountFieldError(field, null);
        _accountValidationShown = false;
    }

    private void ValidateAccountEntry()
    {
        FillSignInEmails();
        _accountValidationShown = true;
        if (!ValidateAccountFields(focus: true)) throw new EngineException(_accountValidationSummary);
    }

    private bool CanKeepPassword(string section, TextBox host, TextBox port, ComboBox tls, TextBox email)
    {
        var saved = _accounts.FirstOrDefault(account => Value(account, "id") == _accountId);
        if (saved.ValueKind != System.Text.Json.JsonValueKind.Object
            || Value(saved.GetProperty("authentication"), "method") != "password") return false;
        var connection = saved.GetProperty(section);
        if (connection.ValueKind != System.Text.Json.JsonValueKind.Object) return false;
        return Value(connection, "host") == host.Text.Trim().TrimEnd('.').ToLowerInvariant()
            && int.TryParse(port.Text, out var number) && connection.GetProperty("port").GetInt32() == number
            && Value(connection, "tls") == (tls.SelectedIndex == 1 ? "starttls" : "tls")
            && Value(connection, "username") == email.Text.Trim()
            && (section != "incoming" || Value(connection, "protocol") == (_incomingProtocol.SelectedIndex == 1 ? "pop3" : "imap"));
    }

    /// <summary>Marks all missing or malformed fields and focuses the first one on submission.</summary>
    private bool ValidateAccountFields(bool focus)
    {
        Control? first = null;
        var firstTab = 0;
        var messages = new List<string>();
        void Check(Control field, bool valid, string message, int tab)
        {
            SetAccountFieldError(field, valid ? null : message);
            if (!valid) messages.Add(message);
            if (valid || first is not null) return;
            first = field;
            firstTab = tab;
        }
        void Required(TextBox field, string message, int tab) =>
            Check(field, !string.IsNullOrWhiteSpace(field.Text), message, tab);
        void Server(TextBox field, int tab)
        {
            var host = field.Text.Trim().TrimEnd('.');
            Check(field, host.Length is > 0 and <= 255
                && Regex.IsMatch(host, @"^[a-zA-Z0-9](?:[a-zA-Z0-9.-]*[a-zA-Z0-9])?$") && !host.Contains(".."),
                "Enter your mail server, such as mail.example.com.", tab);
        }
        void Port(TextBox field, int tab) => Check(field,
            int.TryParse(field.Text, out var port) && port is >= 1 and <= 65535,
            "Enter a port number from 1 to 65535.", tab);

        Check(_label, _label.Text.Trim().Length is > 0 and <= 100,
            "Enter a connection name with 100 characters or fewer.", 0);
        var email = _email.Text.Trim();
        Check(_email, email.Length is > 0 and <= 254
            && Regex.IsMatch(email, @"^[A-Za-z0-9.!#$%&'*+/=?^_`{|}~-]+@[A-Za-z0-9](?:[A-Za-z0-9.-]*[A-Za-z0-9])?$")
            && !email.Contains("..") && !email.StartsWith('.') && !email.Contains(".@"),
            "Enter your email address, such as you@example.com.", 0);
        Check(_tenantId, _authentication.SelectedIndex != 1 || _tenant.SelectedIndex != 3
            || Guid.TryParse(_tenantId.Text.Trim(), out _), "Enter the organization ID from your IT team.", 0);
        Server(_incomingHost, 1);
        Port(_incomingPort, 1);
        if (_authentication.SelectedIndex == 1)
            Check(_incomingUser, string.Equals(_incomingUser.Text.Trim(), email, StringComparison.OrdinalIgnoreCase),
                "Use the same email address as in Account for Microsoft.", 1);
        else Required(_incomingUser, "Enter the email address used to read your mail.", 1);
        Check(_incomingPassword, _authentication.SelectedIndex != 0
            || CanKeepPassword("incoming", _incomingHost, _incomingPort, _incomingTls, _incomingUser)
            || _incomingPassword.Password.Length > 0, "Enter your mail password or app password. Changed server settings need it again.", 1);
        if (_configureSmtp.IsOn)
        {
            Server(_smtpHost, 2);
            Port(_smtpPort, 2);
            if (_authentication.SelectedIndex == 1)
                Check(_smtpUser, string.Equals(_smtpUser.Text.Trim(), email, StringComparison.OrdinalIgnoreCase),
                    "Use the same email address as in Account for Microsoft.", 2);
            else Required(_smtpUser, "Enter the email address used to send your mail.", 2);
            Check(_smtpPassword, _authentication.SelectedIndex != 0
                || CanKeepPassword("smtp", _smtpHost, _smtpPort, _smtpTls, _smtpUser)
                || _smtpPassword.Password.Length > 0, "Enter the password or app password for sending. Changed server settings need it again.", 2);
        }
        else
        {
            foreach (var field in new Control[] { _smtpHost, _smtpPort, _smtpUser, _smtpPassword }) SetAccountFieldError(field, null);
        }
        if (focus && first is not null)
        {
            _accountTabs.SelectedIndex = firstTab;
            // Tab content is attached after layout; focus it on the next UI turn.
            DispatcherQueue.TryEnqueue(() => { first.StartBringIntoView(); first.Focus(FocusState.Programmatic); });
        }
        _accountValidationSummary = "Check the fields marked in red.\n" + string.Join("\n", messages.Distinct());
        return first is null;
    }
}
