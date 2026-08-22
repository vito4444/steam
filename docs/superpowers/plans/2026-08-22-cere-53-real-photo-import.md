# CERE-53 Real-photo Import Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make PixelFit 0.4.3 import useful flat-lay and worn-photo candidates through a three-level quality policy, show every candidate with measured failures, and satisfy both supplied photos offline.

**Architecture:** Preserve the locked BiRefNet→U2Net CPU cascade, then add a pure scene-aware candidate generator that derives bag and shoe instances from subject/cloth residuals. Keep every CERE-12 metric unchanged while mapping reports to pass, needs-optimization, or retry, and carry structured candidate data through IPC into a review grid and wardrobe badges.

**Tech Stack:** Python 3.11, Pillow, NumPy, SciPy, rembg/ONNX Runtime CPU, Electron 33, TypeScript 5.7, React 18, Vitest 2, PyInstaller 6.15, electron-builder 25.

## Global Constraints

- Baseline is upstream `main` at `v0.4.2`; all work stays on `agent/codex/cere-53-real-photo-import`.
- PR title must include both the requested `CERE-30` text and routable `CERE-53`.
- No cloud AI try-on request and no paid API call may occur.
- Do not lower any existing CERE-12 quality threshold.
- Every candidate must have a visible thumbnail and structured metric/threshold detail.
- Release artifacts are version `0.4.3`, x64, and must be downloaded from the upstream release and SHA-256 verified.

---

### Task 1: Three-level quality policy

**Files:**
- Create: `pipeline/tests/test_quality_tiers.py`
- Modify: `pipeline/src/pixelfit_pipeline/quality.py`

**Interfaces:**
- Produces: `QualityReport.decision: Literal["pass", "needs_optimization", "retry"]`
- Produces: `QualityReport.allowed: bool`, true for pass and needs-optimization only.
- Preserves: every existing metric, threshold, reason code, value, threshold, and message.

- [ ] **Step 1: Write failing tests for cosmetic and severe failures**

```python
def test_structurally_rough_but_present_candidate_is_importable_for_optimization():
    report = evaluate_cutout(present_cutout(), source_mask=rough_source_mask())
    assert report.decision == "needs_optimization"
    assert report.allowed is True
    assert {reason.code for reason in report.reasons} == {"MASK_STRUCTURE_UNRELIABLE"}

def test_sparse_missing_subject_requires_retry():
    report = evaluate_cutout(sparse_cutout())
    assert report.decision == "retry"
    assert report.allowed is False
```

- [ ] **Step 2: Run RED**

Run: `pipeline\.venv\Scripts\python.exe -m pytest pipeline/tests/test_quality_tiers.py -q`

Expected: cosmetic failure is `reject`/not allowed because the current gate is binary.

- [ ] **Step 3: Implement policy classification without changing thresholds**

```python
SEVERE_REASON_CODES = {"SUBJECT_TOO_SMALL", "SUBJECT_TOO_LARGE"}

def _decision(metrics, reasons, thresholds) -> tuple[str, bool]:
    codes = {reason.code for reason in reasons}
    missing_over_half = float(metrics["bbox_fill_ratio"]) < thresholds.min_bbox_fill_ratio / 2
    if codes & SEVERE_REASON_CODES or missing_over_half:
        return "retry", False
    if reasons:
        return "needs_optimization", True
    return "pass", True
```

- [ ] **Step 4: Run GREEN and the complete Python suite**

Run: `pipeline\.venv\Scripts\python.exe -m pytest pipeline/tests -q`

Expected: all tests pass.

- [ ] **Step 5: Commit**

```powershell
git add pipeline/src/pixelfit_pipeline/quality.py pipeline/tests/test_quality_tiers.py
git commit -m "fix: grade CERE-53 candidate quality"
```

### Task 2: Scene-aware candidate generation and metadata

**Files:**
- Create: `pipeline/src/pixelfit_pipeline/candidate_generation.py`
- Create: `pipeline/tests/test_candidate_generation.py`
- Modify: `pipeline/src/pixelfit_pipeline/pipeline.py`
- Modify: `pipeline/src/pixelfit_pipeline/__init__.py`

**Interfaces:**
- Produces: `CandidateMask(asset_id: str, category: str, mask: Image.Image, source: str, scene: str, visibility: str)`.
- Produces: `generate_candidates(subject_mask, garment_masks) -> list[CandidateMask]`.
- Internal: `classify_scene(subject: np.ndarray) -> Literal["flatlay", "worn"]`.
- Internal: `significant_components(mask: np.ndarray, min_ratio: float) -> list[np.ndarray]`.
- Internal: `_flatlay_candidates(subject, cloth) -> list[CandidateMask]` and `_worn_candidates(subject, cloth) -> list[CandidateMask]`.
- Pipeline record adds `preview_file`, `visibility`, and three-level `review_state`.

- [ ] **Step 1: Write synthetic RED tests for both scene classes**

```python
def test_flatlay_keeps_clothes_and_extracts_bag_and_paired_shoes():
    candidates = generate_candidates(flatlay_subject(), flatlay_cloth_masks())
    assert [(c.asset_id, c.category) for c in candidates] == [
        ("upper", "upper-body"), ("lower", "bottoms"),
        ("bag", "bag"), ("shoes", "shoes"),
    ]

def test_worn_scene_maps_outer_layer_skirt_and_partial_shoes():
    candidates = generate_candidates(worn_subject(), worn_cloth_masks())
    assert [(c.category, c.visibility) for c in candidates] == [
        ("outerwear", "complete"), ("bottoms", "complete"), ("shoes", "partial"),
    ]
```

- [ ] **Step 2: Run RED**

Run: `pipeline\.venv\Scripts\python.exe -m pytest pipeline/tests/test_candidate_generation.py -q`

Expected: import of `candidate_generation` fails because the module does not exist.

- [ ] **Step 3: Implement geometric scene and residual extraction**

```python
@dataclass(frozen=True)
class CandidateMask:
    asset_id: str
    category: str
    mask: Image.Image
    source: str
    scene: str
    visibility: str = "complete"

def generate_candidates(
    subject_mask: Image.Image,
    garment_masks: Mapping[str, Image.Image],
) -> list[CandidateMask]:
    subject = np.asarray(subject_mask.convert("L"), dtype=np.uint8) >= 128
    cloth = {
        name: np.asarray(mask.convert("L"), dtype=np.uint8) >= 128
        for name, mask in garment_masks.items()
    }
    if classify_scene(subject) == "worn":
        return _worn_candidates(subject, cloth)
    return _flatlay_candidates(subject, cloth)
```

- [ ] **Step 4: Integrate candidates into `analyze_image`**

Replace direct iteration over `bundle.garment_masks` with `generate_candidates(subject, bundle.garment_masks)`. Write every cutout to either `assets/` or `quarantine/`, and set `preview_file` to that safe relative path in both cases.

- [ ] **Step 5: Run GREEN and regression**

Run: `pipeline\.venv\Scripts\python.exe -m pytest pipeline/tests -q`

Expected: all scene, quality, and pipeline tests pass.

- [ ] **Step 6: Commit**

```powershell
git add pipeline/src/pixelfit_pipeline pipeline/tests
git commit -m "feat: derive CERE-53 garment instances offline"
```

### Task 3: Safe candidate IPC and persisted review status

**Files:**
- Modify: `src/main/photo-import.ts`
- Modify: `src/main/index.ts`
- Modify: `src/shared/ipc.ts`
- Modify: `src/shared/types.ts`
- Modify: `tests/photo-import.test.ts`

**Interfaces:**
- Produces: `PhotoImportCandidate` with `id`, `category`, `state`, `previewUrl`, `visibility`, `score`, and `reasons`.
- Produces: `collectPipelineCandidates(metadata, importRoot) -> PipelineCandidate[]` with root containment checks for every preview and import path.
- Asset metadata adds optional `review_status: "ready" | "needs_optimization"`.

- [ ] **Step 1: Write RED tests for importable optimization and safe retry previews**

```typescript
it('imports needs-optimization and exposes retry preview details', () => {
  const candidates = collectPipelineCandidates(metadata, root);
  expect(candidates.map(({ state, importable }) => ({ state, importable }))).toEqual([
    { state: 'needs_optimization', importable: true },
    { state: 'retry', importable: false },
  ]);
});

it('rejects a preview path that escapes the import root', () => {
  expect(() => collectPipelineCandidates(escapingMetadata, root)).toThrow(/escapes/i);
});
```

- [ ] **Step 2: Run RED**

Run: `npm test -- tests/photo-import.test.ts`

Expected: `collectPipelineCandidates` and candidate IPC fields do not exist.

- [ ] **Step 3: Implement mapping, importing, and asset persistence**

Add the interfaces to shared IPC/types, map safe relative paths in `photo-import.ts`, return candidates from `pipeline:importPhotos`, and pass `reviewStatus` through `importOne`. Add `待优化` to tags only when the state requires it.

- [ ] **Step 4: Run GREEN plus typecheck**

Run: `npm test -- tests/photo-import.test.ts`

Run: `npm run typecheck`

Expected: both pass.

- [ ] **Step 5: Commit**

```powershell
git add src/main src/shared tests/photo-import.test.ts
git commit -m "feat: carry CERE-53 candidate reviews through IPC"
```

### Task 4: Candidate review grid and wardrobe badge

**Files:**
- Create: `src/renderer/src/features/import/candidateReview.ts`
- Modify: `src/renderer/src/features/views/Views.tsx`
- Modify: `src/renderer/src/features/wardrobe/WardrobePanel.tsx`
- Modify: `src/renderer/src/styles.css`
- Modify: `src/renderer/src/features/import/importFeedback.ts`
- Modify: `tests/import-experience.test.ts`

**Interfaces:**
- Produces: `candidateStateLabel(candidate)`, `candidateReasonLine(reason)`, and `summarizeCandidates(candidates)`.
- Import view keeps the latest `PhotoImportCandidate[]` and renders `data-testid="candidate-review"`.

- [ ] **Step 1: Write RED tests for exact metric and summary copy**

```typescript
expect(candidateReasonLine({
  code: 'MASK_STRUCTURE_UNRELIABLE',
  metric: 'input_contour_roughness',
  value: 0.24,
  threshold: 0.05,
  message: 'input mask has structural bites',
})).toContain('实测 0.240');
expect(candidateReasonLine(reason)).toContain('门槛 ≤ 0.050');
expect(candidateReasonLine(reason)).toContain('超出 0.190');
```

- [ ] **Step 2: Run RED**

Run: `npm test -- tests/import-experience.test.ts`

Expected: candidate review helper import fails.

- [ ] **Step 3: Implement helpers, React cards, and badges**

Use semantic status colors already present in the application, keep thumbnails on a subtle checker surface, display at most the measured reasons supplied by the pipeline, and preserve the manual-repair and wardrobe actions. Add `待优化` to wardrobe thumbnails without covering the existing `穿着中` and demo badges.

- [ ] **Step 4: Run GREEN, typecheck, build, and layout check**

Run: `npm test -- tests/import-experience.test.ts`

Run: `npm run typecheck`

Run: `npm run build`

Run: `npm run check:layout`

Expected: all pass without warnings or clipped controls.

- [ ] **Step 5: Commit**

```powershell
git add src/renderer tests/import-experience.test.ts
git commit -m "feat(ui): show CERE-53 candidate quality details"
```

### Task 5: Windows runtime and 0.4.3 packaging

**Files:**
- Modify: `scripts/prepare-windows-pipeline.ps1`
- Modify: `package.json`
- Modify: `package-lock.json`
- Modify: `pipeline/manifest.json`
- Modify: `pipeline/README-CERE12.md`

**Interfaces:**
- PyInstaller command explicitly collects the `backports` namespace required by current setuptools runtime hooks.
- Package and pipeline manifest versions report `0.4.3` and the three-level candidate capability.

- [ ] **Step 1: Record the real packaged-runtime RED**

Run the unmodified `prepare-windows-pipeline.ps1` through PyInstaller and its built-in runtime ping.

Expected: packaging completes, then the real runtime exits 1 with `ModuleNotFoundError: No module named 'backports'`. This observed full-build failure is the regression test; do not replace it with a source-text assertion.

- [ ] **Step 2: Add hidden imports and bump release metadata**

Add `--hidden-import backports` and `--hidden-import backports.tarfile`; set package version to `0.4.3`; update the pipeline manifest/README capability and policy copy. Keep the existing verify-only tests as the locked-model behavior check.

- [ ] **Step 3: Rebuild and ping the pipeline**

Run: `pwsh -File scripts/prepare-windows-pipeline.ps1`

Run: `'{"command":"ping"}' | pipeline/runtime/pixelfit-pipeline/pixelfit-pipeline.exe`

Expected: JSON `ok: true` and exit code 0.

- [ ] **Step 4: Run all automated gates and package**

Run: `npm test`

Run: `npm run typecheck`

Run: `npm run build`

Run: `npm run check:layout`

Run: `npm run dist`

Expected: all gates pass and both x64 artifacts are created.

- [ ] **Step 5: Commit**

```powershell
git add scripts tests package.json package-lock.json pipeline/manifest.json pipeline/README-CERE12.md
git commit -m "chore(release): prepare PixelFit 0.4.3"
```

### Task 6: Real-photo acceptance, PR, and upstream release

**Files:**
- Add: `fixtures/cere53-user/flatlay.png`
- Add: `fixtures/cere53-user/streetshot.png`
- Add: `fixtures/cere53-user/image.png`
- Create: `evidence/cere53/metadata/*.json`
- Create: `evidence/cere53/*.png`
- Create: `dist/SHA256SUMS.txt` (release artifact, not committed unless repository convention requires it)

**Interfaces:**
- Acceptance evidence names the exact source SHA-256 and candidate counts.
- PR title contains `CERE-30` and `CERE-53`; body contains `Closes CERE-53`.

- [ ] **Step 1: Run the locked models on both supplied fixtures**

Run one foreground Python process that reuses `RembgCascadeBackend` sessions and calls `analyze_image` for both fixtures.

Expected: flat lay yields at least three importable categories among top/bottom/bag/shoes; street photo yields at least three among outer/top/bottom/shoes. Inspect every transparent cutout for subject completeness and obvious background residue.

- [ ] **Step 2: Capture the real application flow**

Launch the locally built application with an isolated PixelFit data directory. Capture import selection, candidate review grid, wardrobe presence, and local layered try-on. Do not open or invoke the cloud try-on panel.

- [ ] **Step 3: Run final verification and commit fixtures/evidence**

Run: `git diff --check`

Run: `npm test`

Run: `npm run typecheck`

Run: `npm run build`

Expected: all pass. Commit only the supplied fixtures and concise evidence required for reproducibility.

- [ ] **Step 4: Push and create the PR**

```powershell
git push -u origin agent/codex/cere-53-real-photo-import
gh pr create --repo vito4444/steam --base main --head agent/codex/cere-53-real-photo-import --title "CERE-30 / CERE-53: real-photo candidate import" --body-file .github/cere53-pr.md
```

- [ ] **Step 5: Publish and verify release 0.4.3**

Create tag `v0.4.3`, publish the setup, portable, and checksum assets, then download them into a fresh task-local directory with `gh release download v0.4.3`. Run `Get-FileHash -Algorithm SHA256` and compare both binaries byte-for-byte with `SHA256SUMS.txt`.

- [ ] **Step 6: Post the single Multica delivery comment**

Include the PR and release URLs, exact automated test results, per-photo candidate/import counts, downloaded SHA-256 values, screenshot attachments, and any acceptance shortfall. Move CERE-53 to `in_review`; do not mark it `done`.
