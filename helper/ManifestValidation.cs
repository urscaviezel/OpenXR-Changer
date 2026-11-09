using System.Text.Json;

namespace OpenXRChanger.Helper;

public static class ManifestValidation
{
    public static string LocalFile(string path)
    {
        // Never access UNC, device paths or reparse points with the service identity.
        if (path.Length > 1024 || path.Length < 4 || !char.IsAsciiLetter(path[0]) || path[1] != ':' || path[2] != '\\')
            throw new InvalidDataException("A local absolute path is required");
        var full = System.IO.Path.GetFullPath(path);
        var root = System.IO.Path.GetPathRoot(full)!;
        if (new DriveInfo(root).DriveType != DriveType.Fixed) throw new InvalidDataException("A fixed local drive is required");
        var current = root;
        foreach (var part in full[root.Length..].Split('\\'))
        {
            if (part.Contains(':')) throw new InvalidDataException("Alternate streams are not supported");
            current = System.IO.Path.Combine(current, part);
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Reparse points are not supported");
        }
        return full;
    }

    // Called while impersonating the requesting user, never as LocalService.
    public static string Validate(string input)
    {
        var path = LocalFile(input);
        if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("JSON required");
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length > 1024 * 1024) throw new InvalidDataException("Manifest too large");
        using var json = JsonDocument.Parse(file, new JsonDocumentOptions { MaxDepth = 32 });
        var library = json.RootElement.GetProperty("runtime").GetProperty("library_path").GetString();
        if (string.IsNullOrWhiteSpace(library)) throw new InvalidDataException("Library missing");
        // Reject network/device paths before resolving a relative library path.
        if (library.StartsWith('\\') || library.StartsWith('/')) throw new InvalidDataException("Local library required");
        var dll = LocalFile(System.IO.Path.GetFullPath(library, System.IO.Path.GetDirectoryName(path)!));
        using var reader = new BinaryReader(new FileStream(dll, FileMode.Open, FileAccess.Read, FileShare.Read));
        if (reader.BaseStream.Length < 64 || reader.ReadUInt16() != 0x5a4d) throw new InvalidDataException("Invalid DLL");
        reader.BaseStream.Position = 0x3c; var offset = reader.ReadInt32();
        if (offset < 64 || offset > reader.BaseStream.Length - 6) throw new InvalidDataException("Invalid PE header");
        reader.BaseStream.Position = offset;
        if (reader.ReadUInt32() != 0x4550 || reader.ReadUInt16() != 0x8664) throw new InvalidDataException("x64 DLL required");
        return path;
    }
}
