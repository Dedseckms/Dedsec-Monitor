using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Dedsec.Monitor;

internal static class Ui
{
    internal static readonly SolidColorBrush Acid = Brush("#C2F970");
    internal static readonly SolidColorBrush Muted = Brush("#8C9A89");
    internal static readonly SolidColorBrush Edge = Brush("#2A3329");
    internal static SolidColorBrush Brush(string hex) => new((Color)ColorConverter.ConvertFromString(hex));
    internal static TextBlock Text(string text, double size = 14, Brush? color = null, bool bold = false) => new()
    {
        Text = text,
        FontSize = size,
        Foreground = color ?? Brush("#EBF0E7"),
        FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        VerticalAlignment = VerticalAlignment.Center
    };
    internal static TextBlock Mono(string text, double size = 13, Brush? color = null)
    { var block = Text(text, size, color); block.FontFamily = new FontFamily("Cascadia Code, Consolas"); return block; }
    internal static StackPanel Stack(params UIElement[] children)
    { var panel = new StackPanel(); foreach (var child in children) panel.Children.Add(child); return panel; }
    internal static StackPanel Row(params UIElement[] children)
    { var panel = Stack(children); panel.Orientation = Orientation.Horizontal; return panel; }
    internal static Border Card(UIElement child, double padding = 22) => new()
    {
        Background = Brush("#181D18"),
        BorderBrush = Edge,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(12),
        Padding = new Thickness(padding),
        Child = child
    };
    internal static Border Badge(string label, bool active = true) => new()
    {
        Background = Brush(active ? "#25371B" : "#242B23"),
        CornerRadius = new CornerRadius(4),
        Padding = new Thickness(9, 5, 9, 5),
        Child = Mono(label, 10, active ? Acid : Muted),
        HorizontalAlignment = HorizontalAlignment.Left
    };
    internal static Button Action(string label, Action click, bool primary = false)
    {
        var button = new Button { Content = label, HorizontalAlignment = HorizontalAlignment.Left };
        if (primary) { button.Background = Acid; button.Foreground = Brush("#14200B"); button.FontWeight = FontWeights.SemiBold; }
        button.Click += (_, _) => click(); return button;
    }
    internal static void Gap(Panel panel, double height = 16) => panel.Children.Add(new Border { Height = height });
    internal static string N(double? number, string format = "0") => number?.ToString(format, CultureInfo.CurrentCulture) ?? "—";
    internal static string Unit(double? number, string unit, string format = "0") => number is null ? "—" : N(number, format) + " " + unit;
    internal static Grid Columns(params double[] ratios)
    {
        var grid = new Grid();
        foreach (var ratio in ratios) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ratio, GridUnitType.Star) });
        return grid;
    }
    internal static void Cell(Grid grid, UIElement child, int col)
    { Grid.SetColumn(child, col); grid.Children.Add(child); }
    internal static Border Margin(Border border, double right = 14, double bottom = 0)
    { border.Margin = new Thickness(0, 0, right, bottom); return border; }
}

internal sealed class SignalChart : FrameworkElement
{
    public IReadOnlyList<double?> Values { get; set; } = [];
    public Brush Stroke { get; set; } = Ui.Acid;
    public bool FixedPercent { get; set; }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var w = ActualWidth; var h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        var pen = new Pen(Ui.Brush("#283125"), 1); pen.DashStyle = new DashStyle([3, 5], 0);
        for (var i = 0; i <= 4; i++) dc.DrawLine(pen, new Point(0, i * h / 4), new Point(w, i * h / 4));
        for (var i = 0; i <= 6; i++) dc.DrawLine(pen, new Point(i * w / 6, 0), new Point(i * w / 6, h));
        var available = Values.Where(v => v is not null).Select(v => v!.Value).ToArray();
        if (available.Length < 2) return;
        var max = FixedPercent ? 100 : Math.Max(30, available.Max() * 1.15);
        var geometry = new StreamGeometry();
        var fill = new StreamGeometry();
        using (var line = geometry.Open())
        using (var area = fill.Open())
        {
            var active = false;
            for (var i = 0; i < Values.Count; i++)
            {
                if (Values[i] is not { } v) { active = false; continue; }
                var point = new Point(w * i / Math.Max(1, Values.Count - 1), h - Math.Clamp(v / max, 0, 1) * (h - 8));
                if (!active)
                {
                    line.BeginFigure(point, false, false);
                    area.BeginFigure(new Point(point.X, h), true, false); area.LineTo(point, true, false); active = true;
                }
                else { line.LineTo(point, true, false); area.LineTo(point, true, false); }
                if (i == Values.Count - 1 || Values[i + 1] is null) area.LineTo(new Point(point.X, h), true, false);
            }
        }
        var color = Stroke is SolidColorBrush brush ? brush.Color : Colors.Green;
        dc.DrawGeometry(new LinearGradientBrush(Color.FromArgb(38, color.R, color.G, color.B), Colors.Transparent, 90), null, fill);
        dc.DrawGeometry(null, new Pen(Stroke, 2), geometry);
    }
}
