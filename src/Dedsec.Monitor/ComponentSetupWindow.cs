using System.Net.Http;
using System.Windows;
using System.Windows.Controls;

namespace Dedsec.Monitor;

internal sealed class ComponentSetupWindow : Window
{
    private readonly TextBlock _status = Ui.Text("", 14, Ui.Acid);
    private readonly ProgressBar _progress = new() { Height = 5, IsIndeterminate = true, Foreground = Ui.Acid, Margin = new Thickness(0, 22, 0, 20) };
    private readonly Button _retry;
    private readonly Button _close;
    private bool _running;
    internal bool Prepared { get; private set; }
    internal bool RestartRequired { get; private set; }

    internal ComponentSetupWindow(bool quiet = false, bool preview = false)
    {
        Title = "Dedsec Monitor"; Width = 620; Height = 420; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        _retry = Ui.Action(L.T("componentRetry"), () => _ = PrepareAsync(quiet), true);
        _close = Ui.Action(L.T("windowClose"), Close);
        var content = Ui.Stack(Ui.Mono("[D_]  DEDSEC MONITOR", 14, Ui.Acid), new Border { Height = 22 },
            Ui.Text(L.T("componentTitle"), 25, null, true), new Border { Height = 12 },
            Ui.Text(L.T("componentBody"), 13, Ui.Muted), _progress, _status, new Border { Height = 16 }, Ui.Row(_retry, _close));
        Content = new ScrollViewer { Background = Ui.Brush("#101310"), Content = new Border { Padding = new Thickness(30), Child = content }, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        SourceInitialized += (_, _) => Native.DarkTitleBar(new System.Windows.Interop.WindowInteropHelper(this).Handle);
        Closing += (_, e) => { if (_running) e.Cancel = true; };
        Loaded += (_, _) =>
        {
            if (preview) { _status.Text = L.T("componentChecking"); _retry.Visibility = Visibility.Collapsed; return; }
            _ = PrepareAsync(quiet);
        };
    }

    private async Task PrepareAsync(bool quiet)
    {
        if (_running) return;
        _running = true; _retry.Visibility = Visibility.Collapsed; _close.IsEnabled = false;
        _progress.Visibility = Visibility.Visible; _status.Text = L.T("componentChecking");
        try
        {
            using var client = new HttpClient(new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(20) })
            { Timeout = TimeSpan.FromMinutes(10) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("DedsecMonitor/" + App.ProductVersion);
            var progress = new Progress<string>(key => _status.Text = L.T(key));
            var result = await Task.Run(() => new ComponentSetupService(client).PrepareAsync(progress, CancellationToken.None));
            Prepared = true; RestartRequired = result.RestartRequired;
            _running = false; Close();
        }
        catch (Exception ex)
        {
            App.Log("Component setup: " + ex);
            _status.Text = L.T("componentError") + " " + ex.Message;
            _running = false;
            if (quiet) { Close(); return; }
            _progress.Visibility = Visibility.Collapsed; _retry.Visibility = Visibility.Visible; _close.IsEnabled = true;
        }
    }
}
