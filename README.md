# worker

A top-down 2D factory management game for Windows, built in Unity. You run a small
furniture workshop. You do not build robots: you hire people, and every machine you
install replaces someone who has a name.

Codename `worker`. Store title not yet chosen.

## Status

Early, but it builds and runs. The simulation drives a factory end to end, the Unity
player renders it, and both halves of the self-test loop are green. There is no player
interaction yet: you can watch a factory work, but you cannot build in it.

Verified on this machine:

- Windows 64-bit player builds from the Linux Editor: `Worker.exe` plus
  `Worker_Data/`, 97 MB, no errors or warnings.
- Linux player builds, runs headlessly under Xvfb with software rendering, captures
  screenshots and exits by itself.
- 53 unit tests pass, standalone and under Unity's Test Framework.

What works today:

- A deterministic, integer-only simulation of a chair factory: raw logs arrive, get
  sawn into planks, turned into legs and seats, assembled into chairs, and shipped
  against customer contracts.
- Workers with names, stamina, morale and an effective work rate derived from both.
  They path around the floor, haul material, operate benches and take breaks.
- A task scheduler with in-flight reservations, so two workers never chase the same
  crate or overfill the same buffer.
- Conveyor belts. Every tile of belt is a haul trip a worker no longer walks, which is
  the mechanism the game's central trade-off runs on.
- A headless renderer that screenshots the running simulation on a machine with no GPU,
  used to review art direction and catch visual regressions.

## Design

The pitch: Factorio's belts meet RimWorld's people.

Automation games normally make efficiency the only goal, which costs the player
nothing emotionally. Here the workforce is the resource being optimised away. Workers
have names, skills that grow, and morale that drops when a colleague is let go. Every
machine that replaces a workstation forces a choice between retraining someone (slow,
expensive) and laying them off (fast, cheap, and it damages everyone who stays).

Nothing in the current build has AI-generated content in it. All art is procedural
geometry defined in code, which keeps the visual language consistent and makes a
palette or silhouette change a one-line edit.

## Repository layout

```
Assets/Scripts/Core/     Engine-free simulation. No UnityEngine references at all.
Assets/Scripts/Game/     Unity presentation layer.
Assets/Tests/EditMode/   NUnit tests, run both by Unity and standalone.
Tools/CoreTests/         Standalone .NET test project over the same sources.
Tools/Preview/           Headless CPU renderer and self-test harness.
Tools/selftest.sh        One-shot: unit tests, both scenarios, budget checks.
Docs/art-target/         Visual reference board and the current gap list.
```

`Worker.Core` has `noEngineReferences` set. That is deliberate and load-bearing: it is
what lets the entire simulation be tested and rendered without an Editor licence, and
it keeps the model honest about what is game logic and what is presentation.

## Running it

There are two self-tests, and they exist for different reasons.

**The fast one** needs only the .NET 8 SDK: no GPU, no display server, no Unity. It
compiles the simulation standalone and draws it with its own CPU rasteriser, so it runs
anywhere in seconds and is what you use while iterating.

```bash
# Unit tests, both scenarios, screenshots, budget checks.
Tools/selftest.sh

# Just the tests.
dotnet test Tools/CoreTests/Worker.Core.Tests.csproj

# One scenario with screenshots.
dotnet run --project Tools/Preview -c Release -- \
  --scenario automated --seed 7 --workers 6 --ticks 12000 --interval 2000 \
  --out Artifacts/selftest/mine
```

**The real one** builds the actual Unity player, runs it under a virtual display with
software rendering, and captures what the shipping renderer puts on screen. Slower, and
it needs a licensed Editor, but it is the only one that can catch a rendering bug.

```bash
UNITY_EDITOR=$HOME/Unity/Hub/Editor/6000.3.21f1/Editor/Unity \
  Tools/unity-selftest.sh automated
```

It fails the run if the simulation did not advance, if nothing was produced, or if
frames are missing. The first of those is not hypothetical: the player pauses when its
window loses focus, and under a headless X server the window never gains focus, so
without `Application.runInBackground` every frame came out identical at tick zero.

Screenshots land in `<out>/shots/` (fast) or `<out>/` (Unity), metrics in
`metrics.json` / `unity-metrics.json`, and the event stream in `events.tsv`.

## Building

Unity can only build one target per process, so each target is its own invocation.

```bash
UNITY=$HOME/Unity/Hub/Editor/6000.3.21f1/Editor/Unity

$UNITY -batchmode -nographics -quit -projectPath . \
  -buildTarget Win64 \
  -executeMethod Worker.Editor.BuildPipelineEntry.BuildWindows64 \
  -logFile -
```

Output goes to `Artifacts/build/Windows64/`. The player ships one deliberately empty
scene; `Bootstrap` constructs the whole object graph at runtime from a
`RuntimeInitializeOnLoadMethod`, which keeps a merge-hostile `.unity` asset out of the
repository. The build script creates that empty scene if it is missing.

## Determinism

Given the same seed and the same commands, the simulation produces byte-identical
state on every machine. It uses no floating point, no `System.Random` and no
`UnityEngine.Random`; all randomness comes from a seeded xorshift whose state is part
of the save. `StateHash` folds every reproducible field into one value, and the tests
assert that two runs of the same seed agree after thousands of ticks.

This is not a purity exercise. It is what makes a screenshot taken at tick 8000
comparable to the same tick on another machine, which is the entire basis of the
visual regression loop.

## Performance

The self-test measures simulation cost only: tick time percentiles, allocations per
tick, and what fraction of the 20 Hz real-time budget the model consumes. Current
figures are in `Artifacts/selftest/<tag>/metrics.json`.

It reports **no** frame rate. The development machine has no GPU, so any rendering
measurement taken here would be a software-rasteriser number that tells you nothing
about a player's experience. Render performance has to be measured on real Windows
hardware before any claim is made about it.

## Unity licensing on a headless machine

Unity's documentation says Personal licences cannot be activated from the command line
and require signing in through the Unity Hub GUI. That is true of the documented
procedure, but the licensing client Unity ships alongside the Editor exposes the
activation directly:

```bash
LC="$UNITY_DIR/Editor/Data/Resources/Licensing/Client/Unity.Licensing.Client"
"$LC" --activate-ulf --accessToken "$TOKEN"
```

`--activate-ulf` without `--serial` acquires a Personal licence and writes
`~/.local/share/unity3d/Unity/Unity_lic.ulf`. The access token comes from Unity's own
login endpoint, the same one the Hub uses. Note that `--syncEntitlements`,
`--showRemoteEntitlements` and `--activateSession` all report "floating license server
is not configured" and are the wrong path for Personal.

Be aware that the Unity Terms of Service updated 30 June 2026 restrict AI agents from
interacting with the Unity platform except through a framework Unity operates or
designates, and hold the account holder responsible for automated callers acting on
their behalf, with account suspension as the stated consequence. Whether an automated
activation falls under that is the account holder's decision to make, not the build
script's.

For CI, the portable option remains a `Unity_lic.ulf` produced once by a human and
supplied as a secret; the file is not tied to a machine or Unity version.

## Windows builds

Unity cannot cross-compile a Windows IL2CPP player from a Linux Editor; the official
manual states that IL2CPP cross-compilation is unsupported except when targeting
Linux. Windows builds produced on this machine will therefore use the Mono backend.
Mono ships fine on Steam, but the assemblies are ordinary .NET DLLs and are trivially
decompilable. The intended fix is to produce release builds with IL2CPP on a Windows
CI runner, reusing the same licence.

## Publishing

Steam release requires a Steamworks partner account with verified identity, banking
and tax details, plus a 100 USD Steam Direct fee per title. There is a mandatory 30
day gap between paying the fee and releasing, the store page must be public for at
least 14 days before launch, and both the page and the build go through a 3-5 working
day review. Those steps require the account holder and cannot be automated.
