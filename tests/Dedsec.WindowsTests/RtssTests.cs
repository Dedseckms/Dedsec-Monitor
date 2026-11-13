using System.IO.MemoryMappedFiles;
using System.Text;
using Dedsec.Core;
using Dedsec.Monitor;
using Xunit;

namespace Dedsec.WindowsTests;

public sealed class RtssTests
{
    [Fact]
    public void ReadsOpenGlFpsAndOwnsOnlyItsOsdSlot()
    {
        var name = "DedsecRTSSTest-" + Guid.NewGuid().ToString("N");
        using var memory = MemoryMappedFile.CreateNew(name, 1024 * 1024);
        using var view = memory.CreateViewAccessor();
        Header(view);
        var now = unchecked((uint)Environment.TickCount64);
        view.Write(40, 42u); Write(view, 44, "javaw.exe");
        view.Write(40 + 268, now - 1000); view.Write(40 + 272, now); view.Write(40 + 276, 120u);
        Write(view, 2048 + 256, "PrimaryOverlay");
        using var service = new RtssService(name); service.Poll();
        Assert.True(service.Connected);
        var frame = service.Read(42); Assert.NotNull(frame); Assert.Equal(120d, frame.Fps); Assert.Equal("RTSS", frame.Source);
        var snapshot = new Snapshot(DateTimeOffset.Now, "CPU", "GPU", 20, null, null, 80, null, 1500, null, null, null, 7, 16, frame, [], null);
        service.Publish(snapshot, new Settings(), true);
        Assert.True(service.HasOsdSlot);
        Assert.Equal("PrimaryOverlay", Read(view, 2048 + 256));
        var slot = 2048 + 299520;
        Assert.StartsWith("DedsecMonitor-", Read(view, slot + 256));
        Assert.Contains("FPS   120", Read(view, slot + 266752, 4096));
        Assert.Equal(0, view.ReadInt32(36)); Assert.Equal(1u, view.ReadUInt32(32));
        service.Publish(snapshot, new Settings(), false);
        Assert.Equal("", Read(view, slot + 256)); Assert.Equal("", Read(view, slot + 266752));
        Assert.False(service.HasOsdSlot);
    }

    [Fact]
    public void DoesNotUnlockAnotherClientsBusyFlag()
    {
        var name = "DedsecRTSSTest-" + Guid.NewGuid().ToString("N");
        using var memory = MemoryMappedFile.CreateNew(name, 1024 * 1024); using var view = memory.CreateViewAccessor(); Header(view);
        using var service = new RtssService(name); service.Poll(); view.Write(36, 1);
        var snapshot = new Snapshot(DateTimeOffset.Now, "CPU", "GPU", null, null, null, null, null, null, null, null, null, 7, 16, new(0, "", null, null, null, 0), [], null);
        service.Publish(snapshot, new Settings(), true);
        Assert.Equal(1, view.ReadInt32(36)); Assert.False(service.HasOsdSlot);
        view.Write(36, 0);
    }

    private static void Header(MemoryMappedViewAccessor view)
    {
        foreach (var (offset, value) in new[] { (0, 0x52545353u), (4, 0x20014u), (8, 512u), (12, 40u), (16, 2u), (20, 299520u), (24, 2048u), (28, 3u) }) view.Write(offset, value);
    }
    private static void Write(MemoryMappedViewAccessor view, int offset, string text)
    { var bytes = Encoding.ASCII.GetBytes(text + '\0'); view.WriteArray(offset, bytes, 0, bytes.Length); }
    private static string Read(MemoryMappedViewAccessor view, int offset, int count = 256)
    { var bytes = new byte[count]; view.ReadArray(offset, bytes, 0, count); return Encoding.ASCII.GetString(bytes).Split('\0')[0]; }
}
