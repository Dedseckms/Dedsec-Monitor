using System.Buffers.Binary;
using System.Text;

namespace Dedsec.Core;

// Independently implemented from the published RTSS 7.3.7 SDK shared-memory layout.
// SDK source: Guru3D official distribution, SDK/Include/RTSSSharedMemory.h. No vendor code is included.
public sealed record RtssLayout(uint Version, int AppSize, int AppOffset, int AppCount,
    int OsdSize, int OsdOffset, int OsdCount)
{
    public static RtssLayout? Read(ReadOnlySpan<byte> header, long capacity)
    {
        if (header.Length < 40) return null;
        var fields = new uint[10];
        for (var i = 0; i < fields.Length; i++) fields[i] = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(i * 4, 4));
        uint U(int offset) => fields[offset / 4];
        if (U(0) != 0x52545353 || U(4) < 0x2000e || U(4) >= 0x30000) return null;
        var appSize = U(8); var appOffset = U(12); var apps = U(16);
        var osdSize = U(20); var osdOffset = U(24); var osds = U(28);
        if (appSize < 284 || appSize > 65536 || apps > 1024 || appOffset < 40 ||
            osdSize < 4608 || osdSize > 1048576 || osds < 2 || osds > 128 || osdOffset < 40 ||
            appOffset + (ulong)apps * appSize > (ulong)capacity ||
            osdOffset + (ulong)osds * osdSize > (ulong)capacity) return null;
        return new(U(4), (int)appSize, (int)appOffset, (int)apps, (int)osdSize, (int)osdOffset, (int)osds);
    }

    public static FrameSummary? ReadApp(ReadOnlySpan<byte> entry, uint now)
    {
        if (entry.Length < 284) return null;
        var pid = BinaryPrimitives.ReadUInt32LittleEndian(entry);
        var time0 = BinaryPrimitives.ReadUInt32LittleEndian(entry[268..]);
        var time1 = BinaryPrimitives.ReadUInt32LittleEndian(entry[272..]);
        var frames = BinaryPrimitives.ReadUInt32LittleEndian(entry[276..]);
        var delta = unchecked(time1 - time0);
        if (pid == 0 || pid > int.MaxValue || time0 == 0 || delta == 0 || frames == 0 || unchecked(now - time1) > 3000) return null;
        var fps = 1000d * frames / delta;
        if (!double.IsFinite(fps) || fps <= 0 || fps > 10000) return null;
        var nameBytes = entry.Slice(4, 260); var zero = nameBytes.IndexOf((byte)0);
        var name = Encoding.Default.GetString(zero >= 0 ? nameBytes[..zero] : nameBytes);
        return new((int)pid, Path.GetFileName(name), fps, 1000 / fps, null, (int)Math.Min(frames, int.MaxValue), "RTSS");
    }
}
