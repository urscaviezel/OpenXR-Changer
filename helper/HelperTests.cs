using System.Buffers.Binary;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;

namespace OpenXRChanger.Helper;

static class HelperTests
{
    public static void Run(string report)
    {
        var results = new List<string>();
        void Assert(bool ok, string name) { if (!ok) throw new Exception(name); results.Add("PASS: " + name); }
        bool Reject(Action action) { try { action(); return false; } catch { return true; } }
        var root = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report))!, "helper-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var dll = Path.Combine(root, "runtime.dll");
        using (var writer = new BinaryWriter(File.Create(dll))) { writer.Write((ushort)0x5a4d); writer.BaseStream.Position = 0x3c; writer.Write(64); writer.BaseStream.Position = 64; writer.Write(0x4550); writer.Write((ushort)0x8664); }
        var manifest = Path.Combine(root, "runtime.json"); File.WriteAllText(manifest, "{\"runtime\":{\"library_path\":\"runtime.dll\"}}");
        Assert(ManifestValidation.Validate(manifest) == manifest, "valid local x64 manifest accepted without executing DLL");
        Assert(Reject(() => ManifestValidation.Validate(@"\\server\share\runtime.json")), "UNC manifest rejected before access");
        Assert(Reject(() => ManifestValidation.Validate(@"\\?\C:\runtime.json")), "device path rejected");
        Assert(Reject(() => ManifestValidation.Validate("runtime.json")), "relative manifest rejected");
        File.WriteAllText(manifest, JsonSerializer.Serialize(new { runtime = new { library_path = @"\\server\share\runtime.dll" } }));
        Assert(Reject(() => ManifestValidation.Validate(manifest)), "network DLL rejected");
        File.WriteAllText(manifest, "{\"api_layer\":{\"library_path\":\"runtime.dll\"}}");
        Assert(Reject(() => ManifestValidation.Validate(manifest)), "API layer rejected");
        File.WriteAllText(manifest, new string(' ', 1024 * 1024 + 1));
        Assert(Reject(() => ManifestValidation.Validate(manifest)), "oversized manifest rejected");
        using var message = new MemoryStream();
        Protocol.Write(message, new Request(1, "ping"), CancellationToken.None).GetAwaiter().GetResult(); message.Position = 0;
        Assert(Protocol.Read<Request>(message, CancellationToken.None).GetAwaiter().GetResult().Operation == "ping", "bounded protocol round trip");
        using var oversized = new MemoryStream(); var header = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(header, Protocol.Limit + 1); oversized.Write(header); oversized.Position = 0;
        Assert(Reject(() => Protocol.Read<Request>(oversized, CancellationToken.None).GetAwaiter().GetResult()), "oversized request rejected before allocation");
        var user = WindowsIdentity.GetCurrent().User!;
        var writes = new List<string>();
        void Handle(Request request) => SwitchService.Handle(request, action => action(), user, writes.Add);
        Handle(new Request(1, "ping"));
        Assert(writes.Count == 0, "ping performs no registry write");
        Assert(Reject(() => Handle(new Request(1, "execute", "cmd.exe"))), "arbitrary commands rejected");
        Assert(Reject(() => Handle(new Request(2, "set", manifest))), "unknown protocol version rejected");
        Assert(Reject(() => Handle(new Request(1, "set"))), "missing manifest rejected");
        Assert(Reject(() => SwitchService.Handle(new Request(1, "ping"), action => action(), new SecurityIdentifier(WellKnownSidType.NullSid, null), writes.Add)), "wrong user identity rejected");
        Assert(writes.Count == 0, "rejected requests never reach the privileged write");
        File.WriteAllText(manifest, "{\"runtime\":{\"library_path\":\"runtime.dll\"}}");
        Handle(new Request(1, "set", manifest));
        Assert(writes.SequenceEqual(new[] { manifest }), "valid selection reaches only the fixed write callback");
        var rules = SwitchService.Security(user).GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<PipeAccessRule>().ToArray();
        Assert(rules.Any(r => r.IdentityReference == user && r.AccessControlType == AccessControlType.Allow && (r.PipeAccessRights & PipeAccessRights.CreateNewInstance) == 0), "desktop user cannot create server instances");
        Assert(rules.Any(r => r.IdentityReference == new SecurityIdentifier(WellKnownSidType.NetworkSid, null) && r.AccessControlType == AccessControlType.Deny), "network clients denied");
        PipeRoundTrips().GetAwaiter().GetResult();
        results.Add("PASS: 30 real pipe replies survive delayed readers, including errors and legacy clients");
        PipeTimeout().GetAwaiter().GetResult();
        results.Add("PASS: missing acknowledgement times out instead of blocking the service");
        File.WriteAllLines(report, results);
    }

    static async Task PipeRoundTrips()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var name = "OpenXRChanger-test-" + Guid.NewGuid().ToString("N");
        using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var serving = Task.Run(async () =>
        {
            for (int i = 0; i < 30; i++)
            {
                await server.WaitForConnectionAsync(deadline.Token);
                await Protocol.Read<Request>(server, deadline.Token);
                await Protocol.WriteReply(server, new Reply(i % 2 == 0, "reply-" + i), deadline.Token);
                server.Disconnect();
            }
        });
        for (int i = 0; i < 30; i++)
        {
            using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
            await client.ConnectAsync(deadline.Token);
            await Protocol.Write(client, new Request(1, "ping"), deadline.Token);
            await Task.Delay(40, deadline.Token);
            var reply = i % 3 == 0 ? await Protocol.Read<Reply>(client, deadline.Token) : await Protocol.ReadReply(client, deadline.Token);
            if (reply.Code != "reply-" + i || reply.Success != (i % 2 == 0)) throw new Exception("Pipe reply mismatch");
            // Legacy clients close after reading rather than sending an ACK.
        }
        await serving;
    }

    static async Task PipeTimeout()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var name = "OpenXRChanger-timeout-" + Guid.NewGuid().ToString("N");
        using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        var connected = server.WaitForConnectionAsync(deadline.Token);
        await client.ConnectAsync(deadline.Token); await connected;
        using var shortTimeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        try { await Protocol.WriteReply(server, new Reply(true, "OK"), shortTimeout.Token); }
        catch (OperationCanceledException) { return; }
        throw new Exception("Unresponsive client did not time out");
    }
}
