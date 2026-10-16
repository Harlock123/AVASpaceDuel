using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVASpaceDuel.Engine;
using AVASpaceDuel.Views;
using Controls = AVASpaceDuel.Engine.Controls;

namespace AVASpaceDuel.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set SPACEDUEL_SHOTS to a folder to
/// save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 11)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avaspaceduel-ui-" + Guid.NewGuid().ToString("N")))) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("SPACEDUEL_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVASpaceDuel-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Lets the computer pilot play until something worth a picture is on screen.</summary>
    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 150, bool holdFire = false)
    {
        for (int t = 0; t < 60 * maxSeconds; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            var move = g.Pilot.Decide(g);
            g.Tick(holdFire ? move with { Fire = false } : move);
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        return false;
    }
    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(2.1);
        Shot(w, c, "title");
        g.HighScores.Insert("ACE", 61850, 9);
        g.HighScores.Insert("AVA", 44200, 7);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        Shot(w, c, "high-scores");
    }

    /// <summary>A wave, and the computer at work in it until something worth showing.</summary>
    private static void Fight(int wave, bool twin, string name, Func<GameEngine, bool> busy, int seed = 3, int seconds = 60)
    {
        var (w, c, g) = Open(seed);
        g.Go(wave, twin);
        g.SetLives(20);
        Assert.True(PlayUntil(g, c, g => g.Phase == Phase.Play && g.Craft.Alive && g.Craft.Safe <= 0 && busy(g), seconds));
        Shot(w, c, name);
    }

    [AvaloniaFact]
    public void One_ship() => Fight(2, false, "solo", g => g.Shapes.Count >= 7 && g.Bullets.Count(b => !b.Hostile) >= 2 && g.Wreckage.Count > 4);

    [AvaloniaFact]
    public void The_twins() => Fight(3, true, "twins", g => g.Craft.Pair && g.Shapes.Count >= 8 && g.Bullets.Count(b => !b.Hostile) >= 4);

    [AvaloniaFact]
    public void Mines() => Fight(5, false, "mines", g => g.Shapes.Count(s => s.Kind == ShapeKind.Mine) >= 2 && g.Shapes.Count >= 8);

    [AvaloniaFact]
    public void The_saucer() => Fight(4, true, "saucer", g => g.Saucer is { } u && u.X > 120 && u.X < 900 && g.Bullets.Any(b => b.Hostile));

    [AvaloniaFact]
    public void The_shield() => Fight(6, false, "shield", g => g.Craft.Shielded && g.Shapes.Any(s => Field.Distance(s.X, s.Y, g.Craft.X, g.Craft.Y) < 90), seconds: 120);

    [AvaloniaFact]
    public void A_wave_cleared()
    {
        var (w, c, g) = Open(4);
        g.Go(2, true);
        g.SetLives(20);
        Assert.True(PlayUntil(g, c, g => g.Phase == Phase.Clear && g.PhaseTime > 0.5, 90));
        Shot(w, c, "cleared");
    }
}
