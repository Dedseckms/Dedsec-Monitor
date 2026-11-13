using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using Dedsec.Monitor;
using Xunit;

namespace Dedsec.WindowsTests;

public sealed class ComponentSetupTests
{
    [Fact]
    public async Task ExistingComponentsNeedNoNetworkOrElevation()
    {
        using var fixture = new Fixture();
        await fixture.Service(() => true, () => true).PrepareAsync(new Progress<string>(), CancellationToken.None);
        Assert.Equal(0, fixture.Handler.Requests); Assert.Empty(fixture.Runs);
    }

    [Fact]
    public async Task MissingComponentsAreDownloadedVerifiedAndInstalledAutomatically()
    {
        using var fixture = new Fixture(); bool rtss = false, pawn = false;
        fixture.Run = (file, _) => { if (file.EndsWith("RTSSSetup737.exe", StringComparison.Ordinal)) rtss = true; else pawn = true; return 0; };
        var result = await fixture.Service(() => rtss, () => pawn).PrepareAsync(new Progress<string>(), CancellationToken.None);
        Assert.False(result.RestartRequired); Assert.True(rtss); Assert.True(pawn);
        Assert.Equal(1, fixture.Handler.Requests);
        Assert.Equal(new[] { "/S", "-install -silent" }, fixture.Runs.Select(r => r.Arguments));
    }

    [Fact]
    public async Task CorruptDownloadNeverRunsAnyInstaller()
    {
        using var fixture = new Fixture(); fixture.Handler.Corrupt = true;
        await Assert.ThrowsAsync<IOException>(() => fixture.Service(() => false, () => false).PrepareAsync(new Progress<string>(), CancellationToken.None));
        Assert.Empty(fixture.Runs); Assert.Empty(Directory.GetFiles(fixture.Folder, "*.partial"));
    }

    [Fact]
    public async Task BothPackagesAreVerifiedBeforeTheFirstElevation()
    {
        using var fixture = new Fixture(); File.WriteAllText(fixture.Pawn, "tampered");
        await Assert.ThrowsAsync<IOException>(() => fixture.Service(() => false, () => false).PrepareAsync(new Progress<string>(), CancellationToken.None));
        Assert.Empty(fixture.Runs);
    }

    [Fact]
    public async Task FailedInstallerAndMissingRegistrationAreNotReportedAsSuccess()
    {
        using var fixture = new Fixture(); fixture.Run = (_, _) => 1603;
        await Assert.ThrowsAsync<IOException>(() => fixture.Service(() => false, () => true).PrepareAsync(new Progress<string>(), CancellationToken.None));
        fixture.Run = (_, _) => 0;
        await Assert.ThrowsAsync<IOException>(() => fixture.Service(() => false, () => true).PrepareAsync(new Progress<string>(), CancellationToken.None));
    }

    [Fact]
    public async Task DriverRebootCodeIsPreservedWithoutRequiringTheDriverToBeLoadedYet()
    {
        using var fixture = new Fixture(); fixture.Run = (_, _) => 3010;
        var result = await fixture.Service(() => true, () => false).PrepareAsync(new Progress<string>(), CancellationToken.None);
        Assert.True(result.RestartRequired); Assert.Single(fixture.Runs);
    }

    [Fact]
    public async Task CachedExecutableIsReverifiedAndRepairedWithoutAnotherDownload()
    {
        using var fixture = new Fixture(); var service = fixture.Service(() => false, () => true);
        var exe = await service.DownloadRtssAsync(new Progress<string>(), CancellationToken.None);
        File.WriteAllText(exe, "corrupt");
        await service.DownloadRtssAsync(new Progress<string>(), CancellationToken.None);
        Assert.Equal("test-only-rtss-payload", File.ReadAllText(exe)); Assert.Equal(1, fixture.Handler.Requests);
    }

    [Fact]
    public async Task NetworkFailureDoesNotRequestElevationOrLeavePartialFiles()
    {
        using var fixture = new Fixture(); fixture.Handler.Fail = true;
        await Assert.ThrowsAsync<HttpRequestException>(() => fixture.Service(() => false, () => false).PrepareAsync(new Progress<string>(), CancellationToken.None));
        Assert.Empty(fixture.Runs); Assert.Empty(Directory.GetFiles(fixture.Folder, "*.partial"));
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly string Folder = Path.Combine(Path.GetTempPath(), "DedsecComponents-" + Guid.NewGuid().ToString("N"));
        internal string Pawn => Path.Combine(Folder, "PawnIO_setup.exe");
        internal readonly DownloadHandler Handler = new();
        private readonly HttpClient _client;
        internal readonly List<(string File, string Arguments)> Runs = [];
        internal Func<string, string, int> Run = (_, _) => 0;
        internal Fixture() { Directory.CreateDirectory(Folder); File.WriteAllText(Pawn, "test-only-pawn-payload"); _client = new HttpClient(Handler); }
        internal ComponentSetupService Service(Func<bool> rtss, Func<bool> pawn) => new(_client, Folder, Pawn, rtss, pawn,
            (file, arguments) => { Runs.Add((file, arguments)); return Task.FromResult(Run(file, arguments)); },
            new ComponentPackage("https://example.test/rtss.zip", Hash(Handler.Archive), "RTSSSetup737.exe", Hash("test-only-rtss-payload"u8.ToArray())), Hash("test-only-pawn-payload"u8.ToArray()));
        public void Dispose() { _client.Dispose(); Directory.Delete(Folder, true); }
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private sealed class DownloadHandler : HttpMessageHandler
    {
        internal int Requests;
        internal bool Corrupt, Fail;
        internal byte[] Archive { get; }
        internal DownloadHandler()
        {
            using var memory = new MemoryStream();
            using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
            using (var writer = new StreamWriter(zip.CreateEntry("RTSSSetup737.exe").Open())) writer.Write("test-only-rtss-payload");
            Archive = memory.ToArray();
        }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(Fail ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
            { RequestMessage = request, Content = new ByteArrayContent(Corrupt ? "corrupt"u8.ToArray() : Archive) });
        }
    }
}
