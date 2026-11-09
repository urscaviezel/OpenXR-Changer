using System.Diagnostics;
using System.IO;
using System.Windows;

namespace OpenXRChanger;
public partial class App : Application
{
    SingleInstance? instance;
    protected override void OnExit(ExitEventArgs e)
    {
        instance?.Dispose();
        base.OnExit(e);
    }
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            if (e.Args.Length == 2 && e.Args[0] == "--set-runtime")
            { RuntimeRegistry.Set(e.Args[1]); Shutdown(0); return; }
            if (e.Args.Length == 4 && e.Args[0] == "--apply-update")
            { Updater.Apply(e.Args[1], e.Args[2], int.Parse(e.Args[3])); Shutdown(0); return; }
            if (e.Args.Length == 2 && e.Args[0] == "--diagnose")
            {
                var p = new Preferences(); RuntimeRegistry.Discover(p);
                File.WriteAllText(e.Args[1], System.Text.Json.JsonSerializer.Serialize(new { Active = RuntimeRegistry.Identify(RuntimeRegistry.Active) ?? "Unlisted", Runtimes = p.Runtimes }, Store.Json)); Shutdown(); return;
            }
            if (e.Args.Length == 2 && e.Args[0] == "--self-test")
            { try { SelfTest.Run(e.Args[1]); Shutdown(); } catch (Exception ex) { File.WriteAllText(e.Args[1], ex.ToString()); Shutdown(1); } return; }
            if (e.Args.Length == 2 && e.Args[0] == "--restart-admin")
            {
                try { using var previous = Process.GetProcessById(int.Parse(e.Args[1])); if (!previous.WaitForExit(30000)) throw new IOException("Previous instance is still running."); }
                catch (ArgumentException) { }
            }
            instance = new SingleInstance();
            if (!instance.TryAcquire()) { Shutdown(); return; }
            var window = new MainWindow(); MainWindow = window;
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            window.Closed += (_, _) => Shutdown();
            instance.Listen(window);
            window.Start(e.Args.Contains("--tray"));
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "OpenXR Changer", MessageBoxButton.OK, MessageBoxImage.Error); Shutdown(1); }
    }
}
