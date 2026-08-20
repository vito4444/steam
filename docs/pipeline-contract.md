# CERE-12 本地素材管线契约

应用把 CERE-12 作为独立 Windows 子进程接入。管线固定使用 UTF-8 JSON-lines，
Python/native 依赖与 Electron 主进程隔离。

## 资源与探测

查找顺序：`PIXELFIT_PIPELINE`、`resources/pipeline`、`<appPath>/pipeline`、
`<appPath>/../pipeline`。`manifest.json` 指向独立 EXE，并列出两个锁定模型的字节数。
运行时先 `ping`；只有 EXE 可启动且模型尺寸完整，UI 才显示自动识别可用。
模型在真正推理前仍由 CERE-12 校验 MD5，错误时返回
`MODEL_MISSING` / `MODEL_INTEGRITY_FAILED`，绝不在线下载。

## 自动导入

应用发送：

```json
{"command":"analyze","image_path":"C:\\photo.jpg","import_id":"import-...","output_dir":"C:\\...\\pipeline-imports","models_dir":"C:\\...\\models"}
```

管线执行 BiRefNet-general-lite 人体裁剪、U2Net cloth 粗分类、闭式 alpha matting、
去色边和质量评分。应用只接收同时满足以下条件的记录：

- `admission.allowed === true`
- `auto_file` 非空
- `auto_file` 解析后仍位于本次 import 目录内

拒绝项留在 `quarantine/`，不会写入衣橱。管线品类映射为：
`upper-body → top`、`bottoms → bottom`、`outerwear → outer`、
`dress-or-full-body → dress`、`shoes → shoe`。

## 本地与隐私边界

- 推理 provider 固定 `CPUExecutionProvider`，环境同时清空 `CUDA_VISIBLE_DEVICES`。
- 原图与中间结果只落用户本机；自动导入不调用付费 API。
- 两个 ONNX 权重与运行时随 Windows 包分发，锁定信息见 `pipeline/models.lock.json`。
- 已有透明底 PNG / WebP 可走手动入口；不透明图片会被拒绝并提示改走照片识别。
- CERE-12 的编辑 session 协议保留在管线中；当前桌面 UI 以“修补后透明图再导入”为人工兜底。
