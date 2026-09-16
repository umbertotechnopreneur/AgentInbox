using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MailMeUp.Desktop;

public sealed partial class MainWindow
{
    private ContentDialog? _addAccountDialog;
    private ScrollViewer? _addAccountScroll;
    private readonly InfoBar _addAccountNotice = new() { IsClosable = true };
    private string? _pendingAccountAction;

    private async void AddAccountButton_Click(object sender, RoutedEventArgs e) =>
        await ShowAddAccountAsync();

    private async Task ShowAddAccountAsync()
    {
        if (_busy || _dialogOpen || _lifetime.IsCancellationRequested) return;
        do
        {
            _pendingAccountAction = null;
            var opened = false;
            var surface = new StackPanel { Spacing = 16 };
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(0, 0, 16, 0)
            };
            _addAccountScroll = scroll;
            var dialog = new ContentDialog { Title = "Add an account", CloseButtonText = "Cancel", Content = surface };
            dialog.Resources["ContentDialogMaxWidth"] = 640.0;
            _addAccountDialog = dialog;
            dialog.Opened += (_, _) =>
            {
                opened = true;
                UpdateAddAccountLayout();
                SetSettingsScrollPointerSurface(dialog);
            };
            try
            {
                AddAccountParkingHost.Child = null;
                scroll.Content = AddAccountContent;
                surface.Children.Add(scroll);
                surface.Children.Add(_addAccountNotice);
                _addAccountNotice.IsOpen = false;
                TrackSettingsScrollViewer(scroll);
                UpdateAddAccountLayout();
                await ShowDialogAsync(dialog);
            }
            finally
            {
                UntrackSettingsScrollViewer(scroll);
                scroll.Content = null;
                surface.Children.Clear();
                AddAccountParkingHost.Child = AddAccountContent;
                _addAccountDialog = null;
                _addAccountScroll = null;
                if (!opened && !_lifetime.IsCancellationRequested)
                    SetNotice("Could not open account setup", "Try Add account again.", InfoBarSeverity.Warning);
            }
            if (_lifetime.IsCancellationRequested) return;
            if (_pendingAccountAction == "setup")
                await ConfigureProvidersAsync();
            else if (_pendingAccountAction is "google" or "microsoft")
                await ConnectAsync(_pendingAccountAction);
        }
        while (_pendingAccountAction == "setup" && !_lifetime.IsCancellationRequested);
    }

    private void AddGoogleAccountButton_Click(object sender, RoutedEventArgs e) => ChooseAccountProvider("google");

    private void AddMicrosoftAccountButton_Click(object sender, RoutedEventArgs e) => ChooseAccountProvider("microsoft");

    private void ChooseAccountProvider(string provider)
    {
        if (_busy || _addAccountDialog is null || BlockDemoAction()) return;
        if (RequestMail.IsChecked != true && RequestCalendars.IsChecked != true)
        {
            SetNotice("Choose read access", "Select mail, calendars, or both.", InfoBarSeverity.Warning);
            return;
        }
        // Dismiss the picker before starting browser sign-in or a native file picker.
        _pendingAccountAction = provider;
        _addAccountDialog.Hide();
    }

    private void AddAccountProviderSetupButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _addAccountDialog is null || BlockDemoAction()) return;
        _pendingAccountAction = "setup";
        _addAccountDialog.Hide();
    }

    private void UpdateAddAccountLayout()
    {
        if (_addAccountScroll is null) return;
        _addAccountScroll.Width = Math.Min(520, Math.Max(0, Root.ActualWidth - 96));
        _addAccountScroll.MaxHeight = Math.Max(120, Root.ActualHeight - 280);
        var stacked = _addAccountScroll.Width < 510;
        MicrosoftProviderColumn.Width = stacked ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Grid.SetColumn(MicrosoftButton, stacked ? 0 : 1);
        Grid.SetRow(MicrosoftButton, stacked ? 1 : 0);
    }
}
