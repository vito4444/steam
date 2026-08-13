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

## Camera and art direction: why this moved to isometric 3D

The first renderer was flat orthographic 2D drawn as coloured rectangles. Reviewed
against actual store screenshots rather than descriptions of them, that turned out to
be the wrong end of the problem. Every comparable game that reads well on a store page
has at least three things the flat renderer had none of:

- **Volume.** Factorio is not flat; it is finely drawn sprites with visible thickness,
  wear and a hint of perspective. Timberborn, Two Point Hospital and Against the Storm
  are outright isometric 3D. Even Mini Motorways, the most reductive of them, gives
  every shape a soft drop shadow.
- **A key light and cast shadows.** Shadows are what tell the eye an object stands on
  the floor rather than being painted onto it.
- **Density.** All of them fill the frame with things that mean something.

Flat, unlit, unshadowed and sparse is the hardest combination in which to look good,
and that is precisely where the first renderer sat. The view was therefore rebuilt as
orthographic isometric 3D: low-poly geometry generated from primitives at runtime, one
warm directional key light with soft shadows, cool ambient fill, buildings at visibly
different heights, and a walled shop floor so the plot reads as a room.

Both renderers are still in the build. `--iso` selects the 3D one; without it the flat
one runs. Keeping them side by side is deliberate while the direction is settled.

Three-way comparison, reference against both renderers:
[`comparison/reference-vs-flat-vs-iso.png`](comparison/reference-vs-flat-vs-iso.png).

What the isometric pass fixed: volume, key lighting, cast shadows, a sense of enclosure.

What it did not fix, and what the reference still does far better: density of meaningful
detail, building silhouettes beyond "box with a lump on top", colour saturation, and any
environmental dressing at all. Two Point Hospital fills its frame with furniture,
patients, signage and props; this fills its frame with tinted boxes. That gap is content
and modelling work, not a camera decision.

Bugs the isometric pass surfaced, all of which present as a black screen or a black
surface and none of which are obvious from code:

- Runtime-created materials use `Shader.Find`, so nothing references the URP shaders as
  assets and the build strips them. Every material constructor then throws and the world
  renders black while the interface keeps working.
- Roof details were painted in the near-black edge colour and covered the whole top
  face, so canopied buildings rendered with black roofs under a light that was working
  correctly.
- Shadow distance is measured from the camera. An orthographic rig pulled back 80 units
  with a 60 unit shadow distance culls the shadows of everything it is looking at.

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

## What a video review found that screenshots hid

An 18 second capture of the running game was reviewed frame by frame
([`video/worker-dusk.mp4`](video/worker-dusk.mp4)). Three effects that are
implemented and running turned out to be invisible, which no still frame would
ever have revealed.

Confirmed working: cargo moving along belts, workers walking the floor, status
lamps switching between green and amber, warm pools of point light on the floor.

Implemented but invisible, and why:

- **Belt surface scrolling.** The belt is textured with brushed metal, whose
  striations run along the belt's own axis. Scrolling a horizontally striped
  texture horizontally produces no perceivable motion. The fix is a texture with
  cross-belt features, not more scroll speed.
- **Spinning saw blade.** A smooth grey disc has rotational symmetry, so it looks
  identical at every angle. It needs teeth, spokes or an off-centre mark before
  rotation reads at all.
- **Spinning lathe spindle.** Same problem: a plain cylinder turning about its own
  axis is indistinguishable from a stationary one.

The general lesson is worth keeping: motion is only visible on features that
break the symmetry of the axis they move along. Animating a symmetric object is
wasted work.

Also flagged, and not yet addressed: workers slide rather than walk, workers pass
through each other, and cargo vanishes on entering a machine rather than being
consumed visibly.

## Measuring frames instead of judging them

`Tools/analyse-frame.sh` prints five numbers for a captured frame: mean brightness,
luminance standard deviation, mean saturation, edge density and non-background
coverage. It exists because judging screenshots by eye proved unreliable in a way that
cost real time: during the dusk work a change that shifted mean brightness by 28% was
repeatedly called "no visible difference" from thumbnails.

Measured against the reference captures, the useful finding was that two of the five
were already fine and three were not:

| Metric | Flat 2D | First isometric | Dusk | Tuned | Two Point Hospital | Timberborn |
| --- | --- | --- | --- | --- | --- | --- |
| Brightness | 56 | 68 | 61 | 82 | 112 | 87 |
| Contrast | 34 | 41 | 38 | 41 | 36 | 32 |
| Saturation | 39 | 30 | 45 | 49 | 43 | 53 |
| Edge density | 3% | 1% | 2% | 3% | 6% | 14% |
| Coverage | 71% | 62% | 54% | 91% | 95% | 93% |

Contrast and saturation reached the reference range early and did not need further
work; continuing to tune them by eye would have been wasted effort. Coverage was the
largest correctable gap and closed from 54% to 91% by framing the shop floor rather
than the whole tile map. Brightness confirmed the dusk pass had overshot.

Two findings the numbers produced that inspection had not:

- **The floor texture was never visible.** Meshes are unit sized with 0..1 UVs, so a
  texture stretches across whatever the object is scaled to; the floor slab spread one
  192 pixel texture over twenty four tiles. Doubling the texture's contrast changed
  nothing measurable, which is what exposed it. Tiling is now set per surface, in tiles.
- **Edge density does not distinguish good detail from bad.** Raising prop count to 52
  with six saturated hues moved the metric by one point and made the frame look like
  spilled sweets, with the machines lost in it. The count came back down to 34, the
  palette narrowed to drab industrial tones, and props were clustered near machines
  rather than scattered. The metric barely moved; the image improved a lot.

That second point is the limit of the approach. The numbers are good at catching
"this change did nothing" and bad at telling good from bad. Edge density remains at 3%
against a 6-14% reference, and closing it further needs meaningful content -- more
figures, machine sub-assemblies, visible interiors -- not more scattered objects.

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
