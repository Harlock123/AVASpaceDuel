using AVASpaceDuel.Engine;

namespace AVASpaceDuel.Tests;

/// <summary>How the computer player gets on, wave by wave (set SPACEDUEL_PROBE to run it, for tuning: "1" for one ship, "twin" for the twins, "lazy" for one that just spins and fires).</summary>
public sealed class PilotProbe(ITestOutputHelper output)
{
    [Fact]
    public void How_the_computer_plays()
    {
        string? mode = Environment.GetEnvironmentVariable("SPACEDUEL_PROBE");
        if (mode is null) return;
        foreach (int seed in new[] { 1, 2, 3 })
        {
            var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "spaceduel-probe-" + Guid.NewGuid().ToString("N")))) { Silent = true };
            g.StartGame(twin: mode == "twin");
            g.SetLives(99);
            var deaths = new Dictionary<string, int>();
            var waves = new List<string>();
            var doing = new Dictionary<string, int>();
            var last = g.Phase;
            int wave = 1, t = 0, halves = 0;
            bool pair = g.Craft.Pair;
            double start = 0;
            for (; t < 60 * 900 && g.Wave <= 8 && g.State == GameState.Playing; t++)
            {
                var c = mode == "lazy" ? new Controls(Turn: 1, Fire: true, Thrust: t % 300 < 30) : g.Pilot.Decide(g);
                g.Tick(c);
                if (g.Phase == Phase.Play) doing[g.Pilot.Doing] = doing.GetValueOrDefault(g.Pilot.Doing) + 1;
                if (g.Phase == Phase.Dying && last != Phase.Dying) deaths[$"W{g.Wave} {g.Cause}"] = deaths.GetValueOrDefault($"W{g.Wave} {g.Cause}") + 1;
                if (pair && !g.Craft.Pair && g.Craft.Alive) halves++;
                pair = g.Craft.Pair;
                if (g.Wave != wave) { waves.Add($"W{wave}:{(t - start) / 60:0}s"); wave = g.Wave; start = t; }
                last = g.Phase;
            }
            output.WriteLine($"seed {seed}: wave {g.Wave} after {t / 60:0}s score {g.Score} | {string.Join(" ", waves)}");
            output.WriteLine("   deaths " + string.Join(", ", deaths.Select(d => $"{d.Key} x{d.Value}")) + (halves > 0 ? $" | twin halves lost {halves}" : ""));
            output.WriteLine($"   shapes left {g.Shapes.Count} lives {g.Lives} shield {g.Craft.Shield:0}");
            output.WriteLine("   doing " + string.Join(", ", doing.OrderByDescending(d => d.Value).Select(d => $"{d.Key} {d.Value / 60}s")));
        }
    }
}
