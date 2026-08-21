# Third-party and sample notices

Reviewed 2026-08-18. This inventory is an engineering distribution gate, not legal advice. The installer must carry the full upstream license texts and notices for the exact versions it ships.

## Accepted runtime route

| Component | Pin / artifact | License evidence | Closed-source decision |
|---|---|---|---|
| rembg | 2.0.80 | MIT, https://github.com/danielgatis/rembg/blob/main/LICENSE.txt | Accepted; retain notice. 2.0.80 is above the 2.0.75 fix for GHSA-3wqj-33cg-xc48. The HTTP server is not used. |
| ONNX Runtime | 1.29.0 CPU | MIT, https://github.com/microsoft/onnxruntime | Accepted; retain LICENSE and ThirdPartyNotices. |
| BiRefNet-general-lite | rembg artifact MD5 4fab47adc4ff364be1713e97b7e66334 | Official model card reports MIT: https://huggingface.co/ZhengPeng7/BiRefNet | Accepted; retain model-card attribution and MIT notice. |
| U2Net cloth | rembg artifact MD5 2434d1f3cb744e0e49386c906e5a08bb | Model/code repository reports MIT: https://github.com/levindabhi/cloth-segmentation | Accepted; repository was archived 2026-05-18, so pin exact bytes and keep a notice copy. |
| U-2-Net architecture | upstream source | Apache-2.0: https://github.com/xuebinqin/U-2-Net/blob/master/LICENSE | Accepted; retain Apache license/NOTICE obligations. |
| NumPy | 2.4.3 | BSD-3-Clause | Accepted. |
| Pillow | 12.3.0 | HPND | Accepted. |
| PyMatting | 1.1.15 | MIT, https://github.com/pymatting/pymatting/blob/master/LICENSE.md | Accepted; used for CPU closed-form alpha and foreground estimation. |
| SciPy | 1.17.1 | BSD-3-Clause | Accepted. |
| pytest | 8.4.1, development only | MIT | Not shipped in runtime. |

The rembg 2.0.80 wheel itself pins both model MD5 values above. pixelfit_pipeline.segmentation.RembgCascadeBackend repeats those checks before constructing ONNX sessions and returns MODEL_INTEGRITY_FAILED on mismatch. SHA-256 of the actual installed files is recorded in each import's model_info.

## Rejected route

mattmdjaga/segformer_b2_clothes points to the NVIDIA SegFormer license. Section 3.3 restricts the work and derivatives to non-commercial research/evaluation: https://github.com/NVlabs/SegFormer/blob/master/LICENSE. It is excluded from dependencies, code paths, manifests, and model downloads.

## Evidence photos

Evidence sources and hashes are in samples/manifest.json.

- denim-jacket.jpg: Lucius Kwok / Wikimedia Commons / CC BY-SA 2.0.
- layered-striped-outfit.jpg: petitepanoply (Jamie) / Wikimedia Commons / CC BY-SA 2.0.

The photos and derived comparison sheets remain subject to attribution and share-alike terms. They are evidence fixtures, not product stock assets or training-data authorization. Portrait/publicity rights are separate from copyright.
