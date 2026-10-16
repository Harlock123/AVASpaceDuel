namespace AVASpaceDuel.Engine;

/// <summary>
/// The computer player, for the demonstration (and the tests). When something is about to hit a ship it puts up
/// the shield (while there's any left). Otherwise it picks the target it can bring its guns to bear on soonest, the
/// close and the homing ones first, turns to lead it, and fires once it's lined up. With the twin ships it aims
/// so that one ship's line of fire crosses the target, rather than the empty middle of the tether. It thrusts
/// only to close on something far off, or to get out of the way of something it can't shoot in time.
/// </summary>
public sealed class ShipPilot
{
    /// <summary>What it is up to, for the probe.</summary>
    public string Doing { get; private set; } = "";

    /// <summary>How soon a hit has to be coming before it shields.</summary>
    public const double ShieldWithin = 0.25;
    /// <summary>It sees a new target only every so often.</summary>
    public const int LookEvery = 6;

    private int _tick;
    private object? _target;
    private bool _shield;

    public Controls Decide(GameEngine g)
    {
        _tick++;
        var k = g.Craft;
        if (g.Phase is not (Phase.Play or Phase.Start) || !k.Alive) { Doing = "waiting"; return default; }
        var ships = k.Ships.ToList();
        // It notices danger only every few frames, like anyone would.
        if (_tick % 4 == 0) _shield = Danger(g, ships) is { } t && t < ShieldWithin && k.Shield > 2 && k.Safe <= 0.1;
        bool shield = _shield;
        if (_tick % LookEvery == 0 || !Exists(g, _target)) _target = Choose(g);
        if (_target == null) { Doing = shield ? "shielding" : "drifting"; return new Controls(Shield: shield); }
        var (tx, ty, tvx, tvy, r) = Where(_target);
        // Where it will be when a shot gets there.
        double dx = Field.Dx(k.X, tx), dy = Field.Dy(k.Y, ty);
        double time = Math.Sqrt(dx * dx + dy * dy) / GameEngine.BulletSpeed;
        for (int i = 0; i < 3; i++)
        {
            double lx = dx + (tvx - k.Vx) * time, ly = dy + (tvy - k.Vy) * time;
            time = Math.Sqrt(lx * lx + ly * ly) / GameEngine.BulletSpeed;
        }
        dx += (tvx - k.Vx) * time;
        dy += (tvy - k.Vy) * time;
        double dist = Math.Sqrt(dx * dx + dy * dy), want = Math.Atan2(dy, dx);
        if (k.Pair && dist > Craft.Tether * 0.6)
        {
            // Line up one end's guns on it: whichever means turning less.
            double off = Math.Asin(Math.Min(1, Craft.Tether / 2 / dist));
            double a1 = want - off, a2 = want + off;
            want = Math.Abs(Diff(a1, k.Angle)) < Math.Abs(Diff(a2, k.Angle)) ? a1 : a2;
        }
        double diff = Diff(want, k.Angle);
        int turn = Math.Abs(diff) < 0.03 ? 0 : Math.Sign(diff);
        bool lined = Math.Abs(diff) < Math.Atan2(r * 0.8, Math.Max(dist, 1)) + 0.02;
        bool fire = lined && dist < GameEngine.BulletSpeed * GameEngine.BulletLife * 0.95;
        double speed = Math.Sqrt(k.Vx * k.Vx + k.Vy * k.Vy);
        bool thrust = !shield && Math.Abs(diff) < 0.3 && dist > 430 && speed < 140;
        Doing = shield ? "shielding" : fire ? "firing" : thrust ? "closing in" : "aiming";
        return new Controls(turn, thrust, fire, shield);
    }

    private static double Diff(double a, double b) => Math.IEEERemainder(a - b, Math.PI * 2);

    private static bool Exists(GameEngine g, object? t) => t switch
    {
        Shape s => !s.Dead && g.Shapes.Contains(s),
        Saucer u => g.Saucer == u,
        _ => false,
    };

    private static (double X, double Y, double Vx, double Vy, double R) Where(object t) => t switch
    {
        Shape s => (s.X, s.Y, s.Vx, s.Vy, s.Radius),
        Saucer u => (u.X, u.Y, u.Vx, 0, Saucer.Radius),
        _ => default,
    };

    /// <summary>The target it can hit soonest, counting the turn to face it; close and homing ones count for more.</summary>
    private static object? Choose(GameEngine g)
    {
        var k = g.Craft;
        object? best = null;
        double cost = double.MaxValue;
        IEnumerable<object> all = g.Shapes.Cast<object>();
        if (g.Saucer != null) all = all.Append(g.Saucer);
        foreach (var t in all)
        {
            var (x, y, _, _, _) = Where(t);
            double dx = Field.Dx(k.X, x), dy = Field.Dy(k.Y, y), d = Math.Sqrt(dx * dx + dy * dy);
            double c = Math.Abs(Diff(Math.Atan2(dy, dx), k.Angle)) / GameEngine.TurnRate + d / GameEngine.BulletSpeed;
            if (t is Shape { Kind: ShapeKind.Mine or ShapeKind.Spinner }) c *= 0.6;
            if (t is Saucer) c *= 0.7;
            if (d < 160) c *= 0.5;
            if (c < cost) { cost = c; best = t; }
        }
        return best;
    }

    /// <summary>How soon something will touch one of the ships, if it's going to within the next second.</summary>
    private static double? Danger(GameEngine g, List<(double X, double Y)> ships)
    {
        var k = g.Craft;
        double? soonest = null;
        void Check(double x, double y, double vx, double vy, double r)
        {
            foreach (var (sx, sy) in ships)
            {
                double px = Field.Dx(sx, x), py = Field.Dy(sy, y), rvx = vx - k.Vx, rvy = vy - k.Vy;
                double vv = rvx * rvx + rvy * rvy;
                double t = vv < 1e-6 ? 0 : Math.Clamp(-(px * rvx + py * rvy) / vv, 0, 1);
                double cx = px + rvx * t, cy = py + rvy * t;
                if (Math.Sqrt(cx * cx + cy * cy) > r + Craft.Radius + 6) continue;
                // When it first touches, near enough.
                double reach = Math.Max(0, Math.Sqrt(px * px + py * py) - r - Craft.Radius) / Math.Max(1, Math.Sqrt(vv));
                if (soonest is not { } s || reach < s) soonest = reach;
            }
        }
        foreach (var s in g.Shapes) Check(s.X, s.Y, s.Vx, s.Vy, s.Radius);
        foreach (var b in g.Bullets.Where(b => b.Hostile)) Check(b.X, b.Y, b.Vx, b.Vy, 3);
        if (g.Saucer is { } u) Check(u.X, u.Y, u.Vx, 0, Saucer.Radius);
        return soonest;
    }
}
