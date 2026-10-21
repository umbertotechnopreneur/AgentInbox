// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices;
using MailMeUp.Desktop.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace MailMeUp.Desktop.Views;

/// <summary>Presents actual connection progress and explicit folder choices in an owned Acrylic window.</summary>
internal sealed class AccountCheckWindow : Window, IDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<string[]?> _selection = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Dictionary<string, TextBlock> _stages = new(StringComparer.Ordinal);
    private readonly ProgressRing _ring = new() { IsActive = true, Width = 28, Height = 28 };
    private readonly TextBlock _heading = new() { Text = "Checking your account", FontSize = 24, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _message = new() { Text = "Please wait while I check the connection and sign-in.", TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
    private readonly TextBlock _folderHeading = new() { Text = "Choose folders for your assistant", Visibility = Visibility.Collapsed };
    private readonly ListView _folders = new() { SelectionMode = ListViewSelectionMode.Multiple, MaxHeight = 180, Visibility = Visibility.Collapsed };
    private readonly CheckBox _readSample = new() { Content = "Also test reading one message", Visibility = Visibility.Collapsed };
    private readonly Button _primary = new() { Visibility = Visibility.Collapsed, MinWidth = 112 };
    private readonly Button _close = new() { Content = "Cancel", MinWidth = 100 };
    private readonly bool _saveAccount;
    private readonly bool _smtpOnly;
    private bool _working = true;
    private bool _saving;
    private bool _closeWhenReady;
    private bool _isClosed;
    private string? _activeStage;

    /// <summary>Creates a modal, DPI-aware progress window without performing mail or storage work.</summary>
    internal AccountCheckWindow(Window owner, bool smtp, bool saveAccount, bool smtpOnly = false)
    {
        _saveAccount = saveAccount;
        _smtpOnly = smtpOnly;
        if (smtpOnly) _heading.Text = "Checking SMTP connection";
        Title = "AgentInbox";
        SystemBackdrop = new DesktopAcrylicBackdrop();
        var root = new Grid { Padding = new Thickness(24), RowSpacing = 18 };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var heading = new Grid { ColumnSpacing = 14 };
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        heading.Children.Add(_ring);
        Grid.SetColumn(_heading, 1);
        heading.Children.Add(_heading);
        root.Children.Add(heading);
        var body = new StackPanel { Spacing = 14 };
        AddStage(body, "settings", smtpOnly ? "SMTP settings" : "Account settings");
        AddStage(body, "connection", "Secure connection");
        AddStage(body, "authentication", "Sign-in");
        if (!smtpOnly)
        {
            AddStage(body, "folders", "Server folders");
            AddStage(body, "reading", "Read one message (optional)");
            _stages["reading"].Text = "Not requested";
            if (smtp) AddStage(body, "smtp", "SMTP connection and sign-in");
            if (saveAccount) AddStage(body, "save", "Save account");
        }
        body.Children.Add(_folderHeading);
        body.Children.Add(_folders);
        body.Children.Add(_readSample);
        var scroll = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);
        var footer = new StackPanel { Spacing = 14 };
        footer.Children.Add(new Border { Height = 1, Background = (Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"] });
        footer.Children.Add(new ScrollViewer
        {
            Content = _message, MaxHeight = 220,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right };
        _primary.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        actions.Children.Add(_primary);
        actions.Children.Add(_close);
        root.SizeChanged += (_, args) =>
        {
            actions.Orientation = args.NewSize.Width < 460 ? Orientation.Vertical : Orientation.Horizontal;
        };
        footer.Children.Add(actions);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);
        Content = root;
        AutomationProperties.SetName(_folders, "Folders available to your assistant");
        SetButtonDescription(_close, "Cancel this check without saving settings or sending an email.");
        _close.Click += (_, _) => CancelOrClose();
        _primary.Click += (_, _) => ChooseFolders();
        _readSample.Checked += (_, _) => UpdatePrimaryText();
        _readSample.Unchecked += (_, _) => UpdatePrimaryText();
        ToolTipService.SetToolTip(_readSample, "Read one bounded sample from the first selected folder. IMAP keeps its read status; POP3 sends no delete commands. No message content is saved or returned.");
        _folders.SelectionChanged += (_, _) => _primary.IsEnabled = _folders.SelectedItems.Count is >= 1 and <= 20;
        root.KeyDown += (_, args) =>
        {
            if (args.Key != Windows.System.VirtualKey.Escape) return;
            args.Handled = true;
            if (!_saving) CancelOrClose();
        };
        AppWindow.Closing += (_, args) =>
        {
            if (!_working) return;
            args.Cancel = true;
            _closeWhenReady = true;
            if (!_saving) _stop.Cancel();
        };
        Closed += (_, _) =>
        {
            _isClosed = true;
            _selection.TrySetResult(null);
            _closed.TrySetResult();
            if (!_saving) _stop.Cancel();
        };
        OwnAndPosition(owner);
    }

    internal CancellationToken StopToken => _stop.Token;
    internal Task ClosedTask => _closed.Task;
    internal Task<string[]?> FolderSelection => _selection.Task;
    internal bool ReadMessageRequested => _readSample.IsChecked == true;

    /// <summary>Updates only a recognized stage from an actual engine or save result.</summary>
    internal void Report(AccountCheckProgress progress)
    {
        if (_isClosed || !_working || !_stages.TryGetValue(progress.Stage, out var status)) return;
        status.Text = progress.State switch { "passed" => "Passed", "empty" => "Empty folder", _ => "Checking…" };
        _activeStage = progress.State == "started" ? progress.Stage : null;
    }

    /// <summary>Pauses after authentication so only explicitly selected folders are shared.</summary>
    internal void ShowFolders(IReadOnlyList<string> folders, IReadOnlyCollection<string> selected, bool pop)
    {
        if (_isClosed) return;
        _working = false;
        _ring.IsActive = false;
        _heading.Text = "Connection verified";
        _message.Text = pop ? "POP3 provides one mailbox: INBOX. No message was read or sent."
            : $"Found {folders.Count} selectable folders. Choose between one and twenty. No message was read or sent.";
        foreach (var folder in folders)
        {
            _folders.Items.Add(folder);
            if (selected.Contains(folder)) _folders.SelectedItems.Add(folder);
        }
        _folderHeading.Visibility = Visibility.Visible;
        _folders.Visibility = Visibility.Visible;
        _folders.IsEnabled = !pop;
        _readSample.Visibility = Visibility.Visible;
        UpdatePrimaryText();
        _primary.Visibility = Visibility.Visible;
        _primary.IsEnabled = _folders.SelectedItems.Count is >= 1 and <= 20;
        SetButtonDescription(_primary, _saveAccount ? "Save this checked account with only the selected folders." : "Use these folder choices in the account form without saving it.");
        _close.Content = _saveAccount ? "Cancel" : "Close";
        SetButtonDescription(_close, "Close without saving or applying new folder choices.");
        _folders.Focus(FocusState.Programmatic);
        if (_closeWhenReady) Close();
    }

    /// <summary>Shows a separate, cancellable real message-read check before a requested save.</summary>
    internal void BeginReadCheck()
    {
        _saving = false;
        _working = true;
        _ring.IsActive = true;
        _heading.Text = "Testing message reading";
        _message.Text = "I will read one bounded sample from the first selected folder. No email will be sent.";
        _stages["reading"].Text = "Waiting";
        _close.IsEnabled = true;
        _close.Content = "Cancel";
        SetButtonDescription(_close, "Cancel the read check without saving an account.");
    }

    /// <summary>Prevents cancellation of a save whose committed result must be preserved.</summary>
    internal void BeginSaving()
    {
        _saving = true;
        _working = true;
        _ring.IsActive = true;
        _heading.Text = "Saving your account";
        _close.IsEnabled = false;
        _primary.IsEnabled = false;
        Report(new AccountCheckProgress("save", "started"));
    }

    /// <summary>Shows the final verified outcome and keeps the window open until the owner closes it.</summary>
    internal void Complete(bool succeeded, string message)
    {
        if (_isClosed) return;
        if (!succeeded && _activeStage is not null) _stages[_activeStage].Text = "Failed";
        _working = false;
        _saving = false;
        _ring.IsActive = false;
        _heading.Text = _smtpOnly
            ? succeeded ? "SMTP check complete" : "SMTP check stopped"
            : succeeded ? (_saveAccount ? "Account added" : "Account check complete") : "Account check stopped";
        _message.Text = message;
        _primary.Visibility = Visibility.Collapsed;
        _folders.IsEnabled = false;
        _close.Content = "Close";
        _close.IsEnabled = true;
        SetButtonDescription(_close, "Close the connection check window.");
        _close.Focus(FocusState.Programmatic);
        if (_closeWhenReady) Close();
    }

    /// <summary>Releases the cancellation source after the check and owned window have ended.</summary>
    public void Dispose() => _stop.Dispose();

    /// <summary>Adds one open, unboxed progress row with a readable status.</summary>
    private void AddStage(StackPanel panel, string key, string label)
    {
        var row = new Grid { ColumnSpacing = 16 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
        var status = new TextBlock { Text = "Waiting", VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(status, label + " status");
        Grid.SetColumn(status, 1);
        row.Children.Add(status);
        panel.Children.Add(row);
        _stages.Add(key, status);
    }

    /// <summary>Returns an explicit folder choice without performing the requested save.</summary>
    private void ChooseFolders()
    {
        if (_folders.SelectedItems.Count is < 1 or > 20) return;
        _primary.IsEnabled = false;
        _folders.IsEnabled = false;
        _readSample.IsEnabled = false;
        _working = true;
        _saving = _saveAccount && !ReadMessageRequested;
        _close.IsEnabled = !_saving;
        _selection.TrySetResult(_folders.SelectedItems.Cast<string>().ToArray());
    }

    /// <summary>Makes any optional message-read test explicit in the action that will run it.</summary>
    private void UpdatePrimaryText()
    {
        _primary.Content = (_saveAccount, ReadMessageRequested) switch
        {
            (true, true) => "Test reading and add account",
            (true, false) => "Add account",
            (false, true) => "Test reading and use folders",
            _ => "Use selected folders"
        };
        SetButtonDescription(_primary, ReadMessageRequested
            ? "Test reading one message, then apply the selected folders. No email is sent."
            : _saveAccount ? "Save this checked account with only the selected folders." : "Use these folder choices in the form without saving it.");
    }

    /// <summary>Cancels a running check or closes a completed or paused window.</summary>
    private void CancelOrClose()
    {
        if (_saving) return;
        if (!_working) { Close(); return; }
        _closeWhenReady = true;
        _close.IsEnabled = false;
        _stop.Cancel();
    }

    /// <summary>Gives every button a matching accessible action name and tooltip.</summary>
    private static void SetButtonDescription(Button button, string description)
    {
        AutomationProperties.SetName(button, button.Content?.ToString() ?? description);
        ToolTipService.SetToolTip(button, description);
    }

    /// <summary>Uses the owner relationship for native modality and centers the window within its display.</summary>
    private void OwnAndPosition(Window owner)
    {
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var ownerHandle = WinRT.Interop.WindowNative.GetWindowHandle(owner);
        Marshal.SetLastPInvokeError(0);
        if (SetWindowLongPtr(handle, -8, ownerHandle) == IntPtr.Zero && Marshal.GetLastPInvokeError() != 0)
            throw new InvalidOperationException("The account check window could not be attached to its owner.");
        var presenter = OverlappedPresenter.CreateForDialog();
        AppWindow.SetPresenter(presenter);
        presenter.IsModal = true;
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        var scale = owner.Content.XamlRoot?.RasterizationScale ?? throw new InvalidOperationException("The account form is not ready.");
        var area = DisplayArea.GetFromWindowId(owner.AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var width = Math.Min((int)Math.Round(600 * scale), area.Width);
        var height = Math.Min((int)Math.Round(640 * scale), area.Height);
        var x = Math.Clamp(owner.AppWindow.Position.X + (owner.AppWindow.Size.Width - width) / 2, area.X, area.X + area.Width - width);
        var y = Math.Clamp(owner.AppWindow.Position.Y + (owner.AppWindow.Size.Height - height) / 2, area.Y, area.Y + area.Height - height);
        AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
    }

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
}
