# CERE-26 验收矩阵（PixelFit 0.4.0）

日期：2026-08-21（Asia/Tokyo）  交付分支：`agent/codex2/cere-26-github`

| CERE-26 验收项 | 结果 | 可复核证据 / 边界 |
| --- | --- | --- |
| 发布自动化门禁 | 通过 | 本矩阵随源代码进入 GitHub PR；`npm test`：15 个文件、109/109 通过；`npm run typecheck`、`npm run build`、`npm run check:layout` 全部退出 0。布局检查覆盖 2,000 组 / 10,088 件，报告无遮挡、无越界、主列顺序正确。 |
| GitHub 源码与可复现构建 | 通过 | 以仓库根目录结构交付 PixelFit 0.4.0；`.gitignore` 排除密钥、模型、运行时和发布二进制。`scripts/prepare-windows-pipeline.ps1` 按锁定清单下载、校验 MD5、建立 Python 环境并生成管线运行时；README 与 `docs/build-windows.md` 给出从干净 clone 构建的命令。 |
| Windows 0.4.0 双目标 | 通过 | 保持 `signAndEditExecutable: true` 的 `npm run dist` 退出 0，耗时 57,312 ms；生成 `PixelFit-Setup-0.4.0-x64.exe`（526,757,255 bytes）和 `PixelFit-Portable-0.4.0-x64.exe`（526,529,608 bytes）。 |
| EXE 身份与版本资源 | 通过 | 解包 `dist/win-unpacked/PixelFit.exe` 和 clean-installed `PixelFit.exe` 均报告 `ProductName=PixelFit`、`FileDescription=PixelFit — 离线个人衣橱与搭配台（左衣橱 / 右模特）`、`ProductVersion=0.4.0.0`、`FileVersion=0.4.0`；不再显示 Electron `33.4.11` 身份/版本。 |
| 打包本地管线 | 通过 | 已验证包内 `pipeline/runtime/pixelfit-pipeline/pixelfit-pipeline.exe`（20,121,625 bytes）和两个锁定模型：`birefnet-general-lite.onnx`（224,005,088 bytes）、`u2net_cloth_seg.onnx`（176,194,565 bytes）。已安装运行时 `ping` 返回 `{"ok":true,"result":{"service":"pixelfit-pipeline","schema_version":"2.0"}}`，退出 0，2,642 ms。 |
| 八件示例、无 dress 出货不变量 | 通过 | `app.asar` 清单包含恰 8 个 `assets/wardrobe/items/*.png`，dress 匹配为 0。CERE-13 的 `c10n_dress_1087` 只存在于 `fixtures/cere26/`，仅供证据用，未加入出货衣橱。 |
| NSIS 安装 | 部分通过 | 历史直接仓库路径 `release/cere26/clean-install` 的最长投影路径为 282 字符，NSIS 在 5,230 ms 后以 `0xC0000005` 退出（Windows MAX_PATH 限制）；未修改产品代码。本轮使用新的批准短根 clean install 成功：退出 0、12,002 ms，且已安装 EXE 元数据为 PixelFit 0.4.0。 |
| 已安装应用、无 Key 的五张真窗口截图 | 通过 | 使用新的 fresh `PIXELFIT_ROOT` 与 fresh Chromium profile，移除 `DASHSCOPE_API_KEY`，一次前台运行退出 0（11,639 ms），生成 exactly `onboarding`、`main`、`board`、`dressing`、`settings`。每张均为 2280×1425，逐张原尺寸检查无白屏、崩溃页或堆栈。`settings` 显示本地“分层贴图 可用”且云端需要 Key；`main` 显示即时本地预览。 |
| 无 Key 本地预览 smoke | 通过 | 已安装版新的 fresh root 的 `main` screenshot smoke：退出 0，3,826 ms；本地分层模特预览可见。 |
| Portable smoke | 通过 | Portable 新的 fresh root 的 `onboarding` screenshot smoke：退出 0，13,497 ms；首屏、照片/链接 CTA 与 8 件示例披露完整。 |
| 可分卷交付与重组 | 通过 | 两个 EXE 均按最大 45 MiB（47,185,920 bytes）切为 12 个带序号 parts。`SHA256SUMS.txt` 含原始/每 part 精确 hash 和 count；`REASSEMBLE.ps1` 重组到 `final-reassembled-5faf06f/` 后两个 hash 均匹配。脚本还验证了：已有匹配目标会 skip，已有 7-byte 不匹配目标保留且拒绝覆盖，脚本自己创建但最终 hash 不匹配的文件会删除。 |
| Aliyun 三张试穿比较 | 通过 | 使用 `aitryon-plus` 顺序执行 exactly 3 次，无重试：top `usage.image_count=1`、123,685 ms、task `658dd9bf-6491-4847-bf84-a1655661d286`、response request `4215f39f-6ead-9d33-b251-8d5b220aa23a`；separates `1`、101,573 ms、task `76803d16-2f24-40ff-857e-a5af385fbafb`、response request `e1e090d1-99e5-9f1e-9f74-20ebfad1c544`；dress `1`、100,376 ms、task `3452ed40-6745-4a11-933e-47a41169296a`、response request `52e0d03a-6c58-987c-96e5-33ffed06a74f`。合计 3 张、325,634 ms、按 ¥0.50/张计最大/实际金额 ¥1.50。 |
| CERE-26 三联图证据 | 通过 | `top.png`、`separates.png`、`dress.png` 均为 3420×1920，分别展示原始人物 / 本地分层试穿 / Aliyun 生成试穿；三者 SHA-256 互不相同并已逐张原尺寸目检。无 Key 时证据脚本拒绝提交云端任务，本地分层仍可用。 |
| 图片、凭据与用户资料边界 | 通过 | Key 仅存在于单个前台进程内，调用后清空，未写入文件或 Git；`results.json` 只保存 hash、计数、耗时和 provider id。三张输入衣物为项目固定 fixture，仅向 Aliyun 上传完成本次获批的 3 个任务；未部署或外发其他资料。 |
| 签名与 SmartScreen | 部分通过 | electron-builder 明确报告未识别 signing info 并跳过签名；交付 EXE 未签名，Windows SmartScreen 可能提示警告。 |
| 本地分层表现边界 | 部分通过 | 预览可用但不声称生成式试穿质量：top 保留衣架且未贴合身体；separates 仍有衣架、裤脚和尺度失真；dress 露出灰色 camisole、偏窄且错位。Aliyun 三例明显改善人体拟合，但 top 被改成露脐款且补出了紫色下装，存在款式/语义漂移。 |

## 截图证据

| 文件 | 尺寸 | 字节 | SHA-256 |
| --- | ---: | ---: | --- |
| `evidence/cere26/windows/installed-no-key-onboarding.png` | 2280×1425 | 198,157 | `5ed47668f9222fd527a2e3662e0a21fe2b47038a941fe9ebe1393c826ec3b152` |
| `evidence/cere26/windows/installed-no-key-main.png` | 2280×1425 | 972,789 | `5a2af347197ced9a2764377e937603a2129bc7f6790aa88c9cb8dfd629c5ee0c` |
| `evidence/cere26/windows/installed-no-key-board.png` | 2280×1425 | 1,356,758 | `03c47f48910932b052e024bf676cc4a11a2a0b8cc0955fd4ca565439559c2852` |
| `evidence/cere26/windows/installed-no-key-dressing.png` | 2280×1425 | 933,498 | `e8040b6545b519ce56d76368858f2f3ab28e19ee530d4c442975af7b9f6cf6cc` |
| `evidence/cere26/windows/installed-no-key-settings.png` | 2280×1425 | 198,253 | `3420b2f4fb58c85b03bd2899103d9288a1e61029c2d0efbbeb88b0ad7f7adefa` |

## 交付校验值

| 目标 | 原始 SHA-256 | Parts |
| --- | --- | ---: |
| `PixelFit-Setup-0.4.0-x64.exe` | `f32062be8340aa6923012dcc38f3ae286ac6b49341bf75dad763bcfce2043b4b` | 12 |
| `PixelFit-Portable-0.4.0-x64.exe` | `67e72ad1ff755c2c9841399fdd8767b7b39444ade8d77d88d7866475eb7912ae` | 12 |

完整 part checksum、重组命令和本地日志均见 `release/cere26/`；该目录的二进制、parts、截图与日志为本地附件证据，不进入 Git。

## 三联图校验值

| 文件 | 尺寸 | 字节 | SHA-256 |
| --- | ---: | ---: | --- |
| `evidence/cere26/triptychs/top.png` | 3420×1920 | 2,570,663 | `a5f82e802819a18d59359d15b090634109a57ede2a9fe3fa27f69a222bf0c662` |
| `evidence/cere26/triptychs/separates.png` | 3420×1920 | 3,008,426 | `331ea9ee30e4a840fc90849e3b844b61ba2cda3491110bfd387300f0f67833d8` |
| `evidence/cere26/triptychs/dress.png` | 3420×1920 | 2,357,681 | `e171a0e4bd311aee4bff44ed1e7832d0a5470788350df8cb118e0881d772b104` |
