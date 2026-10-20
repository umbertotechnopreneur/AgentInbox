// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices;
using MailMeUp.Desktop.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace MailMeUp.Desktop;

/// <summary>Creates the Windows configuration and human approval interface.</summary>
public partial class App : Microsoft.UI.Xaml.Application
{
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private readonly SetupLaunchOptions _launchOptions;
    private MainWindow? _window;
    private int _started;

    /// <summary>Initializes application resources.</summary>
    public App() : this(new SetupLaunchOptions()) { }

    internal App(SetupLaunchOptions launchOptions)
    {
        _launchOptions = launchOptions;
        InitializeComponent();
    }

    /// <inheritdoc />
    protected override void OnLaunched(LaunchActivatedEventArgs args) => StartSetupWindow();

    internal void StartSetupWindow()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0) return;
        _window = new MainWindow(new PythonEngine());
        _window.Closed += (_, _) => _window = null;
        _window.RequestReview(_launchOptions.ReviewRequestId);
        _window.Activate();
    }

    internal void ActivateExistingWindow(SetupLaunchOptions options) => _dispatcher.TryEnqueue(() =>
    {
        var window = _window;
        if (window is null) return;
        try
        {
            if (window.AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
                presenter.Restore();
            window.RequestReview(options.ReviewRequestId);
            window.Activate();
            SetForegroundWindow(WinRT.Interop.WindowNative.GetWindowHandle(window));
        }
        catch (Exception)
        {
            Console.Error.WriteLine("AgentInbox could not activate its existing window.");
        }
    });

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);
}
