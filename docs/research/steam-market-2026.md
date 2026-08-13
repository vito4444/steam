# Project MONSTER — Steam Market Research (August 2026)

**Purpose.** Decide what kind of 3D Windows game to build so that it has a realistic
chance of succeeding commercially on Steam, given a very small team and a from-scratch
codebase.

**Data collection date.** 2026-08-13.

**Method.** Two independent sources were used and are kept separate throughout:

1. **Primary, first-hand.** Steam's own public endpoints, queried directly from this
   machine. Scripts are checked in at `tools/research/steam_probe.py`; raw captured
   output is at `docs/research/steam_probe.txt` and `docs/research/steam_appdetails.txt`.
   Everything in the "verified" tables below (title, developer, publisher, release date,
   price, review count, review percentage, store categories) came from
   `store.steampowered.com/search/results/` and
   `store.steampowered.com/api/appdetails`. These are facts as Steam reported them on
   the collection date.
2. **Secondary, third-party estimates.** Published market analyses. Revenue figures from
   these sources are *modelled estimates*, not Valve-reported numbers. They are labelled
   as estimates every time they appear.

**Anti-fabrication note.** No revenue number in this document was computed, rounded, or
inferred by the author. Review counts and prices are quoted exactly as captured. Where a
claim is an interpretation rather than a measurement, it is marked *Interpretation*.

---

## 1. The shape of the market

### 1.1 Platform-level numbers (third-party estimates)

| Claim | Value | Source |
| --- | --- | --- |
| Steam gross revenue, H1 2026 | $11.1 billion (record) | Alinea Analytics, via [gamedevreports](https://gamedevreports.substack.com/p/alinea-analytics-steam-reached-highest) and [OtakuKart](https://otakukart.com/steam-generated-a-record-11-1-billion-in-the-first-half-of-2026-with-back-catalog-games-driving-79-of-revenue/) |
| Share of revenue from games released the same year | 21% in H1'26, down from 27% in H1'25 and 29% in H1'24 | Alinea Analytics |
| Share of revenue from back catalogue | 79% | Alinea Analytics |
| Approximate new releases per day | ~70 | [Games-Stats Q1 2026 analysis](https://www.patreon.com/posts/indie-pocalypse-156244811) |
| Median lifetime gross revenue, all 2026 releases | under $250 | Games-Stats Q1 2026 |
| Median lifetime gross revenue, indie titles overall | $5,000–$15,000 | [Steam Page Analyzer](https://www.steampageanalyzer.com/blog/indie-game-revenue-data) |
| Indie titles reaching 100+ reviews and "Very Positive" | typically $75,000–$300,000 | Steam Page Analyzer |
| Top 5% of indie titles | over $1,000,000 lifetime | Steam Page Analyzer |

**Interpretation.** Two numbers matter more than the rest. First, the median 2026 release
earns under $250 — the default outcome of shipping a game is that nobody buys it. Second,
the gap between that median and the $75k–$300k band for games with 100+ reviews and a
"Very Positive" rating is enormous. Crossing the review threshold with a good score is not
one improvement among many; it is close to the whole game commercially. Every design
decision below is therefore evaluated against a single question: *does this raise the
probability of accumulating a few hundred positive reviews?*

### 1.2 What the biggest 2026 releases tell us (third-party estimates)

| Title | Estimated Steam revenue | Notes |
| --- | --- | --- |
| Forza Horizon 6 | $197.7M | ~3.5M copies |
| Resident Evil Requiem | $194.5M | ~3.4M copies; 8.9% wishlist conversion |
| Crimson Desert | ~$190M | Only new IP in the top tier |
| Slay the Spire 2 | $141.7M | 7.1M copies at $25 in Early Access; 40% wishlist conversion |
| Subnautica 2 | $133.6M | 5.6M wishlists, 12% converted |
| Meccha Chameleon | $71.3M | $6 price; Steam's best seller *by units* in 2026 |

**Interpretation.** These are not our competitors and none of this is reachable. They are
included for one reason: Meccha Chameleon at $6 topping the unit chart, and Slay the Spire
2 converting 40% of its wishlists, both confirm that on Steam in 2026 *price and clarity of
promise* move volume more than production budget does. A cheap, instantly legible game is a
viable shape. An expensive, hard-to-describe game is not, at our scale.

### 1.3 Genre-level signals (third-party)

From the Games-Stats Q1 2026 tag distribution: Singleplayer 76%, Indie 56%, Simulation 26%,
Strategy 24%, RPG 18%, Story Rich 18%, Horror 14%.

From the same analysis and from Steam Page Analyzer's genre revenue breakdown:

- Co-op combined with horror or survival is described as "the most consistent path to
  success", with "playing with friends" identified as the strongest organic marketing hook.
- Factory / automation: median $200,000–$500,000+ for titles with 100+ reviews (estimate).
- Colony sim / management: median $150,000–$400,000 (estimate).
- Roguelites: median $100,000–$300,000 (estimate).
- Survival crafting: median $100,000–$350,000 (estimate).
- City builders: median $75,000–$250,000 (estimate).
- Explicit warning: 2D games flood the market and are the most likely to fail; the
  "viral indie horror" niche is getting harder as the market tires of low-effort clones.

Pricing, from the [State of Steam 2026](https://dev.to/tonywangca/the-state-of-steam-2026-what-10000-games-reveal-516o)
analysis of the top 10,000 games: Open World carries the highest median price at $11.99;
Multiplayer, Co-op, Simulation and Story Rich cluster at $9–10; Puzzle, Casual and Indie sit
at $4.99. Co-op titles show the highest median ownership of any tag, around 550k, against a
catalogue-wide 350k.

**Interpretation.** The Co-op tag showing both the highest median ownership *and* a
mid-range price is the single most useful data point in this whole document for a small
team. It says co-op games reach more people per unit of price than anything else measured.

---

## 2. First-hand competitive data

Everything in this section was captured directly from Steam on 2026-08-13 via
`tools/research/steam_probe.py`. Review counts change daily; they are a snapshot.

### 2.1 The small-scope 3D co-op cluster — the most important finding

This is a group of games that share a specific and unusually reproducible shape: 3D, first
person, four-to-six player online co-op, one repeating "job" loop of twenty to forty
minutes, monsters as the threat, physics as the comedy, proximity voice chat as the social
engine, low-poly or deliberately degraded visuals, and a price under $10.

| Title | AppID | Developer | Released | Price | Reviews | Positive |
| --- | --- | --- | --- | --- | --- | --- |
| Lethal Company | 1966720 | Zeekerss (solo) | 2023-10-23 | $9.99 | — | — |
| Content Warning | 2881650 | Zorro, Wilnyl, Philip, thePetHen, Skog | 2024-04-01 | $7.99 | 34,932 | 93% |
| R.E.P.O. | 3241660 | semiwork | 2025-02-26 | $9.99 | 136,779 | 96% |
| PEAK | 3527290 | Team PEAK | 2025-06-16 | $3.99 | 136,446 | 95% |
| Shift At Midnight | 3722330 | Bun Muen | 2026-07-22 | $9.99 | 3,464 | 94% |
| Machine Party | 4108000 | Mike Klubnika, GDeavid | 2026-07-30 | $6.79 | 1,198 | 88% |
| GRAIN ROT | 4450620 | Beck & Branch Games | 2026-08-07 | $8.99 | 954 | 89% |
| Gamble With Your Friends | 3892270 | — | — | $7.99 | 10,610 | 88% |
| YAPYAP | 3834090 | — | — | $9.99 | 3,693 | 84% |
| Stonewards | 4502710 | — | — | $8.99 | 146 | 95% |

Store descriptions, verbatim from the appdetails API:

- **R.E.P.O.** — "An online co-op horror game with up to 6 players. Locate valuable, fully
  physics-based objects and handle them with care as you retrieve and extract to satisfy
  your creator's desires."
- **Lethal Company** — "A co-op horror about scavenging at abandoned moons to sell scrap to
  the Company."
- **PEAK** — "PEAK is a co-op climbing game where the slightest mistake can spell your doom."
- **Shift At Midnight** — "An online co-op detective horror for up to 3 players. Work
  together across randomly generated shifts to investigate your customers, as some are only
  pretending to be human."
- **GRAIN ROT** — "A horror co-op extraction builder set in a scorched wasteland where
  everything burns. Descend with your friends into shifting ruins, rip out furniture, and
  scavenge for loot before the Corru[ption]…"

**Interpretation, and the caveat that matters.** Three observations.

First, the pattern is genuinely reproducible by tiny teams. Lethal Company was one person.
R.E.P.O. is a studio of a handful. GRAIN ROT reached 954 reviews at 89% within six days of
release. The lineage is unusually forgiving of low art budgets because the visual roughness
reads as a deliberate aesthetic rather than as a lack of money.

Second, the pattern is now crowded, and the Q1 2026 analysis explicitly warns that the
market is tiring of low-effort clones. GRAIN ROT and Shift At Midnight both succeeded, but
note *what* they changed: GRAIN ROT bolted a base-builder onto the extraction loop, and
Shift At Midnight replaced monster-avoidance with social deduction. Neither is a reskin.
Entering this space with a reskin of Lethal Company in 2026 is a near-certain failure. **The
requirement is one genuinely novel verb.**

Third, and least obvious: the price ceiling here is real. Nothing in this cluster clears
$10. That is a hard cap on revenue per unit which must be accepted going in.

### 2.2 The small-scope single-player deep-simulator cluster

The second reproducible pattern: one setting, one machine or one shop, no procedural
sprawl, no multiplayer, but unusual mechanical depth and high finish. These reach a *higher*
price point than the co-op cluster.

| Title | AppID | Developer | Released | Price | Reviews | Positive |
| --- | --- | --- | --- | --- | --- | --- |
| IRON NEST: Heavy Turret Simulator | 2950790 | Nick Nieuwoudt, Dominik Latos (2 people) | 2026-08-06 | $14.99 | 4,648 | 98% |
| ReStory: Chill Electronics Repairs | 3812600 | Mandragora (pub. tinyBuild) | 2026-08-06 | $17.99 | 2,434 | 96% |
| Schedule I | 3164500 | TVGS (solo) | 2025-03-24 | $19.99 | 198,811 | 97% |
| Waterpark Simulator | 3293260 | — | — | $10.39 | 8,541 | 96% |
| Chop Chop Inc. | 4369130 | NullRef Entertainment | 2026-08-07 | $12.99 | 509 | 88% |
| Cast n Chill | 3483740 | Wombat Brawler | 2025-06-16 | $9.74 | 4,990 | 95% |
| Leafy Corner | 3558600 | — | — | $7.99 | 1,572 | 97% |
| Ore Factory Squad | 4210580 | threeW (pub. PlayWay) | 2026-07-16 | $12.99 | 1,211 | 89% |

**Interpretation.** IRON NEST is the standout reference for us. Two developers, one week on
the market at capture time, 4,648 reviews at 98% positive, $14.99 — and the entire game is
*one artillery piece in one emplacement*. It is the strongest available evidence that
extreme scope restriction plus mechanical depth beats breadth. Schedule I is the same lesson
at a larger scale: one solo developer, one city, first-person 3D, systems rather than
content, 198,811 reviews.

Note also that this cluster prices at $10–$20, roughly double the co-op cluster, and does
not require any networking code.

### 2.3 Clusters examined and set aside

- **Extraction shooters** (probe: Mistfall Hunter 63% / 5,440; Escape from Tarkov 62% /
  12,435; Dark and Darker 67% / 47,480; ARC Raiders 82% / 197,674). PvP extraction is
  dominated by Embark's ARC Raiders and is a live-service arms race. Review scores in this
  space are notably worse than every other cluster examined — several sit at Mixed. Not
  viable for us.
- **Souls-likes** (Black Myth: Wukong, Crimson Desert, Elden Ring, Mortal Shell II,
  Phantom Blade Zero). Animation and combat-authoring cost scales brutally and the
  comparison set is AAA. Not viable.
- **Colony sim / automation.** Best median revenue estimates of any indie genre, and
  genuinely reachable — but the audience expects hundreds of hours of systemic depth and
  years of Early Access support. Reachable in principle, wrong shape for a first title.
- **Cozy 3D** (Cast n Chill 95% / 4,990; Leafy Corner 97% / 1,572; ReStory 96% / 2,434).
  Very healthy review scores, genuinely small scope. Held as a serious option; the
  weakness is that "cozy" is a crowded discovery space with weak differentiation hooks,
  and it fits the MONSTER codename poorly.

---

## 3. Constraints that come from our side, not the market

These are hard facts about the development environment, established by direct inspection.
They shape which of the concepts is actually buildable.

| Constraint | Detail | Consequence for design |
| --- | --- | --- |
| No GPU | The build machine has no discrete or integrated GPU (`nvidia-smi` absent, no VGA device). Rendering runs on Mesa's llvmpipe software rasteriser. | Stylised, low-poly, low-overdraw art is not just an aesthetic choice, it is the only art direction the machine can iterate on at usable speed. Realistic HDRP is off the table. |
| 4 CPU cores, 15 GB RAM | Direct inspection. | Long light bakes and heavy import steps must be avoided; favour real-time lighting and procedural/primitive geometry. |
| Linux editor, Windows target | Unity Linux editor with the Windows Build Support (Mono) module. | Windows `.exe` builds are produced with the **Mono** scripting backend. IL2CPP for Windows cannot be cross-compiled from Linux; it needs a Windows host with MSVC. A final IL2CPP pass on a Windows machine is required before release. This is a release-blocking item, tracked in the tech plan, not a detail. |
| No Steam partner account or AppID yet | Not provided. | Steamworks features (achievements, Steam lobbies/relay, cloud saves) can be *coded against* an interface now, but cannot be tested until an AppID exists. Any networking design must have a non-Steam transport (direct IP / LAN) so that it is testable today. |
| Automated headless testing | Two headless instances of a game can be run and driven on this machine; rendered screenshots are possible but slow. | **Networked co-op is easier to self-test here than graphics are.** A dedicated-server / host-client architecture with headless clients is the cheapest thing to verify automatically. This inverts the usual assumption that multiplayer is the risky part. |

---

## 4. What the research implies

Stated plainly, and separated into what is measured and what is judgement.

**Measured.**

1. Co-op carries the highest median ownership of any community tag (~550k) at a $9–10 median price.
2. Small-team 3D co-op horror with a job loop has produced repeated breakouts, including two within the last month of the capture date, and is reachable at our scale.
3. That same cluster is capped at roughly $10 and is explicitly identified as clone-saturated.
4. Small-scope single-player deep simulators reach $10–$20 with review scores of 96–98%, from teams of one or two, with no networking.
5. The commercial cliff is at "100+ reviews and Very Positive". Below it the expected outcome is a few hundred dollars.

**Judgement (Interpretation).**

6. The correct target is therefore *not* "pick the highest-median genre". It is: pick the
   shape with the highest probability of clearing 100 reviews at 90%+, and make sure it has
   exactly one hook that can be stated in a single sentence and shown in a single GIF.
7. The MONSTER codename is worth honouring rather than ignoring. Monsters give a game a
   marketable subject, a natural source of set-piece screenshots, and a threat model that
   generates the emergent stories that drive word of mouth in co-op.
8. Given the no-GPU constraint, any concept requiring realistic rendering should be
   discarded now rather than discovered to be undeliverable later.

These conclusions feed directly into the five concepts in `docs/concepts/`.

---

## 5. Sources

Primary (captured by us, 2026-08-13):

- `store.steampowered.com/search/results/` — genre/tag top-seller probes. Raw: `docs/research/steam_probe.txt`
- `store.steampowered.com/api/appdetails` — per-title verification. Raw: `docs/research/steam_appdetails.txt`
- `store.steampowered.com/tagdata/populartags/english` — tag ID mapping.

Secondary (third-party estimates and analysis):

- Games-Stats.com, ["The 'Indie-pocalypse' is here? Steam Q1 2026 Data Analysis"](https://www.patreon.com/posts/indie-pocalypse-156244811)
- Alinea Analytics, ["Steam is having another record year for revenue"](https://alineaanalytics.substack.com/p/steam-is-having-another-record-year), and its coverage in [gamedevreports](https://gamedevreports.substack.com/p/alinea-analytics-steam-reached-highest), [OtakuKart](https://otakukart.com/steam-generated-a-record-11-1-billion-in-the-first-half-of-2026-with-back-catalog-games-driving-79-of-revenue/) and [shattered.io](https://shattered.io/steam-revenue-record-h1-2026/)
- Steam Page Analyzer, ["Indie Game Revenue Data 2026"](https://www.steampageanalyzer.com/blog/indie-game-revenue-data) and ["Steam Revenue by Genre"](https://www.steampageanalyzer.com/blog/steam-revenue-by-genre)
- ["The State of Steam 2026: What 10,000 Games Reveal"](https://dev.to/tonywangca/the-state-of-steam-2026-what-10000-games-reveal-516o)
- Shahriar Shahrabi, ["Deep dive in Steam 2026 indie market"](https://medium.com/@shahriyarshahrabi/deep-dive-in-steam-2026-indie-market-4c0aec5c0533)
