using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace Dedsec.TestScene;

// A separate, bounded graphics workload for end-to-end PresentMon testing. Not a game benchmark.
internal sealed class RenderScene : FrameworkElement
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    internal RenderScene() => CompositionTarget.Rendering += (_, _) => InvalidateVisual();
    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(16, 19, 16)), null, new Rect(0, 0, ActualWidth, ActualHeight));
        var time = _clock.Elapsed.TotalSeconds;
        for (var i = 0; i < 120; i++)
        {
            var x = (i * 71 + time * 60) % Math.Max(1, ActualWidth);
            var y = (i * 37 + Math.Sin(time + i) * 30 + ActualHeight) % Math.Max(1, ActualHeight);
            var color = Color.FromArgb((byte)(50 + i % 180), 194, 249, 112);
            dc.DrawRoundedRectangle(new SolidColorBrush(color), null, new Rect(x, y, 20 + i % 40, 8), 3, 3);
        }
    }

    [STAThread]
    internal static void Main()
    {
        var app = new Application();
        var window = new Window { Title = "Dedsec / Render test (not a game benchmark)", Width = 960, Height = 600, Content = new RenderScene() };
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(55) };
        timer.Tick += (_, _) => app.Shutdown(); timer.Start();
        app.Run(window);
    }
}
