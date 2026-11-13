using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace Dedsec.Core;

public sealed record UpdateCandidate(Version Version, string Tag, string ArchiveName, Uri ArchiveUrl, Uri ChecksumsUrl)
{
    public static UpdateCandidate? Read(string json, Version current)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean()) return null;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v'), out var version) || version <= current) return null;
        string? name = null; Uri? archive = null; Uri? checksums = null;
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            var file = asset.GetProperty("name").GetString() ?? "";
            if (!Uri.TryCreate(asset.GetProperty("browser_download_url").GetString(), UriKind.Absolute, out var url) ||
                url.Scheme != "https" || url.Host != "github.com" || !url.AbsolutePath.StartsWith(
                    "/Dedseckms/Dedsec-Monitor/releases/download/" + Uri.EscapeDataString(tag) + "/", StringComparison.Ordinal)) continue;
            if (file.StartsWith("Dedsec-Monitor-", StringComparison.Ordinal) && file.EndsWith("-Portable-x64.zip", StringComparison.Ordinal)) { name = file; archive = url; }
            if (file == "SHA256SUMS.txt") checksums = url;
        }
        return archive is not null && checksums is not null && name is not null ? new(version, tag, name, archive, checksums) : null;
    }
}

public static class UpdatePackage
{
    public static void VerifyHash(string archive, string checksums, string fileName)
    {
        var expected = checksums.Split('\n').Select(l => l.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .FirstOrDefault(parts => parts.Length == 2 && parts[1].TrimStart('*') == fileName)?[0];
        if (expected is null || expected.Length != 64 || !expected.All(Uri.IsHexDigit)) throw new InvalidDataException("Update checksum is missing.");
        using var stream = File.OpenRead(archive);
        var actual = Convert.ToHexString(SHA256.HashData(stream));
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Update checksum mismatch.");
    }

    public static string ResolveFile(string root, string relative)
    {
        var parts = relative.Replace('\\', '/').Split('/');
        if (string.IsNullOrWhiteSpace(relative) || parts.Any(p => p.Length == 0 || p is "." or ".." ||
            p.EndsWith('.') || p.EndsWith(' ') || p.IndexOfAny([':', '\0', '\r', '\n', '*', '?', '"', '<', '>', '|']) >= 0))
            throw new InvalidDataException("Invalid update path.");
        foreach (var part in parts)
        {
            var stem = part.Split('.')[0].ToUpperInvariant();
            if (stem is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$" or "CLOCK$" ||
                stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) && stem[3] is >= '1' and <= '9')
                throw new InvalidDataException("Update path contains a reserved device name.");
        }
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(fullRoot, Path.Combine(parts)));
        if (!path.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Update path escaped its root.");
        return path;
    }

    public static void Extract(string archive, string folder)
    {
        Directory.CreateDirectory(folder);
        using var zip = ZipFile.OpenRead(archive);
        if (zip.Entries.Count > 5000 || zip.Entries.Sum(e => e.Length) > 1024L * 1024 * 1024) throw new InvalidDataException("Update archive is too large.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in zip.Entries)
        {
            if (entry.Name.Length == 0) continue;
            var path = ResolveFile(folder, entry.FullName);
            if (!names.Add(path) || (entry.ExternalAttributes >> 16 & 0xF000) == 0xA000 ||
                (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Unsafe update archive entry.");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            entry.ExtractToFile(path, false);
        }
        Validate(folder);
    }

    public static string[] Validate(string folder)
    {
        var manifest = ResolveFile(folder, "app-files.json");
        if (!File.Exists(manifest) || new FileInfo(manifest).Length > 1024 * 1024) throw new InvalidDataException("Update file manifest is missing.");
        var files = JsonSerializer.Deserialize<string[]>(File.ReadAllText(manifest)) ?? [];
        if (files.Length is < 2 or > 5000 || files.Distinct(StringComparer.OrdinalIgnoreCase).Count() != files.Length ||
            !files.Contains("Dedsec.Monitor.exe") || !files.Contains("app-files.json")) throw new InvalidDataException("Invalid update file manifest.");
        foreach (var file in files) if (!File.Exists(ResolveFile(folder, file))) throw new InvalidDataException("Update payload is incomplete.");
        return files;
    }

    /// <summary>Replace only manifest-listed application files; preserve unrelated portable-folder files and roll back on failure.</summary>
    public static void Apply(string stage, string target, string backup, Action launchAndVerify)
    {
        var files = Validate(stage);
        if (!File.Exists(ResolveFile(target, "Dedsec.Monitor.exe"))) throw new InvalidDataException("Target is not a Dedsec Monitor installation.");
        var touched = new List<(string Destination, string? Backup)>();
        try
        {
            foreach (var file in files)
            {
                var destination = ResolveFile(target, file);
                for (var parent = Path.GetDirectoryName(destination); parent is not null && parent.Length >= Path.GetFullPath(target).Length; parent = Path.GetDirectoryName(parent))
                    if (Directory.Exists(parent) && (File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Update target contains a directory link.");
                if (File.Exists(destination) && (File.GetAttributes(destination) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Update target contains a file link.");
                string? old = null;
                if (File.Exists(destination))
                {
                    old = ResolveFile(backup, file); Directory.CreateDirectory(Path.GetDirectoryName(old)!); File.Copy(destination, old, true);
                }
                touched.Add((destination, old));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.Copy(ResolveFile(stage, file), destination, true);
            }
            launchAndVerify();
        }
        catch
        {
            foreach (var (destination, old) in touched.AsEnumerable().Reverse())
            { if (old is not null) File.Copy(old, destination, true); else File.Delete(destination); }
            throw;
        }
    }
}
