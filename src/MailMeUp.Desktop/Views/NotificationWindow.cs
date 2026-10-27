/* VBWR B
 *
 * Project: AgentInbox
 * Repository: https://github.com/umbertotechnopreneur/AgentInbox
 * Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
 *
 * VibeWare: Human intent, AI execution, and plenty of tokens
 * Manifesto: https://umbertogiacobbi.biz/vibeware/manifesto
 *
 * File: Owned native operation outcome and explicit cleanup-retry presentation.
 * Modified with AI: OpenAI Codex; code readability and documentation, 2026-10-27.
 * Human guidance: Umberto Giacobbi; document parameters and simplify maintenance.
 * Evidence: docs/VALIDATION.md (source review; execution checks pending).
 *
 * Copyright (c) 2026 Umberto Giacobbi
 * License: MIT - see LICENSE
 * SPDX-License-Identifier: MIT
 *
 * VBWR E */

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

    /// <summary>Initialize the owned model or native view and its explicit dependencies.</summary>
    /// <param name="owner">Configuration window that owns this operation or notification window.</param>
    /// <param name="title">User-facing heading for this page, dialog or notification.</param>
    /// <param name="message">Safe user-facing outcome or guidance; never raw credentials or mail content.</param>
    /// <param name="severity">Notification severity used for presentation.</param>
    /// <param name="allowRetry">Whether to show an explicit cleanup retry action.</param>
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
    /// <summary>Close the owned notification when its configuration window closes.</summary>
    /// <param name="sender">Native event source; business state is obtained from the model.</param>
    /// <param name="args">Arguments supplied by the launch or WinUI event; no mail operation is inferred from them.</param>
    private void OwnerClosed(object sender, WindowEventArgs args) => Close();
    /// <summary>Create an accessible notification button with a descriptive tooltip.</summary>
    /// <param name="caption">Optional visible button caption in addition to its icon.</param>
    /// <param name="hint">Model tooltip property name or flag selecting supporting text styling.</param>
    private static Button Button(string caption, string hint)
    {
        var button = new Button { Content = caption, MinWidth = 100 };
        AutomationProperties.SetName(button, caption);
        ToolTipService.SetToolTip(button, hint);
        return button;
    }
    /// <summary>Release owned resources and sensitive operation state.</summary>
    public void Dispose() => _owner.Closed -= OwnerClosed;
}
