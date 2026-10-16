using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVASpaceDuel.Engine;
using Effect = AVASpaceDuel.Engine.Effect;

namespace AVASpaceDuel.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVASpaceDuel/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round);
        return p;
    }

    private const uint White = 0xFFF2F2FF, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF8A9AC8, Fire = 0xFFFF8A2A, Cyan = 0xFF6AE0FF;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    private static uint Alpha(uint colour, double a) => (uint)(Math.Clamp(a, 0, 1) * 255) << 24 | (colour & 0x00FFFFFF);

    private const uint ShipCyan = 0xFF6AE8FF, TwinPink = 0xFFFF7AE0, TetherViolet = 0xFFB08AFF, Hostile = 0xFFFF5A4A;

    private bool InGame => Engine.State is GameState.Playing or GameState.Attract or GameState.Paused or GameState.GameOver;

    /// <summary>A line as a vector display draws it: a soft glow under a bright core.</summary>
    private static void Glow(DrawingContext dc, Point a, Point b, uint colour, double alpha = 1, double width = 2)
    {
        if (alpha <= 0.01) return;
        dc.DrawLine(Pen(Alpha(colour, 0.22 * alpha), width * 3.2), a, b);
        dc.DrawLine(Pen(Alpha(colour, alpha), width), a, b);
    }

    /// <summary>A closed outline of points given about (0, 0), turned and placed.</summary>
    private static void Shape(DrawingContext dc, double x, double y, double angle, double k, uint colour, double alpha, params (double X, double Y)[] pts)
    {
        double c = Math.Cos(angle), s = Math.Sin(angle);
        Point At((double X, double Y) p) => new(x + (p.X * c - p.Y * s) * k, y + (p.X * s + p.Y * c) * k);
        for (int i = 0; i < pts.Length; i++) Glow(dc, At(pts[i]), At(pts[(i + 1) % pts.Length]), colour, alpha);
    }

    /// <summary>
    /// Draws something wherever it shows: near an edge, its other half shows at the far side too, as space wraps.
    /// </summary>
    private static void Wrapped(double x, double y, double r, Action<double, double> draw)
    {
        draw(x, y);
        bool l = x < r, rt = x > Field.Width - r, t = y < r, b = y > Field.Height - r;
        if (l) draw(x + Field.Width, y);
        if (rt) draw(x - Field.Width, y);
        if (t) draw(x, y + Field.Height);
        if (b) draw(x, y - Field.Height);
    }

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF000004), new Rect(0, 0, Field.Width, Field.Height));
        DrawStars(dc);
        if (InGame)
        {
            foreach (var s in g.Shapes) Wrapped(s.X, s.Y, s.Radius + 6, (x, y) => DrawShape(dc, s.Kind, s.Size, x, y, s.Angle, s.Tilt, s.T, 1));
            if (g.Saucer is { } u) DrawSaucer(dc, u.X, u.Y, u.T, 1);
            foreach (var d in g.Wreckage)
            {
                double a = Math.Clamp(1 - d.Age / d.Life, 0, 1), c = Math.Cos(d.Angle) * d.Length / 2, s = Math.Sin(d.Angle) * d.Length / 2;
                Glow(dc, new Point(d.X - c, d.Y - s), new Point(d.X + c, d.Y + s), d.Colour, a, 1.6);
            }
            foreach (var b in g.Bullets)
            {
                double l = Math.Sqrt(b.Vx * b.Vx + b.Vy * b.Vy);
                var tail = new Point(b.X - b.Vx / l * (b.Hostile ? 6 : 10), b.Y - b.Vy / l * (b.Hostile ? 6 : 10));
                Glow(dc, tail, new Point(b.X, b.Y), b.Hostile ? Hostile : 0xFFF8FFFF, 1, b.Hostile ? 3 : 2.2);
            }
            DrawCraft(dc, g.Craft);
        }
        foreach (var e in g.Effects) DrawEffect(dc, e);
        DrawHud(dc);
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawInitials(dc); break;
            default: DrawMessages(dc); break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(Field.Width / 2, 340), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(Field.Width / 2, 400), 26, White, center: true);
        }
        if (g.State == GameState.GameOver)
            Text(dc, "GAME OVER", new Point(Field.Width / 2, 360), 100, Red, center: true);
        if (g.State == GameState.Attract)
            Text(dc, "DEMONSTRATION - PRESS ENTER (1 SHIP) OR T (2 SHIPS)", new Point(Field.Width / 2, Field.Height - 16), 24, Yellow, center: true);
    }

    private void DrawStars(DrawingContext dc)
    {
        for (int i = 0; i < 160; i++)
        {
            uint h = Hash(i, 3, 7);
            double x = h % 1024, y = (h >> 10) % 768, tw = 0.35 + 0.4 * Math.Abs(Math.Sin(_clock * (0.6 + (h >> 20) % 5 * 0.3) + i));
            dc.DrawEllipse(Brush(Alpha(0xFFDDE4FF, tw)), null, new Point(x, y), (h >> 6) % 7 == 0 ? 1.4 : 0.8, (h >> 6) % 7 == 0 ? 1.4 : 0.8);
        }
    }

    // ───────────────────────────── the craft ─────────────────────────────

    /// <summary>The craft: one ship, or two on a glowing tether; a flame when thrusting, and a bubble round each while shielded.</summary>
    private void DrawCraft(DrawingContext dc, Craft k)
    {
        if (!k.Alive) return;
        bool blink = k.Safe > 0 && (int)(_clock * 10) % 2 == 0;
        var ships = k.Ships.ToList();
        if (k.Pair && ships.Count == 2)
        {
            // The tether: drawn the short way across, with a pulse running along it.
            var (ax, ay) = ships[0];
            double bx = ax + Field.Dx(ax, ships[1].X), by = ay + Field.Dy(ay, ships[1].Y);
            Glow(dc, new Point(ax, ay), new Point(bx, by), TetherViolet, blink ? 0.3 : 0.6, 1.4);
            double p = (_clock * 1.5) % 1;
            dc.DrawEllipse(Brush(Alpha(0xFFFFFFFF, 0.8)), null, new Point(ax + (bx - ax) * p, ay + (by - ay) * p), 2.2, 2.2);
        }
        for (int i = 0; i < ships.Count; i++)
        {
            var (x, y) = ships[i];
            uint col = k.Pair && i == 1 ? TwinPink : ShipCyan;
            Wrapped(x, y, 30, (sx, sy) =>
            {
                if (!blink) DrawShip(dc, sx, sy, k.Angle, col, k.Thrusting, 1.2);
                if (k.Shielded)
                {
                    double r = 22 + Math.Sin(_clock * 30) * 1.5;
                    dc.DrawEllipse(null, Pen(Alpha(0xFF8AF0FF, 0.25), 7), new Point(sx, sy), r, r);
                    dc.DrawEllipse(null, Pen(Alpha(0xFFCFFFFF, 0.85), 1.6), new Point(sx, sy), r, r);
                }
            });
        }
    }

    private void DrawShip(DrawingContext dc, double x, double y, double angle, uint colour, bool thrust, double k)
    {
        Shape(dc, x, y, angle, k, colour, 1, (16, 0), (-11, -10), (-6, 0), (-11, 10));
        Shape(dc, x, y, angle, k, Alpha(colour, 0.7), 0.7, (2, -3), (6, 0), (2, 3));
        if (thrust && (int)(_clock * 24) % 2 == 0)
        {
            double c = Math.Cos(angle), s = Math.Sin(angle);
            Point At(double fx, double fy) => new(x + (fx * c - fy * s) * k, y + (fx * s + fy * c) * k);
            Glow(dc, At(-8, -4), At(-20 - Math.Sin(_clock * 40) * 3, 0), 0xFFFFB04A, 1, 1.8);
            Glow(dc, At(-8, 4), At(-20 - Math.Sin(_clock * 40) * 3, 0), 0xFFFFB04A, 1, 1.8);
        }
    }

    // ───────────────────────────── the enemy ─────────────────────────────

    /// <summary>
    /// A shape in glowing lines: a cube or a prism turning in three dimensions, a pinwheel, or a pulsing mine.
    /// </summary>
    private static void DrawShape(DrawingContext dc, ShapeKind kind, int size, double x, double y, double angle, double tilt, double t, double k)
    {
        uint col = GameEngine.Colour(kind);
        double r = kind switch
        {
            ShapeKind.Block => size switch { 3 => 34, 2 => 22, _ => 13 },
            ShapeKind.Prism => size == 2 ? 30 : 16,
            ShapeKind.Spinner => 18,
            _ => 11,
        } * k;
        switch (kind)
        {
            case ShapeKind.Block:
            {
                var v = new (double, double, double)[8];
                for (int i = 0; i < 8; i++) v[i] = ((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1);
                Wire(dc, x, y, r * 0.6, angle, tilt, col, v, [(0, 1), (2, 3), (4, 5), (6, 7), (0, 2), (1, 3), (4, 6), (5, 7), (0, 4), (1, 5), (2, 6), (3, 7)]);
                break;
            }
            case ShapeKind.Prism:
            {
                var v = new (double, double, double)[6];
                for (int i = 0; i < 3; i++)
                {
                    double a = i * Math.PI * 2 / 3;
                    v[i] = (Math.Cos(a), Math.Sin(a), -0.7);
                    v[i + 3] = (Math.Cos(a), Math.Sin(a), 0.7);
                }
                Wire(dc, x, y, r * 0.85, angle, tilt, col, v, [(0, 1), (1, 2), (2, 0), (3, 4), (4, 5), (5, 3), (0, 3), (1, 4), (2, 5)]);
                break;
            }
            case ShapeKind.Spinner:
                for (int i = 0; i < 4; i++)
                {
                    double a = angle * 2 + i * Math.PI / 2;
                    Shape(dc, x, y, a, r / 18, col, 1, (0, 0), (18, 0), (10, 8));
                }
                break;
            default:
            {
                double p = 1 + Math.Sin(t * 10) * 0.18;
                Shape(dc, x, y, angle, r / 11 * p, col, 1, (11, 0), (0, 11), (-11, 0), (0, -11));
                Shape(dc, x, y, angle + Math.PI / 4, r / 11 * p * 0.55, 0xFFFFE04A, 1, (11, 0), (0, 11), (-11, 0), (0, -11));
                break;
            }
        }
    }

    /// <summary>A wireframe turned two ways and seen flat on.</summary>
    private static void Wire(DrawingContext dc, double x, double y, double r, double yaw, double pitch, uint col, (double X, double Y, double Z)[] v, (int, int)[] edges)
    {
        double cy = Math.Cos(yaw), sy = Math.Sin(yaw), cp = Math.Cos(pitch), sp = Math.Sin(pitch);
        var p = new Point[v.Length];
        var depth = new double[v.Length];
        for (int i = 0; i < v.Length; i++)
        {
            var (a, b, c) = v[i];
            double x1 = a * cy + c * sy, z1 = -a * sy + c * cy;
            double y1 = b * cp - z1 * sp, z2 = b * sp + z1 * cp;
            p[i] = new Point(x + x1 * r, y + y1 * r);
            depth[i] = z2;
        }
        foreach (var (i, j) in edges) Glow(dc, p[i], p[j], col, 0.55 + 0.45 * Math.Clamp((depth[i] + depth[j]) / 3 + 0.5, 0, 1), 1.8);
    }

    private void DrawSaucer(DrawingContext dc, double x, double y, double t, double k)
    {
        const uint col = 0xFFFF5AD8;
        Shape(dc, x, y, 0, k, col, 1, (-20, 0), (-9, -7), (9, -7), (20, 0), (9, 7), (-9, 7));
        Glow(dc, new Point(x - 20 * k, y), new Point(x + 20 * k, y), col, 0.8, 1.4);
        Shape(dc, x, y, 0, k, col, 1, (-7, -7), (-4, -13), (4, -13), (7, -7));
        for (int i = 0; i < 3; i++)
            if ((int)(t * 8 + i) % 3 == 0) dc.DrawEllipse(Brush(Yellow), null, new Point(x + (i - 1) * 9 * k, y + 3 * k), 1.8, 1.8);
    }

    private void DrawEffect(DrawingContext dc, Effect e)
    {
        double t = e.Age / e.Life;
        var c = new Point(e.X, e.Y);
        switch (e.Kind)
        {
            case EffectKind.Burst:
            {
                double r = (6 + 30 * t) * e.Size;
                dc.DrawEllipse(null, Pen(Alpha(e.Colour, 1 - t), 2.5), c, r, r);
                break;
            }
            case EffectKind.Score:
                Text(dc, e.Text, new Point(c.X, c.Y - t * 24), 20, e.Colour, center: true);
                break;
            case EffectKind.Word:
                Text(dc, e.Text, new Point(c.X, c.Y - t * 30), 34, e.Colour, center: true);
                break;
        }
    }

    // ───────────────────────────── the score bar, and the screens ─────────────────────────────

    private void DrawHud(DrawingContext dc)
    {
        var g = Engine;
        Text(dc, "SCORE", new Point(16, 16), 22, Dim);
        Text(dc, g.Score.ToString("D6"), new Point(16, 46), 36, Yellow);
        Text(dc, "HI", new Point(Field.Width - 16, 16), 22, Dim, right: true);
        Text(dc, Math.Max(g.HighScores.TopScore, g.Score).ToString("D6"), new Point(Field.Width - 16, 46), 30, Dim, right: true);
        if (!InGame) return;
        Text(dc, $"WAVE {g.Wave}", new Point(512, 18), 26, ShipCyan, center: true);
        // The shield: a bar that only fills up again between waves.
        double s = g.Craft.Shield / GameEngine.ShieldMax;
        dc.DrawRectangle(null, Pen(Dim, 1.5), new Rect(432, 42, 160, 12), 3, 3);
        dc.FillRectangle(Brush(s < 0.25 ? Red : 0xFF8AF0FF), new Rect(434, 44, 156 * s, 8));
        Text(dc, "SHIELD", new Point(512, 68), 18, Dim, center: true);
        int spare = Math.Max(0, g.Lives - (g.Craft.Alive ? 1 : 0));
        for (int i = 0; i < Math.Min(spare, g.Twin ? 4 : 6); i++)
        {
            double x = 220 + i * (g.Twin ? 46 : 28);
            if (g.Twin)
            {
                Glow(dc, new Point(x - 10, 40), new Point(x + 10, 40), TetherViolet, 0.5, 1);
                DrawShip(dc, x - 10, 40, -Math.PI / 2, ShipCyan, false, 0.6);
                DrawShip(dc, x + 10, 40, -Math.PI / 2, TwinPink, false, 0.6);
            }
            else DrawShip(dc, x, 40, -Math.PI / 2, ShipCyan, false, 0.7);
        }
    }

    private void DrawMessages(DrawingContext dc)
    {
        var g = Engine;
        if (g.Message is not { } msg) return;
        double cx = Field.Width / 2, cy = 250;
        var (w, h) = (Math.Max(msg.Length * 26, (g.SubMessage?.Length ?? 0) * 15) + 60, g.SubMessage != null ? 106.0 : 70.0);
        dc.FillRectangle(Brush(0xA0080A20), new Rect(cx - w / 2, cy - 46, w, h), 6);
        Text(dc, msg, new Point(cx, cy - 12), msg.Length > 14 ? 42 : 54, g.Phase == Phase.Dying ? Red : Yellow, center: true);
        if (g.SubMessage is { } sub) Text(dc, sub, new Point(cx, cy + 34), 26, White, center: true);
    }

    private void DrawTitle(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        string title = "AVA SPACE DUEL";
        uint[] hues = [ShipCyan, 0xFFB08AFF, TwinPink, 0xFFFFE04A];
        for (int i = 0; i < title.Length; i++)
            Text(dc, title[i].ToString(), new Point(cx - title.Length * 26 + 26 + i * 52, 110), 108, hues[(i + (int)(_clock * 3)) % hues.Length], center: true);
        Text(dc, "AFTER THE 1982 ARCADE CLASSIC", new Point(cx, 176), 28, Dim, center: true);
        // The twins, swinging round.
        double a = _clock * 0.9, px = -Math.Sin(a) * 60, py = Math.Cos(a) * 60;
        Glow(dc, new Point(cx + px, 250 + py * 0.5), new Point(cx - px, 250 - py * 0.5), TetherViolet, 0.6, 1.4);
        double head = Math.Atan2(Math.Sin(a) * 0.5, Math.Cos(a));
        DrawShip(dc, cx + px, 250 + py * 0.5, head, ShipCyan, true, 1.4);
        DrawShip(dc, cx - px, 250 - py * 0.5, head, TwinPink, true, 1.4);
        Text(dc, "SHOOT THE SHAPES TO PIECES - THE SHIELD SAVES YOU, BUT ONLY FILLS UP BETWEEN WAVES", new Point(cx, 330), 23, White, center: true);
        (string Name, string Points, Action<double> Draw)[] legend =
        [
            ("CUBE", "20 50 100", x => DrawShape(dc, ShapeKind.Block, 3, x, 410, _clock, _clock * 0.7, _clock, 1)),
            ("PRISM", "150 250", x => DrawShape(dc, ShapeKind.Prism, 2, x, 410, _clock * 1.2, _clock * 0.5, _clock, 1)),
            ("SPINNER", "300", x => DrawShape(dc, ShapeKind.Spinner, 1, x, 410, _clock * 2, 0, _clock, 1.4)),
            ("MINE", "200", x => DrawShape(dc, ShapeKind.Mine, 1, x, 410, _clock, 0, _clock, 1.6)),
            ("SAUCER", "1000", x => DrawSaucer(dc, x, 410, _clock, 1.3)),
        ];
        for (int i = 0; i < legend.Length; i++)
        {
            double x = cx - 320 + i * 160;
            legend[i].Draw(x);
            Text(dc, legend[i].Name, new Point(x, 466), 22, Dim, center: true);
            Text(dc, legend[i].Points, new Point(x, 492), 22, Yellow, center: true);
        }
        if ((int)(_clock * 2) % 2 == 0)
        {
            Text(dc, "ENTER OR 1: ONE SHIP", new Point(cx, 560), 40, Yellow, center: true);
            Text(dc, "T OR 2: TWIN SHIPS ON A TETHER", new Point(cx, 604), 40, TwinPink, center: true);
        }
        Text(dc, "ARROWS: TURN AND THRUST    SPACE: FIRE    DOWN OR SHIFT: SHIELD", new Point(cx, 664), 23, Dim, center: true);
        Text(dc, $"P: PAUSE    M: SOUND    EXTRA LIFE EVERY {GameEngine.ExtraLifeEvery}    GAMEPAD: START OR Y", new Point(cx, 694), 22, Dim, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        Text(dc, "TOP DUELLISTS", new Point(cx, 140), 68, ShipCyan, center: true);
        Text(dc, "RANK NAME    SCORE    WAVE", new Point(cx, 204), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {e.Stage,4}", new Point(cx, 248 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "ENTER: ONE SHIP    T: TWIN SHIPS", new Point(cx, 710), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, center: true);
        Text(dc, g.Score.ToString(), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }
}
