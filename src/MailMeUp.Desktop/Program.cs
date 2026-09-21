using System.Runtime.InteropServices;
using MailMeUp.Desktop.Services;
using Microsoft.UI.Dispatching;
using Microsoft.Windows.AppLifecycle;
using Windows.ApplicationModel.Activation;

namespace MailMeUp.Desktop;

/// <summary>Registers the desktop instance before any setup window or application services are created.</summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (!SetupLaunchOptions.TryParse(args, out var options))
        {
            Console.Error.WriteLine(SetupLaunchOptions.UsageError);
            return 2;
        }

        try
        {
            WinRT.ComWrappersSupport.InitializeComWrappers();
            // Read the activation payload once, before packaged launch data can become unavailable.
            var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
            var instance = AppInstance.FindOrRegisterForKey(
                options.IsDemo ? "AgentInbox.Desktop.Setup.Demo" : "AgentInbox.Desktop.Setup");
            if (!instance.IsCurrent) return RedirectToExistingInstance(instance, activation);

            App? app = null;
            var activationGate = new object();
            var pendingActivations = new Queue<SetupLaunchOptions>();
            void OnActivated(object? sender, AppActivationArguments arguments)
            {
                if (!TryParseActivation(arguments, out var requested) || requested.IsDemo != options.IsDemo)
                {
                    Console.Error.WriteLine(SetupLaunchOptions.UsageError);
                    return;
                }

                lock (activationGate)
                {
                    if (app is null) pendingActivations.Enqueue(requested);
                    else app.ActivateExistingWindow(requested);
                }
            }
            instance.Activated += OnActivated;
            try
            {
                Microsoft.UI.Xaml.Application.Start(_ =>
                {
                    SynchronizationContext.SetSynchronizationContext(
                        new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
                    var setupApp = new App(options);
                    setupApp.StartSetupWindow();
                    lock (activationGate)
                    {
                        app = setupApp;
                        while (pendingActivations.TryDequeue(out var pending))
                            setupApp.ActivateExistingWindow(pending);
                    }
                });
            }
            finally
            {
                instance.Activated -= OnActivated;
                instance.UnregisterKey();
            }
            return 0;
        }
        catch (Exception)
        {
            // Fail closed: a lifecycle failure must not open an uncoordinated second setup window.
            Console.Error.WriteLine("AgentInbox could not open or activate its setup window. Close setup and try again.");
            return 1;
        }
    }

    private static bool TryParseActivation(AppActivationArguments activation, out SetupLaunchOptions options)
    {
        options = new();
        try
        {
            var commandLine = activation.Data switch
            {
                ILaunchActivatedEventArgs launch => launch.Arguments,
                ICommandLineActivatedEventArgs command => command.Operation.Arguments,
                _ => null
            };
            if (commandLine is null) return false;
            if (string.IsNullOrWhiteSpace(commandLine)) return true;

            var buffer = CommandLineToArgvW(commandLine.TrimStart(), out var count);
            if (buffer == IntPtr.Zero) return false;
            try
            {
                var arguments = new string[count];
                for (var index = 0; index < count; index++)
                    arguments[index] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(buffer, index * IntPtr.Size)) ?? "";
                return SetupLaunchOptions.TryParseActivation(arguments, out options);
            }
            finally
            {
                LocalFree(buffer);
            }
        }
        catch (Exception)
        {
            // Never surface activation payloads, executable paths or untrusted values in diagnostics.
            return false;
        }
    }

    private static int RedirectToExistingInstance(AppInstance instance, AppActivationArguments arguments)
    {
        AllowSetForegroundWindow(instance.ProcessId);
        var completed = new ManualResetEvent(false);
        var redirect = Task.Run(async () =>
        {
            try
            {
                await instance.RedirectActivationToAsync(arguments);
            }
            finally
            {
                completed.Set();
            }
        });

        // Pump COM on the STA while redirection runs on the worker, as required by AppLifecycle.
        // A bounded wait prevents a stuck primary instance from leaving a second process hanging.
        var result = CoWaitForMultipleObjects(0, 30_000, 1,
            [completed.SafeWaitHandle.DangerousGetHandle()], out _);
        if (result != 0)
        {
            // The worker still owns the event. Observe failures and dispose only after it finishes.
            _ = redirect.ContinueWith(task =>
            {
                _ = task.Exception;
                completed.Dispose();
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            Console.Error.WriteLine("The existing AgentInbox setup window did not respond. Try activating it from the taskbar.");
            return 1;
        }

        completed.Dispose();
        redirect.GetAwaiter().GetResult();
        return 0;
    }

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoWaitForMultipleObjects(uint flags, uint milliseconds, uint handleCount,
        [In] IntPtr[] handles, out uint index);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint processId);

    [DllImport("shell32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CommandLineToArgvW(string commandLine, out int argumentCount);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern IntPtr LocalFree(IntPtr memory);
}
