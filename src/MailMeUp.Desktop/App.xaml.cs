/* VBWR B
 *
 * Project: AgentInbox
 * Repository: https://github.com/umbertotechnopreneur/AgentInbox
 * Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
 *
 * VibeWare: Human intent, AI execution, and plenty of tokens
 * Manifesto: https://umbertogiacobbi.biz/vibeware/manifesto
 *
 * File: WinUI application resources and configuration-window lifetime.
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
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace MailMeUp.Desktop;

/// <summary>Creates the Windows configuration interface.</summary>
public partial class App : Microsoft.UI.Xaml.Application
{
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private MainWindow? _window;
    private int _started;

    /// <summary>Initializes application resources.</summary>
    public App()
    {
        InitializeComponent();
    }

    /// <inheritdoc />
    /// <summary>Open or activate the configuration window for an application launch.</summary>
    /// <param name="args">Arguments supplied by the launch or WinUI event; no mail operation is inferred from them.</param>
    protected override void OnLaunched(LaunchActivatedEventArgs args) => StartSetupWindow();

    /// <summary>Create the setup window once and activate it.</summary>
    internal void StartSetupWindow()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0) return;
        _window = new MainWindow(new PythonEngine());
        _window.Closed += (_, _) => _window = null;
        _window.Activate();
    }

    /// <summary>Bring the existing configuration window to the foreground.</summary>
    internal void ActivateExistingWindow() => _dispatcher.TryEnqueue(() =>
    {
        var window = _window;
        if (window is null) return;
        try
        {
            if (window.AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
                presenter.Restore();
            window.Activate();
            SetForegroundWindow(WinRT.Interop.WindowNative.GetWindowHandle(window));
        }
        catch (Exception)
        {
            Console.Error.WriteLine("AgentInbox could not activate its existing window.");
        }
    });

    /// <summary>Request foreground activation of the specified native window.</summary>
    /// <param name="window">Native window handle or WinUI window whose ownership or activation is being changed.</param>
    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);
}
