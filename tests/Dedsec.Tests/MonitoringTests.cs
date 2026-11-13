using System.Globalization;
using System.Text.Json;
using Dedsec.Core;
using Xunit;

namespace Dedsec.Tests;

public sealed class MonitoringTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ParserUsesHeadersAndHandlesQuotedApplicationNames()
    {
        var parser = new PresentMonParser();
        Assert.Null(parser.Parse("warning: not CSV", Now));
        parser.Parse("Application,SwapChainAddress,MsBetweenPresents,ProcessID", Now);
        var frame = parser.Parse("\"game, \"\"special\"\".exe\",0x42,16.6667,123", Now);
        Assert.NotNull(frame); Assert.Equal(123, frame.ProcessId);
        Assert.Equal("game, \"special\".exe", frame.Application);
        Assert.Equal(16.6667, frame.Milliseconds);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("NA")]
    [InlineData("2000")]
    public void ParserRejectsInvalidFrameTimes(string value)
    {
        var parser = new PresentMonParser();
        parser.Parse("Application,ProcessID,MsBetweenPresents", Now);
        Assert.Null(parser.Parse($"game.exe,2,{value}", Now));
    }

    [Fact]
    public void ParserReadsActualPresentMon231V1Output()
    {
        var parser = new PresentMonParser();
        parser.Parse("Application,ProcessID,SwapChainAddress,Runtime,SyncInterval,PresentFlags,Dropped,TimeInSeconds,msInPresentAPI,msBetweenPresents,msSinceInput", Now);
        var frame = parser.Parse("game.exe,11388,0x0000022F72E19EE0,DXGI,1,0,0,0.73766430000000,0.02110000000000,16.66660000000,0.00000000000000", Now);
        Assert.NotNull(frame);
        Assert.Equal(16.6666, frame.Milliseconds);
    }

    [Fact]
    public void ParserSupportsV2FrameTimeAndDecimalPointInRussianCulture()
    {
        var prior = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
            var parser = new PresentMonParser(); parser.Parse("ProcessID,Application,FrameTime", Now);
            Assert.Equal(8.5, parser.Parse("7,game.exe,8.5", Now)!.Milliseconds);
        }
        finally { CultureInfo.CurrentCulture = prior; }
    }

    [Fact]
    public void FpsUsesOnlyTheBusiestSwapChainAndExpiresStaleData()
    {
        var store = new FrameStore();
        for (var i = 0; i < 100; i++) store.Add(new(7, "game.exe", "main", 10, Now));
        for (var i = 0; i < 10; i++) store.Add(new(7, "game.exe", "menu", 30, Now));
        var summary = store.Read(7, Now);
        Assert.Equal(100d, summary.Fps); Assert.Equal(100, summary.Samples);
        Assert.Null(store.Read(8, Now).Fps);
        Assert.Null(store.Read(7, Now.AddSeconds(3)).Fps);
        Assert.Empty(store.Processes(Now.AddSeconds(3)));
    }

    [Fact]
    public void OnePercentLowUsesTheAverageOfTheSlowestOnePercent()
    {
        var store = new FrameStore();
        for (var i = 0; i < 198; i++) store.Add(new(2, "g", "a", 10, Now));
        store.Add(new(2, "g", "a", 40, Now)); store.Add(new(2, "g", "a", 60, Now));
        Assert.Equal(20d, store.Read(2, Now).LowOnePercent);
    }

    [Fact]
    public void FrameStoreBoundsMemory()
    {
        var store = new FrameStore();
        for (var i = 0; i < 10000; i++) store.Add(new(1, "g", "a", 1, Now));
        Assert.Equal(8192, store.Read(1, Now).Samples);
        for (var i = 0; i < 500; i++) store.Add(new(i + 2, "g", "a", 1, Now));
        Assert.InRange(store.Processes(Now).Count, 1, 256);
    }

    [Fact]
    public void SettingsRecoverFromCorruptionAndNormalizeOutOfRangeValues()
    {
        InTemporaryFolder(folder =>
        {
            var path = Path.Combine(folder, "settings.json"); File.WriteAllText(path, "{bad");
            Assert.Equal("ru", SettingsFile.Load(path).Language);
            var settings = new Settings { Language = "xx", OverlayScale = 99, OverlayOpacity = .01, IntervalMs = -1, MonitorIndex = -3 };
            SettingsFile.Save(path, settings);
            var loaded = SettingsFile.Load(path);
            Assert.Equal(2, loaded.OverlayScale); Assert.Equal(.35, loaded.OverlayOpacity);
            Assert.Equal(500, loaded.IntervalMs); Assert.Equal(0, loaded.MonitorIndex);
            Assert.False(File.Exists(path + ".tmp"));
        });
    }

    [Fact]
    public void RecordingWritesInvariantCsvAndPersistsSummaryWithoutInventingFps()
    {
        InTemporaryFolder(folder =>
        {
            using var recorder = new SessionRecorder(); recorder.Start(folder, Now);
            var frame = new FrameSummary(1, "game, x.exe", null, null, null, 0);
            var sample = new Snapshot(Now.AddSeconds(1), "cpu", "gpu", 30.5, null, null, null,
                null, null, null, null, null, 7.5, 16, frame, [], null);
            recorder.Add(sample);
            recorder.Add(sample with { Time = Now.AddSeconds(2), Frames = frame with { Fps = 60 } });
            var info = recorder.Stop(); Assert.NotNull(info);
            Assert.Equal(60d, info.AverageFps); Assert.Equal(2, info.Samples);
            var csv = File.ReadAllLines(info.FilePath);
            Assert.Equal(3, csv.Length); Assert.Contains("\"game, x.exe\"", csv[1]); Assert.Contains("30.5", csv[1]);
            Assert.Equal("", PresentMonParser.SplitCsv(csv[1])[3]);
            var saved = JsonSerializer.Deserialize<SessionInfo>(File.ReadAllText(Path.ChangeExtension(info.FilePath, ".json")));
            Assert.Equal(info, saved); Assert.False(recorder.IsRecording);
        });
    }

    private static void InTemporaryFolder(Action<string> action)
    {
        var folder = Path.Combine(Path.GetTempPath(), "DedsecTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try { action(folder); } finally { Directory.Delete(folder, true); }
    }
}
