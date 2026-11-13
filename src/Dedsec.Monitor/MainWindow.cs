using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Shell;
using Dedsec.Core;
using Microsoft.Win32;

namespace Dedsec.Monitor;

internal sealed class MainWindow : Window
{
    private readonly App _app;
    private readonly ContentControl _content = new();
    private readonly StackPanel _navigation = Ui.Stack();
    private readonly TextBlock _status = Ui.Mono("", 10, Ui.Acid);
    private readonly TextBlock _notice = Ui.Text("", 12, Ui.Brush("#E5B56B"));
    private readonly Dictionary<string, TextBlock> _values = [];
    private readonly Dictionary<string, SignalChart> _charts = [];
    private readonly Queue<Snapshot> _history = new();
    private Button? _recordButton, _captureButton, _overlayButton;
    private ComboBox? _processes;
    private ListView? _sensorList;
    private HwndSource? _source;
    private OverlayPanel? _preview;
    private nint _handle;
    internal bool HotkeysRegistered { get; private set; }
    internal string CurrentPage { get; private set; } = "dashboard";

    internal MainWindow(App app)
    {
        _app = app; Title = "Dedsec Monitor"; Width = 1440; Height = 960;
        Background = Ui.Brush("#101310"); Foreground = Ui.Brush("#EBF0E7"); FontFamily = new FontFamily("Segoe UI"); FontSize = 14;
        var area = SystemParameters.WorkArea;
        Width = Math.Min(1440, area.Width - 40); Height = Math.Min(960, area.Height - 40);
        MinWidth = Math.Min(1000, area.Width - 40); MinHeight = Math.Min(620, area.Height - 40);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowStyle = WindowStyle.None;
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 0,
            ResizeBorderThickness = new Thickness(7),
            GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false
        });
        var root = new Grid { Background = Ui.Brush("#101310") }; root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(214) });
        root.ColumnDefinitions.Add(new ColumnDefinition());
        var sidebar = new DockPanel { LastChildFill = true, Margin = new Thickness(22, 28, 20, 22) };
        var logo = Ui.Stack(Ui.Text("[D_]", 44, Ui.Acid, true), Ui.Text("DEDSEC", 25, null, true),
            Ui.Mono("M O N I T O R", 11, Ui.Muted));
        logo.Margin = new Thickness(6, 0, 0, 44); DockPanel.SetDock(logo, Dock.Top); sidebar.Children.Add(logo);
        var bottom = Ui.Stack(Ui.Mono("v" + App.ProductVersion, 10, Ui.Muted), new Border { Height = 12 }, Ui.Text("", 10, Ui.Muted));
        DockPanel.SetDock(bottom, Dock.Bottom); sidebar.Children.Add(bottom); sidebar.Children.Add(_navigation);
        Ui.Cell(root, new Border
        {
            Background = Ui.Brush("#141914"),
            BorderBrush = Ui.Edge,
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = sidebar
        }, 0);
        var main = new DockPanel { Margin = new Thickness(32, 25, 32, 22) };
        var top = Ui.Columns(1, 1);
        Ui.Cell(top, Ui.Mono("DEDSEC / PERFORMANCE CONTROL", 11, Ui.Muted), 0);
        _status.HorizontalAlignment = HorizontalAlignment.Right; Ui.Cell(top, _status, 1);
        top.Margin = new Thickness(0, 0, 0, 25); DockPanel.SetDock(top, Dock.Top); main.Children.Add(top);
        _notice.Margin = new Thickness(0, 10, 0, 0); DockPanel.SetDock(_notice, Dock.Bottom); main.Children.Add(_notice);
        main.Children.Add(new ScrollViewer { Content = _content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        Ui.Cell(root, main, 1);
        var frame = new DockPanel { Background = Ui.Brush("#101310") };
        var caption = CreateCaption(); DockPanel.SetDock(caption, Dock.Top); frame.Children.Add(caption);
        frame.Children.Add(root); Content = frame;
        SourceInitialized += (_, _) => RegisterKeys();
        Closing += OnClosing;
        Navigate("dashboard");
    }

    private UIElement CreateCaption()
    {
        var caption = new DockPanel { Height = 44, Background = Ui.Brush("#171F15"), LastChildFill = true };
        var actions = Ui.Row();
        var minimize = Ui.Action("—", () => WindowState = WindowState.Minimized);
        var maximize = Ui.Action("□", ToggleMaximize);
        var close = Ui.Action("×", Close);
        foreach (var (button, key) in new[] { (minimize, "windowMin"), (maximize, "windowMax"), (close, "windowClose") })
        {
            button.Width = 48; button.Height = 44; button.Padding = new Thickness(0);
            button.FontSize = 19; button.BorderThickness = new Thickness(0); button.Background = Brushes.Transparent;
            button.ToolTip = L.T(key); System.Windows.Automation.AutomationProperties.SetName(button, L.T(key));
            actions.Children.Add(button);
        }
        close.Foreground = Ui.Brush("#FFB0A0"); DockPanel.SetDock(actions, Dock.Right); caption.Children.Add(actions);
        var drag = new Border { Background = Brushes.Transparent, Padding = new Thickness(22, 0, 0, 0), Child = Ui.Mono("[D_]  DEDSEC MONITOR", 12, Ui.Acid) };
        drag.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2) ToggleMaximize();
            else if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        };
        caption.Children.Add(drag); return caption;
    }

    private void ToggleMaximize() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void RegisterKeys()
    {
        _handle = new WindowInteropHelper(this).Handle;
        Native.DarkTitleBar(_handle);
        _source = HwndSource.FromHwnd(_handle); _source?.AddHook(WindowMessage);
        var overlay = Native.RegisterHotKey(_handle, 1, 0x4006, 0x4F);
        var record = Native.RegisterHotKey(_handle, 2, 0x4006, 0x52);
        HotkeysRegistered = overlay && record;
        if (!HotkeysRegistered) SetNotice(L.T("hotkeyFailed"));
    }

    private nint WindowMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message != 0x0312) return 0;
        if (wParam == 1) _app.ToggleOverlay();
        if (wParam == 2) _app.ToggleRecording();
        handled = true; return 0;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_app.Exiting)
        {
            e.Cancel = true;
            if (_app.Settings.CloseToTray) Hide(); else _app.RequestExit();
        }
        else
        {
            Native.UnregisterHotKey(_handle, 1); Native.UnregisterHotKey(_handle, 2);
            _source?.RemoveHook(WindowMessage);
        }
    }

    internal void Restore() { Show(); WindowState = WindowState.Normal; Activate(); }
    internal bool VerifyWindowControls()
    {
        WindowState = WindowState.Minimized; var minimized = WindowState == WindowState.Minimized;
        WindowState = WindowState.Normal; ToggleMaximize(); var maximized = WindowState == WindowState.Maximized;
        ToggleMaximize(); return minimized && maximized && WindowState == WindowState.Normal;
    }
    internal void SetNotice(string message) => _notice.Text = message;

    internal void ExportScreenshot(string path)
    {
        var root = (FrameworkElement)Content;
        Content = null; UpdateLayout();
        var export = new Border { Width = 1440, Height = 1000, Background = Ui.Brush("#101310"), Child = root };
        try
        {
            export.Measure(new Size(1440, 1000)); export.Arrange(new Rect(0, 0, 1440, 1000)); export.UpdateLayout();
            App.SaveVisual(export, path);
        }
        finally
        {
            export.Child = null; Content = root; UpdateLayout();
        }
    }

    internal void Navigate(string page)
    {
        CurrentPage = page; _values.Clear(); _charts.Clear(); _recordButton = null; _captureButton = null;
        _overlayButton = null; _processes = null; _sensorList = null; _preview = null;
        _navigation.Children.Clear();
        var index = 0;
        foreach (var key in new[] { "dashboard", "overlay", "sessions", "sensors", "settings" })
        {
            var navKey = key;
            var label = Ui.Row(Ui.Mono($"0{++index}", 11, key == page ? Ui.Acid : Ui.Muted),
                new Border { Width = 14 }, Ui.Text(L.T(key), 14, key == page ? Ui.Acid : Ui.Muted));
            var button = Ui.Action("", () => Navigate(navKey)); button.Content = label;
            button.HorizontalContentAlignment = HorizontalAlignment.Left; button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.Margin = new Thickness(0, 0, 0, 9); button.Padding = new Thickness(14, 13, 10, 13);
            button.Background = Ui.Brush(key == page ? "#29371F" : "#141914");
            button.BorderThickness = new Thickness(key == page ? 1 : 0); _navigation.Children.Add(button);
        }
        Ui.Gap(_navigation, 30);
        _navigation.Children.Add(Ui.Mono(L.T("local"), 9, Ui.Muted));
        _content.Content = page switch
        {
            "overlay" => OverlayPage(),
            "sessions" => SessionsPage(),
            "sensors" => SensorsPage(),
            "settings" => SettingsPage(),
            _ => Dashboard()
        };
        RefreshButtons(); if (_app.Latest is { } snapshot) Update(snapshot, false);
    }

    private StackPanel Header(string titleKey, string subtitleKey)
    {
        var subtitle = Ui.Text(L.T(subtitleKey), 13, Ui.Muted); subtitle.Margin = new Thickness(0, 9, 0, 0);
        return Ui.Stack(Ui.Text(L.T(titleKey), 32, null, true), subtitle, new Border { Height = 24 });
    }

    private TextBlock Value(string key, double size = 16, Brush? color = null)
    { var label = Ui.Mono("—", size, color); _values[key] = label; return label; }

    private UIElement Metric(string title, string key, string suffix, string detailKey, Brush? color = null)
    {
        var stack = Ui.Stack(Ui.Mono(L.T(title), 10, Ui.Muted)); Ui.Gap(stack, 15);
        var value = Value(key, 36, color);
        var suffixLabel = Ui.Mono(suffix, 12, Ui.Muted); suffixLabel.Margin = new Thickness(8, 18, 0, 0);
        stack.Children.Add(Ui.Row(value, suffixLabel)); Ui.Gap(stack, 8);
        stack.Children.Add(Value(detailKey, 11, Ui.Muted));
        return Ui.Card(stack, 20);
    }

    private UIElement Dashboard()
    {
        var page = Header("intro", "introSub");
        var actions = Ui.Row();
        _recordButton = Ui.Action("", _app.ToggleRecording, true); _recordButton.Margin = new Thickness(0, 0, 10, 0);
        _captureButton = Ui.Action("", _app.ToggleCapture); _captureButton.Margin = new Thickness(0, 0, 10, 0);
        _overlayButton = Ui.Action("", _app.ToggleOverlay);
        actions.Children.Add(_recordButton); actions.Children.Add(_captureButton); actions.Children.Add(_overlayButton);
        if (!_app.Elevated)
        {
            var elevate = Ui.Action(L.T("elevate"), _app.Elevate); elevate.Margin = new Thickness(10, 0, 0, 0);
            actions.Children.Add(elevate);
        }
        page.Children.Add(actions); Ui.Gap(page, 23);
        var metrics = Ui.Columns(1, 1, 1, 1);
        var cards = new[] { Metric("fps", "fps", "FPS", "fpsDetail", Ui.Acid),
            Metric("cpu", "cpuLoad", "%", "cpuDetail"), Metric("gpu", "gpuLoad", "%", "gpuDetail"),
            Metric("ram", "ramUsed", "GB", "ramDetail") };
        for (var i = 0; i < cards.Length; i++)
        { ((FrameworkElement)cards[i]).Margin = new Thickness(0, 0, i < 3 ? 14 : 0, 0); Ui.Cell(metrics, cards[i], i); }
        page.Children.Add(metrics); Ui.Gap(page, 18);
        var middle = Ui.Columns(2.3, 1);
        var chartStack = Ui.Stack();
        var titleRow = Ui.Columns(2, 1); Ui.Cell(titleRow, Ui.Text(L.T("history"), 17, null, true), 0);
        var badge = Ui.Badge("● " + L.T("live")); badge.HorizontalAlignment = HorizontalAlignment.Right; Ui.Cell(titleRow, badge, 1);
        chartStack.Children.Add(titleRow); Ui.Gap(chartStack, 8);
        chartStack.Children.Add(Value("game", 12, Ui.Muted)); Ui.Gap(chartStack, 24);
        var chart = new SignalChart { Height = 178 }; _charts["fps"] = chart; chartStack.Children.Add(chart);
        Ui.Gap(chartStack, 12);
        var chartLabels = Ui.Columns(1, 1); Ui.Cell(chartLabels, Ui.Mono(L.T("last60"), 9, Ui.Muted), 0);
        var now = Ui.Mono(L.T("now"), 9, Ui.Muted); now.HorizontalAlignment = HorizontalAlignment.Right; Ui.Cell(chartLabels, now, 1);
        chartStack.Children.Add(chartLabels); Ui.Gap(chartStack, 20);
        chartStack.Children.Add(Ui.Text(L.T("fpsHelp"), 12, Ui.Muted));
        Ui.Cell(middle, Ui.Margin(Ui.Card(chartStack), 16), 0);
        var stats = Ui.Stack(Ui.Mono(L.T("target").ToUpperInvariant(), 10, Ui.Muted)); Ui.Gap(stats, 12);
        _processes = new ComboBox { MaxDropDownHeight = 260 };
        PopulateProcesses(); _processes.DropDownOpened += (_, _) => PopulateProcesses();
        _processes.SelectionChanged += (_, _) => { if (_processes.SelectedItem is TargetChoice choice) _app.ManualTarget = choice.Id; };
        stats.Children.Add(_processes); Ui.Gap(stats, 22);
        AddStat(stats, "frameTime", "frameTime"); AddStat(stats, "oneLow", "oneLow"); AddStat(stats, "samples", "samples");
        Ui.Gap(stats, 10); stats.Children.Add(Value("frameHelp", 11, Ui.Muted));
        Ui.Cell(middle, Ui.Card(stats), 1); page.Children.Add(middle); Ui.Gap(page, 18);
        var details = Ui.Columns(1, 1, 1);
        Ui.Cell(details, Ui.Margin(DeviceCard("cpu", "cpuName", "cpuTemp", "cpuClock")), 0);
        Ui.Cell(details, Ui.Margin(DeviceCard("gpu", "gpuName", "gpuTemp", "gpuClock")), 1);
        var memory = Ui.Stack(Ui.Mono(L.T("ram"), 10, Ui.Muted)); Ui.Gap(memory, 13);
        memory.Children.Add(Value("ramFull", 16)); Ui.Gap(memory, 12);
        AddStat(memory, "load", "ramLoad"); AddStat(memory, "vram", "vram");
        Ui.Cell(details, Ui.Card(memory, 20), 2); page.Children.Add(details);
        return page;
    }

    private Border DeviceCard(string type, string nameKey, string temperatureKey, string clockKey)
    {
        var stack = Ui.Stack(Ui.Mono(L.T(type), 10, Ui.Muted)); Ui.Gap(stack, 13);
        var name = Value(nameKey, 13); name.MinHeight = 38; stack.Children.Add(name); Ui.Gap(stack, 8);
        AddStat(stack, "temp", temperatureKey); AddStat(stack, "clock", clockKey);
        return Ui.Card(stack, 20);
    }

    private void AddStat(StackPanel panel, string labelKey, string valueKey)
    {
        var row = Ui.Columns(1.3, 1); row.Margin = new Thickness(0, 0, 0, 11);
        Ui.Cell(row, Ui.Text(L.T(labelKey), 12, Ui.Muted), 0);
        var value = Value(valueKey, 13); value.HorizontalAlignment = HorizontalAlignment.Right; Ui.Cell(row, value, 1); panel.Children.Add(row);
    }

    private sealed record TargetChoice(int Id, string Label) { public override string ToString() => Label; }
    private void PopulateProcesses()
    {
        if (_processes is null) return;
        var values = new List<TargetChoice> { new(0, L.T("auto")) };
        values.AddRange(_app.Frames.Processes(DateTimeOffset.Now).Select(p => new TargetChoice(p.Pid, $"{p.Name} · {p.Pid}")));
        values.AddRange(_app.Rtss.Applications.Where(p => values.All(v => v.Id != p.ProcessId))
            .Select(p => new TargetChoice(p.ProcessId, $"{p.Application} · {p.ProcessId} · RTSS")));
        if (_app.ManualTarget > 0 && values.All(p => p.Id != _app.ManualTarget))
            values.Add(new(_app.ManualTarget, $"PID {_app.ManualTarget}"));
        _processes.ItemsSource = values;
        _processes.SelectedItem = values.FirstOrDefault(p => p.Id == _app.ManualTarget) ?? values[0];
    }

    private CheckBox Toggle(string key, bool initial, Action<bool> changed)
    {
        var toggle = new CheckBox { Content = L.T(key), IsChecked = initial };
        toggle.Checked += (_, _) => { changed(true); _app.Save(); };
        toggle.Unchecked += (_, _) => { changed(false); _app.Save(); };
        return toggle;
    }

    private UIElement OverlayPage()
    {
        var page = Header("quick", "quickSub"); var columns = Ui.Columns(1.25, 1);
        var settings = _app.Settings;
        var controls = Ui.Stack(Ui.Text(L.T("overlay"), 20, null, true), new Border { Height = 8 },
            Ui.Text(L.T("overlaySub"), 13, Ui.Muted), new Border { Height = 18 });
        controls.Children.Add(Toggle("enableOverlay", settings.OverlayEnabled, value => { settings.OverlayEnabled = value; RefreshButtons(); }));
        controls.Children.Add(Toggle("onlyGame", settings.OverlayOnlyInGame, value => settings.OverlayOnlyInGame = value));
        Ui.Gap(controls, 12);
        controls.Children.Add(Labelled("overlayBackend", Choice([L.T("backendAuto"), L.T("backendDesktop"), "RTSS"],
            Array.IndexOf(new[] { "Auto", "Desktop", "Rtss" }, settings.OverlayBackend), i => settings.OverlayBackend = new[] { "Auto", "Desktop", "Rtss" }[i])));
        controls.Children.Add(Value("rtssStatus", 11, Ui.Acid));
        controls.Children.Add(Ui.Text(L.T("rtssHint"), 12, Ui.Muted)); Ui.Gap(controls, 10);
        controls.Children.Add(Ui.Action(L.T("componentRepair"), _app.PrepareComponents));
        Ui.Gap(controls, 20); controls.Children.Add(Ui.Mono(L.T("metrics"), 10, Ui.Muted));
        var toggles = Ui.Columns(1, 1);
        Ui.Cell(toggles, Ui.Stack(Toggle("fps", settings.ShowFps, v => settings.ShowFps = v),
            Toggle("cpu", settings.ShowCpu, v => settings.ShowCpu = v), Toggle("gpu", settings.ShowGpu, v => settings.ShowGpu = v),
            Toggle("clocks", settings.ShowClocks, v => settings.ShowClocks = v)), 0);
        Ui.Cell(toggles, Ui.Stack(Toggle("ram", settings.ShowMemory, v => settings.ShowMemory = v),
            Toggle("frameTime", settings.ShowFrameTime, v => settings.ShowFrameTime = v),
            Toggle("vram", settings.ShowVram, v => settings.ShowVram = v), Toggle("power", settings.ShowPower, v => settings.ShowPower = v)), 1);
        controls.Children.Add(toggles); Ui.Gap(controls, 16);
        controls.Children.Add(Labelled("position", Choice(new[] { L.T("topLeft"), L.T("topRight"), L.T("bottomLeft"), L.T("bottomRight") },
            Array.IndexOf(new[] { "TopLeft", "TopRight", "BottomLeft", "BottomRight" }, settings.OverlayCorner),
            i => settings.OverlayCorner = new[] { "TopLeft", "TopRight", "BottomLeft", "BottomRight" }[i])));
        var screens = System.Windows.Forms.Screen.AllScreens;
        controls.Children.Add(Labelled("monitor", Choice(screens.Select((s, i) => $"{i + 1} · {s.Bounds.Width} × {s.Bounds.Height}").ToArray(),
            Math.Min(settings.MonitorIndex, screens.Length - 1), i => settings.MonitorIndex = i)));
        controls.Children.Add(Labelled("scale", Slider(.75, 2, settings.OverlayScale, v => settings.OverlayScale = v)));
        controls.Children.Add(Labelled("opacity", Slider(.35, 1, settings.OverlayOpacity, v => settings.OverlayOpacity = v)));
        Ui.Cell(columns, Ui.Margin(Ui.Card(controls), 18), 0);
        var right = Ui.Stack(Ui.Mono(L.T("preview"), 10, Ui.Muted)); Ui.Gap(right, 24);
        _preview = new OverlayPanel { HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var sample in _history) _preview.Update(sample, settings, true);
        right.Children.Add(_preview); Ui.Gap(right, 24);
        right.Children.Add(Ui.Mono("CTRL + SHIFT + O", 15, Ui.Acid)); Ui.Gap(right, 8);
        right.Children.Add(Ui.Text(L.T("overlayKey"), 12, Ui.Muted)); Ui.Gap(right, 30);
        right.Children.Add(Ui.Text(L.T("compatibility"), 18, null, true)); Ui.Gap(right, 12);
        right.Children.Add(Ui.Text(L.T("compatBody"), 13, Ui.Muted));
        Ui.Cell(columns, Ui.Card(right), 1); page.Children.Add(columns); return page;
    }

    private ComboBox Choice(string[] labels, int index, Action<int> changed)
    {
        var combo = new ComboBox { ItemsSource = labels, SelectedIndex = Math.Max(0, index), Margin = new Thickness(0, 0, 0, 4) };
        combo.SelectionChanged += (_, _) => { if (combo.SelectedIndex >= 0) { changed(combo.SelectedIndex); _app.Save(); } }; return combo;
    }

    private UIElement Labelled(string key, UIElement control)
    { var stack = Ui.Stack(Ui.Text(L.T(key), 12, Ui.Muted), new Border { Height = 8 }, control); stack.Margin = new Thickness(0, 0, 0, 18); return stack; }
    private Slider Slider(double min, double max, double value, Action<double> changed)
    {
        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            Value = value,
            TickFrequency = .05,
            IsSnapToTickEnabled = true,
            Foreground = Ui.Acid,
            ToolTip = value.ToString("0.00")
        };
        slider.ValueChanged += (_, _) => { changed(slider.Value); slider.ToolTip = slider.Value.ToString("0.00"); _app.Save(); }; return slider;
    }

    private UIElement SessionsPage()
    {
        var page = Header("sessionsIntro", "sessionsSub");
        _recordButton = Ui.Action("", _app.ToggleRecording, true);
        var folder = Ui.Action(L.T("openFolder"), () => { Directory.CreateDirectory(App.SessionsDirectory); App.OpenPath(App.SessionsDirectory); });
        folder.Margin = new Thickness(12, 0, 0, 0); page.Children.Add(Ui.Row(_recordButton, folder)); Ui.Gap(page, 24);
        if (_app.Recorder.IsRecording)
        {
            var active = Ui.Stack(Ui.Badge("● " + L.T("recording")), new Border { Height = 16 }, Value("recordDuration", 32, Ui.Acid));
            page.Children.Add(Ui.Card(active)); Ui.Gap(page);
        }
        var sessions = new List<SessionInfo>();
        if (Directory.Exists(App.SessionsDirectory))
        {
            foreach (var path in Directory.EnumerateFiles(App.SessionsDirectory, "*.json").OrderDescending().Take(50))
            {
                try { if (JsonSerializer.Deserialize<SessionInfo>(File.ReadAllText(path)) is { } session) sessions.Add(session); }
                catch (Exception ex) when (ex is JsonException or IOException) { App.Log("Session: " + ex.Message); }
            }
        }
        if (sessions.Count == 0 && !_app.Recorder.IsRecording)
        {
            var empty = Ui.Stack(Ui.Mono("REC / 001", 12, Ui.Acid), new Border { Height = 30 },
                Ui.Text(L.T("emptySessions"), 27, null, true), new Border { Height = 12 }, Ui.Text(L.T("emptySessionsSub"), 14, Ui.Muted));
            page.Children.Add(Ui.Card(empty, 38));
        }
        foreach (var session in sessions)
        {
            var row = Ui.Columns(1.4, 1, 1, 1);
            Ui.Cell(row, Ui.Stack(Ui.Text(session.Started.LocalDateTime.ToString("dd.MM.yyyy / HH:mm"), 15, null, true),
                new Border { Height = 8 }, Ui.Mono(TimeSpan.FromSeconds(session.DurationSeconds).ToString(@"hh\:mm\:ss"), 12, Ui.Muted)), 0);
            Ui.Cell(row, Ui.Stack(Ui.Text(L.T("avgFps"), 11, Ui.Muted), Ui.Mono(Ui.N(session.AverageFps), 26, Ui.Acid)), 1);
            Ui.Cell(row, Ui.Stack(Ui.Text(L.T("minFps"), 11, Ui.Muted), Ui.Mono(Ui.N(session.MinimumFps), 26)), 2);
            // Open only a CSV inside our own recordings directory, never an arbitrary JSON-supplied path.
            var file = Path.Combine(App.SessionsDirectory, Path.GetFileName(session.FilePath));
            Ui.Cell(row, Ui.Action("CSV ↗", () => { if (File.Exists(file)) App.OpenPath(file); }), 3);
            page.Children.Add(Ui.Margin(Ui.Card(row), 0, 14));
        }
        Ui.Gap(page, 20); page.Children.Add(Ui.Text(L.T("sessionNote"), 12, Ui.Muted)); return page;
    }

    private UIElement SensorsPage()
    {
        var page = Header("sensorIntro", "sensorSub");
        page.Children.Add(Ui.Text(L.T("missingSensor"), 13, Ui.Muted)); Ui.Gap(page, 20);
        if (!_app.Elevated) { page.Children.Add(Ui.Action(L.T("elevate"), _app.Elevate, true)); Ui.Gap(page, 20); }
        _sensorList = new ListView
        {
            Height = 560,
            Background = Ui.Brush("#181D18"),
            Foreground = Ui.Brush("#EBF0E7"),
            BorderBrush = Ui.Edge,
            BorderThickness = new Thickness(1),
            FontSize = 13
        };
        VirtualizingPanel.SetIsVirtualizing(_sensorList, true);
        VirtualizingPanel.SetVirtualizationMode(_sensorList, VirtualizationMode.Recycling);
        ScrollViewer.SetCanContentScroll(_sensorList, true);
        var view = new GridView();
        foreach (var (key, member, width) in new[] { ("device", "Device", 275d), ("sensor", "Name", 300d), ("type", "Type", 140d), ("value", "Display", 145d) })
            view.Columns.Add(new GridViewColumn { Header = L.T(key), Width = width, DisplayMemberBinding = new System.Windows.Data.Binding(member) });
        _sensorList.View = view; page.Children.Add(_sensorList); return page;
    }

    private UIElement SettingsPage()
    {
        var page = Header("settingsIntro", "settingsSub"); var columns = Ui.Columns(1.25, 1);
        var settings = _app.Settings;
        var general = Ui.Stack(Ui.Mono(L.T("general"), 10, Ui.Muted)); Ui.Gap(general, 20);
        general.Children.Add(Labelled("language", Choice(["Русский", "English"], settings.Language == "en" ? 1 : 0,
            i => Dispatcher.BeginInvoke(() => _app.ChangeLanguage(i == 1 ? "en" : "ru")))));
        general.Children.Add(Labelled("interval", Choice(["500 ms", "1 s", "2 s", "3 s"], Array.IndexOf(new[] { 500, 1000, 2000, 3000 }, settings.IntervalMs),
            i => settings.IntervalMs = new[] { 500, 1000, 2000, 3000 }[i])));
        var gpus = _app.Hardware.Gpus.ToArray();
        general.Children.Add(Labelled("graphicsAdapter", Choice(new[] { L.T("gpuAuto") }.Concat(gpus.Select(g => g.Name)).ToArray(),
            Array.FindIndex(gpus, g => g.Id == settings.GpuIdentifier) + 1, i => settings.GpuIdentifier = i == 0 ? null : gpus[i - 1].Id)));
        general.Children.Add(Toggle("autoCapture", settings.AutoCapture, v => settings.AutoCapture = v));
        general.Children.Add(Toggle("autoRtss", settings.StartRtss, v => settings.StartRtss = v));
        general.Children.Add(Toggle("autoUpdate", settings.AutoUpdate, v => settings.AutoUpdate = v));
        general.Children.Add(Toggle("trayClose", settings.CloseToTray, v => settings.CloseToTray = v));
        general.Children.Add(Toggle("minimized", settings.StartMinimized, v => settings.StartMinimized = v));
        general.Children.Add(Toggle("startup", StartupEnabled(), SetStartup));
        Ui.Gap(general, 28); general.Children.Add(Ui.Mono(L.T("shortcuts"), 10, Ui.Muted)); Ui.Gap(general, 20);
        general.Children.Add(Ui.Mono("CTRL + SHIFT + O", 16, Ui.Acid)); Ui.Gap(general, 6);
        general.Children.Add(Ui.Text(L.T("overlayKey"), 12, Ui.Muted)); Ui.Gap(general, 18);
        general.Children.Add(Ui.Mono("CTRL + SHIFT + R", 16, Ui.Acid)); Ui.Gap(general, 6);
        general.Children.Add(Ui.Text(L.T("recordKey"), 12, Ui.Muted));
        Ui.Cell(columns, Ui.Margin(Ui.Card(general), 18), 0);
        var right = Ui.Stack();
        var driver = Ui.Stack(Ui.Mono(L.T("driver"), 10, Ui.Muted)); Ui.Gap(driver, 16);
        driver.Children.Add(Ui.Badge(L.T(_app.DriverInstalled ? "driverReady" : "driverMissing"), _app.DriverInstalled)); Ui.Gap(driver, 14);
        driver.Children.Add(Ui.Text(L.T("driverBody"), 12, Ui.Muted)); Ui.Gap(driver, 18);
        driver.Children.Add(Ui.Action(L.T("componentRepair"), _app.PrepareComponents, !_app.DriverInstalled));
        right.Children.Add(Ui.Card(driver)); Ui.Gap(right, 18);
        var access = Ui.Stack(Ui.Mono(L.T("access"), 10, Ui.Muted)); Ui.Gap(access, 18);
        access.Children.Add(Ui.Badge(L.T(_app.Elevated ? "admin" : "standard"), _app.Elevated)); Ui.Gap(access, 18);
        access.Children.Add(Ui.Text(L.T("accessBody"), 13, Ui.Muted)); Ui.Gap(access, 22);
        if (!_app.Elevated) access.Children.Add(Ui.Action(L.T("elevate"), _app.Elevate, true));
        right.Children.Add(Ui.Card(access)); Ui.Gap(right, 18);
        var about = Ui.Stack(Ui.Mono(L.T("about"), 10, Ui.Muted)); Ui.Gap(about, 20);
        about.Children.Add(Ui.Text(L.T("aboutBody").Replace("Dedsec Monitor", "Dedsec Monitor " + App.ProductVersion), 14)); Ui.Gap(about, 20);
        about.Children.Add(Ui.Action(L.T("repo"), () => App.OpenPath("https://github.com/Dedseckms/Dedsec-Monitor"))); Ui.Gap(about, 12);
        about.Children.Add(Ui.Action(L.T("logs"), () => App.OpenPath(App.DataDirectory)));
        right.Children.Add(Ui.Card(about)); Ui.Cell(columns, right, 1); page.Children.Add(columns); return page;
    }

    private static bool StartupEnabled()
    { using var run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"); return run?.GetValue("DedsecMonitor") is string; }
    private void SetStartup(bool enabled)
    {
        try
        {
            using var run = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            if (enabled) run.SetValue("DedsecMonitor", "\"" + Environment.ProcessPath + "\""); else run.DeleteValue("DedsecMonitor", false);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { _app.ShowError(ex); }
    }

    internal void RefreshButtons()
    {
        if (_recordButton is not null) _recordButton.Content = L.T(_app.Recorder.IsRecording ? "stopRecord" : "record");
        if (_captureButton is not null) _captureButton.Content = L.T(_app.CaptureActive ? "stopCapture" : "startCapture");
        if (_overlayButton is not null) _overlayButton.Content = L.T(_app.Settings.OverlayEnabled ? "overlayOn" : "overlayOff");
    }

    internal void Update(Snapshot s, bool appendHistory = true)
    {
        if (appendHistory) { _history.Enqueue(s); while (_history.Count > 60) _history.Dequeue(); }
        void Set(string key, string value) { if (_values.TryGetValue(key, out var block)) block.Text = value; }
        _status.Text = "● " + L.T(_app.CaptureActive ? "captureRunning" : _app.Capture.State switch
        { "running" => "captureRunning", "failed" => "captureFailed", "missing" => "captureMissing", _ => "captureStopped" });
        _status.ToolTip = _app.Capture.Error ?? L.T("ready");
        Set("fps", Ui.N(s.Frames.Fps)); Set("fpsDetail", s.Frames.Fps is null ? L.T("waiting") : "1% LOW  " + Ui.N(s.Frames.LowOnePercent));
        Set("cpuLoad", Ui.N(s.CpuLoad)); Set("cpuDetail", Ui.Unit(s.CpuTemperature, "°C") + "  /  " + Ui.Unit(s.CpuClock / 1000, "GHz", "0.00"));
        Set("gpuLoad", Ui.N(s.GpuLoad)); Set("gpuDetail", Ui.Unit(s.GpuTemperature, "°C") + "  /  " + Ui.Unit(s.GpuClock, "MHz"));
        Set("ramUsed", Ui.N(s.MemoryUsed, "0.0")); Set("ramDetail", "/ " + Ui.N(s.MemoryTotal, "0.0") + " GB  ·  " + Ui.N(s.MemoryLoad) + "%");
        Set("game", string.IsNullOrEmpty(s.Frames.Application) ? L.T("waiting") : s.Frames.Application + " / " + s.Frames.Source + " / PID " + s.Frames.ProcessId);
        Set("rtssStatus", L.T(_app.Rtss.Connected ? "rtssReady" : "rtssMissing"));
        Set("frameHelp", L.T(s.Frames.Source == "RTSS" ? "rtssWindow" : "fpsWindow"));
        Set("frameTime", Ui.Unit(s.Frames.FrameTime, "ms", "0.00")); Set("oneLow", Ui.Unit(s.Frames.LowOnePercent, "FPS"));
        Set("samples", s.Frames.Samples.ToString()); Set("cpuName", s.CpuName); Set("gpuName", s.GpuName);
        Set("cpuTemp", Ui.Unit(s.CpuTemperature, "°C")); Set("cpuClock", Ui.Unit(s.CpuClock / 1000, "GHz", "0.00"));
        Set("gpuTemp", Ui.Unit(s.GpuTemperature, "°C")); Set("gpuClock", Ui.Unit(s.GpuClock, "MHz"));
        Set("ramFull", Ui.N(s.MemoryUsed, "0.0") + " / " + Ui.N(s.MemoryTotal, "0.0") + " GB");
        Set("ramLoad", Ui.Unit(s.MemoryLoad, "%")); Set("vram", Ui.Unit(s.GpuMemoryUsed / 1024, "GB", "0.0"));
        Set("previewFps", Ui.Unit(s.Frames.Fps, "FPS"));
        Set("previewCpu", "CPU  " + Ui.Unit(s.CpuLoad, "%") + "  " + Ui.Unit(s.CpuTemperature, "°C"));
        Set("previewGpu", "GPU  " + Ui.Unit(s.GpuLoad, "%") + "  " + Ui.Unit(s.GpuTemperature, "°C"));
        Set("previewRam", "RAM  " + Ui.Unit(s.MemoryUsed, "GB", "0.0"));
        if (_app.Recorder.Current is { } session) Set("recordDuration", (DateTimeOffset.Now - session.Started).ToString(@"hh\:mm\:ss"));
        if (_charts.TryGetValue("fps", out var chart)) { chart.Values = _history.Select(x => x.Frames.Fps).ToArray(); chart.InvalidateVisual(); }
        if (_sensorList is not null) _sensorList.ItemsSource = s.Sensors.Select(x => new { x.Device, x.Name, x.Type, Display = Ui.N(x.Value, "0.##") }).ToArray();
        _preview?.Update(s, _app.Settings, true);
        RefreshButtons();
    }
}
