using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using Microsoft.Win32;

namespace Dedsec.Monitor;

internal sealed record ComponentPackage(string Url, string ArchiveHash, string Executable, string ExecutableHash);
internal sealed record ComponentSetupResult(bool RestartRequired);

internal sealed class ComponentSetupService
{
    // Download from Guru3D's official mirror on the user's device; never rehost RTSS.
    internal static readonly ComponentPackage RtssPackage = new(
        "https://ftp.nluug.nl/pub/games/PC/guru3d/afterburner/%5BGuru3D%5D-RTSSSetup737Build28314.zip",
        "9b084a8cb3e53ec1a673894d0b66e22b16c9fd8785636b020b2d422f3f2a820e",
        "RTSSSetup737.exe", "04f4704ced86a68b650bf87156c02e69160e6c049b576661250250beeabaa5ef");
    internal const string PawnHash = "1f519a22e47187f70a1379a48ca604981c4fcf694f4e65b734aaa74a9fba3032";
    private readonly HttpClient _client;
    private readonly string _cache;
    private readonly string _pawnInstaller;
    private readonly Func<bool> _rtssInstalled;
    private readonly Func<bool> _pawnInstalled;
    private readonly Func<string, string, Task<int>> _run;
    private readonly ComponentPackage _package;
    private readonly string _pawnHash;

    internal ComponentSetupService(HttpClient client, string? cache = null, string? pawnInstaller = null,
        Func<bool>? rtssInstalled = null, Func<bool>? pawnInstalled = null,
        Func<string, string, Task<int>>? run = null, ComponentPackage? package = null, string? pawnHash = null)
    {
        _client = client;
        _cache = cache ?? Path.Combine(App.DataDirectory, "SetupCache");
        _pawnInstaller = pawnInstaller ?? Path.Combine(AppContext.BaseDirectory, "tools", "PawnIO_setup.exe");
        _rtssInstalled = rtssInstalled ?? (() => RtssService.FindExecutable() is not null);
        _pawnInstalled = pawnInstalled ?? IsPawnInstalled;
        _run = run ?? RunInstallerAsync;
        _package = package ?? RtssPackage;
        _pawnHash = pawnHash ?? PawnHash;
    }

    internal static bool IsPawnInstalled()
    {
        // LHM caches this value in a static constructor; re-read after installation.
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var key = machine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO");
            if (Version.TryParse(key?.GetValue("DisplayVersion") as string, out _)) return true;
        }
        return false;
    }

    internal async Task<ComponentSetupResult> PrepareAsync(IProgress<string> progress, CancellationToken token)
    {
        Directory.CreateDirectory(_cache);
        // Keep the handle across awaits: a second installer must not prepare the same cache.
        using var setupLock = new FileStream(Path.Combine(_cache, "setup.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var needRtss = !_rtssInstalled();
        var needPawn = !_pawnInstalled();
        string? rtss = null;
        if (needRtss) rtss = await DownloadRtssAsync(progress, token);
        // Verify both packages before requesting elevation or changing the machine.
        if (needPawn) VerifyFile(_pawnInstaller, _pawnHash);
        token.ThrowIfCancellationRequested();
        var restart = false;
        if (rtss is not null)
        {
            progress.Report("componentInstallingRtss");
            using var locked = OpenVerified(rtss, _package.ExecutableHash);
            restart |= CheckExitCode(await _run(rtss, "/S"), "RTSS");
            if (!_rtssInstalled()) throw new IOException(L.T("componentRtssFailed"));
        }
        if (needPawn)
        {
            progress.Report("componentInstallingPawn");
            using var locked = OpenVerified(_pawnInstaller, _pawnHash);
            var pawnRestart = CheckExitCode(await _run(_pawnInstaller, "-install -silent"), "PawnIO");
            restart |= pawnRestart;
            if (!pawnRestart && !_pawnInstalled()) throw new IOException(L.T("componentPawnFailed"));
        }
        progress.Report(restart ? "componentReboot" : "componentReady");
        return new ComponentSetupResult(restart);
    }

    internal async Task<string> DownloadRtssAsync(IProgress<string> progress, CancellationToken token)
    {
        Directory.CreateDirectory(_cache);
        var executable = Path.Combine(_cache, _package.Executable);
        if (IsVerified(executable, _package.ExecutableHash)) return executable;
        var archive = Path.Combine(_cache, "rtss-7.3.7.zip");
        if (!IsVerified(archive, _package.ArchiveHash))
        {
            progress.Report("componentDownloading");
            var partial = archive + ".partial";
            try
            {
                using var response = await _client.GetAsync(_package.Url, HttpCompletionOption.ResponseHeadersRead, token);
                response.EnsureSuccessStatusCode();
                var final = response.RequestMessage?.RequestUri;
                var expected = new Uri(_package.Url);
                if (final is null || final.Scheme != "https" || !final.Host.Equals(expected.Host, StringComparison.OrdinalIgnoreCase))
                    throw new IOException(L.T("componentDownloadFailed"));
                const long limit = 64 * 1024 * 1024;
                if (response.Content.Headers.ContentLength > limit) throw new IOException("RTSS archive is too large.");
                await using (var input = await response.Content.ReadAsStreamAsync(token))
                await using (var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                {
                    var buffer = new byte[81920]; long length = 0; int count;
                    while ((count = await input.ReadAsync(buffer, token)) != 0)
                    {
                        length += count;
                        if (length > limit) throw new IOException("RTSS archive is too large.");
                        await output.WriteAsync(buffer.AsMemory(0, count), token);
                    }
                }
                VerifyFile(partial, _package.ArchiveHash);
                File.Move(partial, archive, true);
            }
            finally { if (File.Exists(partial)) File.Delete(partial); }
        }
        progress.Report("componentVerifying");
        using (var zip = ZipFile.OpenRead(archive))
        {
            // Extract only the pinned executable, never paths supplied by the archive.
            var entries = zip.Entries.Where(e => e.FullName == _package.Executable).ToArray();
            if (entries.Length != 1 || entries[0].Length > 64 * 1024 * 1024) throw new IOException("Unexpected RTSS archive layout.");
            entries[0].ExtractToFile(executable, true);
        }
        VerifyFile(executable, _package.ExecutableHash);
        return executable;
    }

    private static bool IsVerified(string file, string hash)
    {
        if (!File.Exists(file)) return false;
        using var input = File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(input)).Equals(hash, StringComparison.OrdinalIgnoreCase);
    }

    internal static void VerifyFile(string file, string hash)
    { using var input = OpenVerified(file, hash); }

    private static FileStream OpenVerified(string file, string hash)
    {
        var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (Convert.ToHexString(SHA256.HashData(input)).Equals(hash, StringComparison.OrdinalIgnoreCase)) return input;
        input.Dispose(); throw new IOException(L.T("componentHashFailed"));
    }

    private static bool CheckExitCode(int code, string name) => code switch
    {
        0 => false,
        3010 => true,
        _ => throw new IOException($"{name}: {L.T("componentInstallFailed")} ({code})")
    };

    private static async Task<int> RunInstallerAsync(string file, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo(file, arguments)
        { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden });
        if (process is null) throw new IOException(L.T("componentInstallFailed"));
        // Do not terminate a driver/runtime installer halfway through on cancellation.
        await process.WaitForExitAsync();
        return process.ExitCode;
    }
}
