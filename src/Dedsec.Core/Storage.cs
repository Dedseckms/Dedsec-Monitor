using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Dedsec.Core;

public static class SettingsFile
{
    public static Settings Load(string path)
    {
        try
        {
            var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path)) ?? new Settings();
            settings.Normalize();
            return settings;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        { return new Settings(); }
    }

    public static void Save(string path, Settings settings)
    {
        settings.Normalize();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, path, true);
    }
}

public sealed record SessionInfo(string FilePath, DateTimeOffset Started, double DurationSeconds,
    long Samples, double? AverageFps, double? MinimumFps, double? MaxCpuTemperature, double? MaxGpuTemperature);

public sealed class SessionRecorder : IDisposable
{
    private StreamWriter? _writer;
    private long _fpsSamples;
    private double _fpsSum;
    public SessionInfo? Current { get; private set; }
    public bool IsRecording => _writer is not null;

    public void Start(string folder, DateTimeOffset now)
    {
        if (IsRecording) return;
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"Dedsec-{now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}.csv");
        _writer = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(true));
        _writer.WriteLine("Timestamp,ProcessId,Application,FPS,FrameTimeMs,Low1PercentFPS,CpuLoadPercent,CpuTemperatureC,CpuClockMHz,GpuLoadPercent,GpuTemperatureC,GpuClockMHz,VramUsedMB,GpuPowerW,RamUsedGB,RamTotalGB");
        _writer.Flush();
        _fpsSum = 0; _fpsSamples = 0;
        Current = new(path, now, 0, 0, null, null, null, null);
    }

    public void Add(Snapshot sample)
    {
        if (_writer is null || Current is null) return;
        static string N(double? value) => value?.ToString("0.###", CultureInfo.InvariantCulture) ?? "";
        _writer.WriteLine(string.Join(',', sample.Time.ToString("O"), sample.Frames.ProcessId,
            Escape(sample.Frames.Application), N(sample.Frames.Fps), N(sample.Frames.FrameTime),
            N(sample.Frames.LowOnePercent), N(sample.CpuLoad), N(sample.CpuTemperature), N(sample.CpuClock),
            N(sample.GpuLoad), N(sample.GpuTemperature), N(sample.GpuClock), N(sample.GpuMemoryUsed),
            N(sample.GpuPower), N(sample.MemoryUsed), N(sample.MemoryTotal)));
        _writer.Flush();
        if (sample.Frames.Fps is { } fps) { _fpsSum += fps; _fpsSamples++; }
        static double? Max(double? a, double? b) => a is null ? b : b is null ? a : Math.Max(a.Value, b.Value);
        Current = Current with
        {
            Samples = Current.Samples + 1,
            DurationSeconds = (sample.Time - Current.Started).TotalSeconds,
            AverageFps = _fpsSamples > 0 ? _fpsSum / _fpsSamples : null,
            MinimumFps = sample.Frames.Fps is { } f ? Math.Min(Current.MinimumFps ?? f, f) : Current.MinimumFps,
            MaxCpuTemperature = Max(Current.MaxCpuTemperature, sample.CpuTemperature),
            MaxGpuTemperature = Max(Current.MaxGpuTemperature, sample.GpuTemperature)
        };
    }

    public SessionInfo? Stop()
    {
        _writer?.Dispose(); _writer = null;
        if (Current is { } current)
            File.WriteAllText(Path.ChangeExtension(current.FilePath, ".json"), JsonSerializer.Serialize(current,
                new JsonSerializerOptions { WriteIndented = true }));
        return Current;
    }

    public static string Escape(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
    public void Dispose() { _writer?.Dispose(); _writer = null; }
}
