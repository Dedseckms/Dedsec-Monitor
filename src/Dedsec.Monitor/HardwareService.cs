using System.Runtime.InteropServices;
using LibreHardwareMonitor.Hardware;
using Dedsec.Core;

namespace Dedsec.Monitor;

internal sealed class HardwareService : IDisposable
{
    private readonly Computer _computer = new() { IsCpuEnabled = true, IsGpuEnabled = true, IsMemoryEnabled = true };
    private bool _opened;
    private string? _error;
    private long _lastIdle, _lastTotal;
    public IReadOnlyList<(string Id, string Name)> Gpus { get; private set; } = [];

    public void Open()
    {
        try
        {
            _computer.Open(); _opened = true;
            Gpus = _computer.Hardware.Where(h => IsGpu(h.HardwareType))
                .Select(h => (h.Identifier.ToString(), h.Name)).ToArray();
        }
        catch (Exception ex) { _error = ex.Message; App.Log("Hardware: " + ex.Message); }
    }

    public Snapshot Read(FrameSummary frames, string? selectedGpu)
    {
        var sensors = new List<SensorReading>();
        if (_opened)
        {
            foreach (var hardware in _computer.Hardware)
            {
                try { Update(hardware, sensors); }
                catch (Exception ex) { _error = ex.Message; App.Log("Sensor: " + ex.Message); }
            }
        }
        var cpu = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
        var gpus = _computer.Hardware.Where(h => IsGpu(h.HardwareType)).ToArray();
        var gpu = gpus.FirstOrDefault(h => h.Identifier.ToString() == selectedGpu)
            ?? gpus.OrderByDescending(h => h.HardwareType == HardwareType.GpuNvidia || h.HardwareType == HardwareType.GpuAmd)
                .ThenByDescending(h => Find(h, SensorType.Load, "GPU Core") ?? 0).FirstOrDefault();
        var memory = new Native.MemoryStatus { Length = (uint)Marshal.SizeOf<Native.MemoryStatus>() };
        var memoryOk = Native.GlobalMemoryStatusEx(ref memory);
        var cpuClock = cpu?.Sensors.Where(s => s.SensorType == SensorType.Clock && s.Name.StartsWith("CPU Core", StringComparison.OrdinalIgnoreCase))
            .Select(s => (double?)s.Value).Where(v => v > 0).ToArray();
        return new(DateTimeOffset.Now, cpu?.Name ?? System.Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "CPU",
            gpu?.Name ?? "GPU", Find(cpu, SensorType.Load, "CPU Total") ?? CpuFallback(),
            Find(cpu, SensorType.Temperature, "CPU Package", "Core (Tctl/Tdie)", "Core (Tdie)")
                ?? Max(cpu, SensorType.Temperature),
            cpuClock is { Length: > 0 } ? cpuClock.Average() : null,
            Find(gpu, SensorType.Load, "GPU Core", "D3D 3D"), Find(gpu, SensorType.Temperature, "GPU Core"),
            Find(gpu, SensorType.Clock, "GPU Core"), Find(gpu, SensorType.SmallData, "GPU Memory Used"),
            Find(gpu, SensorType.SmallData, "GPU Memory Total"), Find(gpu, SensorType.Power, "GPU Package", "GPU Board"),
            memoryOk ? (memory.TotalPhysical - memory.AvailablePhysical) / 1073741824d : 0,
            memoryOk ? memory.TotalPhysical / 1073741824d : 0, frames, sensors, _error);
    }

    private double? CpuFallback()
    {
        if (!Native.GetSystemTimes(out var idle, out var kernel, out var user)) return null;
        var total = kernel + user;
        var delta = total - _lastTotal;
        var result = _lastTotal > 0 && delta > 0 ? Math.Clamp(100d * (delta - (idle - _lastIdle)) / delta, 0, 100) : (double?)null;
        _lastIdle = idle; _lastTotal = total;
        return result;
    }

    private static void Update(IHardware hardware, List<SensorReading> readings)
    {
        hardware.Update();
        foreach (var sensor in hardware.Sensors)
            readings.Add(new(hardware.Name, sensor.Name, sensor.SensorType.ToString(), sensor.Value));
        foreach (var child in hardware.SubHardware) Update(child, readings);
    }

    private static bool IsGpu(HardwareType type) => type is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel;
    private static double? Find(IHardware? hardware, SensorType type, params string[] names)
    {
        foreach (var name in names)
        {
            var value = hardware?.Sensors.FirstOrDefault(s => s.SensorType == type && s.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value;
            if (value is not null && float.IsFinite(value.Value)) return value;
        }
        return null;
    }
    private static double? Max(IHardware? hardware, SensorType type) => hardware?.Sensors
        .Where(s => s.SensorType == type && s.Value is not null).Select(s => (double?)s.Value).DefaultIfEmpty().Max();
    public void Dispose() { if (_opened) _computer.Close(); }
}
