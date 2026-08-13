# Project MONSTER — Five Game Concepts

All five concepts below are 3D, target Windows, are built in Unity, and are sized so that a
very small team can actually finish them. They are grounded in the market data in
`docs/research/steam-market-2026.md` and in the hardware constraints of the build machine
(no GPU, 4 cores, Linux editor cross-building to Windows with the Mono backend).

Every concept honours the MONSTER codename. They differ in *what a monster is for*: a thing
you capture, a place you are inside of, a thing you raise, a thing you fight, or a thing you
identify.

Concept art in `docs/concepts/art/` is a **target**, not a screenshot of anything that
exists yet. Its purpose is to be the reference image that later self-check screenshots get
compared against.

---

## 0. How to read the comparison

The evaluation criterion is the one established by the research: **probability of reaching
100+ Steam reviews at 90%+ positive**, because that is the threshold that separates a few
hundred dollars from the $75k–$300k band. Scope, marketability and technical risk are all
scored against that, not against artistic ambition.

| | A — Containment | B — Deep Nest | C — The Pens | D — The Hunt | E — Night Shift |
| --- | --- | --- | --- | --- | --- |
| **One-line hook** | Pest control, but the pests are monsters and the building is fully destructible | An extraction run inside the body of a living god | Raise monsters on a failing farm; feed them, or they eat you | One hunter, one monster, one arena, no filler | Man the checkpoint. Decide what is human. |
| **Camera** | First person, co-op 1–4 | First person, co-op 1–4 | Third person over-shoulder, solo | Third person action, lock-on, solo | First person, seated, solo |
| **Session length** | 15–25 min | 20–35 min | Open-ended, ~40 min days | 8–15 min per hunt | 10–20 min per shift |
| **Novel verb** | Capture alive, intact, without wrecking the client's house | Harvest organs from an environment that reacts | Feed / breed / contain | — (execution-led, not verb-led) | Classify under uncertainty |
| **Networking required** | Yes, 4-player | Yes, 4-player | No | No | No |
| **Realistic price** | $8.99 | $9.99 | $14.99 | $19.99 | $12.99 |
| **Content volume needed** | Medium | Medium-high | High | Very high | **Low** |
| **Art risk on a GPU-less box** | Low | Medium | Medium | High | **Very low** |
| **Animation cost** | Low (physics-led) | Medium | High (many creatures) | **Very high** (combat) | Very low |
| **Automated self-testability** | High | High | Medium | Low | **Very high** |
| **Market fit vs. 2026 data** | **Very strong** | Strong | Medium | Weak at our scale | Strong |
| **Clone-saturation risk** | **High** — must earn its novelty | Medium | Medium | Low | Medium |
| **Probability of finishing** | Medium-high | Medium | Low-medium | **Low** | **High** |
| **Ceiling if it hits** | **Very high** | High | Medium | Medium | Medium-high |

---

## 1. Recommendation

**Build Concept A (Containment) as the product, and build Concept E (Night Shift) first as
the engine-proving vertical slice.**

The reasoning is not "A scores best on average" — it does not. It is:

1. **A is the only concept with a very high ceiling that is also finishable.** The 2026 data
   shows repeated breakouts from teams our size in exactly this shape, including two within
   a month of the data capture (GRAIN ROT, 954 reviews at 89% in six days; Machine Party,
   1,198 at 88%). No other concept here has produced that outcome for a team of this size in
   the last two years.
2. **A's biggest risk is clone saturation, and that risk is addressable by design rather
   than by budget.** Its novel verb — *capture the monster alive and intact without
   destroying the client's property* — inverts the Lethal Company loop from "avoid the
   monster, take the loot" to "the monster **is** the loot". That single inversion changes
   every downstream system: you approach monsters instead of fleeing them, damage becomes a
   penalty rather than a fail state, and the comedy comes from four people trying to
   manoeuvre a two-metre thrashing creature through a one-metre doorway. It is one sentence
   and one GIF, which is what the research says a store page needs.
3. **A's second-biggest risk is networking, and networking is the *easiest* thing to verify
   on this specific machine.** Two headless instances can be launched and driven with no
   renderer at all. The usual reason small teams avoid multiplayer — you cannot test it
   alone — does not apply here.
4. **E is not a consolation prize; it is genuinely the highest-completion-probability
   concept and has real commercial evidence behind it** (IRON NEST: two developers, one
   emplacement, $14.99, 4,648 reviews at 98%). Building it first is not a detour, because it
   exercises exactly the subsystems A needs — first-person camera, interaction, diegetic UI,
   audio, save/load, the headless screenshot harness, and the Windows build pipeline —
   inside a scope that can be finished and looked at quickly. If A later proves undeliverable,
   E is already a shippable product rather than a pile of prototypes.

**Rejected as the primary, and why, stated plainly:**

- **B (Deep Nest)** is the most visually distinctive concept here and the most likely to
  produce a striking store page. It is the runner-up. It loses to A on one point: an
  environment made of reactive organic geometry is a *rendering and authoring* problem, and
  this build machine has no GPU. A's environment is boxes, doors and props, which the
  software rasteriser handles fine.
- **C (The Pens)** needs many distinct creatures, each with idle/eat/sleep/panic animation
  sets, plus a breeding-genetics system. Creature count is the cost driver and it is high.
  Real ceiling, wrong first project.
- **D (The Hunt)** is the one I would actively argue against. Third-person action combat
  against a large monster lives or dies on animation quality, hit-reaction fidelity and
  frame-level tuning, all of which need a GPU to iterate on and an animator to author. The
  comparison set on Steam is Black Myth: Wukong and Elden Ring. Shipping a mediocre one is
  worse than not shipping, because the review score will reflect the comparison. It is
  documented in full for completeness, but it should not be chosen.

**If you disagree with the recommendation**, the second-best answer is E alone, shipped
polished and single-player. That is the lowest-variance path to a real Steam release, and
it is worth saying that lowest-variance is a legitimate choice for a first title.

---

## 2. The concepts

| Doc | Concept |
| --- | --- |
| [`plan-a-containment.md`](plan-a-containment.md) | **MONSTER: Containment** — co-op monster removal contractors |
| [`plan-b-deep-nest.md`](plan-b-deep-nest.md) | **MONSTER: Deep Nest** — co-op extraction inside a living creature |
| [`plan-c-the-pens.md`](plan-c-the-pens.md) | **MONSTER: The Pens** — third-person monster ranching |
| [`plan-d-the-hunt.md`](plan-d-the-hunt.md) | **MONSTER: The Hunt** — single-player arena monster hunting |
| [`plan-e-night-shift.md`](plan-e-night-shift.md) | **MONSTER: Night Shift** — single-player checkpoint identification sim |

---

## 3. Concept art index

| Image | Concept | What it is showing |
| --- | --- | --- |
| `art/concept_a_containment.png` | A | First-person, net-launcher in hand, a pinned creature in a flooded basement, two teammates with headlamps. Establishes the low-poly / harsh-practical-light / green-amber look. |
| `art/concept_b_deepnest.png` | B | First-person, inside a ribbed organic tunnel lit by bioluminescence, salvage crate fused into the flesh. Establishes the teal-and-amber bio-architecture look. |
| `art/concept_c_ranch.png` | C | Third-person over-shoulder on a dusk monster farm with pens, barn light, and eyes in the treeline. Establishes the painterly warm/cold split. |
| `art/concept_d_hunt.png` | D | Third-person mid-dodge against a single armoured monster in a ruined courtyard. Establishes scale contrast and the cold-moonlight-vs-molten-orange key. |
| `art/concept_e_nightshift.png` | E | First-person seated at a CRT-covered control desk, a too-tall silhouette in the fog beyond the barrier. Establishes the diegetic-only UI and single-lamp lighting. |

---

## 4. What happens next, whichever concept is chosen

These steps are concept-independent and are already in progress:

1. Unity 6000.5.8f1 with Windows Build Support (Mono), installed and licensed on the build machine. ✅
2. A repeatable headless pipeline: open project → run edit-mode and play-mode tests → build `MONSTER.exe` for Windows → archive.
3. A self-check harness: launch the built game headless under Xvfb, drive it with a scripted
   input sequence, capture screenshots at defined checkpoints, and record frame time, memory
   and error-log contents.
4. A visual regression step: diff each self-check screenshot against the concept art target
   and against the previous run, so drift is visible rather than discovered late.
5. A published progress log with screenshots at every milestone, so the gap between the
   concept art and the current build is always visible.

Details in `docs/tech/build-pipeline.md`.
