/* VBWR B
 *
 * Project: AgentInbox
 * Repository: https://github.com/umbertotechnopreneur/AgentInbox
 * Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
 *
 * VibeWare: Human intent, AI execution, and plenty of tokens
 * Manifesto: https://umbertogiacobbi.biz/vibeware/manifesto
 *
 * File: Configuration-window lifetime, navigation and operation notifications.
 * Modified with AI: OpenAI Codex; code readability and documentation, 2026-10-27.
 * Human guidance: Umberto Giacobbi; document parameters and simplify maintenance.
 * Evidence: docs/VALIDATION.md (source review; execution checks pending).
 *
 * Copyright (c) 2026 Umberto Giacobbi
 * License: MIT - see LICENSE
 * SPDX-License-Identifier: MIT
 *
 * VBWR E */

using System.Runtime.InteropServices;
using MailMeUp.Desktop.Services;
using MailMeUp.Desktop.ViewModels;
using MailMeUp.Desktop.Views;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace MailMeUp.Desktop;

/// <summary>Configures email accounts and assistant access on Windows.</summary>
public sealed partial class MainWindow : Window
{
    private readonly ConfigurationModel _configuration;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _loaded;
    private ListView _accountPicker = new();
    private TabView _accountTabs = new();
    private StackPanel _accountToolbar = new();
    private Grid _accountWorkspace = new();
    private StackPanel _accountEditor = new();
    private Grid _accountSelectionState = new();
    private Grid _accountEmptyState = new();
    private Button _createFirstAccount = new();
    private Button _removeAccount = new();
    private Button _cancelAccount = new();
    private TextBlock _newAccountHeading = new();
    private ComboBox _preset = new();
    private TextBox _label = new();
    private TextBox _email = new();
    private ComboBox _authentication = new();
    private TextBlock _authenticationHint = new();
    private HyperlinkButton _providerHelpLink = new();
    private ComboBox _tenant = new();
    private TextBox _tenantId = new();
    private StackPanel _microsoftFields = new();
    private Button _microsoftSignIn = new();
    private TextBox _microsoftLog = new();
    private ComboBox _incomingProtocol = new();
    private TextBox _incomingHost = new();
    private TextBox _incomingPort = new();
    private ComboBox _incomingTls = new();
    private TextBox _incomingUser = new();
    private PasswordBox _incomingPassword = new();
    private TextBlock _incomingSignInHint = new();
    private ListView _folders = new();
    private Button _checkConnection = new();
    private TextBlock _folderHint = new();
    private ToggleSwitch _readEnabled = new();
    private ToggleSwitch _configureSmtp = new();
    private ToggleSwitch _sendEnabled = new();
    private TextBox _smtpHost = new();
    private TextBox _smtpPort = new();
    private ComboBox _smtpTls = new();
    private TextBox _smtpUser = new();
    private PasswordBox _smtpPassword = new();
    private TextBlock _smtpSignInHint = new();
    private StackPanel _smtpFields = new();
    private Button _save = new();
    private TextBlock _saveStatus = new();
    private Button _testRead = new();
    private StackPanel _incomingChecks = new();
    private StackPanel _smtpChecks = new();
    private Button _testSmtp = new();

    /// <summary>Initialize model bindings, window size and lifetime cancellation.</summary>
    /// <param name="engine">Shared adapter for the local Python management contract.</param>
    internal MainWindow(PythonEngine engine)
    {
        _configuration = new ConfigurationModel(engine, DesktopSettings.Load().MicrosoftClientId);
        InitializeComponent();
        Root.DataContext = _configuration;
        BindApplicationState();
        _configuration.Editor.PropertyChanged += (_, _) => { RenderAccountErrors(); RenderFolderSelection(); };
        SystemBackdrop = new DesktopAcrylicBackdrop();
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var dpi = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
        if (dpi == 0) throw new InvalidOperationException("The window display scale could not be determined.");
        var scale = dpi / 96.0;
        // AppWindow uses physical pixels; size the form in logical pixels at the monitor's scale.
        var width = Math.Min((int)Math.Round(1280 * scale), area.Width);
        var height = Math.Min((int)Math.Round(960 * scale), area.Height);
        AppWindow.MoveAndResize(new RectInt32(area.X + (area.Width - width) / 2,
            area.Y + (area.Height - height) / 2, width, height));
        SaveBar.SizeChanged += (_, args) =>
            PageActions.Orientation = args.NewSize.Width < 480 ? Orientation.Vertical : Orientation.Horizontal;
        PageScroll.SizeChanged += (_, _) => UpdateAccountLayout();
        Closed += (_, _) => _lifetime.Cancel();
    }

    /// <summary>Read the display scale associated with the native window.</summary>
    /// <param name="window">Native window handle or WinUI window whose ownership or activation is being changed.</param>
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint GetDpiForWindow(IntPtr window);

    /// <summary>Select the initial configuration page once the root is loaded.</summary>
    /// <param name="sender">Native event source; business state is obtained from the model.</param>
    /// <param name="args">Arguments supplied by the launch or WinUI event; no mail operation is inferred from them.</param>
    private void Root_Loaded(object sender, RoutedEventArgs args)
    {
        if (_loaded) return;
        _loaded = true;
        Navigation.SelectedItem = Navigation.MenuItems[0];
    }

    /// <summary>Show the native mail icon when a provider favicon cannot load.</summary>
    /// <param name="sender">Native event source; business state is obtained from the model.</param>
    /// <param name="args">Arguments supplied by the launch or WinUI event; no mail operation is inferred from them.</param>
    private void ProviderLogo_Failed(object sender, ExceptionRoutedEventArgs args)
    {
        if (sender is Image image && image.Parent is Grid grid)
        {
            image.Opacity = 0;
            grid.Children[0].Opacity = 0.55;
        }
    }

    /// <summary>Bind an account status icon to its session check result.</summary>
    /// <param name="sender">Native event source; business state is obtained from the model.</param>
    /// <param name="args">Arguments supplied by the launch or WinUI event; no mail operation is inferred from them.</param>
    private void AccountStatus_DataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        if (sender is FontIcon icon && args.NewValue is AccountListItemModel model)
            ModelBinding.Bind(icon, IconElement.ForegroundProperty, model, nameof(AccountListItemModel.CheckTone), converter: ModelBinding.ToneConverter.Instance);
    }

    /// <summary>Replace the fallback mail icon with the loaded provider favicon.</summary>
    /// <param name="sender">Native event source; business state is obtained from the model.</param>
    /// <param name="args">Arguments supplied by the launch or WinUI event; no mail operation is inferred from them.</param>
    private void ProviderLogo_Opened(object sender, RoutedEventArgs args)
    {
        if (sender is Image image && image.Parent is Grid grid)
        {
            image.Opacity = 1;
            grid.Children[0].Opacity = 0;
        }
    }

    /// <summary>Open the selected configuration page after initialization.</summary>
    /// <param name="sender">Native event source; business state is obtained from the model.</param>
    /// <param name="args">Arguments supplied by the launch or WinUI event; no mail operation is inferred from them.</param>
    private async void Navigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (!_loaded || args.SelectedItem is not NavigationViewItem item) return;
        if (item.Tag is string page) await OpenPageAsync(page);
    }

    /// <summary>Build the requested setup page and clear incomplete content on failure.</summary>
    /// <param name="page">Known navigation page key, accounts or assistant.</param>
    private async Task OpenPageAsync(string page)
    {
        await RunAsync(async () =>
        {
            Page.Children.Clear();
            PageActions.Children.Clear();
            _configuration.Page = page;
            PageScroll.ChangeView(0, 0, null, disableAnimation: true);
            try
            {
                switch (page)
                {
                    case "accounts":
                        BuildAccountsPage();
                        if (!_configuration.AccountsLoaded) await LoadAccountsAsync();
                        break;
                    case "assistant": await BuildAssistantPageAsync(); break;
                    default: throw new InvalidOperationException("Unknown setup page.");
                }
            }
            catch
            {
                // Never leave a partially constructed form with actionable Save or Cancel buttons.
                Page.Children.Clear();
                PageActions.Children.Clear();
                _configuration.Page = "";
                Page.Children.Add(Text("This page could not be opened", 22));
                Page.Children.Add(Hint("You can try again or choose another page."));
                Page.Children.Add(Action("Try again", () => _ = OpenPageAsync(page),
                    tooltip: "Reload this page without repeating a mail operation."));
                throw;
            }
        });
    }

    /// <summary>Run the model operation and present its outcome and pending cleanup separately.</summary>
    /// <param name="operation">Explicit operation name or asynchronous action forwarded to the shared model.</param>
    private async Task RunAsync(Func<Task> operation)
    {
        if (_configuration.IsBusy || _lifetime.IsCancellationRequested) return;
        await _configuration.RunAsync(operation, _lifetime.Token);
        if (_lifetime.IsCancellationRequested) return;
        if (_configuration.Notice.IsOpen)
        {
            var notice = _configuration.Notice;
            notice.IsOpen = false;
            using var window = new NotificationWindow(this, notice.Title, notice.Message, notice.Severity);
            window.Activate();
            await window.ClosedTask;
        }
        if (_configuration.Cleanup.IsOpen && !_lifetime.IsCancellationRequested)
        {
            var cleanup = _configuration.Cleanup;
            cleanup.IsOpen = false;
            using var window = new NotificationWindow(this, cleanup.Title, cleanup.Message, cleanup.Severity, allowRetry: true);
            window.Activate();
            await window.ClosedTask;
            if (window.RetryRequested) await RunAsync(() => _configuration.RetryCleanupAsync(_lifetime.Token));
        }
    }

    /// <summary>Publish a model notification for presentation by the window adapter.</summary>
    /// <param name="title">User-facing heading for this page, dialog or notification.</param>
    /// <param name="message">Safe user-facing outcome or guidance; never raw credentials or mail content.</param>
    /// <param name="severity">Notification severity used for presentation.</param>
    private void ShowNotice(string title, string message, InfoBarSeverity severity) =>
        _configuration.Notice.Show(title, message, (int)severity);

    /// <summary>Keeps legal links on the left and the VibeWare signature on the right at smaller widths.</summary>
    /// <param name="sender">Native event source; business state is obtained from the model.</param>
    /// <param name="args">Arguments supplied by the launch or WinUI event; no mail operation is inferred from them.</param>
    private void BrandFooter_SizeChanged(object sender, SizeChangedEventArgs args)
    {
        var compact = args.NewSize.Width < 760;
        Grid.SetColumnSpan(FooterLinks, compact ? 2 : 1);
        FooterLinks.Orientation = args.NewSize.Width < 360 ? Orientation.Vertical : Orientation.Horizontal;
        Grid.SetRow(VibeWareLink, compact ? 1 : 0);
        Grid.SetColumn(VibeWareLink, compact ? 0 : 1);
        Grid.SetColumnSpan(VibeWareLink, compact ? 2 : 1);
    }

}
