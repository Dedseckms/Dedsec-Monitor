using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Dedsec.Core;
using Xunit;

namespace Dedsec.Tests;

public sealed class UpdateTests
{
    [Fact]
    public void IgnoresDraftsAndOnlyAcceptsOurPublishedRepositoryAssets()
    {
        string Release(bool draft, string host = "github.com") => JsonSerializer.Serialize(new
        {
            draft,
            prerelease = false,
            tag_name = "v1.0.2",
            assets = new[]
            {
                new { name = "Dedsec-Monitor-1.0.2-Portable-x64.zip", browser_download_url = $"https://{host}/Dedseckms/Dedsec-Monitor/releases/download/v1.0.2/app.zip" },
                new { name = "SHA256SUMS.txt", browser_download_url = $"https://{host}/Dedseckms/Dedsec-Monitor/releases/download/v1.0.2/SHA256SUMS.txt" }
            }
        });
        Assert.Null(UpdateCandidate.Read(Release(true), new(1, 0, 1)));
        Assert.Null(UpdateCandidate.Read(Release(false, "example.com"), new(1, 0, 1)));
        Assert.NotNull(UpdateCandidate.Read(Release(false), new(1, 0, 1)));
        Assert.Null(UpdateCandidate.Read(Release(false), new(1, 0, 2)));
    }

    [Theory]
    [InlineData("../outside.exe")]
    [InlineData("/outside.exe")]
    [InlineData("C:\\outside.exe")]
    [InlineData("safe/../../outside.exe")]
    [InlineData("a.exe:stream")]
    [InlineData("dir./app.exe")]
    [InlineData("CON")]
    [InlineData("nested/COM1.txt")]
    public void RejectsUnsafeArchivePaths(string path) => Assert.Throws<InvalidDataException>(() => UpdatePackage.ResolveFile(Path.GetTempPath(), path));

    [Fact]
    public void ChecksumRejectsTamperedDownloads()
    {
        Temporary(folder =>
        {
            var file = Path.Combine(folder, "update.zip"); File.WriteAllText(file, "original");
            var sum = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))) + "  update.zip\n";
            UpdatePackage.VerifyHash(file, sum, "update.zip");
            File.AppendAllText(file, "tampered"); Assert.Throws<InvalidDataException>(() => UpdatePackage.VerifyHash(file, sum, "update.zip"));
        });
    }

    [Fact]
    public void ZipTraversalCannotWriteOutsideTheStagingFolder()
    {
        Temporary(folder =>
        {
            var archive = Path.Combine(folder, "bad.zip");
            using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create)) using (var writer = new StreamWriter(zip.CreateEntry("../outside.txt").Open())) writer.Write("bad");
            Assert.Throws<InvalidDataException>(() => UpdatePackage.Extract(archive, Path.Combine(folder, "stage")));
            Assert.False(File.Exists(Path.Combine(folder, "outside.txt")));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ApplyingAnUpdatePreservesUnrelatedFilesAndRollsBackOnStartupFailure(bool fail)
    {
        Temporary(folder =>
        {
            var stage = Path.Combine(folder, "stage"); var target = Path.Combine(folder, "portable"); var backup = Path.Combine(folder, "backup");
            Directory.CreateDirectory(stage); Directory.CreateDirectory(target);
            File.WriteAllText(Path.Combine(target, "Dedsec.Monitor.exe"), "old"); File.WriteAllText(Path.Combine(target, "personal.txt"), "keep");
            File.WriteAllText(Path.Combine(stage, "Dedsec.Monitor.exe"), "new"); File.WriteAllText(Path.Combine(stage, "new.dll"), "dll");
            File.WriteAllText(Path.Combine(stage, "app-files.json"), JsonSerializer.Serialize(new[] { "Dedsec.Monitor.exe", "new.dll", "app-files.json" }));
            void Launch() { if (fail) throw new IOException("Cannot launch"); }
            if (fail) Assert.Throws<IOException>(() => UpdatePackage.Apply(stage, target, backup, Launch)); else UpdatePackage.Apply(stage, target, backup, Launch);
            Assert.Equal(fail ? "old" : "new", File.ReadAllText(Path.Combine(target, "Dedsec.Monitor.exe")));
            Assert.Equal("keep", File.ReadAllText(Path.Combine(target, "personal.txt")));
            Assert.Equal(!fail, File.Exists(Path.Combine(target, "new.dll")));
        });
    }

    private static void Temporary(Action<string> action)
    {
        var folder = Path.Combine(Path.GetTempPath(), "DedsecUpdates-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try { action(folder); } finally { Directory.Delete(folder, true); }
    }
}
