namespace Dedsec.Core;

public sealed record FrameSummary(int ProcessId, string Application, double? Fps,
    double? FrameTime, double? LowOnePercent, int Samples);

public sealed record SensorReading(string Device, string Name, string Type, double? Value);

public sealed record Snapshot(DateTimeOffset Time, string CpuName, string GpuName,
    double? CpuLoad, double? CpuTemperature, double? CpuClock,
    double? GpuLoad, double? GpuTemperature, double? GpuClock,
    double? GpuMemoryUsed, double? GpuMemoryTotal, double? GpuPower,
    double MemoryUsed, double MemoryTotal, FrameSummary Frames,
    IReadOnlyList<SensorReading> Sensors, string? SensorError)
{
    public double MemoryLoad => MemoryTotal > 0 ? MemoryUsed / MemoryTotal * 100 : 0;
}

public sealed class Settings
{
    public string Language { get; set; } = "ru";
    public bool OverlayEnabled { get; set; }
    public string OverlayCorner { get; set; } = "TopLeft";
    public int MonitorIndex { get; set; }
    public double OverlayScale { get; set; } = 1;
    public double OverlayOpacity { get; set; } = .92;
    public bool OverlayOnlyInGame { get; set; } = true;
    public bool ShowFps { get; set; } = true;
    public bool ShowCpu { get; set; } = true;
    public bool ShowGpu { get; set; } = true;
    public bool ShowMemory { get; set; } = true;
    public bool ShowFrameTime { get; set; } = true;
    public bool ShowClocks { get; set; } = true;
    public bool ShowVram { get; set; } = true;
    public bool ShowPower { get; set; }
    public bool CloseToTray { get; set; } = true;
    public bool StartMinimized { get; set; }
    public bool AutoCapture { get; set; } = true;
    public int IntervalMs { get; set; } = 1000;
    public string? GpuIdentifier { get; set; }

    public void Normalize()
    {
        Language = Language == "en" ? "en" : "ru";
        if (OverlayCorner is not ("TopLeft" or "TopRight" or "BottomLeft" or "BottomRight"))
            OverlayCorner = "TopLeft";
        OverlayScale = double.IsFinite(OverlayScale) ? Math.Clamp(OverlayScale, .75, 2) : 1;
        OverlayOpacity = double.IsFinite(OverlayOpacity) ? Math.Clamp(OverlayOpacity, .35, 1) : .92;
        IntervalMs = Math.Clamp(IntervalMs, 500, 3000);
        MonitorIndex = Math.Max(0, MonitorIndex);
    }
}
