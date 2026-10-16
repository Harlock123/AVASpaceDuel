namespace AVASpaceDuel.Engine;

public sealed partial class GameEngine
{
    public const double TurnRate = 4.6, ThrustPower = 430, Drag = 0.55, MaxSpeed = 430;
    public const double BulletSpeed = 720, BulletLife = 0.85, FireGap = 0.16;
    public const int MaxBullets = 4;
    public const double ShieldMax = 100, ShieldDrain = 60, ShieldRefill = 30;
    public const double StartSeconds = 1.8, DyingSeconds = 2.4, SafeSeconds = 2.5, ClearSeconds = 2.8;
    public const int ExtraLifeEvery = 10000;

    /// <summary>The wave: 1 for the first.</summary>
    public int Wave { get; private set; } = 1;
    public int Score { get; private set; }
    public int Lives { get; private set; }
    /// <summary>A game with the twin ships on their tether.</summary>
    public bool Twin { get; private set; }
    public Craft Craft { get; } = new();
    public List<Bullet> Bullets { get; } = [];
    public List<Shape> Shapes { get; } = [];
    public Saucer? Saucer { get; private set; }
    public List<Debris> Wreckage { get; } = [];
    public ShipPilot Pilot { get; } = new();
    public string Cause { get; private set; } = "";
    public int Bonus { get; private set; }
    /// <summary>Mines still to come in this wave.</summary>
    public int MinesToCome { get; private set; }
    /// <summary>For tests: the shapes hold still, no mines or saucer come, and nothing shoots.</summary>
    internal bool Calm { get; set; }

    private int _nextExtra;
    private double _mineIn, _saucerIn, _thrustSound;

    public double ShapeSpeed => Math.Min(2.2, 1.25 + 0.1 * (Wave - 1));
    public double MineSpeed => Math.Min(230, 140 + 10 * (Wave - 1));
    public double SaucerEvery => Math.Max(9, 24 - 2 * (Wave - 1));

    private void NewGame()
    {
        _rng = new Random(_seed);
        Score = 0;
        Lives = 3;
        Wave = 1;
        _nextExtra = ExtraLifeEvery;
        Craft.Shield = ShieldMax;
        Wreckage.Clear();
        Respawn();
        SetPhase(Phase.Start);
    }

    /// <summary>The craft back in the middle, still, and safe for a moment.</summary>
    private void Respawn()
    {
        var c = Craft;
        c.X = Field.CX;
        c.Y = Field.CY;
        c.Vx = c.Vy = 0;
        c.Angle = -Math.PI / 2;
        c.Pair = Twin;
        c.Alive = true;
        c.Shielded = c.Thrusting = false;
        c.Safe = SafeSeconds;
        Bullets.RemoveAll(b => b.Hostile);
    }

    /// <summary>A wave: its shapes come in from the edges, away from the craft.</summary>
    private void BeginWave(int wave)
    {
        Wave = wave;
        Shapes.Clear();
        Bullets.Clear();
        Saucer = null;
        int blocks = Math.Min(7, 3 + wave / 2), prisms = wave >= 2 ? Math.Min(4, 1 + wave / 3) : 0, spinners = wave >= 2 ? Math.Min(5, wave / 2) : 0;
        for (int i = 0; i < blocks; i++) Add(ShapeKind.Block, 3);
        for (int i = 0; i < prisms; i++) Add(ShapeKind.Prism, 2);
        for (int i = 0; i < spinners; i++) Add(ShapeKind.Spinner, 1);
        MinesToCome = wave >= 3 ? Math.Min(10, wave - 1) : 0;
        _mineIn = 6;
        _saucerIn = wave >= 2 ? SaucerEvery * 0.6 : double.MaxValue;
        Craft.Safe = Math.Max(Craft.Safe, 1);
        SetPhase(Phase.Start);
        Say($"WAVE {wave}", Twin ? "TWIN SHIPS" : null, StartSeconds + 0.3);
        Play(Sfx.WaveStart);
    }

    /// <summary>A shape at an edge, well away from the craft, drifting in.</summary>
    private Shape Add(ShapeKind kind, int size)
    {
        double x, y;
        do
        {
            if (_rng.Next(2) == 0) { x = _rng.NextDouble() * Field.Width; y = _rng.Next(2) == 0 ? 10 : Field.Height - 10; }
            else { x = _rng.Next(2) == 0 ? 10 : Field.Width - 10; y = _rng.NextDouble() * Field.Height; }
        } while (Field.Distance(x, y, Craft.X, Craft.Y) < 260);
        double a = _rng.NextDouble() * Math.PI * 2, v = (kind == ShapeKind.Prism ? 70 : 50) * ShapeSpeed * (0.7 + _rng.NextDouble() * 0.6);
        var s = new Shape
        {
            Kind = kind, Size = size, X = x, Y = y, Vx = Math.Cos(a) * v, Vy = Math.Sin(a) * v,
            Spin = (_rng.NextDouble() - 0.5) * 3, Angle = _rng.NextDouble() * 6, Tilt = _rng.NextDouble() * 6,
        };
        Shapes.Add(s);
        return s;
    }

    private void Simulate(double dt, Controls c)
    {
        MoveDebris(dt);
        switch (Phase)
        {
            case Phase.Start:
                Fly(dt, c);
                MoveShapes(dt);
                if (PhaseTime >= StartSeconds) SetPhase(Phase.Play);
                break;
            case Phase.Play:
                Fly(dt, c);
                Fire(dt, c);
                MoveShapes(dt);
                MoveBullets(dt);
                Reinforce(dt);
                MoveSaucer(dt);
                Hits();
                if (Phase == Phase.Play && Shapes.Count == 0 && MinesToCome == 0 && Saucer == null) Clear();
                break;
            case Phase.Dying:
                MoveShapes(dt);
                MoveBullets(dt);
                MoveSaucer(dt);
                if (PhaseTime >= DyingSeconds)
                {
                    if (Lives <= 0) { GameOver(); return; }
                    Respawn();
                    SetPhase(Phase.Play);
                }
                break;
            case Phase.Clear:
                Fly(dt, c);
                MoveBullets(dt);
                if (PhaseTime >= 1 && PhaseTime - dt < 1 && Bonus > 0)
                {
                    Score += Bonus;
                    CheckExtra();
                }
                if (PhaseTime >= ClearSeconds) BeginWave(Wave + 1);
                break;
        }
        PhaseTime += dt;
    }

    // ───────────────────────────── the craft ─────────────────────────────

    private void Fly(double dt, Controls c)
    {
        var k = Craft;
        if (!k.Alive) return;
        k.Safe = Math.Max(0, k.Safe - dt);
        k.Angle += c.Turn * TurnRate * (k.Pair ? 0.85 : 1) * dt;
        k.Thrusting = c.Thrust;
        if (c.Thrust)
        {
            k.Vx += Math.Cos(k.Angle) * ThrustPower * dt;
            k.Vy += Math.Sin(k.Angle) * ThrustPower * dt;
            if ((_thrustSound -= dt) <= 0) { _thrustSound = 0.25; Play(Sfx.Thrust); }
        }
        k.Vx *= 1 - Drag * dt;
        k.Vy *= 1 - Drag * dt;
        double v = Math.Sqrt(k.Vx * k.Vx + k.Vy * k.Vy);
        if (v > MaxSpeed) { k.Vx *= MaxSpeed / v; k.Vy *= MaxSpeed / v; }
        k.X = Field.WrapX(k.X + k.Vx * dt);
        k.Y = Field.WrapY(k.Y + k.Vy * dt);
        bool was = k.Shielded;
        k.Shielded = c.Shield && k.Shield > 0 && Phase == Phase.Play;
        if (k.Shielded)
        {
            // Two bubbles cost more than one.
            k.Shield = Math.Max(0, k.Shield - ShieldDrain * (k.Pair ? 1.7 : 1) * dt);
            if (!was) Play(Sfx.Shield);
        }
    }

    /// <summary>Every ship fires at once, straight ahead, while it has shots to spare.</summary>
    private void Fire(double dt, Controls c)
    {
        var k = Craft;
        k.FireIn -= dt;
        if (!c.Fire || k.FireIn > 0 || !k.Alive) return;
        int ships = k.Ships.Count();
        if (Bullets.Count(b => !b.Hostile) + ships > MaxBullets * ships) return;
        k.FireIn = FireGap;
        double fx = Math.Cos(k.Angle), fy = Math.Sin(k.Angle);
        foreach (var (x, y) in k.Ships)
            Bullets.Add(new Bullet { X = Field.WrapX(x + fx * 16), Y = Field.WrapY(y + fy * 16), Vx = fx * BulletSpeed + k.Vx, Vy = fy * BulletSpeed + k.Vy, Life = BulletLife });
        Play(Sfx.Fire);
    }

    // ───────────────────────────── the enemy ─────────────────────────────

    private void MoveShapes(double dt)
    {
        var k = Craft;
        foreach (var s in Shapes)
        {
            s.T += dt;
            s.Angle += s.Spin * dt;
            s.Tilt += s.Spin * 0.7 * dt;
            if (!Calm && k.Alive)
            {
                double dx = Field.Dx(s.X, k.X), dy = Field.Dy(s.Y, k.Y), d = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
                if (s.Kind == ShapeKind.Spinner)
                {
                    // Drifts after the craft, slowly.
                    double want = (60 + 8 * Wave) * ShapeSpeed * 0.8;
                    s.Vx += (dx / d * want - s.Vx) * 0.6 * dt;
                    s.Vy += (dy / d * want - s.Vy) * 0.6 * dt;
                }
                else if (s.Kind == ShapeKind.Mine)
                {
                    // Homes in, faster the longer it's out.
                    double want = MineSpeed * Math.Min(1, 0.45 + s.T * 0.06);
                    s.Vx += (dx / d * want - s.Vx) * 1.4 * dt;
                    s.Vy += (dy / d * want - s.Vy) * 1.4 * dt;
                }
            }
            if (Calm) continue;
            s.X = Field.WrapX(s.X + s.Vx * dt);
            s.Y = Field.WrapY(s.Y + s.Vy * dt);
        }
    }

    /// <summary>Mines come in through the wave, and the saucer now and then.</summary>
    private void Reinforce(double dt)
    {
        if (Calm) return;
        if (MinesToCome > 0 && (_mineIn -= dt) <= 0)
        {
            _mineIn = Math.Max(2.2, 4.5 - 0.2 * Wave);
            MinesToCome--;
            Add(ShapeKind.Mine, 1).Spin = 4;
        }
        if (Saucer == null && (_saucerIn -= dt) <= 0)
        {
            _saucerIn = SaucerEvery;
            bool left = _rng.Next(2) == 0;
            double y = 120 + _rng.NextDouble() * (Field.Height - 240);
            Saucer = new Saucer { X = left ? -20 : Field.Width + 20, Y = y, BaseY = y, Vx = (left ? 1 : -1) * (110 + 6 * Wave) };
            Music(true);
        }
    }

    private void MoveSaucer(double dt)
    {
        if (Saucer is not { } u) return;
        u.T += dt;
        u.X += u.Vx * dt;
        u.Y = u.BaseY + Math.Sin(u.T * 1.6) * 70;
        if (u.X < -40 || u.X > Field.Width + 40) { Saucer = null; Music(false); return; }
        if (!Calm && Phase == Phase.Play && Craft.Alive && (u.FireIn -= dt) <= 0)
        {
            u.FireIn = Math.Max(0.8, 1.6 - 0.07 * Wave);
            // At a ship, a little off: closer as the waves go on.
            var (sx, sy) = Craft.Ships.OrderBy(s => Field.Distance(u.X, u.Y, s.X, s.Y)).First();
            double a = Math.Atan2(Field.Dy(u.Y, sy), Field.Dx(u.X, sx)) + (_rng.NextDouble() - 0.5) * Math.Max(0.1, 0.5 - 0.04 * Wave);
            Bullets.Add(new Bullet { X = u.X, Y = u.Y, Vx = Math.Cos(a) * 330, Vy = Math.Sin(a) * 330, Life = 1.6, Hostile = true });
            Play(Sfx.SaucerShot);
        }
    }

    private void MoveBullets(double dt)
    {
        foreach (var b in Bullets)
        {
            b.Life -= dt;
            b.X = Field.WrapX(b.X + b.Vx * dt);
            b.Y = Field.WrapY(b.Y + b.Vy * dt);
        }
        Bullets.RemoveAll(b => b.Life <= 0);
    }

    // ───────────────────────────── hits ─────────────────────────────

    private void Hits()
    {
        foreach (var b in Bullets.Where(b => !b.Hostile && b.Life > 0).ToList())
        {
            var s = Shapes.FirstOrDefault(s => !s.Dead && Field.Distance(b.X, b.Y, s.X, s.Y) < s.Radius + 3);
            if (s != null)
            {
                b.Life = 0;
                Break(s, b.Vx, b.Vy);
                continue;
            }
            if (Saucer is { } u && Field.Distance(b.X, b.Y, u.X, u.Y) < Saucer.Radius + 3)
            {
                b.Life = 0;
                Award(Saucer.Points, u.X, u.Y);
                Shatter(u.X, u.Y, 0xFFFF5AD8, 14, 1.4);
                Play(Sfx.SaucerDown);
                Saucer = null;
                Music(false);
            }
        }
        Shapes.RemoveAll(s => s.Dead);
        Bullets.RemoveAll(b => b.Life <= 0);
        var k = Craft;
        if (!k.Alive) return;
        foreach (var (x, y) in k.Ships.ToList())
        {
            foreach (var s in Shapes.ToList())
            {
                if (s.Dead) continue;
                double d = Field.Distance(x, y, s.X, s.Y);
                if (d > s.Radius + Craft.Radius) continue;
                if (k.Shielded)
                {
                    // Bounced off.
                    double nx = Field.Dx(x, s.X) / Math.Max(1, d), ny = Field.Dy(y, s.Y) / Math.Max(1, d);
                    double along = s.Vx * nx + s.Vy * ny;
                    if (along < 0) { s.Vx -= 2 * along * nx; s.Vy -= 2 * along * ny; }
                    s.X = Field.WrapX(x + nx * (s.Radius + Craft.Radius + 1));
                    s.Y = Field.WrapY(y + ny * (s.Radius + Craft.Radius + 1));
                    if (s.Kind == ShapeKind.Mine) { s.Dead = true; Blast(s); }
                    continue;
                }
                if (k.Safe > 0) continue;
                if (s.Kind == ShapeKind.Mine) { s.Dead = true; Blast(s); }
                Hit(x, y, s.Kind.ToString().ToLowerInvariant());
                Shapes.RemoveAll(o => o.Dead);
                return;
            }
            foreach (var b in Bullets.Where(b => b.Hostile && b.Life > 0))
            {
                if (Field.Distance(x, y, b.X, b.Y) > Craft.Radius + 2) continue;
                b.Life = 0;
                if (k.Shielded || k.Safe > 0) continue;
                Hit(x, y, "saucer");
                return;
            }
            if (Saucer is { } u && Field.Distance(x, y, u.X, u.Y) < Saucer.Radius + Craft.Radius && !k.Shielded && k.Safe <= 0)
            {
                Hit(x, y, "saucer");
                return;
            }
        }
        Shapes.RemoveAll(o => o.Dead);
    }

    /// <summary>A shape shot: blocks and prisms break into smaller pieces flying apart; the rest just go.</summary>
    private void Break(Shape s, double bvx, double bvy)
    {
        s.Dead = true;
        Award(s.Points, s.X, s.Y);
        uint colour = Colour(s.Kind);
        Shatter(s.X, s.Y, colour, 6 + s.Size * 3, 0.9);
        bool splits = (s.Kind == ShapeKind.Block && s.Size > 1) || (s.Kind == ShapeKind.Prism && s.Size > 1);
        if (s.Kind == ShapeKind.Mine) { Blast(s); return; }
        Play(splits ? Sfx.Split : Sfx.Smash);
        if (!splits) return;
        double a = Math.Atan2(bvy, bvx) + Math.PI / 2, v = Math.Sqrt(s.Vx * s.Vx + s.Vy * s.Vy) * 1.3 + 40 * ShapeSpeed;
        foreach (int side in new[] { -1, 1 })
        {
            Shapes.Add(new Shape
            {
                Kind = s.Kind, Size = s.Size - 1, X = s.X, Y = s.Y, T = 0,
                Vx = s.Vx * 0.5 + Math.Cos(a) * side * v, Vy = s.Vy * 0.5 + Math.Sin(a) * side * v,
                Spin = s.Spin * 1.4 + side * 0.8, Angle = s.Angle, Tilt = s.Tilt,
            });
        }
    }

    /// <summary>A mine going off: whatever else is near goes with it.</summary>
    private void Blast(Shape mine)
    {
        Play(Sfx.MineBlast);
        Effects.Add(new Effect { Kind = EffectKind.Burst, X = mine.X, Y = mine.Y, Life = 0.5, Size = 2.2, Colour = 0xFFFFE04A });
        foreach (var o in Shapes.Where(o => !o.Dead && o != mine && Field.Distance(o.X, o.Y, mine.X, mine.Y) < 70).ToList()) Break(o, o.Vx, o.Vy);
    }

    public static uint Colour(ShapeKind kind) => kind switch
    {
        ShapeKind.Block => 0xFF6AE8FF, ShapeKind.Prism => 0xFFFFE04A, ShapeKind.Spinner => 0xFF8AFF6A, _ => 0xFFFF6A4A,
    };

    /// <summary>A ship hit: on its own, the life goes; one of the twins, and the other flies on alone.</summary>
    private void Hit(double x, double y, string cause)
    {
        var k = Craft;
        Cause = cause;
        Shatter(x, y, 0xFFF2F2FF, 16, 1.6);
        Effects.Add(new Effect { Kind = EffectKind.Burst, X = x, Y = y, Life = 0.7, Size = 2, Colour = 0xFFFFFFFF });
        if (k.Pair)
        {
            // The survivor is the other one: the craft carries on as it, alone.
            var other = k.Ships.First(s => Math.Abs(Field.Dx(s.X, x)) + Math.Abs(Field.Dy(s.Y, y)) > 1);
            k.Pair = false;
            k.X = other.X;
            k.Y = other.Y;
            k.Safe = 1;
            Play(Sfx.ShipHit);
            return;
        }
        k.Alive = false;
        k.Shielded = k.Thrusting = false;
        Lives--;
        Play(Sfx.Death);
        SetPhase(Phase.Dying);
        if (Lives <= 0) Say("GAME OVER", null, DyingSeconds);
    }

    private void Clear()
    {
        Bonus = (int)Craft.Shield * 10;
        Craft.Shield = Math.Min(ShieldMax, Craft.Shield + ShieldRefill);
        Bullets.RemoveAll(b => b.Hostile);
        Play(Sfx.WaveClear);
        Say($"WAVE {Wave} CLEARED", Bonus > 0 ? $"SHIELD BONUS {Bonus}" : null, ClearSeconds);
        SetPhase(Phase.Clear);
    }

    private void Award(int points, double x, double y)
    {
        Score += points;
        Effects.Add(new Effect { Kind = EffectKind.Score, X = x, Y = y - 12, Text = points.ToString(), Life = 0.7, Colour = 0xFFFFE14A });
        CheckExtra();
    }

    private void CheckExtra()
    {
        while (Score >= _nextExtra)
        {
            _nextExtra += ExtraLifeEvery;
            Lives++;
            Play(Sfx.ExtraLife);
        }
    }

    private void Shatter(double x, double y, uint colour, int n, double life)
    {
        for (int i = 0; i < n; i++)
        {
            double a = _rng.NextDouble() * Math.PI * 2, v = 40 + _rng.NextDouble() * 170;
            Wreckage.Add(new Debris
            {
                X = x, Y = y, Vx = Math.Cos(a) * v, Vy = Math.Sin(a) * v, Angle = _rng.NextDouble() * 6, Spin = (_rng.NextDouble() - 0.5) * 12,
                Length = 6 + _rng.NextDouble() * 12, Life = life * (0.5 + _rng.NextDouble() * 0.5), Colour = colour,
            });
        }
    }

    private void MoveDebris(double dt)
    {
        foreach (var d in Wreckage)
        {
            d.Age += dt;
            d.X += d.Vx * dt;
            d.Y += d.Vy * dt;
            d.Angle += d.Spin * dt;
        }
        Wreckage.RemoveAll(d => d.Age >= d.Life);
    }

    // ───────────────────────────── test hooks ─────────────────────────────

    internal void Run(int ticks, Controls c)
    {
        for (int i = 0; i < ticks; i++) Tick(c);
    }

    internal void Idle(int ticks) => Run(ticks, default);

    /// <summary>Straight into a wave, past its number.</summary>
    internal void Go(int wave = 1, bool twin = false)
    {
        StartGame(twin);
        if (wave != 1) BeginWave(wave);
        SetPhase(Phase.Play);
        Craft.Safe = 0;
        Message = SubMessage = null;
    }

    internal Shape PutShape(ShapeKind kind, int size, double x, double y)
    {
        var s = new Shape { Kind = kind, Size = size, X = x, Y = y };
        Shapes.Add(s);
        return s;
    }

    internal Saucer PutSaucer(double x, double y)
    {
        Saucer = new Saucer { X = x, Y = y, BaseY = y };
        return Saucer;
    }

    internal void SetLives(int n) => Lives = n;
    internal void SetScore(int n) => Score = n;
}
