using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ServiceProcess;
using Microsoft.Win32;

namespace OpenXRChanger.Helper;

static class Program
{
    static int Main(string[] args)
    {
        try
        {
            if (args.Length == 2 && args[0] == "--self-test") { HelperTests.Run(args[1]); return 0; }
            if (args.Length == 2 && args[0] == "--install") { Install(args[1]); return 0; }
            if (args.Length == 1 && args[0] == "--remove") { Remove(); return 0; }
            if (args.Length == 1 && args[0] == "--stop") { RequireAdmin(); StopExisting(); return 0; }
            if (args.Length != 0) return 2;
            ServiceBase.Run(new SwitchService()); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
    }

    static void RequireAdmin()
    {
        if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator)) throw new UnauthorizedAccessException("Administrator rights required for setup only");
        var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "OpenXR Changer Helper", "OpenXR-Changer.Helper.exe");
        if (!string.Equals(Path.GetFullPath(Environment.ProcessPath!), expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Install using the helper installer in Program Files");
    }

    static bool Exists() => ServiceController.GetServices().Any(s => s.ServiceName == Protocol.ServiceName);
    static void StopExisting()
    {
        if (!Exists()) return;
        using var service = new ServiceController(Protocol.ServiceName);
        if (service.Status == ServiceControllerStatus.Stopped) return;
        if (service.Status != ServiceControllerStatus.StopPending) service.Stop();
        service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20));
    }

    static void Sc(params string[] arguments)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "sc.exe")) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("Could not start service setup");
        if (!process.WaitForExit(20000) || process.ExitCode != 0) throw new IOException("Service setup failed");
    }

    static SecurityIdentifier ServiceSid() => (SecurityIdentifier)new NTAccount(@"NT SERVICE\" + Protocol.ServiceName).Translate(typeof(SecurityIdentifier));
    static RegistryAccessRule RuntimeRule(SecurityIdentifier sid) => new(sid, RegistryRights.QueryValues | RegistryRights.SetValue, InheritanceFlags.None, PropagationFlags.None, AccessControlType.Allow);

    static void Install(string allowedSid)
    {
        RequireAdmin();
        var user = new SecurityIdentifier(allowedSid);
        if (!user.IsAccountSid()) throw new InvalidDataException("A Windows user SID is required");
        _ = user.Translate(typeof(NTAccount));
        var binary = '"' + Environment.ProcessPath! + '"';
        StopExisting();
        if (Exists()) Sc("config", Protocol.ServiceName, "binPath=", binary, "start=", "auto", "obj=", "NT AUTHORITY\\LocalService");
        else Sc("create", Protocol.ServiceName, "binPath=", binary, "start=", "auto", "obj=", "NT AUTHORITY\\LocalService", "DisplayName=", "OpenXR Changer Runtime Helper");
        Sc("sidtype", Protocol.ServiceName, "unrestricted");
        Sc("privs", Protocol.ServiceName, "SeChangeNotifyPrivilege/SeImpersonatePrivilege");
        using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using (var config = hklm.CreateSubKey(Protocol.ConfigurationKey, true)) config.SetValue("AllowedUser", user.Value);
        using (hklm.CreateSubKey(Protocol.RuntimeKey, true)) { }
        using (var runtime = hklm.OpenSubKey(Protocol.RuntimeKey, RegistryKeyPermissionCheck.ReadWriteSubTree, RegistryRights.ReadPermissions | RegistryRights.ChangePermissions)!)
        {
            var acl = runtime.GetAccessControl();
            acl.SetAccessRule(RuntimeRule(ServiceSid())); runtime.SetAccessControl(acl);
        }
        Sc("description", Protocol.ServiceName, "Allows the configured user to select a local x64 OpenXR runtime without repeated UAC prompts.");
        using var service = new ServiceController(Protocol.ServiceName);
        service.Start(); service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(20));
    }

    static void Remove()
    {
        RequireAdmin(); StopExisting();
        using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        if (Exists())
        {
            using var runtime = hklm.OpenSubKey(Protocol.RuntimeKey, RegistryKeyPermissionCheck.ReadWriteSubTree, RegistryRights.ReadPermissions | RegistryRights.ChangePermissions);
            if (runtime != null) { var acl = runtime.GetAccessControl(); acl.RemoveAccessRuleSpecific(RuntimeRule(ServiceSid())); runtime.SetAccessControl(acl); }
            Sc("delete", Protocol.ServiceName);
        }
        hklm.DeleteSubKeyTree(Protocol.ConfigurationKey, false);
    }
}
