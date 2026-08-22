# CERE-53 Real-photo Import Design

## Goal and evidence baseline

PixelFit 0.4.3 must turn both CERE-53 member photos into inspectable wardrobe candidates without calling a cloud try-on API. The flat-lay photo must import at least three of top, skirt, bag, and shoes. The worn street photo must import at least three of jacket, shirt, skirt, and shoes. Every candidate, including a retry-only result, must remain visible with its measured quality failures.

The 0.4.2 baseline was reproduced with the locked BiRefNet-general-lite and U2Net-cloth ONNX files. Both photos produce exactly three U2Net channels and all six records are rejected. Four useful candidates are rejected only because `input_contour_roughness` exceeds `0.05`; meanwhile the current three-channel schema cannot represent bags or shoes. This proves that threshold-only work cannot satisfy the issue.

## Approaches considered

### A. Relax the CERE-12 thresholds

This would admit the visually usable upper and skirt masks, but it would still produce no bag or shoe candidate and would hide the distinction between a cosmetic defect and a missing subject. It is rejected.

### B. Add a third SAM or human-parsing model

A dedicated model can expose more semantic classes, but it adds a large weight, another license and checksum surface, longer CPU inference, and substantial 0.4.3 packaging risk. It remains a future accuracy upgrade if the locked-model postprocessor cannot meet the two supplied acceptance photos.

### C. Scene-aware postprocessing of the two locked models

Keep the model contract and derive instances from signals already produced offline. U2Net continues to supply coarse upper/lower/full masks. BiRefNet's general foreground mask supplies the objects that U2Net omits. The supplied flat-lay mask has a distinct bag residual and two shoe components; the supplied worn mask has a tall person silhouette whose lower residual contains the feet. This approach is selected because it adds no network, model, or release-size dependency and directly addresses the observed missing-candidate root cause.

## Architecture

### Candidate generation

Add a pure `candidate_generation` module between `SegmentationBackend.segment()` and matting. It consumes the source size, subject mask, and U2Net masks and returns named candidate masks with a category, source, scene kind, and visibility note.

Scene classification is geometric, not filename-based:

- `worn`: the largest subject component is at least 1.6 times as tall as it is wide and reaches the lower 80% of the image.
- `flatlay`: all other inputs.

For flat lays, keep usable upper and lower channels, subtract a dilated union of the cloth masks from the subject, and retain significant residual components. A side residual above the footwear band becomes `bag`; paired or adjacent bottom components become one `shoes` candidate. For worn photos, the upper channel becomes `outerwear`, the best lower-half cloth channel becomes `bottoms`, and the bottom band of the subject becomes a partial-visibility `shoes` candidate. Overlapping masks are scored by usable area and duplicate overlap so the pipeline does not emit two names for the same pixels.

The algorithm does not fabricate occluded pixels. A cropped or occluded result carries `visibility: partial` and the UI states that explicitly.

### Three-level quality admission

`evaluate_cutout()` keeps all existing metrics and thresholds, but separates defect reporting from import permission:

- `pass`: no reasons; import normally.
- `needs_optimization`: the subject is present and plausibly occupied, but one or more edge, alpha, isolation, hole, or truncation checks fail; import and persist `review_status: needs_optimization`.
- `retry`: empty/nearly transparent output or evidence that more than half the bounding box is missing; do not import.

This changes policy, not measurement. No CERE-12 threshold is loosened. Every reason retains its metric value, threshold, message, and a UI-computed difference.

### IPC and storage

Pipeline metadata always exposes a safe relative `preview_file`. `auto_file` remains non-null only for importable `pass` and `needs_optimization` candidates. Main-process path validation applies equally to imported and retry previews.

`PhotoImportResult` returns `candidates` containing category, state, preview URL, visibility, score, and structured reasons. Imported assets persist an optional `review_status` field and a `待优化` tag when needed. Retry candidates remain in the per-import quarantine directory and are not copied into the wardrobe.

### Renderer

The import page shows a responsive candidate review grid after analysis. Each card contains the transparent thumbnail, category label, state badge, visibility note, score, and one line per failed metric in the form “measured / threshold / difference”. A summary distinguishes imported-ready, imported-needs-optimization, and retry counts. Wardrobe cards retain a small `待优化` badge. Existing manual repair and wardrobe navigation remain available.

## Failure handling and privacy

- Candidate and preview paths must resolve under the current import root before they cross IPC.
- A photo with no generated records still raises `SEGMENTATION_EMPTY`.
- Retry previews are visible but never imported.
- Model inference remains `CPUExecutionProvider`; no cloud try-on provider is invoked.
- The build script must package and ping the pipeline runtime before Electron packaging. The observed PyInstaller `backports` hidden-import failure is fixed as part of release hardening.

## Verification

Automated tests cover scene classification, flat-lay residual bag/shoes, worn outerwear/bottom/shoes generation, three-level quality decisions, safe preview paths, persisted review status, detailed UI metric formatting, and feedback counts. Existing TypeScript and pipeline suites, typecheck, Electron build, and layout checks must remain green.

The locked models then run against both supplied photos. Evidence must include pipeline metadata, candidate thumbnails, the import-page candidate grid, the resulting wardrobe, and at least one imported garment from each photo worn on the local model. Finally build both Windows artifacts as version 0.4.3, publish the upstream release, download the published assets, and verify SHA-256 against the release checksum file.
