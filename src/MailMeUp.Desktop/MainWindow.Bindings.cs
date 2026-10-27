/* VBWR B
 *
 * Project: AgentInbox
 * Repository: https://github.com/umbertotechnopreneur/AgentInbox
 * Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
 *
 * VibeWare: Human intent, AI execution, and plenty of tokens
 * Manifesto: https://umbertogiacobbi.biz/vibeware/manifesto
 *
 * File: Native account-control bindings to the retained observable draft.
 * Modified with AI: OpenAI Codex; code readability and documentation, 2026-10-27.
 * Human guidance: Umberto Giacobbi; document parameters and simplify maintenance.
 * Evidence: docs/VALIDATION.md (source review; execution checks pending).
 *
 * Copyright (c) 2026 Umberto Giacobbi
 * License: MIT - see LICENSE
 * SPDX-License-Identifier: MIT
 *
 * VBWR E */

using MailMeUp.Desktop.ViewModels;
using MailMeUp.Desktop.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;

namespace MailMeUp.Desktop;

public sealed partial class MainWindow
{
    private readonly Dictionary<string, Control> _accountFields = [];
    private bool _renderingFolders;

    /// <summary>Bind navigation and action availability to observable configuration state.</summary>
    private void BindApplicationState()
    {
        ModelBinding.Bind(Navigation, Control.IsEnabledProperty, _configuration, nameof(ConfigurationModel.IsIdle));
        ModelBinding.Visible(Progress, _configuration, nameof(ConfigurationModel.IsBusy));
        ModelBinding.Visible(SaveBar, _configuration, nameof(ConfigurationModel.ShowSaveBar));
        _configuration.PropertyChanged += (_, _) => UpdateAccountLayout();
    }

    /// <summary>Bind account controls to the existing editor draft and forward native selection events.</summary>
    private void BindAccountControls()
    {
        _accountFields.Clear();
        void TextField(TextBox field, string property)
        {
            ModelBinding.Bind(field, TextBox.TextProperty, _configuration.Editor, property, BindingMode.TwoWay);
            ModelBinding.Bind(field, Control.IsEnabledProperty, _configuration, nameof(ConfigurationModel.IsIdle));
            _accountFields[property] = field;
        }
        void Choice(ComboBox field, string property)
        {
            ModelBinding.Bind(field, ComboBox.SelectedIndexProperty, _configuration.Editor, property, BindingMode.TwoWay);
            ModelBinding.Bind(field, Control.IsEnabledProperty, _configuration, nameof(ConfigurationModel.IsIdle));
            _accountFields[property] = field;
        }
        void PasswordField(PasswordBox field, string property)
        {
            ModelBinding.Bind(field, PasswordBox.PasswordProperty, _configuration.Editor, property, BindingMode.TwoWay);
            ModelBinding.Bind(field, Control.IsEnabledProperty, _configuration, nameof(ConfigurationModel.IsIdle));
            _accountFields[property] = field;
        }
        TextField(_label, nameof(AccountEditorModel.Label));
        TextField(_email, nameof(AccountEditorModel.Email));
        TextField(_tenantId, nameof(AccountEditorModel.OrganizationId));
        TextField(_incomingHost, nameof(AccountEditorModel.IncomingHost));
        TextField(_incomingPort, nameof(AccountEditorModel.IncomingPort));
        TextField(_incomingUser, nameof(AccountEditorModel.IncomingEmail));
        TextField(_smtpHost, nameof(AccountEditorModel.SendingHost));
        TextField(_smtpPort, nameof(AccountEditorModel.SendingPort));
        TextField(_smtpUser, nameof(AccountEditorModel.SendingEmail));
        Choice(_preset, nameof(AccountEditorModel.PresetIndex));
        ModelBinding.Bind(_authentication, ItemsControl.ItemsSourceProperty, _configuration.Editor, nameof(AccountEditorModel.AuthenticationChoices));
        Choice(_authentication, nameof(AccountEditorModel.AuthenticationIndex));
        Choice(_tenant, nameof(AccountEditorModel.TenantIndex));
        Choice(_incomingProtocol, nameof(AccountEditorModel.ProtocolIndex));
        Choice(_incomingTls, nameof(AccountEditorModel.IncomingSecurityIndex));
        Choice(_smtpTls, nameof(AccountEditorModel.SendingSecurityIndex));
        PasswordField(_incomingPassword, nameof(AccountEditorModel.IncomingPassword));
        PasswordField(_smtpPassword, nameof(AccountEditorModel.SendingPassword));
        ModelBinding.Bind(_readEnabled, ToggleSwitch.IsOnProperty, _configuration.Editor, nameof(AccountEditorModel.ReadEnabled), BindingMode.TwoWay);
        ModelBinding.Bind(_configureSmtp, ToggleSwitch.IsOnProperty, _configuration.Editor, nameof(AccountEditorModel.ConfigureSending), BindingMode.TwoWay);
        ModelBinding.Bind(_sendEnabled, ToggleSwitch.IsOnProperty, _configuration.Editor, nameof(AccountEditorModel.SendEnabled), BindingMode.TwoWay);
        foreach (var control in new Control[] { _readEnabled, _configureSmtp, _sendEnabled, _checkConnection, _microsoftSignIn, _cancelAccount, _createFirstAccount })
            ModelBinding.Bind(control, Control.IsEnabledProperty, _configuration, nameof(ConfigurationModel.IsIdle));
        ModelBinding.Visible(_smtpFields, _configuration.Editor, nameof(AccountEditorModel.ConfigureSending));
        ModelBinding.Visible(_smtpChecks, _configuration.Editor, nameof(AccountEditorModel.ConfigureSending));
        ModelBinding.Visible(_incomingChecks, _configuration.Editor, nameof(AccountEditorModel.HasSavedAccount));
        ModelBinding.Bind(_testRead, Control.IsEnabledProperty, _configuration, nameof(ConfigurationModel.CanCheckReading));
        ModelBinding.Bind(_testSmtp, Control.IsEnabledProperty, _configuration, nameof(ConfigurationModel.CanCheckSending));
        ModelBinding.Visible(_tenantId, _configuration.Editor, nameof(AccountEditorModel.UsesOrganizationId));
        ModelBinding.Visible(_microsoftFields, _configuration.Editor, nameof(AccountEditorModel.IsMicrosoft));
        foreach (var field in new Control[] { _incomingPassword, _smtpPassword })
            ModelBinding.Visible(field, _configuration.Editor, nameof(AccountEditorModel.IsMicrosoft), invert: true);
        foreach (var hint in new[] { _incomingSignInHint, _smtpSignInHint })
            ModelBinding.Visible(hint, _configuration.Editor, nameof(AccountEditorModel.IsMicrosoft));
        ModelBinding.Bind(_incomingPassword, PasswordBox.PlaceholderTextProperty, _configuration.Editor, nameof(AccountEditorModel.IncomingPasswordPlaceholder));
        ModelBinding.Bind(_smtpPassword, PasswordBox.PlaceholderTextProperty, _configuration.Editor, nameof(AccountEditorModel.SendingPasswordPlaceholder));
        ModelBinding.Bind(_incomingHost, TextBox.HeaderProperty, _configuration.Editor, nameof(AccountEditorModel.IncomingServerLabel));
        ModelBinding.Bind(_incomingHost, TextBox.PlaceholderTextProperty, _configuration.Editor, nameof(AccountEditorModel.IncomingServerPlaceholder));
        ModelBinding.Bind(_folderHint, TextBlock.TextProperty, _configuration.Editor, nameof(AccountEditorModel.FolderHint));
        ModelBinding.Bind(_authenticationHint, TextBlock.TextProperty, _configuration.Editor, nameof(AccountEditorModel.ProviderHint));
        ModelBinding.Bind(_providerHelpLink, ContentControl.ContentProperty, _configuration.Editor, nameof(AccountEditorModel.ProviderHelpText));
        ModelBinding.Bind(_providerHelpLink, HyperlinkButton.NavigateUriProperty, _configuration.Editor, nameof(AccountEditorModel.ProviderHelpUri));
        ModelBinding.Bind(_providerHelpLink, AutomationProperties.NameProperty, _configuration.Editor, nameof(AccountEditorModel.ProviderHelpText));
        ModelBinding.Bind(_providerHelpLink, ToolTipService.ToolTipProperty, _configuration.Editor, nameof(AccountEditorModel.ProviderHelpText));
        ModelBinding.Visible(_providerHelpLink, _configuration.Editor, nameof(AccountEditorModel.HasProviderHelp));
        ModelBinding.Bind(_testRead, ToolTipService.ToolTipProperty, _configuration.Editor, nameof(AccountEditorModel.ReadCheckHint));
        ModelBinding.Bind(_folders, ItemsControl.ItemsSourceProperty, _configuration.Editor, nameof(AccountEditorModel.AvailableFolders));
        ModelBinding.Bind(_folders, Control.IsEnabledProperty, _configuration, nameof(ConfigurationModel.CanChooseAccountFolders));
        _accountFields[nameof(AccountEditorModel.SelectedFolders)] = _folders;
        _folders.SelectionChanged += (_, _) =>
        {
            if (!_renderingFolders && !_configuration.Editor.IsLoading)
                _configuration.Editor.SelectFolders(_folders.SelectedItems.Cast<string>().ToArray());
        };
        ModelBinding.Bind(_accountPicker, ItemsControl.ItemsSourceProperty, _configuration, nameof(ConfigurationModel.AccountItems));
        ModelBinding.Bind(_accountPicker, ListView.SelectedItemProperty, _configuration, nameof(ConfigurationModel.SelectedAccountItem), BindingMode.TwoWay);
        _accountPicker.ItemTemplate = (DataTemplate)Root.Resources["AccountListTemplate"];
        ModelBinding.Bind(_accountPicker, Control.IsEnabledProperty, _configuration, nameof(ConfigurationModel.IsIdle));
        ModelBinding.Bind(_accountTabs, TabView.SelectedIndexProperty, _configuration, nameof(ConfigurationModel.EditorTab), BindingMode.TwoWay);
        ModelBinding.Bind(_save, Control.IsEnabledProperty, _configuration, nameof(ConfigurationModel.CanSave));
        ModelBinding.Bind(_save, ContentControl.ContentProperty, _configuration, nameof(ConfigurationModel.SaveText));
        ModelBinding.Bind(_save, AutomationProperties.NameProperty, _configuration, nameof(ConfigurationModel.SaveText));
        ModelBinding.Bind(_save, ToolTipService.ToolTipProperty, _configuration, nameof(ConfigurationModel.SaveHint));
        ModelBinding.Bind(_saveStatus, TextBlock.TextProperty, _configuration, nameof(ConfigurationModel.SaveStatus));
        ModelBinding.Bind(_removeAccount, Control.IsEnabledProperty, _configuration, nameof(ConfigurationModel.CanRemoveAccount));
        ModelBinding.Visible(_accountEmptyState, _configuration, nameof(ConfigurationModel.ShowEmptyAccounts));
        ModelBinding.Visible(_accountWorkspace, _configuration, nameof(ConfigurationModel.ShowWorkspace));
        ModelBinding.Visible(_accountToolbar, _configuration, nameof(ConfigurationModel.ShowAccountList));
        ModelBinding.Visible(_accountEditor, _configuration, nameof(ConfigurationModel.EditingAccount));
        ModelBinding.Visible(_accountSelectionState, _configuration, nameof(ConfigurationModel.ShowSelectionPrompt));
        ModelBinding.Visible(_cancelAccount, _configuration, nameof(ConfigurationModel.CreatingAccount));
        ModelBinding.Visible(_newAccountHeading, _configuration, nameof(ConfigurationModel.CreatingAccount));
        ModelBinding.Bind(_microsoftLog, TextBox.TextProperty, _configuration.SignIn, nameof(MicrosoftSignInModel.Log));
        RenderAccountErrors();
        RenderFolderSelection();
    }

    /// <summary>Reflect the model selection in the native multi-select folder list.</summary>
    private void RenderFolderSelection()
    {
        _renderingFolders = true;
        try
        {
            _folders.SelectedItems.Clear();
            foreach (var folder in _configuration.Editor.SelectedFolders) _folders.SelectedItems.Add(folder);
        }
        finally { _renderingFolders = false; }
    }
}
