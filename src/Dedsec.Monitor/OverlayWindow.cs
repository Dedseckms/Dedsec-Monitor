using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Dedsec.Core;

namespace Dedsec.Monitor;

internal sealed class OverlayPanel : Border
{
    private readonly Dictionary<string, TextBlock> _labels = [];
    private readonly Dictionary<string, FrameworkElement> _rows = [];
    private readonly SignalChart _chart = new() { Height = 34, Width = 252 };
    private readonly Queue<double?> _history = new();
    private DateTimeOffset _lastSample;

    internal OverlayPanel()
    {
        Background = Ui.Brush("#101810"); BorderBrush = Ui.Brush("#567438");
        BorderThickness = new Thickness(1); CornerRadius = new CornerRadius(9); Padding = new Thickness(18);
        var stack = Ui.Stack(Ui.Mono("[D_]  DEDSEC / MONITOR", 11, Ui.Acid)); Ui.Gap(stack, 15);
        foreach (var (key, name) in new[] { ("fps", "FPS"), ("low", "1% LOW"), ("time", "FRAME"),
            ("cpu", "CPU"), ("cpuClock", "C.CLK"), ("gpu", "GPU"), ("gpuClock", "G.CLK"),
            ("vram", "VRAM"), ("power", "POWER"), ("ram", "RAM") })
        {
            var row = Ui.Columns(1, 2.8);
            Ui.Cell(row, Ui.Mono(name, 10, Ui.Muted), 0);
            var label = Ui.Mono("—", key == "fps" ? 28 : 12, key == "fps" ? Ui.Acid : null);
            label.HorizontalAlignment = HorizontalAlignment.Right;
            Ui.Cell(row, label, 1); row.Margin = new Thickness(0, 0, 0, 8);
            _labels[key] = label; _rows[key] = row; stack.Children.Add(row);
        }
        Ui.Gap(stack, 5); stack.Children.Add(_chart); Child = stack;
    }

    internal void Update(Snapshot s, Settings settings, bool preview = false)
    {
        if (s.Time != _lastSample)
        { _history.Enqueue(s.Frames.Fps); while (_history.Count > 40) _history.Dequeue(); _lastSample = s.Time; }
        _chart.Values = _history.ToArray(); _chart.InvalidateVisual();
        _labels["fps"].Text = Ui.N(s.Frames.Fps); _labels["low"].Text = Ui.Unit(s.Frames.LowOnePercent, "FPS");
        _labels["time"].Text = Ui.Unit(s.Frames.FrameTime, "ms", "0.0");
        _labels["cpu"].Text = Ui.Unit(s.CpuLoad, "%") + "  " + Ui.Unit(s.CpuTemperature, "°C");
        _labels["gpu"].Text = Ui.Unit(s.GpuLoad, "%") + "  " + Ui.Unit(s.GpuTemperature, "°C");
        _labels["cpuClock"].Text = Ui.Unit(s.CpuClock, "MHz"); _labels["gpuClock"].Text = Ui.Unit(s.GpuClock, "MHz");
        _labels["vram"].Text = Ui.Unit(s.GpuMemoryUsed / 1024, "GB", "0.00");
        _labels["power"].Text = Ui.Unit(s.GpuPower, "W", "0.0");
        _labels["ram"].Text = Ui.N(s.MemoryUsed, "0.0") + " / " + Ui.N(s.MemoryTotal, "0") + " GB";
        foreach (var (key, enabled) in new[] { ("fps", settings.ShowFps), ("low", settings.ShowFps),
            ("time", settings.ShowFrameTime), ("cpu", settings.ShowCpu), ("gpu", settings.ShowGpu),
            ("cpuClock", settings.ShowCpu && settings.ShowClocks), ("gpuClock", settings.ShowGpu && settings.ShowClocks),
            ("vram", settings.ShowVram), ("power", settings.ShowPower), ("ram", settings.ShowMemory) })
            _rows[key].Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        _chart.Visibility = settings.ShowFrameTime ? Visibility.Visible : Visibility.Collapsed;
        Opacity = settings.OverlayOpacity;
        if (!preview) LayoutTransform = new ScaleTransform(settings.OverlayScale, settings.OverlayScale);
    }
}

internal sealed class OverlayWindow : Window
{
    private readonly App _app;
    private OverlayPanel _panel = new();
    private bool _initialized;

    internal OverlayWindow(App app)
    {
        _app = app; Title = "Dedsec Monitor Overlay";
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ShowInTaskbar = false; ShowActivated = false; Topmost = true; ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight; Content = _panel;
        SourceInitialized += (_, _) => { _initialized = true; Native.MakeClickThrough(this); };
    }

    internal void Rebuild() { _panel = new OverlayPanel(); Content = _panel; }
    internal void Update(Snapshot snapshot)
    {
        var settings = _app.Settings;
        _panel.Update(snapshot, settings);
        var target = _app.ManualTarget > 0 ? _app.ManualTarget : _app.AutoTarget;
        var show = !_app.UsesRtssOverlay && settings.OverlayEnabled && (!settings.OverlayOnlyInGame || target > 0 && Native.ForegroundProcess() == target);
        if (show)
        {
            if (!IsVisible) Show();
            if (_initialized)
            {
                UpdateLayout();
                var screens = System.Windows.Forms.Screen.AllScreens;
                Native.PlaceOverlay(this, screens[Math.Min(settings.MonitorIndex, screens.Length - 1)].WorkingArea, settings.OverlayCorner);
            }
        }
        else if (IsVisible) Hide();
    }
}
