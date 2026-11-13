using System.Diagnostics;
using System.IO;
using Dedsec.Core;

namespace Dedsec.Monitor;

internal sealed class CaptureService : IDisposable
{
    private Process? _process;
    private Task? _reader;
    private Task? _errors;
    private readonly FrameStore _store;
    private readonly object _gate = new();
    private volatile bool _running;
    private volatile bool _stopping;
    public string State { get; private set; } = "stopped";
    public string? Error { get; private set; }
    public bool Running => _running;

    public CaptureService(FrameStore store) { _store = store; }

    public void Start()
    { lock (_gate) StartCore(); }

    private void StartCore()
    {
        if (Running) return;
        Stop(); _stopping = false; Error = null; _store.Clear();
        var binary = Path.Combine(AppContext.BaseDirectory, "tools", "PresentMon.exe");
        if (!File.Exists(binary)) { State = "missing"; return; }
        try
        {
            var start = new ProcessStartInfo(binary)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = App.DataDirectory
            };
            foreach (var arg in new[] { "--output_stdout", "--no_console_stats", "--v1_metrics",
                "--no_track_gpu", "--no_track_display", "--session_name", "DedsecMonitor-" + Environment.ProcessId,
                "--stop_existing_session", "--exclude", "Dedsec.Monitor.exe", "--exclude", "dwm.exe" })
                start.ArgumentList.Add(arg);
            _process = Process.Start(start);
            if (_process is null) { State = "failed"; return; }
            var process = _process;
            State = "running";
            _running = true;
            _reader = Task.Run(async () =>
            {
                var parser = new PresentMonParser();
                try
                {
                    while (await process.StandardOutput.ReadLineAsync() is { } line)
                        if (parser.Parse(line, DateTimeOffset.Now) is { } frame) _store.Add(frame);
                    await process.WaitForExitAsync();
                    if (!_stopping) { State = "failed"; _running = false; }
                }
                catch (Exception ex) when (ex is IOException or InvalidOperationException or ObjectDisposedException)
                { if (!_stopping) { Error = ex.Message; State = "failed"; _running = false; } }
            });
            _errors = Task.Run(async () =>
            {
                try
                {
                    while (await process.StandardError.ReadLineAsync() is { } line)
                    { if (line.Length > 0) { Error = line.Length > 500 ? line[..500] : line; App.Log("PresentMon: " + Error); } }
                }
                catch (Exception ex) when (ex is IOException or InvalidOperationException or ObjectDisposedException) { }
            });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        { State = "failed"; Error = ex.Message; App.Log("Capture: " + ex.Message); }
    }

    public void Stop()
    { lock (_gate) StopCore(); }

    private void StopCore()
    {
        _stopping = true;
        if (_process is { } process)
        {
            try
            {
                if (!process.HasExited)
                {
                    // Terminate our own named ETW session first, so no trace session is left behind.
                    var end = new ProcessStartInfo(process.StartInfo.FileName) { UseShellExecute = false, CreateNoWindow = true };
                    foreach (var arg in new[] { "--session_name", "DedsecMonitor-" + Environment.ProcessId, "--terminate_existing_session" })
                        end.ArgumentList.Add(arg);
                    using var terminator = Process.Start(end);
                    if (terminator is not null && !terminator.WaitForExit(2000)) terminator.Kill();
                    if (!process.WaitForExit(2000)) process.Kill(true);
                }
                _reader?.Wait(2000); _errors?.Wait(2000);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or AggregateException)
            { App.Log("Capture stop: " + ex.Message); }
            process.Dispose(); _process = null;
        }
        _store.Clear(); State = "stopped"; _running = false;
    }
    public void Dispose() => Stop();
}
