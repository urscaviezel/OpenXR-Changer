using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using Microsoft.Win32;
using OpenXRChanger.Helper;

namespace OpenXRChanger;

public static class HelperClient
{
    const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{A3AA1E51-9DCF-42CF-8306-7DAEDB56F56B}_is1";
    static string? Uninstaller()
    {
        using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = hklm.OpenSubKey(UninstallKey);
        var path = (key?.GetValue("UninstallString") as string)?.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(path)) return null;
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "OpenXR Changer Helper");
        // Only launch the registered uninstaller from the protected helper directory.
        return Path.IsPathFullyQualified(path) && string.Equals(Path.GetDirectoryName(path), directory, StringComparison.OrdinalIgnoreCase)
            && System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(path), @"^unins\d{3}\.exe$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            && File.Exists(path) ? path : null;
    }
    public static bool CanUninstall => Uninstaller() != null;

    public static async Task Uninstall()
    {
        var path = Uninstaller() ?? throw new FileNotFoundException("Helper uninstaller missing. Repair the helper installation first.");
        using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = "runas" })
            ?? throw new IOException("Helper uninstall did not start");
        await process.WaitForExitAsync();
        if (process.ExitCode != 0 || CanUninstall || IsConfigured) throw new IOException("Helper removal was not completed.");
    }

    public static bool IsConfigured
    {
        get
        {
            using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = hklm.OpenSubKey(Protocol.ConfigurationKey);
            return key?.GetValue("AllowedUser") as string == WindowsIdentity.GetCurrent().User?.Value;
        }
    }

    public static async Task Send(string operation, string? path = null)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        using var pipe = new NamedPipeClientStream(".", Protocol.PipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous, TokenImpersonationLevel.Impersonation);
        await pipe.ConnectAsync(timeout.Token);
        // Authenticate the server against SCM before permitting impersonation.
        if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var server) || server != ServiceProcessId())
            throw new IOException("Runtime helper identity could not be verified");
        await Protocol.Write(pipe, new Request(1, operation, path), timeout.Token);
        Reply reply;
        try { reply = await Protocol.ReadReply(pipe, timeout.Token); }
        catch (EndOfStreamException ex) { throw new IOException("The helper closed the connection before replying. Update the app and run Set up / repair with the new package.", ex); }
        if (!reply.Success) throw new IOException("Runtime helper rejected the request [" + reply.Code + "].");
    }

    public static async Task Setup()
    {
        var installer = Path.Combine(Store.Root, "assets", "OpenXR-Changer-Helper-Setup.exe");
        if (!File.Exists(installer)) throw new FileNotFoundException("Helper installer missing. Use the complete ZIP or installer package.");
        var start = new ProcessStartInfo(installer) { UseShellExecute = true, Verb = "runas" };
        start.ArgumentList.Add("/AllowedSid=" + WindowsIdentity.GetCurrent().User!.Value);
        using var process = Process.Start(start) ?? throw new IOException("Helper setup did not start");
        await process.WaitForExitAsync();
        if (process.ExitCode != 0 || !IsConfigured) throw new IOException("Helper setup was not completed");
        await Send("ping");
    }

    static uint ServiceProcessId()
    {
        var manager = OpenSCManager(null, null, 1);
        if (manager == IntPtr.Zero) throw new IOException("Service manager unavailable");
        try
        {
            var service = OpenService(manager, Protocol.ServiceName, 4);
            if (service == IntPtr.Zero) throw new IOException("Runtime helper is not installed");
            try
            {
                if (!QueryServiceStatusEx(service, 0, out var status, Marshal.SizeOf<ServiceStatus>(), out _) || status.State != 4)
                    throw new IOException("Runtime helper is not running");
                return status.ProcessId;
            }
            finally { CloseServiceHandle(service); }
        }
        finally { CloseServiceHandle(manager); }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct ServiceStatus { public uint Type, State, Controls, Win32Exit, ServiceExit, Checkpoint, WaitHint, ProcessId, Flags; }
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] static extern IntPtr OpenSCManager(string? machine, string? database, uint access);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] static extern IntPtr OpenService(IntPtr manager, string name, uint access);
    [DllImport("advapi32.dll")] static extern bool QueryServiceStatusEx(IntPtr service, int level, out ServiceStatus status, int size, out int needed);
    [DllImport("advapi32.dll")] static extern bool CloseServiceHandle(IntPtr handle);
    [DllImport("kernel32.dll")] static extern bool GetNamedPipeServerProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint processId);
}
