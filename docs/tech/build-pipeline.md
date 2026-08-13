# Project MONSTER — Build and Self-Check Pipeline

This document describes how the game is built, run, tested and visually checked on a
headless Linux machine with no GPU, targeting Windows.

Everything below marked ✅ has been executed on this machine and its output observed.
Everything marked ⏳ is designed but not yet run.

---

## 1. Machine facts

Established by direct inspection on 2026-08-13.

| Property | Value |
| --- | --- |
| OS | Ubuntu 24.04.4 LTS, kernel 6.12.94 |
| CPU | 4 cores |
| RAM | 15 GB |
| Disk | 235 GB free |
| GPU | **None.** No `nvidia-smi`, no VGA device. Rendering falls back to Mesa llvmpipe (software). |
| Display | None. Xvfb is used where a display is required. |

The absence of a GPU is the constraint that shapes every other decision here. It does not
prevent building or testing; it means rendered frames are produced by the CPU, so they are
correct but slow, and it means the game's art direction must be one that a software
rasteriser can produce at a usable rate.

---

## 2. Toolchain

| Component | Version / path | Status |
| --- | --- | --- |
| Pillow + numpy | for the screenshot comparison tool | ✅ installed |
| Unity Hub | 3.20.1, from Unity's official apt repository | ✅ installed |
| Unity Editor | **6000.5.8f1**, at `/opt/unity/editors/6000.5.8f1/Editor/Unity` | ✅ installed |
| Windows Build Support (Mono) | Editor module | ✅ installed |
| Unity licence | Unity Personal (ULF), entitlements include `com.unity.editor.headless` | ✅ activated |
| Xvfb | system package | ✅ present |
| ffmpeg | system package | ✅ present (for turning screenshot sequences into clips) |
| git-lfs | system package | ✅ present (for binary art assets) |

Scripts: `tools/env/install_unity.sh`, `tools/env/activate_unity.sh`. Both are idempotent and
read credentials from the environment only — **no credential is ever written into this
repository**.

### 2.1 Editor version choice

`6000.5.8f1` was selected as the newest final (`f1`) release the Hub offered; the Hub also
listed `6000.7.0a4` (alpha), `6000.6.0b7` (beta), `6000.3.22f1` and `6000.0.81f1`. Alphas and
betas are excluded from a project intended to ship.

This choice is **provisional and empirical**: it is kept only for as long as it can produce a
working Windows player. If a Windows Mono build from the Linux editor fails or misbehaves on
6000.5.8f1, the fallback is `6000.0.81f1`, the Unity 6 LTS line, which is the most
battle-tested option available. The decision is recorded here so the reason is not lost.

### 2.2 Scripting backend — a release blocker to be aware of now

The Linux editor can cross-compile a Windows player with the **Mono** scripting backend.
It **cannot** produce a Windows **IL2CPP** player: IL2CPP for Windows requires a Windows host
with the MSVC toolchain.

This is confirmed by inspection rather than taken from documentation. The editor's
`PlaybackEngines/WindowsStandaloneSupport/Variations/` directory contains only
`win64_player_development_mono`, `win64_player_nondevelopment_mono` and their 32-bit and
ARM64 equivalents. There is no IL2CPP variant present to build against.

Consequences, stated plainly:

- Every build produced on this machine is a Mono build. Mono builds run fine on Windows and
  are legitimate for development, testing and internal review.
- Mono assemblies are trivially decompilable and are slower than IL2CPP.
- **A Windows machine is required to produce the release build.** This is not optional and it
  is not a detail. It is tracked as a hard release gate in every concept's milestone list.

---

## 3. Repository layout

```
/
├── docs/
│   ├── research/          market research + raw captured data
│   ├── concepts/          the five game concepts
│   │   └── art/           concept art targets
│   ├── tech/              this document and its successors
│   └── progress/          milestone reports with screenshots
├── tools/
│   ├── env/               Unity install and licence activation
│   ├── research/          Steam data probes
│   ├── build/             headless build entry points
│   └── selfcheck/         screenshot capture, image diffing, metric collection
└── game/                  the Unity project (created once a concept is chosen)
    ├── Assets/
    ├── Packages/
    └── ProjectSettings/
```

`game/Library/`, `game/Temp/`, `game/Build/` and all generated artefacts are git-ignored.
Binary art assets go through git-lfs.

---

## 4. Headless build

Invocation shape:

```bash
xvfb-run -a /opt/unity/editors/6000.5.8f1/Editor/Unity \
  -batchmode -nographics -quit \
  -projectPath /workspace/game \
  -executeMethod Monster.Build.BuildPipeline.BuildWindows64 \
  -logFile /dev/stdout
```

`BuildWindows64` is an editor-only C# method that:

1. Sets the active build target to `StandaloneWindows64` with the Mono backend.
2. Applies a build number and the current git short SHA to `PlayerSettings.bundleVersion`, so
   every artefact is traceable to a commit.
3. Builds to `game/Build/Windows/MONSTER.exe`.
4. Fails the process with a non-zero exit code on any build error — **required**, because
   Unity's default batch-mode behaviour can exit 0 after a failed build, and a pipeline that
   reports success on a failed build is worse than no pipeline.

✅ Executed. The Windows build produces `MONSTER.exe`, a PE32+ x86-64 GUI executable of
94.2 MB, in 40 seconds. The Linux build produces `MONSTER.x86_64` at 89.4 MB in 43 seconds.
`BuildAll` runs both in one session in about two minutes from cold and about 13 seconds
incrementally.

The non-zero-exit requirement was validated the hard way on the first run: a compile error
in `SelfCheckRunner.cs` was correctly caught and reported as a failure, where Unity's default
batch-mode behaviour would have exited 0.

---

## 5. Automated testing

Three layers, deliberately ordered cheapest-first.

**Layer 1 — Edit-mode tests.** Pure logic: rules engines, economy, procedural layout
solvability, save-schema migration. No rendering, no play mode. Seconds to run. This is where
most correctness lives and where most tests should be.

```bash
xvfb-run -a "$UNITY" -runTests -batchmode -projectPath /workspace/game \
  -testPlatform EditMode -testResults /tmp/editmode-results.xml -logFile /dev/stdout
```

**Layer 2 — Headless play-mode tests.** The game loop actually running, with `-nographics`.
Scripted input drives a full session against a fixed seed and the outcome is asserted against
known values. For a networked concept this is where multi-instance host/client tests live.

**Layer 3 — Rendered self-check.** The built Windows player is not runnable here, so the
rendered check runs the *Linux* player built from the same code, under Xvfb with llvmpipe.
This validates scene composition, lighting, materials and UI layout — not Windows-specific
behaviour, which needs a real Windows machine.

### 5.1 Test effectiveness — a standing rule for this project

A test that cannot fail is not a test. For every test that claims to guard a piece of logic,
it must be possible to state which specific change to that logic would turn it red. On
critical paths this is verified by mutation: deliberately break the logic, confirm the test
fails, restore it, confirm the test passes. Assertions derived from a design requirement are
never weakened to make a failing test pass; if the implementation cannot meet the assertion,
that is reported, not papered over.

---

## 6. Screenshot self-check

This is the mechanism the project uses to see the gap between the current build and the
concept art.

**How it works.**

1. The player is launched under Xvfb at a fixed resolution (1920×1080) with the software
   rasteriser, in a special `--selfcheck` mode.
2. A checkpoint script drives the game to a fixed list of named states — for Concept E these
   are `booth_idle`, `document_inspect`, `camera_feed`, `window_silhouette`, `morning_report`
   — pausing at each one.
3. At each checkpoint the harness captures a PNG, plus a metrics record: frame time, triangle
   count, draw calls, allocated memory, and the contents of the error log.
4. Artefacts land in `artifacts/selfcheck/<git-sha>/`.

**What it is compared against.**

- **Against the previous run** — a perceptual image diff. Any change above a threshold is
  surfaced. Small, expected changes get acknowledged; unexplained changes are treated as
  regressions. This catches the classic silent failures: a material that lost its texture, a
  light that stopped baking, a UI element that drifted off screen.
- **Against the concept art target** in `docs/concepts/art/` — not a pixel diff, which would
  be meaningless, but a structured comparison on palette histogram, value distribution,
  and composition, plus a written note on what is missing. The concept art is the target; the
  screenshot is the current state; the note is the work remaining.

**Honest limitation.** A software rasteriser does not produce the same image a GPU does.
Post-processing, shadow filtering and anti-aliasing will differ. The screenshot check
therefore answers "is the scene composed and lit as intended, and has anything broken" — it
does not answer "does the final game look good on a player's machine". That question needs a
Windows box with a GPU, and pretending otherwise would make the whole harness misleading.

**Why the camera matters here.** Concept E's fixed camera makes run-to-run image diffing
genuinely precise. Concepts A and B need explicit screenshot cameras placed at fixed
transforms in a fixed-seed level, or the diff is pure noise. This is a design requirement on
those concepts, not an afterthought.

✅ Implemented and running. `tools/selfcheck/run_selfcheck.sh` launches the player, and
`tools/selfcheck/compare.py` produces the diffs and composites. A full cycle — regenerate the
scene from code, build, run, capture, compare — takes about 25 seconds via
`tools/build/iterate.sh`.

First results are in `docs/progress/m1/`: 40.6 ms/frame at 1280x720 on llvmpipe, 206
renderers, 3,764 triangles, 9 lights, 0 errors.

---

## 7. Progress reporting

Every milestone produces a dated entry in `docs/progress/` containing:

- the self-check screenshots for that build,
- the concept art target beside them,
- a written list of the differences and what is planned about each,
- the metrics record (frame time, memory, triangle count),
- and the test results, including anything that is failing.

This is the mechanism by which the gap between intent and reality stays visible, which is a
stated requirement of the project.

---

## 8. Steam release requirements not yet satisfied

Recorded now so they are not discovered late. None of these are blocked by anything technical
on our side; they all need account-level access we do not currently have.

| Requirement | Status | Note |
| --- | --- | --- |
| Steamworks partner account | ❌ not available | $100 app deposit per title. |
| Steam AppID | ❌ not available | Blocks achievements, cloud saves, Steam lobbies, Steam Voice, and the store page. |
| Steamworks SDK integration | ⏳ design only | Will be built behind an interface with a non-Steam fallback so that development is never blocked on it. |
| IL2CPP Windows release build | ⏳ blocked | Needs a Windows host with MSVC. |
| Code signing certificate | ❌ not available | Unsigned Windows executables trigger SmartScreen warnings. |
| Store page assets (capsules, trailer) | ⏳ not started | Capsule art is the highest-leverage single asset on Steam; it should be commissioned or produced early, not last. |
| Age rating / regional content review | ⏳ not started | Relevant to Concepts A, B and E. |
