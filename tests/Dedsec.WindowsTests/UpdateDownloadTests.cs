using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Dedsec.Monitor;
using Xunit;

namespace Dedsec.WindowsTests;

public sealed class UpdateDownloadTests
{
    [Fact]
    public async Task DownloadsVerifiesAndCachesACompleteUpdateWithoutPublishingAnyRelease()
    {
        var folder = Path.Combine(Path.GetTempPath(), "DedsecUpdateDownload-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            using var handler = new FakeGitHub(); using var client = new HttpClient(handler);
            var service = new UpdateService(client, folder);
            var stage = await service.PrepareAsync(new Progress<string>(), CancellationToken.None);
            Assert.NotNull(stage); Assert.True(File.Exists(Path.Combine(stage, "Dedsec.Monitor.exe")));
            Assert.Equal(3, handler.Requests);
            Assert.Equal(stage, await service.PrepareAsync(new Progress<string>(), CancellationToken.None));
            Assert.Equal(3, handler.Requests);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public async Task NoPublishedReleaseIsAQuietNoUpdateResult()
    {
        using var client = new HttpClient(new NoRelease());
        Assert.Null(await new UpdateService(client).PrepareAsync(new Progress<string>(), CancellationToken.None));
    }

    private sealed class NoRelease : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private sealed class FakeGitHub : HttpMessageHandler
    {
        public int Requests { get; private set; }
        private readonly byte[] _archive;
        public FakeGitHub()
        {
            using var memory = new MemoryStream();
            using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
            {
                using (var executable = new StreamWriter(zip.CreateEntry("Dedsec.Monitor.exe").Open())) executable.Write("test-only-payload");
                using (var manifest = new StreamWriter(zip.CreateEntry("app-files.json").Open())) manifest.Write(JsonSerializer.Serialize(new[] { "Dedsec.Monitor.exe", "app-files.json" }));
            }
            _archive = memory.ToArray();
        }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            const string prefix = "https://github.com/Dedseckms/Dedsec-Monitor/releases/download/v9.0.0/";
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            if (request.RequestUri!.AbsolutePath.EndsWith("/latest", StringComparison.Ordinal)) response.Content = new StringContent(JsonSerializer.Serialize(new
            {
                draft = false,
                prerelease = false,
                tag_name = "v9.0.0",
                assets = new[]
                {
                    new { name = "Dedsec-Monitor-9.0.0-Portable-x64.zip", browser_download_url = prefix + "Dedsec-Monitor-9.0.0-Portable-x64.zip" },
                    new { name = "SHA256SUMS.txt", browser_download_url = prefix + "SHA256SUMS.txt" }
                }
            }));
            else if (request.RequestUri.AbsolutePath.EndsWith("SHA256SUMS.txt", StringComparison.Ordinal)) response.Content = new StringContent(Convert.ToHexString(SHA256.HashData(_archive)) + "  Dedsec-Monitor-9.0.0-Portable-x64.zip\n");
            else response.Content = new ByteArrayContent(_archive);
            return Task.FromResult(response);
        }
    }
}
