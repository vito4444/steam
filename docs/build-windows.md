# Windows 构建

## 应用

环境：Windows x64、Node.js 20+、Python 3.11。

```powershell
npm install
npm test
npm run typecheck
npm run build
```

## CERE-12 独立运行时

源码仓库不重复携带约 726 MiB 的 ONNX 权重和 PyInstaller 运行时；发布包已内置。
推荐直接运行准备脚本。它按 `pipeline/models.lock.json` 下载两个文件到
`pipeline/models/`，校验精确字节数与 MD5，在隔离的 `pipeline/.venv/` 中安装
Python 3.11 依赖和 PyInstaller 6.15.0，并构建、`ping` 验证运行时：

```powershell
.\scripts\prepare-windows-pipeline.ps1
```

只复核已有模型而不下载或构建：

```powershell
.\scripts\prepare-windows-pipeline.ps1 -VerifyOnly
```

等价的手动 PyInstaller 命令如下：

```powershell
python -m PyInstaller --noconfirm --clean --onedir `
  --name pixelfit-pipeline `
  --paths pipeline\src `
  --hidden-import rembg `
  --hidden-import onnxruntime `
  --copy-metadata pymatting `
  --distpath pipeline\runtime `
  --workpath $env:TEMP\pfpipe-work `
  --specpath $env:TEMP `
  pipeline\entry.py
```

`pipeline/manifest.json` 中两个模型的字节数和 MD5 必须一致。用以下命令验证运行时：

```powershell
'{"command":"ping"}' | .\pipeline\runtime\pixelfit-pipeline\pixelfit-pipeline.exe
```

## 发布包

```powershell
npm run dist
```

生成 NSIS 安装器与 portable EXE。项目未配置代码签名证书，因此产物未签名；
`win.signAndEditExecutable=true` 仍写入 PixelFit 的产品名、描述与 0.4.0 文件版本资源，
但不会凭空产生 Authenticode 签名。
