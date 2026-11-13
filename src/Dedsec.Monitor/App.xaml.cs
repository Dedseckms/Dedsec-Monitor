using System.Diagnostics;
using System.Security.Principal;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Dedsec.Core;

namespace Dedsec.Monitor;

public partial class App : Application
{
    public static string DataDirectory { get; private set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Dedsec", "Monitor");
    public static string SessionsDirectory => Path.Combine(DataDirectory, "Sessions");
    public Settings Settings { get; private set; } = new();
    internal FrameStore Frames { get; } = new();
    internal CaptureService Capture { get; private set; } = null!;
    internal HardwareService Hardware { get; } = new();
    internal SessionRecorder Recorder { get; } = new();
    internal Snapshot? Latest { get; private set; }
    internal OverlayWindow Overlay { get; private set; } = null!;
    internal volatile int ManualTarget;
    internal int AutoTarget { get; private set; }
    internal bool Elevated => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
    private readonly CancellationTokenSource _stop = new();
    private Task? _worker;
    private Mutex? _mutex;
    private EventWaitHandle? _activate;
    private RegisteredWaitHandle? _activationWait;
    private System.Windows.Forms.NotifyIcon? _tray;
    internal bool Exiting { get; private set; }
    private MainWindow _main = null!;
    private static readonly object LogGate = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var screenshotIndex = Array.IndexOf(e.Args, "--screenshots");
        var smoke = e.Args.Contains("--smoke-test");
        var automation = smoke || screenshotIndex >= 0;
        if (automation) DataDirectory = Path.Combine(Path.GetTempPath(), "DedsecMonitor-QA", Environment.ProcessId.ToString());
        Directory.CreateDirectory(DataDirectory);
        _mutex = new Mutex(true, "Local\\DedsecMonitor-" + Environment.UserName + (automation ? "-" + Environment.ProcessId : ""), out var first);
        if (!first)
        {
            try { using var signal = EventWaitHandle.OpenExisting("Local\\DedsecMonitorActivate-" + Environment.UserName); signal.Set(); }
            catch (WaitHandleCannotBeOpenedException) { }
            Shutdown(); return;
        }
        if (!automation)
        {
            _activate = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\DedsecMonitorActivate-" + Environment.UserName);
            _activationWait = ThreadPool.RegisterWaitForSingleObject(_activate,
                (_, _) => Dispatcher.BeginInvoke(() => _main.Restore()), null, -1, false);
        }
        Settings = SettingsFile.Load(Path.Combine(DataDirectory, "settings.json"));
        L.Language = Settings.Language;
        Capture = new CaptureService(Frames);
        var targetIndex = Array.IndexOf(e.Args, "--target");
        if (targetIndex >= 0 && targetIndex + 1 < e.Args.Length && int.TryParse(e.Args[targetIndex + 1], out var target)) ManualTarget = target;
        DispatcherUnhandledException += (_, args) =>
        {
            Log(args.Exception.ToString());
            MessageBox.Show(args.Exception.Message, L.T("error"));
            args.Handled = true;
        };
        Overlay = new OverlayWindow(this);
        _main = new MainWindow(this);
        MainWindow = _main;
        new System.Windows.Interop.WindowInteropHelper(_main).EnsureHandle();
        if (!Settings.StartMinimized || automation) _main.Show();
        if (!automation) CreateTray();
        _worker = Task.Run(async () =>
        {
            try
            {
                Hardware.Open();
                if (Settings.AutoCapture || automation) Capture.Start();
                while (!_stop.IsCancellationRequested)
                {
                    var foreground = Native.ForegroundProcess();
                    if (foreground != Environment.ProcessId && Frames.Processes(DateTimeOffset.Now).Any(p => p.Pid == foreground)) AutoTarget = foreground;
                    var frame = Frames.Read(ManualTarget > 0 ? ManualTarget : AutoTarget, DateTimeOffset.Now);
                    var sample = Hardware.Read(frame, Settings.GpuIdentifier);
                    await Dispatcher.InvokeAsync(() =>
                    {
                        Latest = sample;
                        try { Recorder.Add(sample); }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        { Recorder.Dispose(); ShowError(ex); }
                        _main.Update(sample); Overlay.Update(sample);
                    });
                    await Task.Delay(Settings.IntervalMs, _stop.Token);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Log(ex.ToString()); await Dispatcher.InvokeAsync(() => ShowError(ex)); }
            finally { Hardware.Dispose(); }
        });
        if (automation) _ = RunAutomation(e.Args, screenshotIndex, smoke);
    }

    private void CreateTray()
    {
        _tray = new System.Windows.Forms.NotifyIcon
        {
            Text = "Dedsec Monitor",
            Visible = true,
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!)
        };
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add(L.T("open"), null, (_, _) => _main.Restore());
        menu.Items.Add(L.T("overlay"), null, (_, _) => ToggleOverlay());
        menu.Items.Add(L.T("record"), null, (_, _) => ToggleRecording());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add(L.T("exit"), null, (_, _) => RequestExit());
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => _main.Restore();
    }

    internal void Save()
    {
        try { SettingsFile.Save(Path.Combine(DataDirectory, "settings.json"), Settings); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ShowError(ex); }
        if (Latest is { } snapshot) Overlay.Update(snapshot);
    }

    internal void ChangeLanguage(string language)
    {
        Settings.Language = language; L.Language = language; Save();
        _tray?.Dispose(); CreateTray(); _main.Navigate(_main.CurrentPage); Overlay.Rebuild();
    }

    internal void ToggleOverlay()
    { Settings.OverlayEnabled = !Settings.OverlayEnabled; Save(); _main.RefreshButtons(); }

    internal void ToggleCapture()
    {
        _ = Task.Run(() =>
        {
            if (Capture.Running) Capture.Stop(); else Capture.Start();
        });
    }

    internal void ToggleRecording()
    {
        try
        {
            if (Recorder.IsRecording) Recorder.Stop();
            else Recorder.Start(SessionsDirectory, DateTimeOffset.Now);
            _main.RefreshButtons();
            if (_main.CurrentPage == "sessions") _main.Navigate("sessions");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ShowError(ex); }
    }

    internal bool DriverInstalled => LibreHardwareMonitor.PawnIo.PawnIo.IsInstalled;

    internal async void InstallDriver()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "tools", "PawnIO_setup.exe");
            using (var file = File.OpenRead(path))
            {
                var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(file));
                if (!hash.Equals("1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032", StringComparison.OrdinalIgnoreCase))
                    throw new IOException("PawnIO installer checksum mismatch.");
            }
            using var installer = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = "runas" });
            if (installer is not null)
            {
                await installer.WaitForExitAsync();
                if (installer.ExitCode == 0) _main.SetNotice(L.T("driverRestart"));
            }
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223) { }
        catch (Exception ex) { ShowError(ex); }
    }

    internal void Elevate()
    {
        try
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" };
            // The elevated instance waits until the old instance has released its mutex.
            start.ArgumentList.Add("--wait-for"); start.ArgumentList.Add(Environment.ProcessId.ToString());
            Process.Start(start); RequestExit();
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223) { }
        catch (Exception ex) { ShowError(ex); }
    }

    internal async void RequestExit()
    {
        if (Exiting) return;
        Exiting = true; _stop.Cancel();
        _main.Hide(); Overlay.Hide(); _tray?.Dispose();
        try
        {
            if (_worker is not null) await _worker;
            await Task.Run(Capture.Dispose);
            Recorder.Stop();
        }
        catch (Exception ex) { Log("Shutdown: " + ex); }
        Recorder.Dispose(); _activationWait?.Unregister(null); _activate?.Dispose();
        _mutex?.ReleaseMutex(); _mutex?.Dispose();
        Overlay.Close(); _main.Close(); Shutdown();
    }

    internal void ShowError(Exception exception) { Log(exception.Message); _main.SetNotice(L.T("error") + ": " + exception.Message); }

    internal static void OpenPath(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { ((App)Current).ShowError(ex); }
    }

    internal static void Log(string message)
    {
        lock (LogGate)
        {
            try
            {
                var path = Path.Combine(DataDirectory, "monitor.log");
                if (File.Exists(path) && new FileInfo(path).Length > 1024 * 1024) File.Move(path, path + ".previous", true);
                File.AppendAllText(path, $"{DateTimeOffset.Now:O} {message}\n");
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private async Task RunAutomation(string[] args, int screenshotIndex, bool smoke)
    {
        try
        {
            await Task.Delay(5000);
            Recorder.Start(SessionsDirectory, DateTimeOffset.Now);
            await Task.Delay(9000);
            var recorded = Recorder.Stop();
            Settings.OverlayEnabled = true; Settings.OverlayOnlyInGame = false;
            if (Latest is { } current) Overlay.Update(current);
            await Task.Delay(1000);
            var overlayShown = Overlay.IsVisible;
            Settings.OverlayEnabled = false;
            if (Latest is { } recent) Overlay.Update(recent);
            var overlayHidden = !Overlay.IsVisible;
            var path = screenshotIndex >= 0 && screenshotIndex + 1 < args.Length ? Path.GetFullPath(args[screenshotIndex + 1]) : Path.Combine(DataDirectory, "screenshots");
            Directory.CreateDirectory(path);
            _main.Width = 1440; _main.Height = 960;
            foreach (var page in new[] { "dashboard", "overlay", "sensors", "settings", "sessions" })
            {
                _main.Navigate(page);
                if (Latest is { } sample) _main.Update(sample);
                await Task.Delay(300);
                _main.UpdateLayout();
                _main.ExportScreenshot(Path.Combine(path, page + ".png"));
            }
            L.Language = "en"; _main.Navigate("dashboard");
            await Task.Delay(300); _main.ExportScreenshot(Path.Combine(path, "dashboard.en.png"));
            if (Latest is { } final)
            {
                File.WriteAllText(Path.Combine(path, "diagnostics.json"), JsonSerializer.Serialize(new
                {
                    final.CpuName,
                    final.GpuName,
                    final.CpuLoad,
                    final.CpuTemperature,
                    final.CpuClock,
                    final.GpuLoad,
                    final.MemoryUsed,
                    final.MemoryTotal,
                    final.Frames,
                    SensorCount = final.Sensors.Count,
                    Capture.State,
                    Capture.Error,
                    Elevated,
                    Processes = Frames.Processes(DateTimeOffset.Now).Select(p => new { p.Pid, p.Name }),
                    Hotkeys = _main.HotkeysRegistered,
                    OverlayShown = overlayShown,
                    OverlayHidden = overlayHidden,
                    RecordedSamples = recorded?.Samples,
                    DriverInstalled,
                    RuntimeVersion = Environment.Version.ToString()
                }, new JsonSerializerOptions { WriteIndented = true }));
                if (smoke && (final.MemoryTotal <= 0 || final.Sensors.Count == 0 || !overlayShown || !overlayHidden || recorded?.Samples is not > 0)) Environment.ExitCode = 2;
            }
            else Environment.ExitCode = 3;
        }
        catch (Exception ex) { Log(ex.ToString()); Environment.ExitCode = 4; }
        finally { RequestExit(); }
    }

    internal static void SaveVisual(FrameworkElement visual, string path)
    {
        var bitmap = new RenderTargetBitmap((int)visual.ActualWidth, (int)visual.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path); encoder.Save(file);
    }
}
