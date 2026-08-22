# 应用内自动检查更新（CERE-59）

成员的原话是「能否在软件里加入自动检测更新，这样就不用每次重新从 GitHub 上下载了」。
问题不只是「要不要有更新按钮」——是**每发一版都要下 500 MB**。所以这条需求有两半：
接更新机制，和把包体降下来。两半都做了，下面是做法和实测数字。

## 1. 包体：模型拆成独立资源包

0.4.3 的安装包 496.4 MB。拆开看，最大的一块是随包分发的两个 ONNX 权重：

| 内容 | 未压缩 | 说明 |
| --- | --- | --- |
| `birefnet-general-lite.onnx` | 213.6 MB | 主体分割 |
| `u2net_cloth_seg.onnx` | 168.0 MB | 衣物分割 |
| PyInstaller 运行时 `pipeline/runtime` | 308 MB | Python + onnxruntime + scipy… |
| Electron + 界面 + 内置素材 | 约 200 MB | |

两个 .onnx 合计 381.6 MB，而且是**已压缩的二进制**——NSIS 的 LZMA 压不动它们，
几乎 1:1 变成安装包体积。它们还有一个性质：**版本之间根本不变**，由
`pipeline/models.lock.json` 按字节数 + MD5 钉死。每发一版重下一遍是纯浪费。

所以从 0.4.4 起，`electron-builder.yml` 的 `extraResources` 不再包含 `models/**`，
两个权重改为按需下载：

- 镜像在本仓库的 [`models-v1`](https://github.com/vito4444/steam/releases/tag/models-v1) tag，
  与应用版本解耦；只有 `models.lock.json` 变了才发 `models-vN`。
- 镜像不可用时自动回落到 `models.lock.json` 里记的上游地址（rembg v0.0.0 的发布产物）。
- 落盘在 `%APPDATA%/PixelFit/model-pack/`，下载完按字节数 + MD5 校验，
  校验不过就删掉重来，绝不把半截文件当成「已就绪」。
- 设置页「离线识别模型」卡片显示进度、落盘位置和每个文件的状态。

**运行时（308 MB）有意留在安装包里**：它会随 Python 依赖变，但变化很小，
blockmap 差分下载对它很有效；拆出去只会把首次安装拆成两段长下载，不划算。

结果：

| | 安装包 | 免安装包 |
| --- | --- | --- |
| 0.4.3 | 496.4 MB | 496.1 MB |
| 0.4.4 | **184.2 MB** | 184.0 MB |

降幅 **62.9%**。模型那 381.6 MB 一辈子只下一次。

### 老用户不会被凭空加一次下载

`resolveModelsDir()` 先看安装目录里自带的 `pipeline/models`，完整就直接用。
0.4.3 装出来的目录、以及开发机上跑过 `prepare-windows-pipeline.ps1` 的仓库，
都是这个布局，**一个字节都不会下载**。只有自带的缺失或不完整才走资源包。

Python 侧完全没有改动：`models_dir` 本来就是管线请求里的一个参数。

## 2. 更新机制

`electron-updater` + GitHub provider，更新源 `vito4444/steam`。仓库是 public，
不需要 token。

- **启动时静默检查一次**，默认开，设置页可关。
- 设置页有「立即检查更新」。
- 发现新版本时弹提示，写明**版本号、更新说明、包大小**，下不下载由用户点。
  不静默下载，不静默安装。
- 下载显示进度（已下 / 总量 / 百分比 / 速度）。
- 下载完提示重启安装；点了才装。

### 发布产物

`npm run dist` 现在会产出（`publish` 配置存在才有 `latest.yml`）：

```
PixelFit-Setup-0.4.4-x64.exe
PixelFit-Setup-0.4.4-x64.exe.blockmap   ← 差分下载靠它
PixelFit-Portable-0.4.4-x64.exe
latest.yml                               ← electron-updater 读这个
```

**这四个文件必须全部上传到 Release。** 少了 `latest.yml`，客户端检查更新会
404；少了 `.blockmap`，差分下载会退回全量。

## 3. 差分下载

`nsis.differentialPackage: true`（默认值，显式写出来）让 electron-builder
把 NSIS 内嵌的 7z 归档按块对齐，并生成 `.blockmap`。更新时 electron-updater
下载新旧两份 blockmap，逐块比对，只对变化的块发 HTTP range 请求。

### 它什么时候**不**生效

差分需要一份可比对的**旧安装包**，位置是 `%LOCALAPPDATA%/pixelfit-updater/pending/installer.exe`
——也就是**上一次更新时由应用自己下载的那个安装包**。因此：

- 从 GitHub 手动下载安装的版本，下一次更新是**全量**（本机没有那份缓存）。
- 0.4.3 → 0.4.4 必然是全量：0.4.3 里根本没有更新功能，而且 0.4.3 的 Release
  也没有 `.blockmap`。好消息是这次全量只有 184 MB，不是 496 MB。
- 0.4.4 → 0.4.5 起，只要上一版是通过应用内更新装的，差分才真正生效。

界面上如实显示这一点：没生效时不写「差分下载生效」，而是写明为什么这次是全量。

### 自己复算

`scripts/blockmap-diff.ts` 直接调用 electron-updater 内部的 `computeOperations`
——和应用运行时同一份代码——离线算出两个版本之间要下载多少字节：

```powershell
npm run blockmap:diff -- `
  https://github.com/vito4444/steam/releases/download/v0.4.4/PixelFit-Setup-0.4.4-x64.exe.blockmap `
  https://github.com/vito4444/steam/releases/download/v0.4.5/PixelFit-Setup-0.4.5-x64.exe.blockmap
```

## 4. Portable（免安装版）

免安装版走不了 NSIS 的原地覆盖安装。检测方式是 electron-builder 的 portable
启动器设的 `PORTABLE_EXECUTABLE_FILE` 环境变量。

Portable 用户点更新时：

1. 直接把新的 `PixelFit-Portable-<版本>-x64.exe` 下到**当前 exe 所在目录**；
   那里不可写（U 盘、Program Files）就退到「下载」目录。
2. 下载进度照常显示。
3. 下完提示「关掉当前窗口后用新文件替换旧的」，并提供「在资源管理器中打开」。

不去动正在运行的 exe，也不会点了没反应。免安装版没有差分下载——整包替换，
界面上直接写明，不假装有。

## 5. 隐私

检查更新只向 GitHub 发一个 GET，取 `latest.yml` 这一个版本信息文件。
**不上传任何本地数据**：不发送素材库、照片、设置或任何标识信息，也不做使用统计。

**默认开**。理由：这条需求本身就是为了不再手动去 Releases 页面下载，默认关
等于功能不存在；而检查是只读的、静默的，发现新版也只是弹一个提示，
下载和安装都还要用户点，代价只有一次 HTTP GET。不想让它联网的人在设置页
一键关掉，关掉之后应用不会为了更新访问网络，手动检查按钮仍然可用。

## 6. 未签名与 SmartScreen

应用没有购买代码签名证书（成员的决定，不为此买证书）。因此：

- 从 GitHub 手动下载安装包时，浏览器和 Windows Defender SmartScreen 会提示
  「未知发布者 / Windows 已保护你的电脑」。这是未签名程序的正常表现。
- 更新提示弹窗里固定显示这段说明，并告诉用户怎么继续（「更多信息」→「仍要运行」）。

应用内更新走的安装器是由应用自己下载的，不带 Mark-of-the-Web，实际观察到的
行为见 PR 里的实测记录；无论是否弹出，提示文案都如实写明可能会弹。

## 7. 取证

`PixelFit.exe --update-evidence` 走完整流程并对**真窗口** capturePage：
检测到新版 → 下载进度 → 下载完成，同时把 `window.pixelfit.update.state()`
（含差分下载的实际字节数）写成 `report.json`。
`PixelFit.exe --version-proof` 在装完之后再跑一次，证明版本号真的变了。

它点的是界面上那几个真按钮，更新源是真的 Releases，下载的是真的安装包
——和用户手点走同一条代码路径，区别只是「谁来点」。
