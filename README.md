# PixelFit · 桌面应用

离线优先的个人衣橱与搭配台。Windows 桌面应用（Electron），两个并存的搭配视图：

- **模特**：衣服穿在模特身上，看上身效果（CERE-11 / CERE-14）。
- **画板**：单品各自摊开、互不遮挡，看清每一件（CERE-21）。

## 安装与首次使用

PixelFit 0.4.1 提供 Windows x64 安装版与免安装版，从本仓库的 GitHub Release 下载。
可执行文件约 500 MiB（内含离线抠图模型与 Python 运行时），未放入 Git。
安装包未签名，Windows SmartScreen 可能显示警告；请先核对 Release 里的
`SHA256SUMS.txt` 再选择“仍要运行”。

### 界面布局

主界面是左衣橱 / 中模特 / 右当前搭配三栏。**模特图任何时候都不被面板盖住**：
AI 高清的生成控件是舞台**下方**的一条操作坤，展开详情时舞台变矮、人物等比缩小让位。
窗口最小宽 1024，两侧面板宽度在 1400 / 1280 / 1120 三个断点逐级收窄，
优先把宽度留给中间的模特。

首次启动选择“从照片导入”或“从商品链接导入”，也可以先查看 8 件现代日常示例。
**不配置任何云端 Key 时应用仍可完整启动、导入、整理衣橱，并使用本地分层预览；不会上传图片，
也不会产生 AI 试穿费用。**

## AI 高清试穿（可选）

应用默认使用离线分层预览，不会自动上传。切换到“AI 高清”后仍需明确点击生成并确认上传，才会调用云端服务。当前内置三个可配置 provider，供应商和自备 Key 可直接在设置页保存；Key 使用 Windows DPAPI 加密且不回显：

> **密钥安全**：真实 Key 只填写在应用设置页（由 Windows DPAPI 加密保存）或当前进程的环境变量中。不要把 Key 写进 `.env`、`.npmrc`、脚本、配置、文档或仓库内的任何文件，也不要提交含 Key 的日志或截图；下面的 `'your-key'` 只是占位符。

- `fashn`（默认）：FASHN Try-On Max，覆盖衣物、鞋、包和其他可穿戴单品。
- `aliyun`：阿里百炼 OutfitAnyone Plus，国内人民币计费，上装+下装一次生成；不支持鞋、包、配饰。
- `fal`：fal Image Apps V2，单张便宜，但公开能力只承诺 clothing；鞋、包、饰品会被列为不支持并跳过。

PowerShell 启动示例（用户自备 key）：

```powershell
$env:PIXELFIT_VTON_PROVIDER='fashn'
$env:FASHN_API_KEY='your-key'
npm run dev
```

同一人物、同一组衣物、同一 provider 配置会命中本地 SHA-256 缓存，不重复调用或计费。完整配置、能力边界、候选矩阵与隐私说明见 `docs/cloud-tryon-integration.md`、`docs/cloud-tryon-cost-privacy.md` 和 `docs/cloud-tryon-provider-evaluation.md`。

> **CERE-14 要点**：遮挡与贴合闭环。
> 遮挡关系抽成**可配置的规则表**（画序 / 互斥 / 成对挖除 / 区间守卫 / 身体遮罩，
> 见 `docs/occlusion-rules.md`），合成器只执行不判断，运行期还能被素材库里的
> `occlusion.json` 覆盖；新增身体遮罩裁切（CERE-13 的 body mask 到位即接管，
> 现在回落到底图 alpha 也能跑）；贴合改成「按衣长 / 按宽度」两路并给再互相夹；
> 画布上可直接拖动 / 滚轮缩放 / 调层级 / 塞衣角，全部记进 Look 存档。
>
> **CERE-11 / CERE-13 要点**：渲染、底模与真实素材。
> 模特使用 CERE-13 的 `base_f02`（默认）与 `base_m02` 写实底图、人体遮罩和独立锚点，
> 画布均为 1152 × 2304；手绘占位衣物全部删除，
> 衣橱里只放真实照片素材；贴合改为「按底图锚点 + 实测身体宽度」解算；
> 新增边缘羽化与接触阴影；界面从深色游戏 UI 改成浅色试衣工具质感；
> 换装渲染抽成可替换的一层（`TryOnEngine`），给 CERE-8 的 VTON 留好接口。

## 跑起来

```bash
npm install
npm run dev                              # 开发模式
npm run build && npx electron .          # 生产构建后直接跑
npm run typecheck                        # 类型检查
npm run check:layout                     # 画板自动布局的无遮挡回归检查（2000 组随机搭配）
npm run shots                            # 构建 + 真实窗口截图，输出到 shots/
npm run figures                          # 构建 + 出遮挡对比图的格子，输出到 figures/
python scripts/build_figures.py <baseline目录> <输出目录>   # 把格子拼成交付用对比图

# 导入一个素材包（格式见 docs/asset-contract.md）
npx electron . --ingest <pack 目录>
npm run build && npx electron . --ingest <pack 目录> --shots
```

完整 Windows 发布构建需要先准备锁定的本地抠图模型与 Python 运行时：

```powershell
npm ci
.\scripts\prepare-windows-pipeline.ps1
npm test
npm run typecheck
npm run dist
```

准备脚本按 `pipeline/models.lock.json` 下载并校验两个 ONNX 文件，在
`pipeline/.venv/` 中安装锁定依赖，构建 `pipeline/runtime/`，最后执行运行时 `ping`。
这些可复建的大文件均被 `.gitignore` 排除；详细先决条件与手动命令见
`docs/build-windows.md`。

素材库位置是 `%APPDATA%/PixelFit/`，用 `PIXELFIT_ROOT` 可以指到别处
（截图与演示跑独立数据用，`--shots` 的空状态场景会真的清库，不该拿用户的素材库当试验田）。**应用不自带任何手绘占位单品**；
首次启动会幂等导入版本化的 **8 件日常示例素材**，它们在衣橱中始终带“示例”标记，不会冒充个人导入。
首次引导优先提供两个真实入口：从本机选择衣物照片，或粘贴公开商品链接；也可选“先看看示例”进入衣橱。照片只在本地预处理并经过质量门，只有通过的单品才入库；被拒绝的候选保持在衣橱之外，不会被静默当作成功。可先在外部去背景或修补 alpha，再通过手动入口导入透明 PNG / WebP。

示例包升级是窄范围、可追溯的迁移：程序只删除上一版 marker 中记录、且已不在新包内的 ID。个人照片、商品链接和手动导入从不靠扫描或推断判定，不会被该迁移删除。

⚠️ `npm run shots` 的最后一个场景会**真的清空素材库**（为了截真实空状态）。
所以顺序是：先 `--ingest`，再 `npm run figures`，最后 `npm run shots`；
要重跑就再 `--ingest` 一次。`--ingest ... --exit` 只导入不开界面。

## 目录

```
app/
  src/main/       Electron 主进程：窗口、IPC、落盘存储、素材包导入、截图脚本
  src/preload/    contextBridge，渲染进程只认这一个 API 面
  src/shared/     规范与类型（槽位/z-index/锚点/贴合规则、asset & look 结构、IPC 契约）
  src/renderer/   React 界面
    src/render/     底图轮廓实测 + 贴合解算 + 遮挡求解 + 身体遮罩 + Canvas2D 合成器
    src/tryon/      TryOnEngine 接口 / 分层贴图引擎 / VTON 接入点
    src/features/   wardrobe / stage / dressing / board / views
    src/features/board/  搭配画板：自动布局、画布绘制、编辑交互、Look 保存
    src/state/      全局状态（搭配、撤销栈、对比、筛选、引擎选择）
  assets/base/    写实模特底图包（S/M/L，CERE-6 交付）
  scripts/        底图包生成脚本（从 CERE-6 视觉套件转换）
  docs/           遮挡规则表、渲染契约、素材包契约、管线契约
```

## 关键实现

**画布与贴合。** 画布尺寸与锚点全部读自底图包的 `manifest.json`，应用不再假设
固定画布。衣物缩放由「实测身体宽度 ÷ 素材对应段宽度」决定 —— 真实照片素材的
像素尺寸毫无规律，只有身体尺寸能当基准。下装 / 连衣裙再按 `attributes.length`
做衣长校正，宽度差额用 ±12% 以内的横向形变吸收。细节见 `docs/render-contract.md`。

**不做任何风格化。** 不描边、不量化、不改色。渲染层只做三件事：贴合、边缘羽化、
接触阴影，且都只作用于 alpha 与交界处。舞台工具栏的「原始叠图 / 贴合处理」开关
可以直接看处理前后的差别。

**渲染是可替换的一层。** 界面只依赖 `TryOnEngine` 接口。CERE-8 的 VTON 结论落地后，
实现 `VtonBackend` 并 `registerVtonBackend(impl)` 即可接管渲染，界面与素材库不用改。

**遮挡是数据不是代码。** 五张规则表在 `src/shared/occlusion.ts`：画序、互斥、
成对挖除、区间守卫、身体遮罩策略。合成器只执行不判断，加品类只动表。求解与执行
分两轮（先各自裁，再互相挖）—— 因为遮挡里 z 高挖 z 低（裙挖打底裤）和 z 低挖
z 高（裤挖鞋）同时存在，没有任何单一顺序能同时满足。完整表见
`docs/occlusion-rules.md`。

**图层。** z 表在 `src/shared/spec.ts`，CERE-14 按 issue 要求调了三处：外套盖上装、
袜在裤之上、鞋在袜之上；「裤脚盖鞋帮」方向与第三条相反，靠成对挖除实现。右侧面板
每行带 z 值和单独隐藏，贴合页还会列出这一件正在被哪几条规则裁 —— 遮挡一旦看不见
就会被当成渲染 bug。

**搭配画板。** 从衣橱点单品即加入画板，自动排成两列：主列外套 → 上装 / 连衣裙 →
下装 → 鞋，副列包与配饰。**默认布局不允许任何两件互相遮挡** —— 纵向排列天然不叠，
界面右上角的「布局自检」用 `findOverlaps()` 实时判定并显示，不靠肉眼。
「穿着感重叠」滑杆默认 0，拉高也只让上装衣摆压住下装腰头 12% 以内。
拖拽 / 缩放 / 旋转 / 层级 / 删除 / 撤销重做齐全，画布背景六种，可加标题文字，
成图可导出 2× / 3× 高清。保存为 Look 时封面就是拼贴成图，带工作 / 休闲 / 约会 /
运动 / 度假 / 旅游 / 居家七类场景标签，Look 库按标签筛选。细节见
`docs/board-contract.md`。


**存储。** `%APPDATA%/PixelFit/library/` 下每件素材一个目录
（`meta.json` + `cutout.png` + `thumb.png`）。文件是唯一真相，索引可全量重建；
备份=复制目录。写入一律「先写 `.tmp` → 原子 rename」，失败整件回滚不留孤儿目录。
删除素材会同步清掉引用它的 Look 槽位。

## 接口约定

- 素材元数据见 `src/shared/types.ts`，`schema_version` 升到 3：新增 `landmarks`，
  `fit` 由绝对缩放改成**相对自动解的微调**（位移单位是肩宽）。schema 2 的老素材
  载入时会把 `fit.scale` 归一化成 1。
- 画板数据结构与无遮挡约束：`docs/board-contract.md`（CERE-21）。
  `Look` 上新增 `kind` 与 `board` 两个可选字段，老 Look 不用迁移。
- 素材包与底图包接入：`docs/asset-contract.md`（CERE-10）。
- 渲染层做什么、上限在哪：`docs/render-contract.md`。
- CERE-12 本地管线与 fail-closed 准入：`src/main/pipeline.ts`、`docs/pipeline-contract.md`。
- CERE-9 商品链接解析：`src/main/link-import/`；登录墙不对抗，明确回落手动取图。

## 已知边界

- **分层贴图有天花板**：素材是真人穿着时拍的，透视、褶皱、光照方向与模特对不齐，
  贴纸感压得下去但消不掉。要消掉得换生成式试穿（CERE-8）。**逐类实测结论见渲染
  契约 §4.2/§4.3** —— 哪些搭配能看、哪些无论怎么调都不行，写清楚了。
- **身体遮罩已接 CERE-13**：F02 / M02 各自使用正式 body mask；外部底图包仍可通过
  manifest 的 `mask` 字段接管。
- **不透明度补偿是权宜**：CERE-10 有几件抠图整件半透明，渲染层按 alpha 上限补了
  一道。根因在素材质检（CERE-12），素材做实后这段代码空跑。
- 照片自动识别在本机使用 `CPUExecutionProvider`，首次推理约几十秒；质量门拒绝的候选
  只进 quarantine，不会伪装成已导入。当前 UI 提供透明底图片作为人工修补后的兜底入口。
- 商品链接只有公开 SSR 商品页可自动取图；淘宝 / 京东等登录墙按 CERE-9 的合规边界
  回落到“保存主图后手动导入”，不绕过登录或风控。
- 仅一个正面站姿（`pose: front_idle`）。
- 画板拼贴的质量上限是抠图质量：素材边缘残留的白底在纸色背景上看得见，
  归素材管线（CERE-12 / CERE-13）处理，画板不做二次抠图。
- 画板暂无模版（预设版式）与多画板并存，当前一次编辑一块画板。
