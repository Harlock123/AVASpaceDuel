# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers playing the game, its controls and
what the tests check; this file covers how the code is put together and the rules that keep it working.

## What this is

A colour vector shooter in the style of Atari's 1982 arcade game Space Duel, in C# on .NET 10 and Avalonia 12,
drawn through Avalonia's `DrawingContext` as glowing wireframe lines. Fly one ship, or twin ships on a tether. One self-contained
executable per platform (Windows, macOS and Linux, x64 and ARM). Everything in it is original: the shapes are
drawn in code, and the sounds and tunes are synthesised by `tools/make_sounds.py`.

## Commands

```bash
dotnet build AVASpaceDuel.slnx                    # the SDK version is in global.json (10.0.400 or later)
dotnet test AVASpaceDuel.slnx                     # every test runs headless: no window opens
dotnet run --project src/AVASpaceDuel             # play it; --no-audio for silence
SPACEDUEL_SHOTS=$PWD/Images dotnet test tests/AVASpaceDuel.Tests --filter ScreenshotTests   # refresh the README's pictures
SPACEDUEL_PROBE=1 dotnet test tests/AVASpaceDuel.Tests --filter PilotProbe   # how the computer player gets on (for tuning)
python3 tools/make_sounds.py                      # rebuild src/AVASpaceDuel/Sounds (standard-library Python only)
```

`PilotProbe` does nothing unless `SPACEDUEL_PROBE` is set: `1` for one ship, `twin` for the twins, `lazy` for a
player that just spins and fires.

`Directory.Build.props` sets `TreatWarningsAsErrors`, nullable reference types and implicit usings for every
project: a warning fails the build, so keep it at zero.

## Layout

- `src/AVASpaceDuel/Engine/`: the game, with no Avalonia drawing in it.
  - `GameEngine.cs` runs the states (title, demonstration, play, high scores) and a wave's phases.
  - `GameEngine.Play.cs` steps a wave: the ship or the tethered twins, the shield, the shapes and how they
    break, mines, the saucer, and the scoring.
  - `Entities.cs` has the `Controls` record, the wrapping `Field` and its fixed step, and the craft, bullets,
    shapes, saucer and debris.
  - `ShipPilot.cs` is the computer player, used by the attract mode and the tests.
  - `InputHandler.cs` (keys) and `ControllerManager.cs` (SDL2 gamepads) produce `Controls`.
  - `HighScoreTable.cs`, `InitialsEntry.cs` and `SoundManager.cs` do what their names say.
- `src/AVASpaceDuel/Views/`: `GameCanvas` (split over partial files) draws the frame and runs the 60 Hz timer.
- `src/AVASpaceDuel/Audio/`: the OpenAL Soft audio engine, shared with the author's other AVA games.
- `tests/AVASpaceDuel.Tests/`: xUnit v3 with `Avalonia.Headless.XUnit`: `GameplayTests`, `ScreenshotTests`,
  `ShiftReleaseTests` and `PilotProbe`.

## Rules that keep it working

- **Fixed steps.** `GameEngine.Update` advances in fixed 1/60 s steps (`Field.Step`), independent of the frame
  rate, so a seed always plays the same game and tests can play whole waves deterministically. Draw random
  numbers from the engine's seeded `_rng` (reseeded for each new game), never from `Random.Shared`.
- **Engine first, drawing second.** Gameplay belongs in `Engine/`, where tests can reach it without a window.
  `Views/` only reads the engine and draws it. Tests that want still shapes and no attackers set the internal
  `Calm` flag.
- **Input goes through `InputHandler`.** Keys and stick boil down to `Controls(Turn, Thrust, Fire, Shield, Twin)`:
  negative `Turn` is left, up (or the stick's negative Y) is thrust and down is the shield. Test new controls
  through `InputHandler`, not by setting `Controls` directly.
  - Keys are tracked by `PhysicalKey`, because on some Linux setups Avalonia reports the release of Shift (a
    shield key) as CapsLock. `ShiftReleaseTests` covers this.
- **SDL under one lock.** `ControllerManager` starts, polls and stops SDL only inside `lock (Sdl)`. The tests
  create many handlers in parallel, and unlocked SDL calls corrupt its heap.
- **Audio.** OpenAL Soft through Silk.NET. `Directory.Build.targets` swaps in the official Windows `soft_oal.dll`
  from `Audio/native/`, which needs no Visual C++ runtime. In a virtual machine `OpenAlConfig` writes a larger
  mixing period to `ALSOFT_CONF` so the sound doesn't break up.
- **Tests never touch the player's files.** High scores live in the app-data folder (`AVASpaceDuel`); tests
  load the table from a temporary folder (`TempFolder()`) and run silent (`Silent = true`).
- **Line endings.** Keep each file's existing line endings (`app.manifest` is CRLF) so diffs show only real
  changes.

## Making a change

- Add or update tests with every gameplay change. `dotnet test` must pass, with no warnings.
- If the change shows on screen, refresh the screenshots with the command above, and look at them.
- Update `README.md` (features, controls, the test list, the project structure) to match.
- New sounds go into `tools/make_sounds.py`, not as hand-edited WAV files.

## CI and releases

`.github/workflows/build.yml` builds and tests every push. It then publishes self-contained single-file builds for
win-x64/x86/arm64, linux-x64/arm64/arm and osx-x64/arm64. Every run keeps the builds as workflow artifacts.
Only pushes to `main` replace the assets of the `latest` pre-release, and a `v*` tag makes a proper release.
