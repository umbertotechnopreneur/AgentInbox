// SPDX-License-Identifier: MIT

using System.ComponentModel;
using MailMeUp.Desktop.Services;
using MailMeUp.Desktop.ViewModels;
using MailMeUp.Desktop.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace MailMeUp.Desktop;

public sealed partial class MainWindow
{
    private async Task SignInMicrosoftAsync()
    {
        try { await _configuration.SignInMicrosoftAsync(ShowMicrosoftSignInAsync, _lifetime.Token); }
        catch (EngineException) { FocusAccountError(); throw; }
    }

    /// <summary>Shows native browser and clipboard actions; the model owns the sign-in lifecycle.</summary>
    private async Task ShowMicrosoftSignInAsync(MicrosoftSignInModel model, CancellationToken cancellationToken)
    {
        var status = Text("");
        ModelBinding.Bind(status, TextBlock.TextProperty, model, nameof(MicrosoftSignInModel.Status));
        var userCode = new TextBox
        {
            Width = 240, IsReadOnly = true, FontSize = 24, FontFamily = new FontFamily("Consolas"),
            TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
        ModelBinding.Bind(userCode, TextBox.TextProperty, model, nameof(MicrosoftSignInModel.UserCode));
        AutomationProperties.SetName(userCode, "Microsoft sign-in code");
        var copyCode = Action("Copy code", () =>
        {
            try { CopyText(model.UserCode); model.CodeCopied(); }
            catch (EngineException exception) { model.ClipboardFailed(exception.Message); }
        }, tooltip: "Copy this code to paste on Microsoft's sign-in page.");
        copyCode.Content = new SymbolIcon(Symbol.Copy);
        copyCode.HorizontalAlignment = HorizontalAlignment.Left;
        copyCode.VerticalAlignment = VerticalAlignment.Center;
        var codeRow = new Grid { ColumnSpacing = 8 };
        codeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        codeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        codeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(userCode, 1);
        Grid.SetColumn(copyCode, 2);
        codeRow.Children.Add(userCode);
        codeRow.Children.Add(copyCode);
        var codeLabel = Text("Enter this code");
        codeLabel.TextAlignment = TextAlignment.Center;
        var openBrowser = Action("Open Microsoft", async () =>
        {
            try { model.BrowserOpened(model.VerificationUri is { } uri && await Windows.System.Launcher.LaunchUriAsync(uri)); }
            catch (Exception) { model.BrowserOpened(false); }
        }, accent: true);
        var address = Text("");
        ModelBinding.Bind(address, TextBlock.TextProperty, model, nameof(MicrosoftSignInModel.VerificationAddress));
        var expiry = Hint("");
        ModelBinding.Bind(expiry, TextBlock.TextProperty, model, nameof(MicrosoftSignInModel.ExpiryHint));
        var details = SignInLogBox();
        ModelBinding.Bind(details, TextBox.TextProperty, model, nameof(MicrosoftSignInModel.Log));
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot, Title = "Sign in with Microsoft", CloseButtonText = "Cancel",
            CloseButtonStyle = DialogButtonStyle("Cancel Microsoft sign-in."), DefaultButton = ContentDialogButton.Close,
            Content = Stack(Hint("Press Open Microsoft, enter this code on the page, then sign in with your Microsoft account."),
                codeLabel, codeRow, address, openBrowser, status, expiry, SignInDetails(details))
        };
        void ModelChanged(object? sender, PropertyChangedEventArgs args) { if (model.IsFinished) dialog.Hide(); }
        model.PropertyChanged += ModelChanged;
        using var closing = cancellationToken.Register(() => DispatcherQueue.TryEnqueue(dialog.Hide));
        try { if (!model.IsFinished) await dialog.ShowAsync(); }
        finally { model.PropertyChanged -= ModelChanged; }
    }

    private static TextBox SignInLogBox()
    {
        var box = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 180 };
        ScrollViewer.SetVerticalScrollBarVisibility(box, ScrollBarVisibility.Auto);
        AutomationProperties.SetName(box, "Microsoft sign-in details, with private sign-in values hidden");
        return box;
    }

    private Expander SignInDetails(params UIElement[] content)
    {
        var expander = new Expander
        {
            Header = "Sign-in details", HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, Content = Stack(content)
        };
        ModelBinding.Bind(expander, Expander.IsExpandedProperty, _configuration.SignIn, nameof(MicrosoftSignInModel.DetailsExpanded), BindingMode.TwoWay);
        return expander;
    }
}
