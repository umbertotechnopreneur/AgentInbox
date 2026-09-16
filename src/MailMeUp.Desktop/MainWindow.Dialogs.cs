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
        content.Children.Add(Body("Review the policies for MailMeUp and your account providers. Links open in your browser."));
        content.Children.Add(PolicyLinks("MailMeUp", "https://umbertogiacobbi.biz/privacy/", "https://umbertogiacobbi.biz/terms/"));
        content.Children.Add(PolicyLinks("Google", "https://policies.google.com/privacy?hl=en", "https://policies.google.com/terms?hl=en"));
        content.Children.Add(PolicyLinks("Microsoft", "https://www.microsoft.com/en-us/privacy/privacystatement", "https://www.microsoft.com/en-us/servicesagreement"));
        content.Children.Add(Link("Visit our website", "https://umbertogiacobbi.biz/"));
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
            "MailMeUp can search and read email and calendars.",
            "It cannot send mail, mark messages as read, edit or delete anything, create appointments, or invite anyone."));
        tabs.Items.Add(InformationTab("AI sharing",
            "Shared accounts can return names and addresses, matching message headers and short previews, and calendar summaries.",
            "When requested, details can include message text, event descriptions, attendees and meeting links. Results may reach your assistant's AI service; running locally does not make the conversation offline.",
            "Sign-in tokens stay protected on this device and are never returned to the assistant."));
        tabs.Items.Add(InformationTab("Your choices",
            "New accounts connected here start with sharing off. Enable each account and choose mail, calendars, or both.",
            "Save choices applies changes to future reads. Turning sharing off cannot withdraw information already returned to a conversation."));
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
        content.Children.Add(Body("This preview requires your own provider app registration. Configure it once before adding accounts."));
        foreach (var provider in new[] { "google", "microsoft" })
        {
            var line = new StackPanel { Spacing = 6 };
            var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            heading.Children.Add(new Image { Source = ProviderLogo(provider), Width = 24, Height = 24 });
            heading.Children.Add(new TextBlock { Text = ProviderName(provider), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            line.Children.Add(heading);
            line.Children.Add(Body(_providers.Any(item => item.ProviderId == provider && item.Configured) ? "Configured on this device." : "App registration needed."));
            line.Children.Add(Body(provider == "google" ? "Import the downloaded Desktop client JSON." : "Enter the Application (client) ID. No client secret is needed."));
            content.Children.Add(line);
        }
        content.Children.Add(Link("App registration guide", "https://github.com/umbertotechnopreneur/MailMeUp/blob/main/docs/APP_REGISTRATION.md"));
        var dialog = DetailsDialog("Provider setup", content);
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
            content.Children.Add(Body("Fallback only: prepare the local plugin files, then run the commands below. Adding the marketplace makes the plugin available; the second command installs it. Review any direct MCP connection first to avoid duplicate tools."));
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
            var dialog = DetailsDialog("Manual setup (fallback)", content);
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
