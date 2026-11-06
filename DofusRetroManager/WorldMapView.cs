using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DofusRetroManager.Data;

namespace DofusRetroManager;

/// <summary>
/// Carte du monde : tuiles du client dessinées en unités de carte (une map = 40 × 23), déplacement à la souris,
/// zoom à la molette autour du curseur, grille des maps et case survolée. Les marqueurs (enfants du Canvas) gardent
/// leur taille à l'écran : chacun est centré sur sa position, avec une largeur et une hauteur fixées.
/// </summary>
public sealed class WorldMapView : Canvas
{
    const double MinScale = 0.08, MaxScale = 4, ZoomStep = 1.25;

    IReadOnlyList<MapTile> _tiles = [];
    Rect _world = Rect.Empty;
    double _scale = 1;
    Vector _offset;
    bool _fitted;

    Point? _press;
    Vector _pressOffset;
    bool _dragging;

    (int X, int Y)? _hover, _highlight;
    readonly Dictionary<string, List<(FrameworkElement El, Point At)>> _layers = new();

    /// <summary>Case survolée (coordonnées de map) ; HoverLeft quand la souris quitte la carte.</summary>
    public event Action<int, int>? CellHovered;
    public event Action? HoverLeft;
    /// <summary>Clic (sans déplacement) sur un marqueur : son Tag.</summary>
    public event Action<object?>? MarkerClicked;
    /// <summary>Clic sur une case de la carte (hors marqueur, sans déplacement) : coordonnées de map.</summary>
    public event Action<int, int>? CellClicked;

    public WorldMapView()
    {
        ClipToBounds = true;
        Cursor = Cursors.SizeAll;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        SizeChanged += (_, _) => { if (!_fitted) Fit(); else ViewChanged(); };
    }

    public IReadOnlyList<MapTile> Tiles
    {
        get => _tiles;
        set
        {
            _tiles = value;
            _world = Rect.Empty;
            foreach (var t in value) _world.Union(t.Bounds);
            _fitted = false;
            Fit();
        }
    }

    bool _showGrid = true;
    public bool ShowGrid
    {
        get => _showGrid;
        set { _showGrid = value; InvalidateVisual(); }
    }

    /// <summary>Case encadrée (point d'intérêt choisi), ou null.</summary>
    public (int X, int Y)? Highlight
    {
        get => _highlight;
        set { _highlight = value; InvalidateVisual(); }
    }

    /// <summary>Remplace les marqueurs d'un calque (ex. « hints », « players ») ; At en unités de carte.</summary>
    public void SetMarkers(string layer, IEnumerable<(FrameworkElement El, Point At)> markers)
    {
        if (_layers.TryGetValue(layer, out var old))
            foreach (var (el, _) in old) Children.Remove(el);
        var list = markers.ToList();
        _layers[layer] = list;
        foreach (var (el, _) in list) Children.Add(el);
        PlaceMarkers();
    }

    /// <summary>Affiche toute la carte.</summary>
    public void Fit()
    {
        if (_world.IsEmpty || ActualWidth <= 0 || ActualHeight <= 0) return;
        _scale = Math.Clamp(Math.Min(ActualWidth / _world.Width, ActualHeight / _world.Height), MinScale, MaxScale);
        _offset = new Vector((ActualWidth - _world.Width * _scale) / 2 - _world.X * _scale,
            (ActualHeight - _world.Height * _scale) / 2 - _world.Y * _scale);
        _fitted = true;
        ViewChanged();
    }

    /// <summary>Centre la vue sur un point (unités de carte), en zoomant au moins jusqu'à minScale.</summary>
    public void CenterOn(Point at, double minScale = 1)
    {
        _scale = Math.Clamp(Math.Max(_scale, minScale), MinScale, MaxScale);
        _offset = new Vector(ActualWidth / 2 - at.X * _scale, ActualHeight / 2 - at.Y * _scale);
        ViewChanged();
    }

    public void Zoom(bool zoomIn) => ZoomAt(new Point(ActualWidth / 2, ActualHeight / 2), zoomIn ? ZoomStep : 1 / ZoomStep);

    void ZoomAt(Point screen, double factor)
    {
        var at = ToWorld(screen);
        _scale = Math.Clamp(_scale * factor, MinScale, MaxScale);
        _offset = new Vector(screen.X - at.X * _scale, screen.Y - at.Y * _scale);
        ViewChanged();
    }

    Point ToWorld(Point screen) => new((screen.X - _offset.X) / _scale, (screen.Y - _offset.Y) / _scale);

    void ViewChanged()
    {
        PlaceMarkers();
        InvalidateVisual();
    }

    void PlaceMarkers()
    {
        foreach (var list in _layers.Values)
            foreach (var (el, at) in list)
            {
                double w = double.IsNaN(el.Width) ? 0 : el.Width, h = double.IsNaN(el.Height) ? 0 : el.Height;
                SetLeft(el, at.X * _scale + _offset.X - w / 2);
                SetTop(el, at.Y * _scale + _offset.Y - h / 2);
            }
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.PushTransform(new MatrixTransform(_scale, 0, 0, _scale, _offset.X, _offset.Y));
        foreach (var t in _tiles)
        {
            dc.PushTransform(new TranslateTransform(t.Bounds.X, t.Bounds.Y));
            dc.DrawDrawing(t.Drawing);
            dc.Pop();
        }

        // Grille des maps sur la partie visible, si les cases sont assez grandes pour être lisibles
        if (_showGrid && !_world.IsEmpty && _scale * WorldMap.CellHeight >= 6)
        {
            var view = new Rect(ToWorld(new Point(0, 0)), ToWorld(new Point(ActualWidth, ActualHeight)));
            view.Intersect(_world);
            if (!view.IsEmpty)
            {
                var pen = new Pen(new SolidColorBrush(Color.FromArgb(48, 0, 0, 0)), 1 / _scale);
                pen.Freeze();
                for (var x = Math.Floor(view.Left / WorldMap.CellWidth) * WorldMap.CellWidth; x <= view.Right; x += WorldMap.CellWidth)
                    dc.DrawLine(pen, new Point(x, view.Top), new Point(x, view.Bottom));
                for (var y = Math.Floor(view.Top / WorldMap.CellHeight) * WorldMap.CellHeight; y <= view.Bottom; y += WorldMap.CellHeight)
                    dc.DrawLine(pen, new Point(view.Left, y), new Point(view.Right, y));
            }
        }

        var accent = TryFindResource("AccentSolid") as Brush ?? Brushes.OrangeRed;
        if (_highlight is { } hl)
            dc.DrawRectangle(null, new Pen(accent, 3 / _scale), WorldMap.Cell(hl.X, hl.Y));
        if (_hover is { } hv)
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), new Pen(accent, 1.5 / _scale), WorldMap.Cell(hv.X, hv.Y));
        dc.Pop();
    }

    // --- Souris : glisser pour déplacer, molette pour zoomer ---

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        ZoomAt(e.GetPosition(this), e.Delta > 0 ? ZoomStep : 1 / ZoomStep);
        e.Handled = true;
    }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        _press = e.GetPosition(this);
        _pressOffset = _offset;
        _dragging = false;
        base.OnPreviewMouseLeftButtonDown(e);
    }

    protected override void OnPreviewMouseMove(MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        if (_press is { } start && e.LeftButton == MouseButtonState.Pressed)
        {
            if (!_dragging && (p - start).Length > 4)
            {
                _dragging = true;
                CaptureMouse();
            }
            if (_dragging)
            {
                _offset = _pressOffset + (p - start);
                ViewChanged();
            }
        }

        var w = ToWorld(p);
        var cell = ((int)Math.Floor(w.X / WorldMap.CellWidth), (int)Math.Floor(w.Y / WorldMap.CellHeight));
        if (!_world.IsEmpty && _world.Contains(w))
        {
            if (_hover != cell)
            {
                _hover = cell;
                InvalidateVisual();
                CellHovered?.Invoke(cell.Item1, cell.Item2);
            }
        }
        else ClearHover();
        base.OnPreviewMouseMove(e);
    }

    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        bool dragged = _dragging;
        _press = null;
        _dragging = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
        if (dragged) e.Handled = true; // fin d'un déplacement : pas de clic sur le marqueur
        else
        {
            // Marqueur cliqué : l'enfant direct du Canvas qui contient l'élément touché
            var d = e.OriginalSource as DependencyObject;
            while (d != null && d is not Visual) d = LogicalTreeHelper.GetParent(d);
            while (d != null && VisualTreeHelper.GetParent(d) is { } parent && parent != this) d = parent;
            if (d is FrameworkElement el && el != this && Children.Contains(el)) MarkerClicked?.Invoke(el.Tag);
            else if (_hover is { } cell) CellClicked?.Invoke(cell.Item1, cell.Item2);
        }
        base.OnPreviewMouseLeftButtonUp(e);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        ClearHover();
        base.OnMouseLeave(e);
    }

    void ClearHover()
    {
        if (_hover == null) return;
        _hover = null;
        InvalidateVisual();
        HoverLeft?.Invoke();
    }
}
