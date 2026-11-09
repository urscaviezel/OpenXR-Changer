using System.Buffers.Binary;
using System.IO;
using System.Threading;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenXRChanger.Helper;

public record Request(int Version, string Operation, string? Path = null);
public record Reply(bool Success, string Code);

public static class Protocol
{
    public const string PipeName = "OpenXRChanger.RuntimeSwitch.v1";
    public const string ServiceName = "OpenXRChangerHelper";
    public const string ConfigurationKey = @"SOFTWARE\OpenXRChanger\Helper";
    public const string RuntimeKey = @"SOFTWARE\Khronos\OpenXR\1";
    public const int Limit = 8192;
    static readonly JsonSerializerOptions Options = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    public static async Task<T> Read<T>(Stream stream, CancellationToken token)
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, token);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length < 1 || length > Limit) throw new InvalidDataException("Invalid message size");
        var bytes = new byte[length];
        await stream.ReadExactlyAsync(bytes, token);
        return JsonSerializer.Deserialize<T>(bytes, Options) ?? throw new InvalidDataException("Empty message");
    }

    public static async Task Write<T>(Stream stream, T value, CancellationToken token)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, Options);
        if (bytes.Length > Limit) throw new InvalidDataException("Message too large");
        var header = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(header, bytes.Length);
        await stream.WriteAsync(header, token); await stream.WriteAsync(bytes, token); await stream.FlushAsync(token);
    }

    public static async Task WriteReply(Stream stream, Reply reply, CancellationToken token)
    {
        await Write(stream, reply, token);
        // DisconnectNamedPipe discards unread output. FlushAsync alone does not
        // wait for the client. Keep the connection until receipt or a bounded timeout.
        var acknowledgement = new byte[1];
        try { await stream.ReadExactlyAsync(acknowledgement, token); }
        catch (EndOfStreamException) { return; } // Legacy clients close after reading.
        if (acknowledgement[0] != 6) throw new InvalidDataException("Invalid acknowledgement");
    }

    public static async Task<Reply> ReadReply(Stream stream, CancellationToken token)
    {
        var reply = await Read<Reply>(stream, token);
        await stream.WriteAsync(new byte[] { 6 }, token);
        return reply;
    }
}
