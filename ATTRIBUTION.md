# Asset attribution and provenance

Reviewed on 2026-08-22. This inventory covers the image files distributed from `assets/base/` and `assets/wardrobe/`. It records source facts and license evidence; it is not legal advice and does not license the PixelFit source code, whose package metadata remains `UNLICENSED`.

## Distribution decision

- `base_f02`, `base_m02`, and all eight wardrobe items have a recorded rights basis. The two bases are synthetic and do not depict real people. The wardrobe cutouts match the archived CERE-13 deliverables byte-for-byte by SHA-256.
- `s`, `m`, and `l` are legacy procedural mannequin packs from the CERE-6 visual kit. That kit included reproducible SVG-to-PNG generation code, but no LICENSE, NOTICE, public source URL, or other rights grant. Their commercial redistribution is therefore **not cleared**. They are not selected by the current runtime (`base_f02` / `base_m02` are the supported bases), so remove these three legacy packs before the next release unless the project owner documents a valid rights grant.
- Copyright permission does not grant trademark, publicity, privacy, or endorsement rights. Do not imply that a photographer, platform, brand, or depicted product endorses PixelFit.

## Base images

| Pack | Source and original link | License / rights basis | Commercial use | Likeness / portrait basis | Status |
|---|---|---|---|---|---|
| `base_f02` | OpenAI image generation in the CERE-13 project workflow; no public original-image URL | [OpenAI Services Agreement — output ownership](https://openai.com/policies/services-agreement/) | Yes, subject to applicable law, the agreement, and confirmation that the generating account was covered by it | Synthetic adult; not a real person; model release not applicable | Cleared with terms |
| `base_m02` | OpenAI image generation in the CERE-13 project workflow; no public original-image URL | [OpenAI Services Agreement — output ownership](https://openai.com/policies/services-agreement/) | Yes, subject to applicable law, the agreement, and confirmation that the generating account was covered by it | Synthetic adult; not a real person; model release not applicable | Cleared with terms |
| `s` | CERE-6 project-authored procedural SVG-to-PNG generator (`tools/gen_models.py` in the delivered visual kit); no public source or original-image URL | **UNVERIFIED** — no license or rights grant accompanied the kit | **No — not cleared for redistribution** | Procedural faceless mannequin; no real person; model release not applicable | **Removal recommended** |
| `m` | Same CERE-6 generator and missing evidence as `s` | **UNVERIFIED** | **No — not cleared for redistribution** | Procedural faceless mannequin; no real person; model release not applicable | **Removal recommended** |
| `l` | Same CERE-6 generator and missing evidence as `s` | **UNVERIFIED** | **No — not cleared for redistribution** | Procedural faceless mannequin; no real person; model release not applicable | **Removal recommended** |

The OpenAI agreement states that, as between the customer and OpenAI and to the extent permitted by law, the customer owns output. It also makes the customer responsible for inputs and use of outputs and notes that output may not be unique. Keep the CERE-13 generation record with the release evidence; if the project owner cannot confirm the applicable account terms, replace these bases before commercial distribution.

## Bundled wardrobe

All listed files were changed from their source photos by alpha/background cleanup, proportional resizing, and centered transparent padding; their garment RGB was otherwise preserved. “Commercial” below addresses the recorded copyright license only.

| Asset | Source work | Creator | License | Commercial | Required credit |
|---|---|---|---|---|---|
| `c10n_top_011` | [fashion clothes sweater wool cardigan jumper men clothes](https://pxhere.com/en/photo/812325) | Not named on source page | [CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/) | Yes | None required; source link retained for provenance |
| `c10n_top_015` | [Red Raider Nation Texas Tech Ugly Sweater Vest](https://www.flickr.com/photos/54115831@N07/15485372787) | [TheUglySweaterShop](https://www.flickr.com/photos/54115831@N07) | [CC BY 2.0](https://creativecommons.org/licenses/by/2.0/) | Yes, with attribution | “Red Raider Nation Texas Tech Ugly Sweater Vest” by TheUglySweaterShop, CC BY 2.0; modified as described above |
| `c10n_top_393` | [Balls & Tees Women's Tacky Golf Cardigan Ugly Sweater](https://www.flickr.com/photos/54115831@N07/15672141402) | [TheUglySweaterShop](https://www.flickr.com/photos/54115831@N07) | [CC BY 2.0](https://creativecommons.org/licenses/by/2.0/) | Yes, with attribution | “Balls & Tees Women's Tacky Golf Cardigan Ugly Sweater” by TheUglySweaterShop, CC BY 2.0; modified as described above |
| `c10n_bottom_102` | [jeans denim fashion pants closeup clothing trousers aged](https://pxhere.com/en/photo/723773) | Not named on source page | [CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/) | Yes | None required; source link retained for provenance |
| `c10n_shoes_2178` | [black leather](https://stocksnap.io/photo/black-leather-QGSGV72U9O) | [Camila Damásio](https://stocksnap.io/author/743) | [CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/) | Yes | None required; voluntary source credit retained |
| `c10n_shoes_276` | [running shoe shoe brooks highly functional run keen on sport sport shoe shoe market](https://pxhere.com/en/photo/958791) | Not named on source page | [CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/) | Yes | None required; source link retained for provenance |
| `c10n_bag_382` | [book watch backpack coin money](https://pxhere.com/en/photo/132888) | Not named on source page | [CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/) | Yes | None required; source link retained for provenance |
| `c10n_accessory_433` | [berry mix knit cowl](https://www.flickr.com/photos/83202873@N00/8678362438) | [smittenkittenorig](https://www.flickr.com/photos/83202873@N00) | [CC BY 2.0](https://creativecommons.org/licenses/by/2.0/) | Yes, with attribution | “berry mix knit cowl” by smittenkittenorig, CC BY 2.0; modified as described above |

## Required metadata for new assets

Every new base or wardrobe asset must add a `provenance` object to its manifest entry before it can be distributed. Record all of the following without guessing:

- source name, source page URL, original image URL, work title, creator name, and creator profile URL;
- license name/version and canonical license URL;
- whether attribution is required, the exact credit line, and all material transformations;
- whether commercial redistribution is permitted and the evidence supporting that conclusion;
- whether a real person is depicted and, if so, the model/property-release basis;
- SHA-256 of the exact distributed file and the date the source/license evidence was reviewed.

Fail closed: if the source is unknown, a license page is unavailable, commercial use is not expressly permitted, required attribution cannot be satisfied, or a depicted person's release basis is missing, mark the asset `unverified`, keep it out of release builds, and recommend replacement/removal. Preserve dated screenshots or exported source/license pages with the release evidence because websites and license selections can change.
