using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace DofusRetroManager;

/// <summary>
/// Courbe d'évolution d'un prix (une seule série) : axe des temps proportionnel, trois repères de prix,
/// un point par relevé avec son infobulle. Les couleurs suivent le thème (ressources dynamiques).
/// </summary>
public sealed class PriceChart : Canvas
{
    const double Left = 64, Right = 10, Top = 10, Bottom = 22;

    IReadOnlyList<(DateTime Time, double Price)> _points = [];

    /// <summary>Relevés à tracer, dans n'importe quel ordre.</summary>
    public IReadOnlyList<(DateTime Time, double Price)> Points
    {
        get => _points;
        set { _points = value.OrderBy(p => p.Time).ToList(); Redraw(); }
    }

    public PriceChart()
    {
        ClipToBounds = true;
        SizeChanged += (_, _) => Redraw();
    }

    void Redraw()
    {
        Children.Clear();
        double w = ActualWidth, h = ActualHeight;
        if (w < Left + Right + 20 || h < Top + Bottom + 20) return;

        if (_points.Count == 0)
        {
            AddText("Aucun relevé pour l'instant.", w / 2, h / 2, center: true);
            return;
        }

        // Échelles : marge de 10 % autour des prix pour que la courbe ne colle pas aux bords
        double min = _points.Min(p => p.Price), max = _points.Max(p => p.Price);
        double pad = max > min ? (max - min) * 0.1 : Math.Max(1, max * 0.1);
        min = Math.Max(0, min - pad);
        max += pad;
        long t0 = _points[0].Time.Ticks, t1 = _points[^1].Time.Ticks;
        double plotW = w - Left - Right, plotH = h - Top - Bottom;
        double X(DateTime t) => t1 == t0 ? Left + plotW / 2 : Left + (t.Ticks - t0) / (double)(t1 - t0) * plotW;
        double Y(double v) => Top + (max - v) / (max - min) * plotH;

        // Repères horizontaux discrets : bas, milieu, haut
        foreach (var v in new[] { min, (min + max) / 2, max })
        {
            var line = new Line { X1 = Left, X2 = w - Right, Y1 = Y(v), Y2 = Y(v), StrokeThickness = 1, StrokeDashArray = [3, 3] };
            line.SetResourceReference(Shape.StrokeProperty, "Border");
            Children.Add(line);
            AddText($"{v:N0} k", Left - 6, Y(v), alignRight: true);
        }
        AddText(_points[0].Time.ToString("dd/MM"), Left, h - Bottom + 4, top: true);
        if (t1 != t0) AddText(_points[^1].Time.ToString("dd/MM HH:mm"), w - Right, h - Bottom + 4, alignRight: true, top: true);

        if (_points.Count > 1)
        {
            var curve = new Polyline
            {
                StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round,
                Points = new PointCollection(_points.Select(p => new Point(X(p.Time), Y(p.Price)))),
            };
            curve.SetResourceReference(Shape.StrokeProperty, "AccentSolid");
            Children.Add(curve);
        }

        foreach (var p in _points)
        {
            // Point de 8 px cerclé de la couleur du fond, zone de survol plus large que le point
            var dot = new Ellipse { Width = 8, Height = 8, StrokeThickness = 2, IsHitTestVisible = false };
            dot.SetResourceReference(Shape.FillProperty, "AccentSolid");
            dot.SetResourceReference(Shape.StrokeProperty, "Surface2");
            SetLeft(dot, X(p.Time) - 4);
            SetTop(dot, Y(p.Price) - 4);
            var hit = new Ellipse
            {
                Width = 18, Height = 18, Fill = Brushes.Transparent, Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = $"{p.Time:dd/MM/yyyy HH:mm}\n{p.Price:N0} kamas",
            };
            ToolTipService.SetInitialShowDelay(hit, 0);
            SetLeft(hit, X(p.Time) - 9);
            SetTop(hit, Y(p.Price) - 9);
            Children.Add(dot);
            Children.Add(hit);
        }
    }

    void AddText(string text, double x, double y, bool alignRight = false, bool center = false, bool top = false)
    {
        var t = new TextBlock { Text = text, FontSize = 11 };
        t.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
        t.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = t.DesiredSize;
        SetLeft(t, alignRight ? x - size.Width : center ? x - size.Width / 2 : x);
        SetTop(t, top ? y : y - size.Height / 2);
        Children.Add(t);
    }
}
