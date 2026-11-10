/* VBWR B
 *
 * Project: AgentInbox
 * Repository: https://github.com/umbertotechnopreneur/AgentInbox
 * Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
 *
 * VibeWare: Human intent, AI execution, and plenty of tokens
 * Manifesto: https://umbertogiacobbi.biz/vibeware/manifesto
 *
 * File: Connect the native tray to existing setup navigation and activity state.
 * Created with AI: OpenAI Codex, 2026-11-10.
 * Human guidance: Umberto Giacobbi; notify about live MCP and mailbox presence.
 *
 * Copyright (c) 2026 Umberto Giacobbi
 * License: MIT - see LICENSE
 * SPDX-License-Identifier: MIT
 *
 * VBWR E */

using MailMeUp.Desktop.ViewModels;
using MailMeUp.Desktop.Views;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Controls;

namespace MailMeUp.Desktop;

public sealed partial class MainWindow
{
    private NotificationArea? _tray;
    private TrayActivity? _previousActivity;
    private bool _exiting;

    /// <summary>Register matching Windows and Linux menu actions and monitor live status.</summary>
    private void InitializeTray()
    {
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "Tray.ico");
        _tray = new NotificationArea(handle, icon,
        [
            ("Open AgentInbox", ShowSetupWindow),
            ("Accounts", () => OpenTrayPage("accounts")),
            ("Connect Codex", () => OpenTrayPage("assistant")),
            ("Troubleshooting", () => OpenTrayPage("troubleshooting")),
            ("About", () => { ShowSetupWindow(); _ = ShowAboutAsync(); }),
            ("Exit AgentInbox", ExitSetup),
        ]);

        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(5);
        timer.Tick += (_, _) => _ = _configuration.RefreshActivityAsync(_lifetime.Token);
        _configuration.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ConfigurationModel.Activity)) UpdateTray();
        };
        AppWindow.Closing += (_, args) =>
        {
            if (_exiting || _tray?.Available != true) return;
            args.Cancel = true;
            AppWindow.Hide();
        };
        Closed += (_, _) =>
        {
            timer.Stop();
            _tray?.Dispose();
            _tray = null;
        };
        timer.Start();
        _ = _configuration.RefreshActivityAsync(_lifetime.Token);
    }

    /// <summary>Show the same configuration window after hiding or minimizing it.</summary>
    internal void ShowSetupWindow()
    {
        AppWindow.Show();
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
            presenter.Restore();
        Activate();
    }

    /// <summary>Bring setup forward and select the existing WinUI navigation item.</summary>
    /// <param name="page">Stable setup page key used by the navigation view.</param>
    private void OpenTrayPage(string page)
    {
        ShowSetupWindow();
        if (!_configuration.IsIdle) return;
        Navigation.SelectedItem = Navigation.MenuItems.OfType<NavigationViewItem>()
            .First(item => Equals(item.Tag, page));
    }

    /// <summary>Exit only AgentInbox and allow an in-flight setup operation to finish.</summary>
    private void ExitSetup()
    {
        if (!_configuration.IsIdle)
        {
            ShowSetupWindow();
            return;
        }
        _exiting = true;
        DispatcherQueue.TryEnqueue(Close);
    }

    /// <summary>Publish meaningful status transitions without repeating notifications on each poll.</summary>
    private void UpdateTray()
    {
        if (_configuration.Activity is not { } activity)
        {
            _tray?.Update("MCP status unavailable", null);
            return;
        }
        var previous = _previousActivity ?? new TrayActivity(false, true);
        var connection = activity.Connected ? "MCP connected" : "MCP disconnected";
        var status = activity.HasMailboxes ? connection : connection + " · No mailboxes configured";
        var messages = new List<string>();
        if (activity.Connected != previous.Connected) messages.Add(connection);
        if (!activity.HasMailboxes && previous.HasMailboxes) messages.Add("No mailboxes configured");
        _tray?.Update(status, messages.Count == 0 ? null : string.Join("\n", messages));
        _previousActivity = activity;
    }
}
