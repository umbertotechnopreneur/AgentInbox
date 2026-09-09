using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.Windows.AppLifecycle;

namespace MailMeUp.Desktop;

/// <summary>Registers the desktop instance before any setup window or application services are created.</summary>
internal static class Program
{
    [STAThread]
    private static int Main()
    {
        try
        {
            WinRT.ComWrappersSupport.InitializeComWrappers();
            var instance = AppInstance.FindOrRegisterForKey("MailMeUp.Desktop.Setup");
            if (!instance.IsCurrent) return RedirectToExistingInstance(instance);

            App? app = null;
            void OnActivated(object? sender, AppActivationArguments arguments) =>
                Volatile.Read(ref app)?.ActivateExistingWindow();
            instance.Activated += OnActivated;
            try
            {
                Microsoft.UI.Xaml.Application.Start(_ =>
                {
                    SynchronizationContext.SetSynchronizationContext(
                        new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
                    var setupApp = new App();
                    Volatile.Write(ref app, setupApp);
                    setupApp.StartSetupWindow();
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
            Console.Error.WriteLine("MailMeUp could not open or activate its setup window. Close setup and try again.");
            return 1;
        }
    }

    private static int RedirectToExistingInstance(AppInstance instance)
    {
        var arguments = AppInstance.GetCurrent().GetActivatedEventArgs();
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
            Console.Error.WriteLine("The existing MailMeUp setup window did not respond. Try activating it from the taskbar.");
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
}
