using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;
using Dedsec.Core;
using Microsoft.Win32;

namespace Dedsec.Monitor;

internal sealed class RtssService : IDisposable
{
    internal const string OfficialDownload = "https://www.guru3d.com/download/rtss-rivatuner-statistics-server-download/";
    private readonly object _gate = new();
    private readonly string _owner = "DedsecMonitor-" + Environment.ProcessId;
    private MemoryMappedFile? _mapping;
    private MemoryMappedViewAccessor? _view;
    private RtssLayout? _layout;
    private DateTimeOffset _nextConnect;
    private readonly Dictionary<int, int> _entryOffsets = [];
    private int _slot = -1;
    private readonly string _memoryName;
    internal RtssService(string memoryName = "RTSSSharedMemoryV2") { _memoryName = memoryName; }
    internal bool HasOsdSlot => _slot >= 0;
    internal bool Connected { get; private set; }
    internal string? Error { get; private set; }
    internal IReadOnlyList<FrameSummary> Applications { get; private set; } = [];

    internal static string? FindExecutable(string? configured = null)
    {
        string? registered = null;
        foreach (var registryView in new[] { RegistryView.Registry32, RegistryView.Registry64 })
        {
            try
            {
                using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, registryView);
                using var key = machine.OpenSubKey(@"SOFTWARE\Unwinder\RTSS");
                if (key?.GetValue("InstallPath") is string value && File.Exists(value)) { registered = value; break; }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException) { }
        }
        var paths = new[] { configured, registered,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "RivaTuner Statistics Server", "RTSS.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "RivaTuner Statistics Server", "RTSS.exe") };
        return paths.FirstOrDefault(p => p is not null && Path.GetFileName(p).Equals("RTSS.exe", StringComparison.OrdinalIgnoreCase) && File.Exists(p));
    }

    internal static void StartInstalled(string? configured)
    {
        if (Process.GetProcessesByName("RTSS").Length > 0) return;
        var path = FindExecutable(configured);
        if (path is null) return;
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(path)! }); }
        catch (Exception ex) { App.Log("RTSS startup: " + ex.Message); }
    }

    internal void Poll()
    {
        lock (_gate)
        {
            try
            {
                if (_view is null)
                {
                    if (DateTimeOffset.UtcNow < _nextConnect) return;
                    _nextConnect = DateTimeOffset.UtcNow.AddSeconds(3);
                    _mapping = MemoryMappedFile.OpenExisting(_memoryName, MemoryMappedFileRights.ReadWrite);
                    _view = _mapping.CreateViewAccessor(0, 0, MemoryMappedFileAccess.ReadWrite);
                }
                var header = new byte[40]; _view.ReadArray(0, header, 0, header.Length);
                _layout = RtssLayout.Read(header, _view.Capacity);
                if (_layout is null) { Disconnect(); return; }
                var apps = new List<FrameSummary>(); _entryOffsets.Clear();
                var bytes = new byte[284];
                for (var i = 0; i < _layout.AppCount; i++)
                {
                    var offset = _layout.AppOffset + i * _layout.AppSize;
                    _view.ReadArray(offset, bytes, 0, bytes.Length);
                    if (RtssLayout.ReadApp(bytes, unchecked((uint)Environment.TickCount64)) is not { } app) continue;
                    if (app.ProcessId == Environment.ProcessId) continue;
                    apps.Add(app); _entryOffsets[app.ProcessId] = offset;
                }
                Applications = apps; Connected = true; Error = null;
            }
            catch (FileNotFoundException) { Disconnect(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            { Error = ex.Message; Disconnect(); }
        }
    }

    internal FrameSummary? Read(int pid) => Applications.FirstOrDefault(p => p.ProcessId == pid);

    internal void Publish(Snapshot snapshot, Settings settings, bool visible)
    {
        lock (_gate)
        {
            if (_view is null || _layout is null || !Connected) return;
            if (!TryLock(_view)) return;
            try
            {
                var slot = FindSlot(visible);
                if (slot < 0) return;
                var offset = _layout.OsdOffset + slot * _layout.OsdSize;
                var extended = _layout.Version >= 0x20014 && _layout.OsdSize >= 299520;
                var textOffset = offset + (extended ? 266752 : 512);
                WriteString(textOffset, extended ? 32768 : 4096, visible ? Format(snapshot, settings) : "");
                if (!visible) { WriteString(offset + 256, 256, ""); _slot = -1; }
                _view.Write(32, unchecked(_view.ReadUInt32(32) + 1));
            }
            catch (Exception ex) when (ex is IOException or ArgumentException) { Error = ex.Message; }
            finally { Unlock(_view); }
        }
    }

    private int FindSlot(bool claim)
    {
        if (_view is null || _layout is null) return -1;
        for (var i = 1; i < _layout.OsdCount; i++)
        {
            var owner = ReadString(_layout.OsdOffset + i * _layout.OsdSize + 256, 256);
            if (owner == _owner) return _slot = i;
        }
        if (!claim) return -1;
        for (var i = 1; i < _layout.OsdCount; i++)
        {
            var offset = _layout.OsdOffset + i * _layout.OsdSize + 256;
            if (ReadString(offset, 256).Length != 0) continue;
            WriteString(offset, 256, _owner); return _slot = i;
        }
        Error = "RTSS has no free OSD slots."; return -1;
    }

    private string ReadString(int offset, int capacity)
    {
        var bytes = new byte[capacity]; _view!.ReadArray(offset, bytes, 0, bytes.Length);
        var zero = Array.IndexOf(bytes, (byte)0); return Encoding.ASCII.GetString(bytes, 0, zero >= 0 ? zero : bytes.Length);
    }
    private void WriteString(int offset, int capacity, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value); var length = Math.Min(capacity - 1, bytes.Length);
        _view!.WriteArray(offset, bytes, 0, length); _view.Write(offset + length, (byte)0);
    }

    private static string Format(Snapshot s, Settings settings)
    {
        static string N(double? v, string f = "0") => v?.ToString(f, System.Globalization.CultureInfo.InvariantCulture) ?? "--";
        var lines = new List<string> { $"<C=C2F970><S={(int)(settings.OverlayScale * 100)}>DEDSEC MONITOR" };
        if (settings.ShowFps) lines.Add($"FPS   {N(s.Frames.Fps)}");
        if (settings.ShowFrameTime) lines.Add($"FRAME {N(s.Frames.FrameTime, "0.0")} ms");
        if (settings.ShowCpu) lines.Add($"CPU   {N(s.CpuLoad)}%  {N(s.CpuTemperature)} C" + (settings.ShowClocks ? $"  {N(s.CpuClock)} MHz" : ""));
        if (settings.ShowGpu) lines.Add($"GPU   {N(s.GpuLoad)}%  {N(s.GpuTemperature)} C" + (settings.ShowClocks ? $"  {N(s.GpuClock)} MHz" : ""));
        if (settings.ShowMemory) lines.Add($"RAM   {N(s.MemoryUsed, "0.0")} / {N(s.MemoryTotal, "0.0")} GB");
        if (settings.ShowVram) lines.Add($"VRAM  {N(s.GpuMemoryUsed / 1024, "0.0")} GB");
        if (settings.ShowPower) lines.Add($"POWER {N(s.GpuPower)} W");
        return string.Join('\n', lines) + "<S><C>";
    }

    // Exactly one bounded shared-memory int is exposed to Interlocked; no game memory or code is accessed.
    private static unsafe bool TryLock(MemoryMappedViewAccessor view)
    {
        byte* pointer = null;
        try
        {
            view.SafeMemoryMappedViewHandle.AcquirePointer(ref pointer);
            ref var busy = ref *(int*)(pointer + view.PointerOffset + 36);
            var old = Volatile.Read(ref busy);
            return (old & 1) == 0 && Interlocked.CompareExchange(ref busy, old | 1, old) == old;
        }
        finally { if (pointer != null) view.SafeMemoryMappedViewHandle.ReleasePointer(); }
    }
    private static unsafe void Unlock(MemoryMappedViewAccessor view)
    {
        byte* pointer = null;
        try { view.SafeMemoryMappedViewHandle.AcquirePointer(ref pointer); Interlocked.And(ref *(int*)(pointer + view.PointerOffset + 36), ~1); }
        finally { if (pointer != null) view.SafeMemoryMappedViewHandle.ReleasePointer(); }
    }
    private void Disconnect()
    { Connected = false; Applications = []; _layout = null; _slot = -1; _view?.Dispose(); _view = null; _mapping?.Dispose(); _mapping = null; }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_view is not null && _layout is not null && TryLock(_view))
            {
                try
                {
                    var slot = FindSlot(false);
                    if (slot >= 0)
                    {
                        var offset = _layout.OsdOffset + slot * _layout.OsdSize;
                        WriteString(offset, 256, ""); WriteString(offset + 256, 256, ""); WriteString(offset + 512, 4096, "");
                        if (_layout.OsdSize >= 299520) WriteString(offset + 266752, 32768, "");
                        _view.Write(32, unchecked(_view.ReadUInt32(32) + 1));
                    }
                }
                finally { Unlock(_view); }
            }
            Disconnect();
        }
    }
}
