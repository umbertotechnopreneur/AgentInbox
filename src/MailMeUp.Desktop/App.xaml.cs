using System.Runtime.InteropServices;
using MailMeUp.Application;
using MailMeUp.Desktop.Services;
using MailMeUp.Hosting;
using MailMeUp.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Serilog;
using Serilog.Core;

namespace MailMeUp.Desktop;

/// <summary>Hosts the local Windows setup adapter without starting an MCP process.</summary>
public partial class App : Microsoft.UI.Xaml.Application
{
    private IHost? _host;
    private Window? _window;
    private Logger? _diagnostics;
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private ILogger<App>? _logger;
    private int _setupStarted;
    private readonly SetupLaunchOptions _launchOptions;

    /// <summary>Initializes WinUI resources.</summary>
    public App() : this(new SetupLaunchOptions()) { }

    internal App(SetupLaunchOptions launchOptions)
    {
        _launchOptions = launchOptions;
        InitializeComponent();
    }

    /// <inheritdoc />
    protected override void OnLaunched(LaunchActivatedEventArgs args) => StartSetupWindow();

    /// <summary>Creates the setup window once the WinUI dispatcher and the primary app instance are ready.</summary>
    internal void StartSetupWindow()
    {
        if (Interlocked.Exchange(ref _setupStarted, 1) != 0) return;
        Logger? diagnostics = null;
        try
        {
            var dataDirectory = _launchOptions.IsDemo
                ? Path.Combine(Path.GetTempPath(), "AgentInbox-ui-demo", Guid.NewGuid().ToString("N"))
                : DataDirectory.ResolveFromEnvironment();
            diagnostics = DesktopLogging.Create(dataDirectory);
            var startupLogger = diagnostics.ForContext("SourceContext", "AgentInbox.Desktop");
            startupLogger.Information("Starting desktop setup");
            var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = [], DisableDefaults = true });
            builder.Logging.ClearProviders();
            builder.Services.AddSerilog(diagnostics, dispose: false);
            if (_launchOptions.IsDemo)
                builder.Services.AddSingleton<IMailMeUpApplication, DemoMailMeUpApplication>();
            else
                builder.Services.AddMailMeUp(dataDirectory);
            builder.Services.AddSingleton<CodexSetupService>();
            builder.Services.AddTransient<MainWindow>();
            _host = builder.Build();
            _diagnostics = diagnostics;
            diagnostics = null;
            _logger = _host.Services.GetRequiredService<ILogger<App>>();
            var setupWindow = _host.Services.GetRequiredService<MainWindow>();
            setupWindow.RequestStep(_launchOptions.Step);
            _window = setupWindow;
        }
        catch (Exception exception)
        {
            var startupLogger = diagnostics?.ForContext("SourceContext", "AgentInbox.Desktop") ??
                _diagnostics?.ForContext("SourceContext", "AgentInbox.Desktop");
            startupLogger?.Error("Desktop setup startup failed ({ErrorType}, HResult {HResult})", exception.GetType().Name, exception.HResult);
            diagnostics?.Dispose();
            _diagnostics?.Dispose();
            _diagnostics = null;
            if (Environment.GetEnvironmentVariable("AGENTINBOX_STARTUP_DIAGNOSTICS") == "1")
                Console.Error.WriteLine($"AgentInbox startup failure: {exception.GetType().Name} (0x{exception.HResult:X8})");
            _window = new Window
            {
                Title = "AgentInbox",
                Content = new Microsoft.UI.Xaml.Controls.TextBlock
                {
                    Text = "AgentInbox could not open setup. Close the app and try again. If it persists, check the local installation and data directory.",
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(32)
                }
            };
        }

        _window.Closed += (_, _) =>
        {
            _window = null;
            _host?.Dispose();
            _host = null;
            _diagnostics?.Information("Desktop setup stopped");
            _diagnostics?.Dispose();
            _diagnostics = null;
        };
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
            if (window is MainWindow setupWindow)
                setupWindow.RequestStep(options.Step);
            window.Activate();
            SetForegroundWindow(WinRT.Interop.WindowNative.GetWindowHandle(window));
        }
        catch (Exception exception)
        {
            _logger?.LogWarning("Setup activation failed ({ErrorType})", exception.GetType().Name);
        }
    });

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);
}
