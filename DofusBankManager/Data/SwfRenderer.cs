using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Media;

namespace DofusBankManager.Data;

/// <summary>
/// Rendu minimal de SWF vectoriels (icônes d'objets de Dofus Retro) en dessin WPF.
/// Gère les formes (DefineShape 1 à 4 : couleurs pleines, dégradés, traits),
/// les sprites et le placement d'objets de la première image. Les bitmaps,
/// textes, masques et animations sont ignorés : suffisant pour des icônes.
/// </summary>
public sealed class SwfRenderer
{
    readonly Dictionary<int, (int Code, byte[] Data)> _chars = new();
    readonly List<(int Code, byte[] Data)> _rootTags = new();

    SwfRenderer(byte[] swf)
    {
        byte[] data;
        if (swf[0] == (byte)'C')
        {
            using var z = new ZLibStream(new MemoryStream(swf, 8, swf.Length - 8), CompressionMode.Decompress);
            using var ms = new MemoryStream();
            ms.Write(swf, 0, 8);
            z.CopyTo(ms);
            data = ms.ToArray();
        }
        else if (swf[0] == (byte)'F') data = swf;
        else throw new InvalidDataException("Format SWF non supporté");

        var r = new BitReader(data, 8);
        r.ReadRect();
        r.Pos += 4; // cadence + nombre d'images
        foreach (var tag in ReadTags(data, r.Pos, data.Length))
        {
            if (tag.Code is 2 or 22 or 32 or 83 or 39)
                _chars[BitConverter.ToUInt16(tag.Data, 0)] = tag;
            _rootTags.Add(tag);
        }
    }

    /// <summary>Couleurs imposées aux clips nommés (comme setRGB en Flash), ex. « _spellBackground » des icônes de sorts.</summary>
    IReadOnlyDictionary<string, Color>? _tints;

    /// <summary>Rend la première image du SWF ; null si rien de dessinable.</summary>
    public static Drawing? Render(byte[] swf, IReadOnlyDictionary<string, Color>? tints = null)
    {
        var r = new SwfRenderer(swf) { _tints = tints };
        var group = r.RenderTimeline(r._rootTags, ColorTx.Identity, 0);
        return group.Children.Count > 0 ? group : null;
    }

    static List<(int Code, byte[] Data)> ReadTags(byte[] d, int p, int end)
    {
        var tags = new List<(int, byte[])>();
        while (p + 2 <= end)
        {
            int h = BitConverter.ToUInt16(d, p); p += 2;
            int code = h >> 6, len = h & 0x3F;
            if (len == 0x3F) { len = BitConverter.ToInt32(d, p); p += 4; }
            if (code == 0 || p + len > end) break;
            tags.Add((code, d.AsSpan(p, len).ToArray()));
            p += len;
        }
        return tags;
    }

    // --- Timeline (racine ou sprite) : liste d'affichage de la première image ---

    DrawingGroup RenderTimeline(List<(int Code, byte[] Data)> tags, ColorTx cx, int depthLevel)
    {
        var display = new SortedDictionary<int, (int Char, Matrix M, ColorTx Cx, bool IsMask)>();
        foreach (var (code, data) in tags)
        {
            if (code == 1) break; // ShowFrame : fin de la première image
            if (code is 26 or 70) PlaceObject(code, data, display);
            else if (code == 4)
            {
                var r = new BitReader(data, 0);
                int ch = r.U16(), depth = r.U16();
                display[depth] = (ch, r.ReadMatrix(), ColorTx.Identity, false);
            }
            else if (code == 28) display.Remove(BitConverter.ToUInt16(data, 0));
            else if (code == 5) display.Remove(BitConverter.ToUInt16(data, 2));
        }

        var group = new DrawingGroup();
        if (depthLevel > 16) return group; // protection contre les sprites récursifs
        foreach (var (_, o) in display)
        {
            if (o.IsMask || !_chars.TryGetValue(o.Char, out var def)) continue;
            var child = def.Code == 39
                ? RenderTimeline(ReadTags(def.Data, 4, def.Data.Length), o.Cx.Then(cx), depthLevel + 1)
                : RenderShape(def.Code, def.Data, o.Cx.Then(cx));
            if (child.Children.Count == 0) continue;
            child.Transform = new MatrixTransform(o.M);
            group.Children.Add(child);
        }
        return group;
    }

    void PlaceObject(int code, byte[] data, SortedDictionary<int, (int, Matrix, ColorTx, bool)> display)
    {
        var r = new BitReader(data, 0);
        int flags = r.U8();
        int flags2 = code == 70 ? r.U8() : 0;
        int depth = r.U16();
        if (code == 70 && ((flags2 & 0x08) != 0 || ((flags2 & 0x10) != 0 && (flags & 0x02) != 0))) r.ReadString();

        display.TryGetValue(depth, out var cur);
        if (!display.ContainsKey(depth)) cur = (-1, Matrix.Identity, ColorTx.Identity, false);
        if ((flags & 0x02) != 0) cur.Item1 = r.U16();
        if ((flags & 0x04) != 0) cur.Item2 = r.ReadMatrix();
        if ((flags & 0x08) != 0) cur.Item3 = r.ReadCxForm(true);
        if ((flags & 0x10) != 0) r.U16();
        if ((flags & 0x20) != 0 && r.ReadString() is var name && _tints != null && _tints.TryGetValue(name, out var tint))
            cur.Item3 = ColorTx.Fill(tint);
        if ((flags & 0x40) != 0) cur.Item4 = true; // ClipDepth : objet masque, non dessiné
        if (cur.Item1 >= 0) display[depth] = cur;
    }

    // --- Formes ---

    sealed record FillStyle(Brush? Brush);
    sealed record LineStyle(double Width, Brush Brush);
    sealed record Edge(Point From, Point? Ctrl, Point To)
    {
        public Edge Reverse() => new(To, Ctrl, From);
    }

    static DrawingGroup RenderShape(int code, byte[] data, ColorTx cx)
    {
        int version = code switch { 2 => 1, 22 => 2, 32 => 3, _ => 4 };
        var r = new BitReader(data, 2);
        r.ReadRect();
        if (version == 4) { r.ReadRect(); r.Pos++; }

        var group = new DrawingGroup();
        var fills = ReadFillStyles(r, version, cx);
        var lines = ReadLineStyles(r, version, cx);
        int fillBits = (int)r.UB(4), lineBits = (int)r.UB(4);

        var fillEdges = new Dictionary<int, List<Edge>>();
        var lineEdges = new Dictionary<int, List<Edge>>();
        int fill0 = 0, fill1 = 0, line = 0;
        double x = 0, y = 0;

        void Flush()
        {
            foreach (var (idx, edges) in fillEdges.OrderBy(kv => kv.Key))
                if (idx - 1 < fills.Count && fills[idx - 1].Brush is { } b)
                    group.Children.Add(new GeometryDrawing(b, null, BuildGeometry(edges, true)));
            foreach (var (idx, edges) in lineEdges.OrderBy(kv => kv.Key))
                if (idx - 1 < lines.Count)
                {
                    var ls = lines[idx - 1];
                    var pen = new Pen(ls.Brush, Math.Max(ls.Width, 0.5))
                    { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
                    group.Children.Add(new GeometryDrawing(null, pen, BuildGeometry(edges, false)));
                }
            fillEdges.Clear();
            lineEdges.Clear();
        }

        void AddEdge(Edge e)
        {
            if (fill0 > 0) Get(fillEdges, fill0).Add(e.Reverse());
            if (fill1 > 0) Get(fillEdges, fill1).Add(e);
            if (line > 0) Get(lineEdges, line).Add(e);
        }

        while (true)
        {
            if (r.UB(1) == 0)
            {
                int f = (int)r.UB(5);
                if (f == 0) break;
                if ((f & 0x01) != 0) { int n = (int)r.UB(5); x = r.SB(n) / 20.0; y = r.SB(n) / 20.0; }
                if ((f & 0x02) != 0) fill0 = (int)r.UB(fillBits);
                if ((f & 0x04) != 0) fill1 = (int)r.UB(fillBits);
                if ((f & 0x08) != 0) line = (int)r.UB(lineBits);
                if ((f & 0x10) != 0)
                {
                    Flush();
                    fills = ReadFillStyles(r, version, cx);
                    lines = ReadLineStyles(r, version, cx);
                    fillBits = (int)r.UB(4); lineBits = (int)r.UB(4);
                }
            }
            else if (r.UB(1) == 1) // segment droit
            {
                int n = (int)r.UB(4) + 2;
                double dx = 0, dy = 0;
                if (r.UB(1) == 1) { dx = r.SB(n); dy = r.SB(n); }
                else if (r.UB(1) == 1) dy = r.SB(n);
                else dx = r.SB(n);
                var from = new Point(x, y);
                x += dx / 20.0; y += dy / 20.0;
                AddEdge(new Edge(from, null, new Point(x, y)));
            }
            else // courbe de Bézier quadratique
            {
                int n = (int)r.UB(4) + 2;
                double cxd = r.SB(n), cyd = r.SB(n), axd = r.SB(n), ayd = r.SB(n);
                var from = new Point(x, y);
                var ctrl = new Point(x + cxd / 20.0, y + cyd / 20.0);
                x = ctrl.X + axd / 20.0; y = ctrl.Y + ayd / 20.0;
                AddEdge(new Edge(from, ctrl, new Point(x, y)));
            }
        }
        Flush();
        return group;
    }

    static List<Edge> Get(Dictionary<int, List<Edge>> d, int k) =>
        d.TryGetValue(k, out var l) ? l : d[k] = new List<Edge>();

    /// <summary>Relie les segments bout à bout pour former des contours.</summary>
    static Geometry BuildGeometry(List<Edge> edges, bool closed)
    {
        static (long, long) Key(Point p) => ((long)Math.Round(p.X * 20), (long)Math.Round(p.Y * 20));
        var byStart = new Dictionary<(long, long), List<int>>();
        for (int i = 0; i < edges.Count; i++)
        {
            var k = Key(edges[i].From);
            if (!byStart.TryGetValue(k, out var l)) byStart[k] = l = new();
            l.Add(i);
        }

        var used = new bool[edges.Count];
        var geo = new StreamGeometry { FillRule = FillRule.EvenOdd };
        using (var ctx = geo.Open())
        {
            for (int i = 0; i < edges.Count; i++)
            {
                if (used[i]) continue;
                var start = edges[i].From;
                ctx.BeginFigure(start, closed, closed);
                int cur = i;
                while (cur >= 0)
                {
                    used[cur] = true;
                    var e = edges[cur];
                    if (e.Ctrl is { } c) ctx.QuadraticBezierTo(c, e.To, true, true);
                    else ctx.LineTo(e.To, true, true);
                    if (Key(e.To) == Key(start)) break;
                    cur = -1;
                    if (byStart.TryGetValue(Key(e.To), out var next))
                        foreach (var n in next) if (!used[n]) { cur = n; break; }
                }
            }
        }
        geo.Freeze();
        return geo;
    }

    static List<FillStyle> ReadFillStyles(BitReader r, int version, ColorTx cx)
    {
        r.Align();
        int count = r.U8();
        if (count == 0xFF && version >= 2) count = r.U16();
        var list = new List<FillStyle>(count);
        for (int i = 0; i < count; i++) list.Add(new FillStyle(ReadFill(r, version, cx)));
        return list;
    }

    static Brush? ReadFill(BitReader r, int version, ColorTx cx)
    {
        int type = r.U8();
        switch (type)
        {
            case 0x00:
                return Frozen(new SolidColorBrush(cx.Apply(r.ReadColor(version >= 3))));
            case 0x10: case 0x12: case 0x13:
            {
                var m = r.ReadMatrix();
                r.Align();
                int head = r.U8(), n = head & 0x0F;
                var stops = new GradientStopCollection();
                for (int i = 0; i < n; i++)
                {
                    double ratio = r.U8() / 255.0;
                    stops.Add(new GradientStop(cx.Apply(r.ReadColor(version >= 3)), ratio));
                }
                double focal = type == 0x13 ? (short)r.U16() / 256.0 : 0;
                const double S = 16384 / 20.0; // carré de dégradé SWF : -16384..16384 twips
                GradientBrush b = type == 0x10
                    ? new LinearGradientBrush(stops, new Point(-S, 0), new Point(S, 0))
                    : new RadialGradientBrush(stops) { Center = new Point(0, 0), GradientOrigin = new Point(focal * S, 0), RadiusX = S, RadiusY = S };
                b.MappingMode = BrushMappingMode.Absolute;
                b.Transform = new MatrixTransform(m);
                return Frozen(b);
            }
            case 0x40: case 0x41: case 0x42: case 0x43:
                r.U16(); r.ReadMatrix();
                return null; // remplissage bitmap : non géré
            default:
                return null;
        }
    }

    static List<LineStyle> ReadLineStyles(BitReader r, int version, ColorTx cx)
    {
        r.Align();
        int count = r.U8();
        if (count == 0xFF) count = r.U16();
        var list = new List<LineStyle>(count);
        for (int i = 0; i < count; i++)
        {
            double width = r.U16() / 20.0;
            if (version < 4)
            {
                list.Add(new LineStyle(width, Frozen(new SolidColorBrush(cx.Apply(r.ReadColor(version >= 3))))));
                continue;
            }
            int f1 = r.U8(), f2 = r.U8();
            if (((f1 >> 4) & 0x03) == 2) r.U16(); // limite d'onglet
            Brush brush = (f1 & 0x08) != 0
                ? ReadFill(r, version, cx) ?? Brushes.Transparent
                : Frozen(new SolidColorBrush(cx.Apply(r.ReadColor(true))));
            list.Add(new LineStyle(width, brush));
        }
        return list;
    }

    static Brush Frozen(Brush b) { b.Freeze(); return b; }

    // --- Transformation de couleur (multiplication + ajout, comme Flash) ---

    readonly record struct ColorTx(double Rm, double Gm, double Bm, double Am, double Ra, double Ga, double Ba, double Aa)
    {
        public static ColorTx Identity => new(1, 1, 1, 1, 0, 0, 0, 0);

        /// <summary>Remplace la couleur en gardant la transparence (setRGB).</summary>
        public static ColorTx Fill(Color c) => new(0, 0, 0, 1, c.R, c.G, c.B, 0);

        /// <summary>Applique this puis parent.</summary>
        public ColorTx Then(ColorTx p) => new(Rm * p.Rm, Gm * p.Gm, Bm * p.Bm, Am * p.Am,
            Ra * p.Rm + p.Ra, Ga * p.Gm + p.Ga, Ba * p.Bm + p.Ba, Aa * p.Am + p.Aa);

        public Color Apply(Color c) => Color.FromArgb(
            Clamp(c.A * Am + Aa), Clamp(c.R * Rm + Ra), Clamp(c.G * Gm + Ga), Clamp(c.B * Bm + Ba));

        static byte Clamp(double v) => (byte)Math.Clamp(Math.Round(v), 0, 255);
    }

    // --- Lecture de bits ---

    sealed class BitReader(byte[] d, int pos)
    {
        public int Pos = pos;
        int _bit;

        public void Align() { if (_bit != 0) { _bit = 0; Pos++; } }
        public int U8() { Align(); return d[Pos++]; }
        public int U16() { Align(); int v = d[Pos] | (d[Pos + 1] << 8); Pos += 2; return v; }

        public uint UB(int n)
        {
            uint v = 0;
            for (int i = 0; i < n; i++)
            {
                v = (v << 1) | (uint)((d[Pos] >> (7 - _bit)) & 1);
                if (++_bit == 8) { _bit = 0; Pos++; }
            }
            return v;
        }

        public int SB(int n)
        {
            if (n == 0) return 0;
            uint v = UB(n);
            if (n < 32 && (v & (1u << (n - 1))) != 0) v |= ~0u << n;
            return (int)v;
        }

        double FB(int n) => SB(n) / 65536.0;

        public string ReadString()
        {
            Align();
            int start = Pos;
            while (Pos < d.Length && d[Pos] != 0) Pos++;
            var s = System.Text.Encoding.UTF8.GetString(d, start, Pos - start);
            Pos++;
            return s;
        }

        public void ReadRect()
        {
            Align();
            int n = (int)UB(5);
            for (int i = 0; i < 4; i++) SB(n);
            Align();
        }

        public Color ReadColor(bool alpha)
        {
            byte r = (byte)U8(), g = (byte)U8(), b = (byte)U8();
            return Color.FromArgb(alpha ? (byte)U8() : (byte)255, r, g, b);
        }

        public Matrix ReadMatrix()
        {
            Align();
            double sx = 1, sy = 1, r0 = 0, r1 = 0;
            if (UB(1) == 1) { int n = (int)UB(5); sx = FB(n); sy = FB(n); }
            if (UB(1) == 1) { int n = (int)UB(5); r0 = FB(n); r1 = FB(n); }
            int nt = (int)UB(5);
            double tx = SB(nt) / 20.0, ty = SB(nt) / 20.0;
            Align();
            return new Matrix(sx, r0, r1, sy, tx, ty);
        }

        public ColorTx ReadCxForm(bool alpha)
        {
            Align();
            bool hasAdd = UB(1) == 1, hasMult = UB(1) == 1;
            int n = (int)UB(4);
            var t = ColorTx.Identity;
            if (hasMult)
                t = t with { Rm = SB(n) / 256.0, Gm = SB(n) / 256.0, Bm = SB(n) / 256.0, Am = alpha ? SB(n) / 256.0 : 1 };
            if (hasAdd)
                t = t with { Ra = SB(n), Ga = SB(n), Ba = SB(n), Aa = alpha ? SB(n) : 0 };
            Align();
            return t;
        }
    }
}
