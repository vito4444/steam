# PixelFit 照片→衣橱素材管线

这是 PixelFit 的 Windows / CPU-only 照片衣物素材管线。CERE-12 在 CERE-5 的分割与手动修补基础上加入 PyMatting 闭式 alpha matting、连通域清理、小孔修复和半透明边缘去色边；CERE-53 在同一套锁定模型上加入平铺/着装场景分件和三档质量准入。输出保持原图分辨率、颜色、纹理和款式；不会像素化、量化颜色或缩小后伪造细节。

质量结果分为 `pass`、`needs_optimization` 和 `retry`。前两档写入 `assets/`，其中有瑕疵但主体可用的候选保留 `needs_optimization` 标记；只有近乎空白、异常占满或主体缺失过半的结果进入 `quarantine/`。每档都返回安全的 `preview_file`、逐项指标、门槛和失败原因，严重漏分不会被平滑算法冒充为合格素材。

## 组件边界

- `pixelfit_pipeline/segmentation.py`：离线 BiRefNet-general-lite → 主体裁剪 → U2Net cloth CPU 级联。
- `pixelfit_pipeline/matting.py`：连通域/孔洞清理、trimap、闭式 alpha/foreground 估计、边缘去污染。
- `pixelfit_pipeline/quality.py`：可序列化指标、阈值、评分和拒绝原因。
- `pixelfit_pipeline/pipeline.py`：透明 PNG、粗品类、主色板、metadata、准入与隔离路由。
- `pixelfit_pipeline/editor.py`：非破坏式手动修补；导出同样必须通过质量闸门。
- `pixelfit_pipeline/cli.py`：供 CERE-10 桌面应用常驻调用的 UTF-8 JSON-lines 桥。
- `docs/API_CONTRACT.md`：schema 2.0 输入/输出契约。
- `docs/QUALITY_GATE.md`：阈值、原因码、评分和 CERE-10 接入规则。
- `evidence/cere12/`：CERE-11 三样本前后对比与固定种子 10 件质量审计。

本目录不包含 UI 或人物模型美术资产。

## 安装

基础管线、闭式抠图、测试和证据生成：

~~~powershell
py -3.11 -m venv .venv
.\.venv\Scripts\python.exe -m pip install -r requirements.txt
~~~

需要真实自动分割推理时再安装模型运行时：

~~~powershell
.\.venv\Scripts\python.exe -m pip install -r requirements-models.txt
~~~

生产应用应把模型预置到 `%LOCALAPPDATA%\PixelFit\models` 等短路径，首次导入时不得在线下载。模型清单见 `models.lock.json`。上游 BiRefNet 文件须命名为 `birefnet-general-lite.onnx`；启动前会校验 rembg 2.0.80 固定的 MD5。

## Python 调用

~~~python
from pixelfit_pipeline import RembgCascadeBackend, analyze_image

backend = RembgCascadeBackend(r"C:\Users\me\AppData\Local\PixelFit\models")
metadata = analyze_image(
    image_path=r"C:\photos\look.jpg",
    import_id="look-20260818",
    output_dir=r"C:\Users\me\AppData\Local\PixelFit\library",
    backend=backend,
)

approved = [asset for asset in metadata["assets"] if asset["admission"]["allowed"]]
manual = [asset for asset in metadata["assets"] if not asset["admission"]["allowed"]]
~~~

手动修补导出也采用同一准入规则：

~~~python
from PIL import Image
from pixelfit_pipeline import EditSession

session = EditSession(Image.open("source.png"), Image.open("masks/upper-auto.png"))
session.erase_brush([(120, 240), (140, 250)], radius=18)
session.restore_brush([(155, 260)], radius=10, source="opaque")
result = session.export(
    "assets/upper-final.png",
    "revisions/upper.jsonl",
    quarantine_destination="quarantine/upper-final.png",
    category="upper-body",
)
if not result["admission"]["allowed"]:
    print(result["admission"]["reasons"])
~~~

`source="baseline"` 只恢复自动基线；`source="opaque"` 可从原始照片补回漏分区域。原图和自动基线不会被覆盖。

## CERE-10 JSON-lines 接入

~~~powershell
'{"command":"ping"}' | .\.venv\Scripts\python.exe -m pixelfit_pipeline
~~~

返回 schema 2.0：

~~~json
{"ok":true,"result":{"service":"pixelfit-pipeline","schema_version":"2.0"}}
~~~

对已有 RGBA 候选执行升级和准入：

~~~json
{"command":"refine-cutout","cutout_path":"C:\\in\\upper.png","destination":"C:\\library\\assets\\upper.png","quarantine_destination":"C:\\library\\quarantine\\upper.png","category":"upper-body"}
~~~

只有响应中的 `result.admission.allowed` 为 `true` 时，`result.file` 才非空。拒绝时 `file=null`；若提供隔离路径，结果写到 `quarantine_file`。`quality-check` 是只读预检，不会写文件。完整契约见 `docs/API_CONTRACT.md`。

## 复现与验证

~~~powershell
.\.venv\Scripts\python.exe scripts\build_evidence.py
.\.venv\Scripts\python.exe scripts\build_quality_evidence.py
.\.venv\Scripts\python.exe scripts\build_audit_corpus.py
.\.venv\Scripts\python.exe scripts\audit_quality.py --input-dir samples\audit\candidates --sample-size 10 --seed 20260818 --manifest samples\audit\manifest.json --output-dir evidence\cere12
.\.venv\Scripts\python.exe -m pytest tests -q
.\.venv\Scripts\python.exe scripts\verify_package.py
~~~

CERE-12 固定种子审计为 10 个互不重复的真实照片衣物候选，报告锁定输入 SHA-256、来源和许可。三条回归输入与 CERE-11 打包资产逐字节一致。

## CPU、许可与已知边界

PyMatting 1.1.15 以 MIT 许可提供 CPU 闭式 alpha/foreground 估计；完整依赖和样例许可见 `THIRD_PARTY.md`。模型权重不在本 ZIP 中；安装器应按 `models.lock.json` 预下载、校验并随受控资源包分发。

- 粗品类来自 U2Net cloth 的 upper/lower/full 通道，不是独立分类器；用户仍可改为应用词表中的类别。
- 自动模型可能混入皮肤、头发、叠穿衣物或背景，也可能漏分。闭式 matting 能改善边缘，不能凭空重建缺失服装。
- 输入结构明显破损时返回 `MASK_STRUCTURE_UNRELIABLE`，即使平滑后的边缘指标看似正常也不会自动放行。
- 两个面积接近的鞋/配件连通域会保留；小于最大连通域 2% 的碎屑会清除。阈值与评分说明见 `docs/QUALITY_GATE.md`。
