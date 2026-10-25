// SPDX-License-Identifier: MIT

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace MailMeUp.Desktop.Views;

/// <summary>Shows operation results and copyable details in an owned native Mica dialog.</summary>
internal sealed class NotificationWindow : Window, IDisposable
{
    private readonly Window _owner;
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal NotificationWindow(Window owner, string title, string message, int severity, bool allowRetry = false)
    {
        _owner = owner;
        Title = "AgentInbox";
        SystemBackdrop = new MicaBackdrop();
        var root = new Grid { Padding = new Thickness(24), RowSpacing = 20 };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        heading.Children.Add(new SymbolIcon(severity switch { 1 => Symbol.Accept, 2 => Symbol.Important, 3 => Symbol.Cancel, _ => Symbol.Message }));
        heading.Children.Add(new TextBlock { Text = title, FontSize = 22, TextWrapping = TextWrapping.Wrap, MaxWidth = 440 });
        root.Children.Add(heading);
        var details = new TextBox { Text = message, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetName(details, "Operation details");
        ScrollViewer.SetVerticalScrollBarVisibility(details, ScrollBarVisibility.Auto);
        Grid.SetRow(details, 1);
        root.Children.Add(details);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Right };
        if (allowRetry)
        {
            var retry = Button("Retry cleanup", "Retry removal of pending protected local files.");
            retry.Click += (_, _) => { RetryRequested = true; Close(); };
            actions.Children.Add(retry);
        }
        var close = Button("OK", "Close this result.");
        close.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        close.Click += (_, _) => Close();
        actions.Children.Add(close);
        Grid.SetRow(actions, 2);
        root.Children.Add(actions);
        root.Loaded += (_, _) => close.Focus(FocusState.Programmatic);
        root.KeyDown += (_, args) =>
        {
            if (args.Key == Windows.System.VirtualKey.Escape) { args.Handled = true; Close(); }
        };
        Content = root;
        Closed += (_, _) => _closed.TrySetResult();
        owner.Closed += OwnerClosed;
        AccountCheckWindow.OwnAndPosition(this, owner);
    }

    internal Task ClosedTask => _closed.Task;
    internal bool RetryRequested { get; private set; }
    private void OwnerClosed(object sender, WindowEventArgs args) => Close();
    private static Button Button(string caption, string hint)
    {
        var button = new Button { Content = caption, MinWidth = 100 };
        AutomationProperties.SetName(button, caption);
        ToolTipService.SetToolTip(button, hint);
        return button;
    }
    public void Dispose() => _owner.Closed -= OwnerClosed;
}
