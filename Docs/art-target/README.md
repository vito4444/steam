# Visual target board

This is the reference the game's look is measured against, and the record of how far
the current build is from it. It exists because "looks fine" is not a reviewable
statement: every iteration has to produce a per-axis gap list that someone can act on.

## How the comparison is produced

Two harnesses produce frames, and both are compared against the reference works below
on the same seven fixed axes. The axes never change between reviews; only the scores
and the gap list do.

`Tools/selftest.sh` simulates both scenarios with the headless CPU rasteriser and
writes numbered screenshots to `Artifacts/selftest/<tag>/<scenario>/shots/`. It needs
nothing but the .NET SDK, so it is what gets used while iterating. Because it draws
from the same `Worker.Core.Palette` as the Unity renderer, its frames are a valid
stand-in when judging composition, density, value separation and readability. They are
**not** a stand-in for shaders, lighting, particles or animation.

`Tools/unity-selftest.sh` builds the actual Unity player, runs it under Xvfb with
software rendering and captures what the shipping renderer produces. Slower and needs a
licensed Editor, but it is the only source of truth for anything the preview renderer
cannot model. Neither harness says anything about GPU performance.

## Reference works

Chosen because each one solves a specific problem this game also has, not because the
game should look like any of them.

- [Factorio](https://store.steampowered.com/app/427520/) - the standard for reading a
  dense logistics network at a glance. Reference for: belt legibility, how much
  information a single tile can carry before it turns to noise.
- [Fabricatio](https://store.steampowered.com/app/4834620/) - a 2026 pure-2D top-down
  automation game. Reference for: how little visual complexity a factory game can get
  away with while still reading as industrial.
- [2D Factory](https://store.steampowered.com/app/4953940/) - a 2026 cosy top-down
  automation game. Reference for: warmth and approachability, the opposite pole from
  Factorio's density.
- [RimWorld](https://store.steampowered.com/app/294100/) - reference for: making named
  individuals readable as individuals at small pixel sizes, and for surfacing mood and
  need states without a wall of text.
- [Oxygen Not Included](https://store.steampowered.com/app/457140/) - reference for:
  task and priority readouts, and for showing what a worker is currently doing.
- [Mini Motorways](https://store.steampowered.com/app/1127500/) - reference for: how far
  a strictly limited flat palette can carry a whole game's art direction.

## The seven axes

1. **Camera distance and coverage** - can the whole shop floor be understood at once,
   and is a 2x2 bench still large enough to identify by silhouette?
2. **Information density** - what fraction of the screen carries meaning rather than
   empty floor.
3. **Value separation** - are the three palette layers (floor, structures, people)
   distinguishable in greyscale?
4. **State readability** - can a stalled line, a tired worker or a full buffer be
   diagnosed by looking, without opening a panel?
5. **Feedback** - do actions and state changes announce themselves.
6. **UI layout** - is the chrome doing useful work per pixel it occupies.
7. **Style consistency** - does everything look like it comes from one game.

## Current state

Baseline frames from the preview renderer at day 2:
[`baseline/automated-day2.png`](baseline/automated-day2.png) and
[`baseline/starter-day2.png`](baseline/starter-day2.png).

Baseline frame from the real Unity player:
[`baseline/unity-automated-day1.png`](baseline/unity-automated-day1.png), captured at
tick 3160 of the automated scenario under llvmpipe software rendering.

The two renderers agree on layout, palette and belt direction. Where they differ today:
the Unity player draws workers as small figures rather than dots and has no worker
names, no path overlay and no side panel, because those are preview-only debug aids
that the shipping UI has not replaced yet.

| Axis | State | Gap |
| --- | --- | --- |
| Camera distance and coverage | Acceptable | Whole 26x16 floor fits one screen; a 2x2 bench renders at roughly 88px and its glyph is identifiable. No further work needed at this stage. |
| Information density | Below target | Roughly a third of the floor is empty in the automated scenario, and the corners are dead space. The reference works fill nearly every tile. |
| Value separation | Acceptable | Floor tones sit at luminance 47-60, the building body at 84, workers at 232. Pinned by `PaletteTests.LuminanceMatchesTheDocumentedBaselineValues`, and the banding itself is enforced by `PaletteTests.ValueLayersAreSeparated`. That test caught the grid line sitting only 16 below the building body and forced it darker. |
| State readability | Partly there | Work progress, buffer contents, carried items, stamina and the tired-worker colour all read correctly. Missing: any indication of *why* a line has stalled, and belt backpressure is invisible. |
| Feedback | Not started | No particles, no tweening, no impact on craft completion. Static frames cannot evaluate this; it needs the Unity renderer. |
| UI layout | Below target | The top strip works. The right panel is half empty below the roster, and there is no inventory or throughput readout. |
| Style consistency | Acceptable | One palette, one corner radius, one glyph language throughout. |

## Concept target versus the current build

[`comparison/target-vs-current.png`](comparison/target-vs-current.png) puts a concept
frame directly above a real capture of the same game.

A caveat on how to read it. The concept frame is a generated illustration, not a
promise and not a spec. It is a mood reference for density, weight and interface
structure. Some of what it shows is cheap and worth copying, some is expensive, and a
few details are decoration an illustration can afford but a real game cannot: it
implies a clock and a time of day the simulation does not model, worker traits that do
not exist, and a shelf whose contents are individually drawn. Each row below says which
is which.

Ordered by how much each would close the gap, and annotated with what it actually costs.

| Gap | Concept | Current | Cost |
| --- | --- | --- | --- |
| Floor density | Nearly every tile carries something; belts wrap the whole plot | Roughly a third to a half of the floor is bare | Cheap. Shrink the plot again or raise the starting building count. This is the single largest difference and the easiest to fix. |
| Belt weight | Belts are thick, continuous, visibly mechanical, and densely loaded with cargo | Belts are thin, sparse, and often empty | Cheap for the art (wider bed, plated texture, stronger chevrons). The emptiness is a balance problem, not an art problem: throughput is too low to keep belts full. |
| Machine solidity | Machines read as objects with thickness, bevelled highlights and shadowed undersides | Flat rounded squares with a single top highlight | Cheap. A second bevel row and a darker base in `ProceduralSprites`. |
| Shelf contents | Racks visibly hold crates and finished chairs | Racks are uniform grey blocks | Moderate. Requires drawing stored items on the shelf sprite from live inventory. Worth it: it is free information about where the factory's stock actually is. |
| Build bar | Large pictogram buttons, priced, with a visibly locked future item | Small colour swatch plus text | Cheap. Reuse the existing building sprites as button icons. The locked entry also implies a progression system that does not exist yet. |
| Inspector structure | Sectioned into role, bars, traits, status, cargo, then a stack of actions ending in a red destructive one | One text block, two thin bars, one button | Cheap for the layout. The extra actions (set priority, take break, relocate) are new mechanics, not new UI. |
| Worker presence | Figures are larger, outlined, and carry a legible name plate | Small pale figures, name labels disabled | Cheap. Names were switched off because they cluttered at the current zoom; they need a backing plate to work. |
| Palette warmth | Anchored in warm orange and timber brown | Sits cool blue-grey overall | Cheap but risky. Warming the floor is a one-line change; it must not break the value banding the palette tests enforce. |
| Top bar | Iconography, dividers, a grouped set of screen buttons | Plain text pairs | Cheap, but most of those buttons would open screens that do not exist. |
| Content breadth | Thirteen placeable things including splitter, merger, power pole | Seven | Expensive, and gameplay work rather than art. Splitters and mergers in particular would change how belts are routed. |
| Worker traits | Named traits such as Fast Learner and Tidy | No trait system | Expensive. Real design work, and the thing most likely to make workers feel like individuals. |
| Clock and calendar | A time of day alongside the day counter | Day counter only | Cheap to display, but the simulation has no concept of hours; showing one would be a lie. |

## Gap list

Ordered by how much each would move the picture, not by effort. The comparison table
above covers the concept-versus-build differences; these are the remaining issues found
by reviewing captures directly.

1. **Density.** Either shrink the plot further or increase the building count per
   scenario. Empty floor is the single largest difference from every reference work.
2. **Assembly benches look disconnected.** They are fed by hand, so no belt touches
   their input side, and they read as isolated boxes rather than as part of the line.
   Either route input belts to them or make hand-feeding visually explicit.
3. **Belts run mostly empty.** Throughput is low enough that a belt usually carries one
   item or none. This is a balance problem showing up as an art problem, and it makes
   the factory look idle even while it is working.
4. **No environmental detail.** No floor markings, no pipework, no wear. The reference
   works all use incidental detail to make the floor feel built rather than blank.
5. **Palette runs cold.** Everything sits in blue-grey. Factorio and 2D Factory both
   anchor on warm browns and oranges. Worth testing a warmer floor, without breaking the
   value separation the palette tests enforce.
6. **No backpressure signal.** A blocked belt looks identical to a moving one in a
   still frame. Needs a stalled-item indicator.
7. **Feedback layer entirely absent.** No particles, no tweening, no impact on craft
   completion. Static frames cannot evaluate this at all.

## What this board does not cover

GPU-side performance. This machine has no GPU, so frame rate cannot be measured here
and is not claimed anywhere in the self-test output. The self-test asserts CPU-side
simulation cost only; render performance has to be measured on real Windows hardware
before any performance claim is made.
