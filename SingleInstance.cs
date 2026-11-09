using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Windows;
using System.Windows.Interop;

namespace OpenXRChanger;

// Shared by portable copies in the same Windows user session.
internal sealed class SingleInstance : IDisposable
{
    readonly Mutex mutex;
    readonly EventWaitHandle activation;
    RegisteredWaitHandle? listener;
    bool ownsMutex;

    public SingleInstance()
    {
        var name = @"Local\OpenXRChanger-" + WindowsIdentity.GetCurrent().User!.Value;
        activation = new EventWaitHandle(false, EventResetMode.AutoReset, name + "-Activate");
        mutex = new Mutex(false, name + "-Main");
    }

    public bool TryAcquire()
    {
        try { ownsMutex = mutex.WaitOne(0); }
        catch (AbandonedMutexException) { ownsMutex = true; }
        if (!ownsMutex)
        {
            // Let the existing process bring its window to the foreground.
            AllowSetForegroundWindow(uint.MaxValue);
            activation.Set();
        }
        return ownsMutex;
    }

    public void Listen(Window window)
    {
        listener = ThreadPool.RegisterWaitForSingleObject(activation, (_, _) =>
        {
            if (window.Dispatcher.HasShutdownStarted) return;
            window.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!window.IsVisible) window.Show();
                if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
                window.Activate();
                SetForegroundWindow(new WindowInteropHelper(window).Handle);
            }));
        }, null, Timeout.Infinite, false);
    }

    public void Dispose()
    {
        listener?.Unregister(null);
        if (ownsMutex) { mutex.ReleaseMutex(); ownsMutex = false; }
        mutex.Dispose();
        activation.Dispose();
    }

    [DllImport("user32.dll")]
    static extern bool AllowSetForegroundWindow(uint processId);
    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr window);
}
