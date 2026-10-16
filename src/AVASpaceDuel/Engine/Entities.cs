namespace AVASpaceDuel.Engine;

/// <summary>
/// The controls: turning (-1 left, 1 right), thrust, fire and shield; and, on the title, the button for a game
/// with the twin ships.
/// </summary>
public readonly record struct Controls(int Turn = 0, bool Thrust = false, bool Fire = false, bool Shield = false, bool Twin = false);

/// <summary>Space: the whole screen, wrapping round at the edges.</summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768;
    public const double CX = Width / 2, CY = Height / 2;

    /// <summary>The shortest way from one coordinate to another, round the wrap if that's shorter.</summary>
    public static double Delta(double from, double to, double size)
    {
        double d = to - from;
        if (d > size / 2) d -= size;
        else if (d < -size / 2) d += size;
        return d;
    }

    public static double Dx(double from, double to) => Delta(from, to, Width);
    public static double Dy(double from, double to) => Delta(from, to, Height);
    public static double Distance(double ax, double ay, double bx, double by) => Math.Sqrt(Dx(ax, bx) * Dx(ax, bx) + Dy(ay, by) * Dy(ay, by));

    public static double WrapX(double x) => (x % Width + Width) % Width;
    public static double WrapY(double y) => (y % Height + Height) % Height;
}

/// <summary>
/// The player's craft: one ship, or two joined by a tether, each at one end of it and both facing the same way.
/// Turning swings the pair round their middle; thrust drives them along the way they face.
/// </summary>
public sealed class Craft
{
    public double X = Field.CX, Y = Field.CY, Vx, Vy, Angle = -Math.PI / 2;
    /// <summary>Two ships on a tether (else one).</summary>
    public bool Pair;
    public bool Alive = true, Thrusting, Shielded;
    public double Shield = 100, Safe, FireIn;
    public const double Tether = 110, Radius = 13;

    /// <summary>The ships, where they are: one in the middle, or one at each end of the tether.</summary>
    public IEnumerable<(double X, double Y)> Ships
    {
        get
        {
            if (!Alive) yield break;
            if (!Pair) { yield return (X, Y); yield break; }
            double px = -Math.Sin(Angle) * Tether / 2, py = Math.Cos(Angle) * Tether / 2;
            yield return (Field.WrapX(X + px), Field.WrapY(Y + py));
            yield return (Field.WrapX(X - px), Field.WrapY(Y - py));
        }
    }
}

public sealed class Bullet
{
    public double X, Y, Vx, Vy, Life;
    /// <summary>Fired by the saucer, at the craft.</summary>
    public bool Hostile;
}

public enum ShapeKind
{
    /// <summary>A wireframe cube: breaks into two smaller, and those into two smaller still.</summary>
    Block,
    /// <summary>A spinning triangular prism: breaks once into two shards.</summary>
    Prism,
    /// <summary>A pinwheel that drifts after the craft.</summary>
    Spinner,
    /// <summary>A pulsing mine that homes in, faster and faster.</summary>
    Mine,
}

public sealed class Shape
{
    public ShapeKind Kind;
    /// <summary>3 (whole) down to 1 for blocks, 2 or 1 for prisms; 1 for the rest.</summary>
    public int Size = 1;
    public double X, Y, Vx, Vy, Spin, Angle, Tilt, T;
    public bool Dead;

    public double Radius => Kind switch
    {
        ShapeKind.Block => Size switch { 3 => 34, 2 => 22, _ => 13 },
        ShapeKind.Prism => Size == 2 ? 30 : 16,
        ShapeKind.Spinner => 18,
        _ => 11,
    };

    public int Points => Kind switch
    {
        ShapeKind.Block => Size switch { 3 => 20, 2 => 50, _ => 100 },
        ShapeKind.Prism => Size == 2 ? 150 : 250,
        ShapeKind.Spinner => 300,
        _ => 200,
    };
}

/// <summary>The saucer: across the screen, weaving, and shooting at the craft.</summary>
public sealed class Saucer
{
    public double X, Y, Vx, BaseY, T, FireIn = 1.2;
    public const double Radius = 18;
    public const int Points = 1000;
}

/// <summary>A broken-off line, spinning away.</summary>
public sealed class Debris
{
    public double X, Y, Vx, Vy, Angle, Spin, Length, Age, Life = 1;
    public uint Colour;
}

public enum EffectKind { Score, Burst, Word }

public sealed class Effect
{
    public EffectKind Kind;
    public double X, Y, Age, Life = 0.8, Size = 1;
    public string Text = "";
    public uint Colour = 0xFFFFFFFF;
    public bool Done => Age >= Life;
}
