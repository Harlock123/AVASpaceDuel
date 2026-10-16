using AVASpaceDuel.Engine;

namespace AVASpaceDuel.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avaspaceduel-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>A wave in play with its shapes cleared away, calm: nothing moves on its own or comes in.</summary>
    private static GameEngine Game(int wave = 1, bool twin = false, int seed = 7)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true, Calm = true };
        g.Go(wave, twin);
        g.Shapes.Clear();
        // One far off, so the wave isn't over.
        g.PutShape(ShapeKind.Block, 3, 80, 80);
        return g;
    }

    /// <summary>A real game, with plenty of lives.</summary>
    private static GameEngine Real(int wave = 1, bool twin = false, int seed = 7)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Go(wave, twin);
        g.SetLives(99);
        return g;
    }

    private static readonly Controls FireButton = new(Fire: true);

    // ───────────────────────────── the craft ─────────────────────────────

    [Fact]
    public void Space_wraps_round_and_the_short_way_is_across_the_edge()
    {
        Assert.Equal(-20, Field.Dx(10, 1014));
        Assert.Equal(30, Field.Dy(758, 20));
        Assert.Equal(5, Field.WrapX(1029));
        Assert.Equal(763, Field.WrapY(-5));
        var g = Game();
        g.Craft.Angle = 0;
        g.Craft.X = Field.Width - 5;
        g.Craft.Vx = 300;
        g.Idle(10);
        Assert.InRange(g.Craft.X, 0, 60);
    }

    [Fact]
    public void Turning_thrust_drag_and_a_top_speed()
    {
        var g = Game();
        double a = g.Craft.Angle;
        g.Run(30, new Controls(Turn: 1));
        Assert.Equal(a + GameEngine.TurnRate * 0.5, g.Craft.Angle, 3);
        g.Run(300, new Controls(Thrust: true));
        Assert.Equal(GameEngine.MaxSpeed, Math.Sqrt(g.Craft.Vx * g.Craft.Vx + g.Craft.Vy * g.Craft.Vy), 1);
        g.Idle(300);
        Assert.True(Math.Sqrt(g.Craft.Vx * g.Craft.Vx + g.Craft.Vy * g.Craft.Vy) < 40);
    }

    [Fact]
    public void Shots_fly_from_the_nose_and_only_so_many_at_once()
    {
        var g = Game();
        g.Run(1, FireButton);
        var b = Assert.Single(g.Bullets);
        Assert.True(b.Y < g.Craft.Y);
        Assert.Equal(-GameEngine.BulletSpeed, b.Vy, 1);
        g.Run(40, FireButton);
        Assert.Equal(GameEngine.MaxBullets, g.Bullets.Count);
        g.Idle((int)(GameEngine.BulletLife * 60) + 2);
        Assert.Empty(g.Bullets);
    }

    [Fact]
    public void The_twins_swing_round_their_middle_and_both_fire()
    {
        var g = Game(twin: true);
        var ships = g.Craft.Ships.ToList();
        Assert.Equal(2, ships.Count);
        Assert.Equal(Craft.Tether, Field.Distance(ships[0].X, ships[0].Y, ships[1].X, ships[1].Y), 3);
        Assert.Equal(ships[0].Y, ships[1].Y, 3);
        g.Craft.Angle = 0;
        ships = g.Craft.Ships.ToList();
        Assert.Equal(ships[0].X, ships[1].X, 3);
        g.Run(1, FireButton);
        Assert.Equal(2, g.Bullets.Count);
        Assert.All(g.Bullets, b => Assert.Equal(GameEngine.BulletSpeed, b.Vx, 1));
    }

    // ───────────────────────────── the shapes ─────────────────────────────

    /// <summary>A shot straight up at whatever's put above the craft.</summary>
    private static void ShootUp(GameEngine g, int after = 30)
    {
        g.Craft.Angle = -Math.PI / 2;
        g.Run(1, FireButton);
        g.Idle(after);
    }

    [Fact]
    public void A_cube_breaks_in_two_twice_then_goes()
    {
        var g = Game();
        g.Shapes.Clear();
        g.PutShape(ShapeKind.Block, 3, g.Craft.X, g.Craft.Y - 200);
        ShootUp(g, 1);
        g.Idle(20);
        Assert.Equal(2, g.Shapes.Count);
        Assert.All(g.Shapes, s => Assert.Equal(2, s.Size));
        Assert.Equal(20, g.Score);
        // Calm, the pieces stay where they broke: one at a time into the line of fire, the rest out of it.
        void Line(Shape target)
        {
            foreach (var o in g.Shapes) { o.X = 80; o.Y = 700; }
            target.X = g.Craft.X;
            target.Y = g.Craft.Y - 200;
        }
        Line(g.Shapes[0]);
        g.Idle(12);
        ShootUp(g, 30);
        Assert.Equal(3, g.Shapes.Count);
        Assert.Equal(2, g.Shapes.Count(s => s.Size == 1));
        Line(g.Shapes.First(s => s.Size == 1));
        g.Idle(12);
        ShootUp(g, 30);
        Assert.Equal(2, g.Shapes.Count);
        Assert.Equal(20 + 50 + 100, g.Score);
    }

    [Fact]
    public void A_prism_breaks_once_and_spinners_and_mines_just_go()
    {
        var g = Game();
        g.Shapes.Clear();
        g.PutShape(ShapeKind.Prism, 2, g.Craft.X, g.Craft.Y - 200);
        ShootUp(g);
        Assert.Equal(2, g.Shapes.Count);
        Assert.All(g.Shapes, s => Assert.Equal(1, s.Size));
        g.Shapes.Clear();
        g.PutShape(ShapeKind.Spinner, 1, g.Craft.X, g.Craft.Y - 200);
        g.PutShape(ShapeKind.Block, 3, 80, 80);
        g.Idle(12);
        ShootUp(g);
        Assert.Single(g.Shapes);
        Assert.Equal(150 + 300, g.Score);
    }

    [Fact]
    public void A_mine_takes_what_is_near_it_with_it()
    {
        var g = Game();
        g.Shapes.Clear();
        g.PutShape(ShapeKind.Mine, 1, g.Craft.X, g.Craft.Y - 200);
        g.PutShape(ShapeKind.Spinner, 1, g.Craft.X + 45, g.Craft.Y - 210);
        g.PutShape(ShapeKind.Block, 3, 80, 80);
        ShootUp(g);
        Assert.Single(g.Shapes);
        Assert.Equal(200 + 300, g.Score);
    }

    [Fact]
    public void Spinners_drift_after_the_craft_and_mines_home_in()
    {
        var g = Real();
        g.Shapes.Clear();
        g.Craft.Safe = 999;
        var spinner = g.PutShape(ShapeKind.Spinner, 1, g.Craft.X + 300, g.Craft.Y);
        var mine = g.PutShape(ShapeKind.Mine, 1, g.Craft.X - 300, g.Craft.Y);
        g.PutShape(ShapeKind.Block, 3, 60, 60).Vx = 0;
        g.Idle(120);
        Assert.True(spinner.X < g.Craft.X + 300 - 20, $"{spinner.X}");
        Assert.True(mine.X > g.Craft.X - 300 + 40, $"{mine.X}");
    }

    // ───────────────────────────── the craft hit ─────────────────────────────

    [Fact]
    public void A_shape_on_the_ship_ends_the_life_unless_it_is_safe_or_shielded()
    {
        var g = Game();
        g.Craft.Safe = 1;
        var s = g.PutShape(ShapeKind.Block, 2, g.Craft.X, g.Craft.Y);
        g.Idle(1);
        Assert.Equal(Phase.Play, g.Phase);
        g.Craft.Safe = 0;
        // Shielded: it bounces off, and the shield drains.
        s.Vx = -100;
        s.X = g.Craft.X + 30;
        g.Run(30, new Controls(Shield: true));
        Assert.Equal(Phase.Play, g.Phase);
        Assert.True(s.Vx > 0);
        Assert.Equal(GameEngine.ShieldMax - GameEngine.ShieldDrain * 0.5, g.Craft.Shield, 0);
        s.X = g.Craft.X;
        s.Y = g.Craft.Y;
        g.Idle(1);
        Assert.Equal(Phase.Dying, g.Phase);
        Assert.Equal("block", g.Cause);
        g.Idle((int)(GameEngine.DyingSeconds * 60) + 2);
        Assert.Equal(Phase.Play, g.Phase);
        Assert.Equal((Field.CX, Field.CY), (g.Craft.X, g.Craft.Y));
        Assert.True(g.Craft.Safe > 0);
    }

    [Fact]
    public void The_shield_runs_out_and_only_fills_up_again_between_waves()
    {
        var g = Game();
        g.Run(200, new Controls(Shield: true));
        Assert.Equal(0, g.Craft.Shield);
        Assert.False(g.Craft.Shielded);
        g.Idle(300);
        Assert.Equal(0, g.Craft.Shield);
        g.Shapes.Clear();
        g.Idle(2);
        Assert.Equal(Phase.Clear, g.Phase);
        Assert.Equal(GameEngine.ShieldRefill, g.Craft.Shield);
    }

    [Fact]
    public void One_twin_hit_and_the_other_flies_on_alone()
    {
        var g = Game(twin: true);
        var lives = g.Lives;
        var (x, y) = g.Craft.Ships.First();
        var (ox, oy) = g.Craft.Ships.Last();
        g.PutShape(ShapeKind.Spinner, 1, x, y);
        g.Idle(1);
        Assert.Equal(Phase.Play, g.Phase);
        Assert.False(g.Craft.Pair);
        Assert.Equal((ox, oy), (g.Craft.X, g.Craft.Y));
        Assert.Equal(lives, g.Lives);
        g.Shapes.RemoveAll(s => s.Kind == ShapeKind.Spinner);
        g.Craft.Safe = 0;
        g.PutShape(ShapeKind.Spinner, 1, g.Craft.X, g.Craft.Y);
        g.Idle(1);
        Assert.Equal(Phase.Dying, g.Phase);
        Assert.Equal(lives - 1, g.Lives);
        g.Idle((int)(GameEngine.DyingSeconds * 60) + 2);
        Assert.True(g.Craft.Pair);
    }

    [Fact]
    public void The_last_life_ends_the_game()
    {
        var g = Game();
        g.SetLives(1);
        g.PutShape(ShapeKind.Spinner, 1, g.Craft.X, g.Craft.Y);
        g.Idle(1);
        g.Idle((int)(GameEngine.DyingSeconds * 60) + 2);
        Assert.Equal(GameState.GameOver, g.State);
    }

    // ───────────────────────────── the saucer ─────────────────────────────

    [Fact]
    public void The_saucer_comes_across_shooting_and_is_worth_1000()
    {
        var g = Real(2);
        g.Craft.Safe = 999;
        for (int i = 0; i < 60 * 30 && g.Saucer == null; i++) g.Tick(default);
        var u = Assert.IsType<Saucer>(g.Saucer);
        Assert.True(u.X < 0 || u.X > Field.Width);
        for (int i = 0; i < 60 * 5 && !g.Bullets.Any(b => b.Hostile); i++) g.Tick(default);
        Assert.Contains(g.Bullets, b => b.Hostile);

        var h = Game();
        h.PutSaucer(h.Craft.X, h.Craft.Y - 200);
        ShootUp(h);
        Assert.Null(h.Saucer);
        Assert.Equal(Saucer.Points, h.Score);
    }

    [Fact]
    public void A_saucer_shot_hits_the_ship_but_not_through_the_shield()
    {
        var g = Game();
        g.Bullets.Add(new Bullet { X = g.Craft.X - 40, Y = g.Craft.Y, Vx = 330, Life = 1, Hostile = true });
        g.Run(10, new Controls(Shield: true));
        Assert.Equal(Phase.Play, g.Phase);
        Assert.DoesNotContain(g.Bullets, b => b.Hostile);
        g.Bullets.Add(new Bullet { X = g.Craft.X - 40, Y = g.Craft.Y, Vx = 330, Life = 1, Hostile = true });
        g.Idle(10);
        Assert.Equal(Phase.Dying, g.Phase);
        Assert.Equal("saucer", g.Cause);
    }

    // ───────────────────────────── waves ─────────────────────────────

    [Fact]
    public void Each_wave_brings_more_and_more_kinds_of_shape()
    {
        var one = Real(1);
        Assert.Equal(3, one.Shapes.Count);
        Assert.All(one.Shapes, s => Assert.Equal(ShapeKind.Block, s.Kind));
        Assert.Equal(0, one.MinesToCome);
        var three = Real(3);
        Assert.Contains(three.Shapes, s => s.Kind == ShapeKind.Prism);
        Assert.Contains(three.Shapes, s => s.Kind == ShapeKind.Spinner);
        Assert.Equal(2, three.MinesToCome);
        Assert.True(Real(6).Shapes.Count > three.Shapes.Count);
        // And they come in well away from the craft.
        Assert.All(Real(6).Shapes, s => Assert.True(Field.Distance(s.X, s.Y, Field.CX, Field.CY) >= 260));
    }

    [Fact]
    public void Clearing_a_wave_pays_for_the_shield_left_then_on_to_the_next()
    {
        var g = Game();
        g.Shapes.Clear();
        g.Idle(2);
        Assert.Equal(Phase.Clear, g.Phase);
        Assert.Equal((int)GameEngine.ShieldMax * 10, g.Bonus);
        g.Idle((int)(GameEngine.ClearSeconds * 60) + 2);
        Assert.Equal(2, g.Wave);
        Assert.Equal(1000, g.Score);
        Assert.Equal(GameEngine.ShieldMax, g.Craft.Shield);
    }

    [Fact]
    public void An_extra_life_every_10000()
    {
        var g = Game();
        int lives = g.Lives;
        g.SetScore(GameEngine.ExtraLifeEvery - 10);
        g.Shapes.Clear();
        g.PutShape(ShapeKind.Block, 3, g.Craft.X, g.Craft.Y - 200);
        ShootUp(g);
        Assert.Equal(lives + 1, g.Lives);
    }

    // ───────────────────────────── the whole game ─────────────────────────────

    [Fact]
    public void Enter_starts_one_ship_and_T_the_twins()
    {
        var g = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Tick(new Controls(Twin: true));
        Assert.Equal(GameState.Playing, g.State);
        Assert.True(g.Twin);
        Assert.True(g.Craft.Pair);
        var h = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
        h.Tick(default, start: true);
        Assert.False(h.Twin);
        Assert.False(h.Craft.Pair);
    }

    [Fact]
    public void The_title_runs_a_demonstration_then_the_high_scores_and_the_demos_take_turns()
    {
        var g = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
        var modes = new List<bool>();
        for (int round = 0; round < 2; round++)
        {
            g.Idle((int)(GameEngine.TitleIdleSeconds * 60) + 2);
            Assert.Equal(GameState.Attract, g.State);
            modes.Add(g.Twin);
            g.Idle((int)(GameEngine.AttractSeconds * 60) + 2);
            Assert.Equal(GameState.HighScores, g.State);
            g.Idle((int)(GameEngine.HighScoreSeconds * 60) + 2);
            Assert.Equal(GameState.Title, g.State);
        }
        Assert.Equal(new[] { false, true }, modes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_computer_player_clears_the_first_wave(bool twin)
    {
        var g = Real(1, twin);
        for (int i = 0; i < 60 * 90 && g.Wave < 2 && g.State == GameState.Playing; i++) g.Tick(g.Pilot.Decide(g));
        Assert.True(g.Wave >= 2, $"still on wave {g.Wave}, {g.Shapes.Count} shapes left");
    }

    [Fact]
    public void The_same_seed_plays_the_same_game()
    {
        string Play()
        {
            var g = Real(3, seed: 3);
            for (int i = 0; i < 60 * 40; i++) g.Tick(g.Pilot.Decide(g));
            return $"{g.Score} {g.Wave} {g.Lives} {g.Craft.X:0.00} {g.Craft.Y:0.00} {g.Shapes.Count} {g.Phase} {g.Craft.Shield:0.0}";
        }
        Assert.Equal(Play(), Play());
    }

    [Fact]
    public void Pausing_stops_everything()
    {
        var g = Real();
        g.Run(30, new Controls(Thrust: true));
        double y = g.Craft.Y;
        g.Pause();
        g.Idle(120);
        Assert.Equal(y, g.Craft.Y);
        g.Resume();
        Assert.Equal(GameState.Playing, g.State);
    }
}
