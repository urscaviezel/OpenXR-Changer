using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ServiceProcess;
using Microsoft.Win32;

namespace OpenXRChanger.Helper;

public sealed class SwitchService : ServiceBase
{
    readonly CancellationTokenSource stopping = new();
    Task? worker;
    public SwitchService() { ServiceName = Protocol.ServiceName; CanStop = true; AutoLog = false; }
    protected override void OnStart(string[] args) => worker = Task.Run(Run);
    protected override void OnStop() { stopping.Cancel(); worker?.Wait(TimeSpan.FromSeconds(10)); }

    internal static PipeSecurity Security(SecurityIdentifier user)
    {
        var acl = new PipeSecurity(); acl.SetAccessRuleProtection(true, false);
        acl.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
        acl.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        acl.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalServiceSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        // Do not grant CreateNewInstance to the desktop client.
        const PipeAccessRights clientRights = PipeAccessRights.ReadData | PipeAccessRights.WriteData | PipeAccessRights.ReadAttributes |
            PipeAccessRights.WriteAttributes | PipeAccessRights.ReadExtendedAttributes | PipeAccessRights.WriteExtendedAttributes |
            PipeAccessRights.ReadPermissions | PipeAccessRights.Synchronize;
        acl.AddAccessRule(new PipeAccessRule(user, clientRights, AccessControlType.Allow));
        return acl;
    }

    internal static void Handle(Request request, Action<Action> impersonate, SecurityIdentifier user, Action<string> write)
    {
        string? validPath = null;
        impersonate(() =>
        {
            if (WindowsIdentity.GetCurrent().User != user) throw new UnauthorizedAccessException();
            if (request.Version != 1 || request.Operation is not ("ping" or "set")) throw new InvalidDataException("Unsupported operation");
            if (request.Operation == "ping" && request.Path != null) throw new InvalidDataException("Unexpected ping data");
            if (request.Operation == "set") validPath = ManifestValidation.Validate(request.Path ?? "");
        });
        // Impersonation has ended; only the validated selection reaches the write.
        if (validPath != null) write(validPath);
    }

    async Task Run()
    {
        try
        {
            using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var config = hklm.OpenSubKey(Protocol.ConfigurationKey);
            var user = new SecurityIdentifier(config?.GetValue("AllowedUser") as string ?? throw new InvalidDataException("User not configured"));
            using var pipe = NamedPipeServerStreamAcl.Create(Protocol.PipeName, PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.FirstPipeInstance, 8192, 8192, Security(user));
            while (!stopping.IsCancellationRequested)
            {
                await pipe.WaitForConnectionAsync(stopping.Token);
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token);
                deadline.CancelAfter(TimeSpan.FromSeconds(5));
                Reply reply;
                var phase = "REQUEST";
                try
                {
                    var request = await Protocol.Read<Request>(pipe, deadline.Token);
                    phase = "VALIDATION";
                    // Check the real Windows identity, not a user name supplied in a message.
                    Handle(request, action => pipe.RunAsClient(() => action()), user, validPath =>
                    {
                        phase = "REGISTRY";
                        // This is the only privileged write. No executable or DLL is ever loaded.
                        using var runtime = hklm.OpenSubKey(Protocol.RuntimeKey, RegistryKeyPermissionCheck.ReadWriteSubTree,
                            RegistryRights.QueryValues | RegistryRights.SetValue) ?? throw new IOException("Runtime registry key missing");
                        runtime.SetValue("ActiveRuntime", validPath, RegistryValueKind.String);
                        if (!string.Equals(runtime.GetValue("ActiveRuntime") as string, validPath, StringComparison.OrdinalIgnoreCase)) throw new IOException("Write verification failed");
                    });
                    reply = new Reply(true, "OK");
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    reply = new Reply(false, $"{phase}:{ex.GetType().Name}:0x{ex.HResult:X8}");
                }
                try { await Protocol.WriteReply(pipe, reply, deadline.Token); }
                catch (Exception ex) when (ex is IOException or OperationCanceledException) { }
                finally { pipe.Disconnect(); }
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
        catch { ExitCode = 1; _ = Task.Run(Stop); }
    }
}
