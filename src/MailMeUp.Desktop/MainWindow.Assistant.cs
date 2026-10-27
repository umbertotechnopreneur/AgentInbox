/* VBWR B
 *
 * Project: AgentInbox
 * Repository: https://github.com/umbertotechnopreneur/AgentInbox
 * Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
 *
 * VibeWare: Human intent, AI execution, and plenty of tokens
 * Manifesto: https://umbertogiacobbi.biz/vibeware/manifesto
 *
 * File: Assistant connection page, confirmation and explicit clipboard actions.
 * Created with AI: OpenAI Codex; extracted assistant-page composition, 2026-10-27.
 * Modified with AI: OpenAI Codex; code readability and documentation, 2026-10-27.
 * Human guidance: Umberto Giacobbi; document parameters and simplify maintenance.
 * Evidence: docs/VALIDATION.md (source review; execution checks pending).
 *
 * Copyright (c) 2026 Umberto Giacobbi
 * License: MIT - see LICENSE
 * SPDX-License-Identifier: MIT
 *
 * VBWR E */

using MailMeUp.Desktop.Services;
using MailMeUp.Desktop.ViewModels;
using MailMeUp.Desktop.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;

namespace MailMeUp.Desktop;

/// <summary>Configures email accounts and assistant access on Windows.</summary>
public sealed partial class MainWindow
{
    /// <summary>Detect local assistant settings before rendering connection actions.</summary>
    private async Task BuildAssistantPageAsync()
    {
        Page.Children.Clear();
        Page.Children.Add(Heading("Connect Codex", ""));
        Page.Children.Add(Hint("Checking Codex on this computer…"));
        await _configuration.Assistant.DetectAsync(_lifetime.Token);
        RenderAssistantPage();
    }

    /// <summary>Compose guided setup, local verification and manual connection details.</summary>
    private void RenderAssistantPage()
    {
        var model = _configuration.Assistant;
        Page.Children.Clear();
        Page.Children.Add(PageHeading("Connect Codex", "One connection for all enabled mailboxes and their selected folders. Use your AI agent across accounts and providers.",
            IconAction("Check Codex again", "\uE72C", 0x2477C8, () => _ = RunAsync(BuildAssistantPageAsync),
                "Look for Codex and check its saved AgentInbox connection.")));
        // Create text bound to one assistant-connection presentation property.
        // property: Observable property name or native dependency property being updated.
        // hint: Model tooltip property name or flag selecting supporting text styling.
        // size: Requested text size in logical pixels.
        TextBlock Label(string property, bool hint = false, double size = 0)
        {
            var label = hint ? Hint("") : Text("", size > 0 ? size : 14);
            ModelBinding.Bind(label, TextBlock.TextProperty, model, property);
            return label;
        }
        // Create a read-only field bound to assistant-connection details.
        // header: Visible header of the read-only assistant details field.
        // property: Observable property name or native dependency property being updated.
        // multiline: Whether the read-only field accepts multiline presentation.
        TextBox FieldFor(string header, string property, bool multiline = false)
        {
            var box = new TextBox { Header = header, IsReadOnly = true, AcceptsReturn = multiline,
                TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Stretch };
            ModelBinding.Bind(box, TextBox.TextProperty, model, property);
            return box;
        }

        // Bind setup status and explicit connection actions to the existing model.
        var status = Label(nameof(AssistantConnectionModel.StatusText));
        status.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        ModelBinding.Visible(status, model, nameof(AssistantConnectionModel.ShowStatus));
        ModelBinding.Bind(status, TextBlock.ForegroundProperty, model, nameof(AssistantConnectionModel.StatusTone), converter: ModelBinding.ToneConverter.Instance);
        var connect = Action("Connect to Codex", () => _ = RunAsync(ConnectAssistantAsync), accent: true,
            tooltip: "Add this AgentInbox installation to Codex's local connections.");
        ModelBinding.Bind(connect, ContentControl.ContentProperty, model, nameof(AssistantConnectionModel.ConnectText));
        ModelBinding.Bind(connect, AutomationProperties.NameProperty, model, nameof(AssistantConnectionModel.ConnectName));
        ModelBinding.Bind(connect, Control.IsEnabledProperty, _configuration, nameof(ConfigurationModel.CanConnectAssistant));
        ModelBinding.Visible(connect, model, nameof(AssistantConnectionModel.ShowConnect));
        var setupMessage = Label(nameof(AssistantConnectionModel.Message));
        ModelBinding.Visible(setupMessage, model, nameof(AssistantConnectionModel.IsConfigured), invert: true);
        Page.Children.Add(Section("", status, setupMessage, connect));
        var path = FieldFor("Codex settings file", nameof(AssistantConnectionModel.ConfigurationPath));

        // Local launch verification and a request for a new chat are separate steps.
        var checkStatus = Label(nameof(AssistantConnectionModel.CheckStatus), hint: true);
        ModelBinding.Bind(checkStatus, TextBlock.ForegroundProperty, model, nameof(AssistantConnectionModel.CheckTone), converter: ModelBinding.ToneConverter.Instance);
        var test = Action("Test connection", () => _ = RunAsync(() => model.TestAsync(_lifetime.Token)),
            tooltip: "Check the saved Codex settings, start AgentInbox and ask for its tool list. No email is read or sent.");
        ModelBinding.Bind(test, Control.IsEnabledProperty, _configuration, nameof(ConfigurationModel.CanTestAssistant));
        Page.Children.Add(Section("1. Check AgentInbox on this PC",
            Hint("This checks that AgentInbox can start and reply. It does not read or send email."),
            test, checkStatus));
        var prompt = FieldFor("", nameof(AssistantConnectionModel.TryPrompt), multiline: true);
        AutomationProperties.SetName(prompt, "Request to paste into Codex");
        var tryChat = Section("2. Try it in Codex", Hint("Open a new Codex chat and paste this request:"), prompt,
            Action("Copy this prompt", () => _ = RunAsync(() =>
            {
                CopyText(model.TryPrompt);
                ShowNotice("Prompt copied", "Paste it into a new Codex chat to check the connection.", InfoBarSeverity.Informational);
                return Task.CompletedTask;
            }), tooltip: "Copy the first request to try in a new Codex chat."),
            Hint("If Codex cannot connect, restart Codex and open a new chat. A successful local check does not verify the connection inside your chat."));
        ModelBinding.Visible(tryChat, model, nameof(AssistantConnectionModel.IsConfigured));
        Page.Children.Add(tryChat);
        var afterConnecting = Hint("After connecting, open a new chat in Codex to start using AgentInbox.");
        ModelBinding.Visible(afterConnecting, model, nameof(AssistantConnectionModel.IsConfigured), invert: true);
        Page.Children.Add(afterConnecting);

        // Keep manual launcher details together behind the native expander.
        var app = FieldFor("", nameof(AssistantConnectionModel.Command));
        ModelBinding.Bind(app, TextBox.HeaderProperty, model, nameof(AssistantConnectionModel.AppLabel));
        var arguments = FieldFor("Startup options", nameof(AssistantConnectionModel.Arguments), multiline: true);
        arguments.MinHeight = 80;
        arguments.MaxHeight = 140;
        var profile = FieldFor("AgentInbox settings folder", nameof(AssistantConnectionModel.DataDirectory));
        var json = FieldFor("Full local settings (JSON)", nameof(AssistantConnectionModel.Configuration), multiline: true);
        json.Height = 220;
        json.FontFamily = new FontFamily("Consolas");
        json.FontSize = 12;
        ScrollViewer.SetVerticalScrollBarVisibility(json, ScrollBarVisibility.Auto);
        var copy = Action("Copy full settings", () => _ = RunAsync(() =>
        {
            CopyText(model.Configuration);
            ShowNotice("Settings copied", "These JSON settings are for manual setup in another app that supports MCP. Codex saves its own entry in the settings file shown above.", InfoBarSeverity.Informational);
            return Task.CompletedTask;
        }), tooltip: "Copy the local connection settings for an assistant that supports MCP.");
        var details = new Expander
        {
            Header = "Connection details", IsExpanded = false, HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = Stack(Hint("Codex starts AgentInbox locally through MCP. Test mailbox access separately under Accounts."),
                path, Label(nameof(AssistantConnectionModel.DetailsHint), hint: true), app, arguments, profile,
                Hint("Use the JSON below for manual setup in another MCP app."), json, copy)
        };
        ModelBinding.Bind(details, Expander.IsExpandedProperty, model, nameof(AssistantConnectionModel.DetailsExpanded), BindingMode.TwoWay);
        Page.Children.Add(details);
    }
    /// <summary>Copies only text the user explicitly chooses to put on the clipboard.</summary>
    /// <param name="text">Text explicitly chosen for copying, or the model property supplying a button label.</param>
    private static void CopyText(string text)
    {
        try
        {
            var package = new DataPackage();
            package.SetText(text);
            Clipboard.SetContent(package);
        }
        catch (Exception) { throw new EngineException("Windows could not copy the text. Try again."); }
    }

    /// <summary>Confirm replacement when needed and save the selected assistant connection.</summary>
    private async Task ConnectAssistantAsync()
    {
        var replaceExisting = false;
        if (_configuration.Assistant.RequiresConfirmation)
        {
            var confirmation = Dialog("Update the Codex connection?", Stack(Text("Use this app's settings for AgentInbox in Codex?"),
                Hint("This replaces the existing AgentInbox entry. Your other connections stay as they are.")), "Update connection");
            if (await confirmation.ShowAsync() != ContentDialogResult.Primary) return;
            replaceExisting = true;
        }
        await _configuration.Assistant.ConnectAsync(replaceExisting, _lifetime.Token);
    }
}
