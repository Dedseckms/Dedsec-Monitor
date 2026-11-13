using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using Dedsec.Core;

namespace Dedsec.Monitor;

internal sealed class UpdateService
{
    private static readonly HttpClient DefaultClient = CreateClient();
    private readonly HttpClient _client;
    private readonly string _dataDirectory;
    internal UpdateService(HttpClient? client = null, string? dataDirectory = null)
    { _client = client ?? DefaultClient; _dataDirectory = dataDirectory ?? App.DataDirectory; }
    private static HttpClient CreateClient()
    {
        var client = new HttpClient(new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(5) })
        { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DedsecMonitor/" + App.ProductVersion);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    internal async Task<string?> PrepareAsync(IProgress<string> status, CancellationToken token)
    {
        var pendingFile = Path.Combine(_dataDirectory, "Updates", "pending.json");
        if (File.Exists(pendingFile))
        {
            try
            {
                var pending = JsonSerializer.Deserialize<PendingUpdate>(File.ReadAllText(pendingFile));
                var updateRoot = Path.GetFullPath(Path.Combine(_dataDirectory, "Updates")) + Path.DirectorySeparatorChar;
                if (pending is not null && Path.GetFullPath(pending.Stage).StartsWith(updateRoot, StringComparison.OrdinalIgnoreCase) &&
                    Version.Parse(pending.Version) > Version.Parse(App.ProductVersion))
                { UpdatePackage.Validate(pending.Stage); return pending.Stage; }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or ArgumentException or FormatException) { App.Log("Pending update: " + ex.Message); }
            File.Delete(pendingFile);
        }
        using var check = CancellationTokenSource.CreateLinkedTokenSource(token); check.CancelAfter(TimeSpan.FromSeconds(8));
        using var response = await _client.GetAsync("https://api.github.com/repos/Dedseckms/Dedsec-Monitor/releases/latest", HttpCompletionOption.ResponseHeadersRead, check.Token);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > 1024 * 1024) throw new InvalidDataException("Update metadata is too large.");
        var json = await response.Content.ReadAsStringAsync(check.Token);
        if (json.Length > 1024 * 1024) throw new InvalidDataException("Update metadata is too large.");
        var candidate = UpdateCandidate.Read(json, Version.Parse(App.ProductVersion));
        if (candidate is null) return null;
        var failedVersion = Path.Combine(_dataDirectory, "Updates", "failed-version.txt");
        if (File.Exists(failedVersion) && File.ReadAllText(failedVersion).Trim() == candidate.Version.ToString()) return null;
        status.Report(string.Format(L.T("updateDownloading"), candidate.Version));
        var directory = Path.Combine(_dataDirectory, "Updates", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var archive = Path.Combine(directory, "package.zip");
        using var download = CancellationTokenSource.CreateLinkedTokenSource(token); download.CancelAfter(TimeSpan.FromMinutes(4));
        using var checksumResponse = await _client.GetAsync(candidate.ChecksumsUrl, download.Token); checksumResponse.EnsureSuccessStatusCode();
        var checksums = await checksumResponse.Content.ReadAsStringAsync(download.Token);
        if (checksums.Length > 65536) throw new InvalidDataException("Checksum manifest is too large.");
        using var payload = await _client.GetAsync(candidate.ArchiveUrl, HttpCompletionOption.ResponseHeadersRead, download.Token); payload.EnsureSuccessStatusCode();
        if (payload.Content.Headers.ContentLength > 256L * 1024 * 1024) throw new InvalidDataException("Update download is too large.");
        await using (var input = await payload.Content.ReadAsStreamAsync(download.Token))
        await using (var output = new FileStream(archive, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
        {
            var buffer = new byte[65536]; long total = 0;
            while (await input.ReadAsync(buffer, download.Token) is var count && count > 0)
            {
                total += count; if (total > 256L * 1024 * 1024) throw new InvalidDataException("Update download is too large.");
                await output.WriteAsync(buffer.AsMemory(0, count), download.Token);
            }
        }
        UpdatePackage.VerifyHash(archive, checksums, candidate.ArchiveName);
        var stage = Path.Combine(directory, "payload"); UpdatePackage.Extract(archive, stage);
        File.WriteAllText(pendingFile, JsonSerializer.Serialize(new PendingUpdate(stage, candidate.Version.ToString())));
        return stage;
    }

    private sealed record PendingUpdate(string Stage, string Version);

    internal static ProcessStartInfo CreateUpdaterStart(string stage)
    {
        var helper = Path.Combine(AppContext.BaseDirectory, "tools", "Dedsec.Updater.exe");
        if (!File.Exists(helper)) throw new IOException("The bundled updater is missing.");
        var temporary = Path.Combine(Path.GetDirectoryName(stage)!, "Dedsec.Updater.exe");
        File.Copy(helper, temporary, true);
        var start = new ProcessStartInfo(temporary) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add(Environment.ProcessId.ToString()); start.ArgumentList.Add(AppContext.BaseDirectory);
        start.ArgumentList.Add(stage); start.ArgumentList.Add(Path.Combine(Path.GetDirectoryName(stage)!, "startup.ready"));
        return start;
    }
}
