using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace MailMeUp.Desktop;

public sealed partial class MainWindow
{
    private ContentDialog? _activeSetupDialog;
    private double _activeDialogScrollHeight;

    private async Task<ContentDialogResult> ShowDialogAsync(ContentDialog dialog)
    {
        if (_dialogOpen || _lifetime.IsCancellationRequested) return ContentDialogResult.None;
        _dialogOpen = true;
        _activeSetupDialog = dialog;
        var scroll = dialog.Content as ScrollViewer;
        _activeDialogScrollHeight = scroll is not null && double.IsFinite(scroll.MaxHeight) ? scroll.MaxHeight : 440;
        try
        {
            dialog.XamlRoot = Root.XamlRoot;
            dialog.RequestedTheme = Root.ActualTheme;
            UpdateActiveDialogLayout();
            if (scroll is not null)
            {
                TrackSettingsScrollViewer(scroll);
                dialog.Opened += (_, _) => SetSettingsScrollPointerSurface(dialog);
            }
            using var registration = _lifetime.Token.Register(() => DispatcherQueue.TryEnqueue(() => dialog.Hide()));
            return await dialog.ShowAsync();
        }
        catch (Exception exception)
        {
            _logger.LogWarning("Setup details could not open ({ErrorType})", exception.GetType().Name);
            if (!_lifetime.IsCancellationRequested)
                SetNotice("Details unavailable", "Close the current dialog and try again.", InfoBarSeverity.Warning);
            return ContentDialogResult.None;
        }
        finally
        {
            if (scroll is not null) UntrackSettingsScrollViewer(scroll);
            _activeSetupDialog = null;
            _dialogOpen = false;
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, ApplyPendingStep);
        }
    }

    private void UpdateActiveDialogLayout()
    {
        if (_activeSetupDialog?.Content is ScrollViewer scroll)
            scroll.MaxHeight = Math.Min(_activeDialogScrollHeight, Math.Max(120, Root.ActualHeight - 260));
    }

    private async void AboutButton_Click(object sender, RoutedEventArgs e) => await ShowDialogAsync(new AboutDialog());

    private async void PrivacyButton_Click(object sender, RoutedEventArgs e)
    {
        var content = new StackPanel { Spacing = 18 };
        content.Children.Add(Body("Review the policies for AgentInbox and your account providers. Links open in your browser."));
        content.Children.Add(PolicyLinks("AgentInbox", "https://umbertogiacobbi.biz/agentinbox/privacy/", "https://umbertogiacobbi.biz/agentinbox/terms/"));
        content.Children.Add(PolicyLinks("Google", "https://policies.google.com/privacy?hl=en", "https://policies.google.com/terms?hl=en"));
        content.Children.Add(PolicyLinks("Microsoft", "https://www.microsoft.com/en-us/privacy/privacystatement", "https://www.microsoft.com/en-us/servicesagreement"));
        content.Children.Add(Link("Visit the website", "https://umbertogiacobbi.biz/"));
        await ShowDialogAsync(DetailsDialog("Privacy & terms", content));
    }

    private static StackPanel PolicyLinks(string name, string privacy, string terms)
    {
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(new TextBlock { Text = name, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        var links = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 20 };
        links.Children.Add(Link("Privacy policy", privacy, $"{name} privacy policy"));
        links.Children.Add(Link("Terms", terms, $"{name} terms"));
        panel.Children.Add(links);
        return panel;
    }

    private async void SharingInfoButton_Click(object sender, RoutedEventArgs e)
    {
        var tabs = new Pivot();
        tabs.Items.Add(InformationTab("Read access",
            "AgentInbox can search and read email and calendars.",
            "It cannot send mail, mark messages as read, edit or delete anything, create appointments, or invite anyone."));
        tabs.Items.Add(InformationTab("AI sharing",
            "Your assistant can find sender names, email addresses, subjects, short email previews and calendar summaries in the accounts you share.",
            "If you ask for more detail, your assistant can also read email text, appointment descriptions, attendees and meeting links. This information may be sent to your AI service, even though AgentInbox runs on your computer.",
            "Sign-in tokens stay protected on this device and are never returned to the assistant."));
        tabs.Items.Add(InformationTab("Your choices",
            "New accounts connected here start with sharing off. Enable each account and choose mail, calendars, or both.",
            "Select Done to apply your choices. Turning sharing off stops future access. It cannot remove information already shared in a conversation."));
        await ShowDialogAsync(DetailsDialog("How sharing works", tabs));
    }

    private static PivotItem InformationTab(string title, params string[] paragraphs)
    {
        var panel = new StackPanel { Spacing = 16, Margin = new Thickness(0, 12, 0, 0) };
        foreach (var paragraph in paragraphs) panel.Children.Add(Body(paragraph));
        return new PivotItem { Header = title, Content = panel };
    }

    private async void ProviderSetupButton_Click(object sender, RoutedEventArgs e) =>
        await ConfigureProvidersAsync();

    private async Task ConfigureProvidersAsync()
    {
        if (BlockDemoAction()) return;
        var content = new StackPanel { Spacing = 16 };
        content.Children.Add(Body("Before your first sign-in, Google or Microsoft needs an app registration for AgentInbox. Follow the guide below, then add the setup details here. Each provider is optional: configure one now and add the other later."));
        foreach (var provider in new[] { "google", "microsoft" })
        {
            var line = new StackPanel { Spacing = 6 };
            var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            heading.Children.Add(new Image { Source = ProviderLogo(provider), Width = 24, Height = 24 });
            heading.Children.Add(new TextBlock { Text = ProviderName(provider), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            line.Children.Add(heading);
            line.Children.Add(Body(_providers.Any(item => item.ProviderId == provider && item.Configured) ? "Configured on this device." : "App registration needed."));
            line.Children.Add(Body(provider == "google" ? "Choose the JSON file you downloaded when registering a Desktop app with Google. If you do not have it yet, you can configure Microsoft instead." : "Paste the Application (client) ID from Microsoft. You do not need a client secret. If you do not have it yet, you can configure Google instead."));
            content.Children.Add(line);
        }
        content.Children.Add(Link("App registration guide", "https://github.com/umbertotechnopreneur/AgentInbox/blob/main/docs/APP_REGISTRATION.md"));
        var dialog = DetailsDialog("Set up Google or Microsoft", content);
        dialog.PrimaryButtonText = "Configure Google";
        dialog.SecondaryButtonText = "Configure Microsoft";
        var result = await ShowDialogAsync(dialog);
        if (result == ContentDialogResult.None) return;
        await RunAsync("Configuring provider sign-in…", async token =>
        {
            var configured = result == ContentDialogResult.Primary
                ? await ConfigureGoogleAsync(token)
                : await ConfigureMicrosoftAsync(token, signIn: false);
            if (configured) await RefreshAccountsAsync(token);
        }, TimeSpan.FromMinutes(5));
    }

    private async void ManualSetupButton_Click(object sender, RoutedEventArgs e)
    {
        if (BlockDemoAction()) return;
        if (_busy || _dialogOpen) return;
        try
        {
            var preview = _codex.GetPreview();
            var content = new StackPanel { Spacing = 14 };
            content.Children.Add(Body(_codexStatus?.Message ?? "Refresh status in the setup page to inspect local Codex configuration."));
            content.Children.Add(Body("Use these steps if automatic setup is unavailable. First select Prepare plugin files. Then run both commands in a terminal: the first makes the plugin available in Codex, and the second installs it. If you already connected AgentInbox in Codex, review that connection first to avoid adding it twice."));
            var commands = new TextBox
            {
                Header = "Commands to review", Text = preview.ManualCommands, IsReadOnly = true,
                AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"), MinHeight = 130, MaxHeight = 220
            };
            content.Children.Add(commands);
            var feedback = Body(string.Empty);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            var prepare = new Button { Content = "Prepare plugin files" };
            var copy = new Button { Content = "Copy commands" };
            buttons.Children.Add(prepare);
            buttons.Children.Add(copy);
            content.Children.Add(buttons);
            content.Children.Add(feedback);
            var dialog = DetailsDialog("Set up Codex manually", content);
            dialog.Closing += (_, closing) =>
            {
                if (_busy && !_lifetime.IsCancellationRequested) closing.Cancel = true;
            };
            prepare.Click += async (_, _) =>
            {
                prepare.IsEnabled = false;
                feedback.Text = "Preparing local plugin files…";
                var prepared = false;
                await RunAsync("Preparing local plugin files…", async token =>
                {
                    var result = await _codex.PreparePluginAsync(token);
                    commands.Text = result.ManualCommands;
                    prepared = true;
                });
                feedback.Text = prepared ? "Files prepared. Run the commands to install the plugin." : "Preparation did not finish. You can retry.";
                prepare.IsEnabled = true;
            };
            copy.Click += (_, _) =>
            {
                var data = new DataPackage();
                data.SetText(commands.Text);
                try
                {
                    Clipboard.SetContent(data);
                    feedback.Text = "Commands copied.";
                }
                catch (Exception exception)
                {
                    _logger.LogWarning("Clipboard operation failed ({ErrorType})", exception.GetType().Name);
                    feedback.Text = "Select and copy the commands from the text box.";
                }
            };
            await ShowDialogAsync(dialog);
        }
        catch (Exception exception)
        {
            _logger.LogWarning("Manual setup could not open ({ErrorType})", exception.GetType().Name);
            SetNotice("Manual setup unavailable", "Check access to the local application folder and retry.", InfoBarSeverity.Warning);
        }
    }

    private ContentDialog DetailsDialog(string title, UIElement content)
    {
        var dialog = new ContentDialog
        {
            Title = title, CloseButtonText = "Close", DefaultButton = ContentDialogButton.Close,
            Content = new ScrollViewer
            {
                Content = content, MaxHeight = 440,
                Padding = new Thickness(0, 0, 16, 0),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            }
        };
        dialog.Resources["ContentDialogMaxWidth"] = 640.0;
        return dialog;
    }

    private static HyperlinkButton Link(string label, string uri, string? accessibleName = null)
    {
        var link = new HyperlinkButton { Content = label, NavigateUri = new Uri(uri), Padding = new Thickness(0, 4, 0, 4) };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(link, accessibleName ?? label);
        return link;
    }
}
