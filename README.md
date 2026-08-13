# Project MONSTER

A from-scratch 3D game for Windows, built in Unity, intended for commercial release on Steam.

This repository starts from nothing: no code, no assets and no design were carried over from
any other project.

---

## Current state

| | |
| --- | --- |
| Phase | Concept selection |
| Market research | ✅ complete — [`docs/research/steam-market-2026.md`](docs/research/steam-market-2026.md) |
| Game concepts | ✅ five written — [`docs/concepts/`](docs/concepts/) |
| Concept art | ✅ five targets — [`docs/concepts/art/`](docs/concepts/art/) |
| Unity toolchain | ✅ installed, licensed, verified on the build machine |
| Unity project | ⏳ created once a concept is chosen |
| Build pipeline | ⏳ designed — [`docs/tech/build-pipeline.md`](docs/tech/build-pipeline.md) |

## Start here

1. [`docs/concepts/README.md`](docs/concepts/README.md) — the five concepts compared, and the recommendation.
2. [`docs/research/steam-market-2026.md`](docs/research/steam-market-2026.md) — the market data the concepts are derived from, with sources and raw captures.
3. [`docs/tech/build-pipeline.md`](docs/tech/build-pipeline.md) — how this gets built and self-checked on a headless, GPU-less machine.

## The recommendation, in one paragraph

Build **Concept A — MONSTER: Containment**, a first-person four-player co-op game about
removing monsters from buildings *alive and undamaged*, and build **Concept E — MONSTER:
Night Shift** first as the vertical slice that proves the engine, the Windows build pipeline
and the automated screenshot self-check. Concept A has the highest commercial ceiling of the
five and is the shape that small teams have repeatedly broken out with in 2026; Concept E has
the highest probability of being finished, is independently shippable, and is the only
concept the GPU-less build machine can fully evaluate. The full reasoning, including why
Concept D is argued against rather than merely ranked last, is in
[`docs/concepts/README.md`](docs/concepts/README.md).

## Layout

```
docs/      research, concepts, concept art, technical plans, progress reports
tools/     environment setup, Steam research probes, build and self-check scripts
game/      the Unity project (not yet created)
```

## Build machine

Ubuntu 24.04, 4 cores, 15 GB RAM, **no GPU**. Unity 6000.5.8f1 with Windows Build Support
(Mono), cross-building a Windows player from Linux, rendering through Mesa llvmpipe under
Xvfb for automated screenshots. The lack of a GPU is a real constraint on art direction and
on final visual sign-off; it is documented rather than worked around in
[`docs/tech/build-pipeline.md`](docs/tech/build-pipeline.md).
