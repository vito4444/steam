# worker

A top-down 2D factory management game for Windows, built in Unity. You run a small
furniture workshop. You do not build robots: you hire people, and every machine you
install replaces someone who has a name.

Codename `worker`. Store title not yet chosen.

## Status

Early. The simulation is complete enough to run a factory end to end and the visual
self-test loop works, but the Unity presentation layer has not been compiled yet
because the Editor cannot be licensed on this machine. See
[Unity licensing](#unity-licensing) below.

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

Requires the .NET 8 SDK. No GPU, no display server and no Unity installation needed.

```bash
# Everything: unit tests, both scenarios, screenshots, budget checks.
Tools/selftest.sh

# Just the tests.
dotnet test Tools/CoreTests/Worker.Core.Tests.csproj

# A single scenario with screenshots.
dotnet run --project Tools/Preview -c Release -- \
  --scenario automated --seed 7 --workers 6 --ticks 12000 --interval 2000 \
  --out Artifacts/selftest/mine
```

Screenshots land in `<out>/shots/`, metrics in `<out>/metrics.json`, and the event
stream in `<out>/events.tsv`.

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

## Unity licensing

The Unity presentation layer is written but has not been compiled. Unity 6.3 LTS is
installed on the build machine, but the Editor will not start without an activated
licence, and Unity Personal cannot be activated from the command line: the official
documentation states that Personal activation requires signing in through the Unity
Hub GUI.

Additionally, the Unity Terms of Service updated 30 June 2026 restrict AI agents from
interacting with the Unity platform except through a framework Unity operates or
designates, and hold the account holder responsible for automated callers acting on
their behalf. Automating a Hub sign-in from here would breach that, with account
suspension as the stated consequence, so it has not been attempted.

Unblocking this requires a licence file produced by a human signing in to Unity Hub
once on their own machine. The resulting `Unity_lic.ulf` (at
`C:\ProgramData\Unity\Unity_lic.ulf` on Windows, or
`/Library/Application Support/Unity/Unity_lic.ulf` on macOS) can then be supplied to
the build environment as a secret.

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
