# 第三方资产来源与许可登记

Steam 发行要求项目中所有资产的权利清晰可证。本文件登记项目使用的每一项非自制资产。

**规则：**

1. 不使用任何来源不明的资产。
2. 每引入一项第三方资产，必须在下表登记来源 URL、许可类型与引入日期。
3. 优先顺序：代码程序化生成 > CC0 / 公有领域 > 明确购买或授权的资产。
4. AI 生成的概念图仅用于内部方向对齐与文档说明，**不作为游戏内资产使用**。

## 游戏内资产

| 资产 | 类型 | 来源 | 许可 | 引入日期 |
| --- | --- | --- | --- | --- |
| `Assets/Resources/Fonts/DroidSansFallback.ttf` | 字体 | Debian 包 `fonts-droid-fallback`，上游 https://android.googlesource.com/platform/frameworks/base/ | Apache License 2.0（许可全文见同目录 `DroidSansFallback-LICENSE.txt`） | 2026-08-13 |
| `Assets/Resources/Voice/*.ogg` | 语音 | 由 `tools/generate_voice.sh` 用 espeak-ng 合成后经 ffmpeg 处理 | 自制，无第三方权利 | 2026-08-13 |

字体是唯一的第三方二进制资产。它必须随游戏打包，因为发行版不能指望玩家机器上
装了中文字体；实测在 Player 里也拿不到系统字体。

除此之外，工程内的全部几何体、材质、贴图、音效与场景均由代码在运行时生成，
不含任何建模、绘图或录音资产。

## 文档用图

| 文件 | 用途 | 生成方式 | 是否入游戏 |
| --- | --- | --- | --- |
| `docs/concepts/images/maner-concept-A-control-cabin.jpg` | 方案 A 视角与色调基准 | AI 生成 | 否 |
| `docs/concepts/images/maner-concept-B-coop-extraction.jpg` | 方案 B 视角与色调基准 | AI 生成 | 否 |
| `docs/concepts/images/maner-concept-C-automation-factory.jpg` | 方案 C 视角与色调基准 | AI 生成 | 否 |
| `docs/concepts/images/maner-concept-D-cozy-repair.jpg` | 方案 D 视角与色调基准 | AI 生成 | 否 |
| `docs/concepts/images/maner-concept-E-physics-climb.jpg` | 方案 E 视角与色调基准 | AI 生成 | 否 |
