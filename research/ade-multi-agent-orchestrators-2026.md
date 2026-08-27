# 与 Orca、CodeG 同类的多 Agent 编排平台：调研与对比排名

- 调研日期：2026-08-14
- 对标产品：Orca（stablyai/orca）、CodeG（xintaofei/codeg）
- 星标与仓库元数据截止：2026-08-14，来源为 `gh repo view` / 官方站点 / 第三方评测正文

## 1. 调研范围与方法

### 1.1 对标对象是什么

Orca 与 CodeG 不是 LangGraph / CrewAI / AutoGen 这类「自己写编排代码」的框架，也不是 Cursor / Devin 这类「自带一个编码 Agent」的 IDE 或云端 Agent。它们属于同一层：

**ADE / Agent Cockpit / 多 CLI Agent 编排工作台**

共同特征：

1. 不替换 Claude Code、Codex、OpenCode 等现有 CLI Agent，而是把它们装进一个指挥面。
2. 用 git worktree（或容器 / SSH 远端）给每个任务做隔离，避免并行改同一工作树。
3. 提供会话列表、分屏、diff / PR review、任务看板或 fan-out 竞速。
4. 用户自带订阅（Bring Your Own Subscription），平台本身通常不卖模型 token。

因此本报告的主排名只收「能并行调度多个现成编码 Agent」的产品。框架、单 Agent IDE、纯云端托管 Agent 放在第 7 节，不混进主榜。

### 1.2 方法与覆盖

| 层 | 实际使用 | 说明 |
| --- | --- | --- |
| GitHub | `gh repo view` / `gh search repos` | 星标、许可、更新时间、仓库描述 |
| 官方站 | WebFetch | onorca.dev、docs.codeg.app、conductor.build、nimbalyst.com、vibekanban.com |
| 第三方评测 | WebFetch | munderdiffl.in、agentsroom.dev、nimbalyst.com/blog、broomva.tech、augmentcode.com |
| 通用搜索 | WebSearch | 产品发现与交叉核对 |

本环境没有 Tavily Research、Exa、Firecrawl、agent-reach。因此没有跨站深度抓取，也没有 Reddit / 小红书 / 知乎一手帖。社交口碑只引用官方站已公开的 X 嵌入，以及评测文中的转述。冲突处并列，不伪造共识。

### 1.3 排名维度

每项 1–5 分，加权后得到综合分。分数是对公开资料的判断，不是实测基准。

| 维度 | 权重 | 含义 |
| --- | --- | --- |
| 同类契合 | 15% | 是否「包装现有 CLI Agent + 隔离 + 指挥面」，而不是自研单一 Agent |
| Agent 覆盖 | 15% | 预置 / 可插拔的 CLI Agent 数量与异构协作能力 |
| 隔离与远程 | 15% | worktree / 容器 / SSH / 云 VM，以及冲突防护 |
| 编排深度 | 15% | 人工分派、fan-out 竞速、@mention 子 Agent、看板自动流转、DAG / hive |
| Review 闭环 | 10% | diff、批注回灌、PR / Linear / GitHub、测试回传 |
| 平台完整 | 10% | macOS / Windows / Linux / 移动端 / 自托管 |
| 开源与可持续 | 10% | 许可、公司存续、是否还在日更 |
| 社区热度 | 10% | GitHub stars、融资、公开更新频率 |

星标高不等于产品更适合。Vibe Kanban 星标很高，但母公司已关停，可持续分会明显下调。

---

## 2. 对标产品画像

### 2.1 Orca（stablyai/orca）

- 仓库：https://github.com/stablyai/orca
- 官网：https://www.onorca.dev/
- 创建：2026-03-17
- Stars：45,298；Forks：3,165；许可：MIT
- 定位：Agent Development Environment（ADE）。标语是 “The AI Orchestrator for 100x builders” / “Ship 100x With The Agent IDE”。
- 公司：stablyai（YC W22）。产品免费开源，桌面 + 移动伴侣 + VPS。

核心能力（来自官网与 README）：

- 任意终端 CLI Agent 都能跑；预置 25+，包括 Claude Code、Codex、Cursor CLI、OpenCode、Grok、Copilot、Pi、Antigravity、Cline、Goose、Kimi、Qwen Code 等。
- 每个任务一个 git worktree；支持把同一 prompt fan-out 到多个 Agent，对比后合并赢家。
- Ghostty 风格 WebGL 终端、无限分屏、重启保留滚动历史。
- 内嵌 Chromium Design Mode：点击 UI 元素，把 HTML / CSS / 局部截图送进 Agent。
- SSH worktree：Agent 跑在远端机，自动重连、端口转发。
- 原生 GitHub + Linear；diff 行内批注可回灌给 Agent。
- Orca CLI：`orca worktree create`、`snapshot`、`click`、`fill`，Agent 也能驱动 Orca。
- iOS / Android 伴侣：看状态、用量、切账号、继续终端工作。
- Claude / Codex 用量与 rate-limit 追踪，账号热切换。

编排层公开资料（第三方拆解，【来源：chenxutan.com】）提到 Run / Task / Dispatch / Decision Gate 等对象，以及 Race 模式。这属于架构解读，不是官方 API 文档原文，标为转述。

短板：

- 仓库 2026-03 才创建，功能面极宽，文档与稳定性需自行验证。
- 编排仍以「人指挥舰队」为主，不是共享记忆的 hive。
- Electron 桌面壳，资源占用公开评测较少，本报告未实测。

### 2.2 CodeG（xintaofei/codeg）

- 仓库：https://github.com/xintaofei/codeg
- 文档：https://docs.codeg.app/
- 创建：2026-02-09
- Stars：2,721；Forks：337；许可：Apache-2.0
- 定位：多智能体编码工作台。桌面应用、独立服务器或 Docker；原生 iOS / Android 连到你自己的实例。

核心能力（来自文档站与 README）：

- 会话聚合：把本机 Claude Code / Codex / OpenCode / Gemini / OpenClaw / Cline / Hermes / CodeBuddy / Kimi Code / Pi / Grok 等历史会话导入同一可搜索工作区。
- 多智能体协作：主 Agent 用 `@` 点名其他类型子 Agent；子 Agent 作为独立会话并行跑，结果回流当前线程。这是 CodeG 相对 Orca 最明显的差异点。
- 内置 git worktree 并行开发。
- ACP 实时连接，文档称 20+ Agent 适配；0.22 起可自注册 ACP 兼容 Agent。
- MCP 管理中心、Skills 管理（Claude / Codex / OpenCode / Gemini / OpenClaw）。
- 工程闭环：文件树、Diff、Git、终端、项目命令。
- 聊天通道：Telegram、Lark（飞书）、iLink（微信）创建任务、审批权限。
- 自动化：composer 配置可无头 / cron 运行。
- 额外：officecli 处理 docx / xlsx / pptx；科研 skills。

注意：`yangmain/codeg` 是另一份中文描述仓库，0 star，不是本报告对标对象。

短板：

- 社区体量比 Orca / Vibe Kanban 小一个数量级。
- 公开英文评测少，产品完成度主要靠官方文档，第三方交叉验证弱。
- 路线图里「任务 DAG / 可视化流转 / 团队配置分发」在 yangmain 那份说明里仍标为规划；xintaofei 主仓已有 @mention 协作，但完整编排面板深度不如看板类产品清晰。

---

## 3. 品类地图

把市场拆成四层，避免把不同问题排进同一张榜。

```
L4  云端托管 Agent          Cursor Cloud / Devin / Codex Cloud / Terragon(已死)
L3  协调层 / Hive           Munder Difflin、qm（YC）
L2  ADE / Cockpit（本榜）    Orca、CodeG、Conductor、Emdash、Vibe Kanban、
                            Claude Squad、Nimbalyst、Mux、AgentsRoom、Parallel Code
L1  编码 Agent 本体         Claude Code、Codex、OpenCode、Cursor Agent、Amp…
L0  隔离基础设施            git worktree、Dagger container-use、Docker
```

Orca / CodeG 都在 L2。L0 是它们底下用的机制，L1 是它们调度的对象，L3/L4 解决的是另一类问题。

---

## 4. 同类产品清单

星标均为 2026-08-14 `gh repo view`。无公开主仓的产品不填星标。

### 4.1 主榜候选（L2 ADE / Cockpit）

| 产品 | 仓库 / 站点 | Stars | 许可 | 形态 | 隔离 | 代表主要 Agent | 移动端 | 备注 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Orca | stablyai/orca · onorca.dev | 45,298 | MIT | 桌面 + CLI + VPS | worktree + SSH | 25+ 任意 CLI | iOS / Android | 品类现任标杆 |
| Vibe Kanban | BloopAI/vibe-kanban · vibekanban.com | 27,790 | Apache-2.0 | 本地 Web 看板 | worktree | 多 CLI | 仅浏览器 | Bloop 2026-04-10 关停，社区维护 |
| Opcode | winfunc/opcode · opcode.sh | 22,363 | AGPL-3.0 | 桌面 GUI | 偏单会话 + 后台 Agent | Claude Code 为主 | 无 | 原 Claudia；Nimbalyst 文称停更，仓库 2026-08-14 仍有推送 |
| Claude Squad | smtg-ai/claude-squad | 8,309 | AGPL-3.0 | TUI + tmux | worktree | 多 CLI | 无 | 终端派默认选项 |
| Emdash | generalaction/emdash · emdash.com | 5,405 | Apache-2.0 | 开源 ADE | 并行 Agent（官方称 any provider） | 多 provider | 未核实 | YC W26 |
| Crystal | stravu/crystal · 指向 nimbalyst.com | 3,110 | MIT | 桌面 | worktree | Claude + Codex | 无 | 2026-02 弃用，迁 Nimbalyst |
| CodeG | xintaofei/codeg · docs.codeg.app | 2,721 | Apache-2.0 | 桌面 / Server / Docker | worktree | 12+ / ACP 自注册 | iOS / Android | @mention 异构协作、会话聚合、飞书/Telegram |
| Mux | coder/mux · mux.coder.com | 1,968 | AGPL-3.0 | 桌面 + 浏览器 | 本地 / worktree / SSH | 自有多模型 loop | 响应式 Web | Coder 出品，偏自研 Agent |
| Nimbalyst | nimbalyst/nimbalyst · nimbalyst.com | 1,480 | MIT | 桌面 + 看板 + 可视化编辑 | 可选 worktree | Claude、Codex；OpenCode/Copilot Alpha | iOS（官方）；Android 仓库描述也写了 | Crystal 后继；个人免费，Teams $20/用户/月（beta 免费） |
| Parallel Code | johannesjo/parallel-code · parallelcode.app | 970 | MIT | 桌面分屏 | worktree | Claude、Codex、Gemini | 官方 README 提手机监控 | 体量小，功能聚焦 |
| Sculptor | imbue-ai/sculptor | 216 | MIT | 桌面 | Docker 容器 | Anthropic / OpenAI 模型 Agent，不是 CLI 包装器 | 无 | Imbue；macOS Apple Silicon + Linux |
| Acepe | flazouh/acepe · acepe.dev | 89 | MIT | 原生桌面 ADE | worktree | Claude、Codex、Copilot、Cursor、OpenCode | 无 | 早期 |
| Paneflow | arthjean/paneflow · paneflow.dev | 56 | GPL-3.0 | GPU 原生分屏终端 | 分支感知 pane | 任意 shell Agent | 无 | 早期 |
| Conductor | conductor.build（闭源） | — | 专有 | macOS 原生 | worktree | Claude、Codex、Cursor、OpenCode | 无 | Melty Labs，YC S24，$22M A 轮（2026-03） |
| AgentsRoom | agentsroom.dev（无独立主仓） | — | 专有 | 桌面 + 手机 | 本地进程 | 7–9 家 provider | iOS / Android | 评测文自称；需当商业产品看 |

### 4.2 强相关但不进主榜的产品

| 产品 | 仓库 / 站点 | Stars | 为何不进主榜 |
| --- | --- | --- | --- |
| Happy | slopus/happy · happy.engineering | 23,343 | 移动 / Web 遥控器，不是并行编排工作台 |
| Omnara | omnara-ai/omnara · omnara.com | 2,723 | 手机优先远程控制；后期转向 Claude Agent SDK / Managed Agents 替代，不是 worktree ADE |
| Container Use | dagger/container-use · container-use.com | 4,009 | L0 隔离运行时，给 Agent 提供独立环境，本身不是指挥舱 |
| OpenCode | anomalyco/opencode · opencode.ai | 197,347 | L1 Agent 本体，常被 Orca / CodeG 调度 |
| Munder Difflin | 官方博客 munderdiffl.in | 未核实主仓 | L3 hive：共享记忆 + 邮箱 + GOD 编排器；作者自己的产品评测 |
| ParallelCode（parallelcode.dev） | 商业 worktree GUI | — | 更像 Git worktree 管理器，后台分派仍 waitlist |
| Terragon | — | — | 多家评测写「已死 / 已关」 |

---

## 5. 逐项对比

### 5.1 编排模型

| 产品 | 并行方式 | 谁来分派 | Agent 间协作 | 共享记忆 |
| --- | --- | --- | --- | --- |
| Orca | worktree 舰队 + fan-out /orchestrate | 人 + 编排层（Task / Race，第三方转述） | 竞速对比、合并赢家 | 无公开共享脑 |
| CodeG | worktree + 会话内 @mention | 主 Agent 委托 | 异构子 Agent 并行回流 | 会话聚合，不是语义记忆层 |
| Conductor | 每 workspace 一个 worktree | 人 | 无 | 无 |
| Vibe Kanban | 卡片进列，Agent 领任务 | 人拖看板 | 弱（状态机，不是对话） | 无 |
| Claude Squad | tmux 会话 + worktree | 人 | 无 | 无 |
| Nimbalyst | 看板会话 + 可选 worktree | 人；可链式 session | 弱 | 文件 / 会话互链 |
| Emdash | 并行多 Agent（官方） | 人 | 未核实细节 | 未核实 |
| Mux | 本地或远端隔离工作区 | 人 | 自有 loop | 未核实 |
| AgentsRoom | 磁贴 + 角色 + 团队交接 | 人 / 角色流水线 | 有「dev → QA」交接（自评） | 项目 memory（自评） |
| Munder Difflin | 角色 + 邮箱 | GOD 编排器 | 直接消息 | MemPalace（自评） |
| Sculptor | 每 Agent 一容器 | 人 | Pairing Mode 合入 | 无 |

判断：

- 「并排跑、互不踩文件」：Orca、Conductor、Claude Squad、Vibe Kanban、Nimbalyst、Parallel Code 都够用。
- 「一个任务里让 Claude 写、Codex 审」：CodeG 的 `@` 委托是公开文档里写得最清楚的。
- 「自动拆任务、自动路由、共享长期记忆」：公开资料里只有 Munder Difflin / qm 这类 hive 在讲，且多为自家博客，证据弱于 ADE 功能清单。

### 5.2 平台与部署

| 产品 | macOS | Windows | Linux | 移动 | 自托管 / 远端 |
| --- | --- | --- | --- | --- | --- |
| Orca | 是 | 是 | 是 | iOS + Android | SSH worktree、VPS |
| CodeG | 是（Tauri） | 是 | 是 | iOS + Android 连自有实例 | Server / Docker |
| Conductor | 是（多家评测写 Apple Silicon） | 否 | 否 | 否 | 官方已提 Conductor Cloud，细节未核实 |
| Vibe Kanban | 本地 Web | 本地 Web | 本地 Web | 浏览器可用 | 原云服务已关 |
| Claude Squad | 是 | 评测写主要 macOS/Linux | 是 | 否 | SSH 友好 |
| Nimbalyst | 是 | 是 | 是 | iOS（官方强调） | 本地 |
| Emdash | 开源桌面，具体安装包未核实 | 未核实 | 未核实 | 未核实 | 开源，可自建（推测） |
| Mux | 是 | 未强调 | 是 | 浏览器 | 本地 + 远端 / SSH |
| AgentsRoom | 是 | 是（2026-06 博文） | 是 | iOS + Android | Remote Fleet（自评） |
| Happy | Web | Web | Web | 原生向 | 连本机 Agent |
| Sculptor | Apple Silicon | 否 | 是 | 否 | Docker |

### 5.3 工程闭环

| 产品 | Diff / 批注 | PR | 外部任务源 | 设计 / 浏览器 | 账号与用量 |
| --- | --- | --- | --- | --- | --- |
| Orca | 行内 markdown 批注回灌 | 应用内审 PR、看 CI | GitHub、Linear | 每 worktree 一 Chromium + Design Mode | Claude / Codex 用量、热切号 |
| CodeG | 文件 Diff、冲突对比 | Git 提交窗口 | Telegram / 飞书 / 微信 | 无 Design Mode 公开描述 | 未强调 |
| Conductor | 强 diff review（多家评测） | 应用内建 PR、合并 | Linear | 无 | 用本机登录态 |
| Vibe Kanban | 看板内审代码、评论 | 与 PR 状态联动（官网） | Issue / 子 issue | 无 | 无 |
| Nimbalyst | WYSIWYG 红绿 diff，含 markdown / 图 | git / AI commit | 任务板 | 可视化编辑 mockup / 图，不是页面 Design Mode | 用现有订阅 |
| Claude Squad | 终端输出 | 依赖 gh CLI | 无 | 无 | 无 |
| Mux | 有工作区 review（评测） | 未核实 | 未核实 | 无 | 未核实 |

### 5.4 可持续性（2026 年这一品类洗过一轮）

已发生的关停 / 转向，必须进决策：

| 事件 | 时间 | 影响 |
| --- | --- | --- |
| Crystal 弃用，导向 Nimbalyst | 2026-02 | 旧 Electron 应用不再作为方向 |
| Bloop 关停，Vibe Kanban 云停 | 2026-04-10 | 27k star 项目变社区维护；个人可用，团队当骨干有风险 |
| Terragon | 2026 上半年评测称已死 | 云并行 Agent 这条线有过失败案例 |
| Opcode / Claudia | Nimbalyst 2026-06-01 文称停更 | 与仓库 2026-08-14 仍更新冲突；采用前要自己看 commit 节奏 |
| Conductor A 轮 $22M | 2026-03 | 闭源商业派里资金最厚 |
| Orca 星标从约 2 万涨到 4.5 万 | 2026-07 至 08 | 增长极快，但也意味着 API / UI 日更、跟文档容易脱节 |

---

## 6. 综合排名

### 6.1 主榜（与 Orca / CodeG 同一层）

分数为加权 1–5。未亲手跑过的产品，Review 与编排两项偏保守。

| 名次 | 产品 | 契合 | 覆盖 | 隔离 | 编排 | Review | 平台 | 可持续 | 热度 | 加权 | 一句话 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | **Orca** | 5 | 5 | 5 | 4 | 5 | 5 | 4 | 5 | **4.75** | 当前 ADE 默认答案：Agent 最全、平台最完整、开源还在猛更 |
| 2 | **CodeG** | 5 | 4 | 4 | 5 | 3 | 5 | 4 | 3 | **4.20** | 异构 @委托 + 会话聚合 + 飞书/Telegram + 自托管，中文场景最贴 |
| 3 | **Conductor** | 5 | 3 | 4 | 3 | 5 | 2 | 4 | 4 | **3.75** | Mac 上体验最顺的闭源指挥舱，融资最厚；平台锁死 |
| 4 | **Emdash** | 5 | 4 | 4 | 3 | 3 | 3 | 4 | 4 | **3.80** | YC W26 开源 ADE，「any provider」叙事接近 Orca；功能细项公开资料少于 Orca，故综合略置于 Conductor 体验分之后看场景 |
| 5 | **Nimbalyst** | 4 | 3 | 3 | 3 | 4 | 4 | 4 | 3 | **3.45** | 可视化工作区 + 看板 + iPhone；Agent 面比 Orca 窄 |
| 6 | **Claude Squad** | 4 | 4 | 4 | 2 | 2 | 3 | 4 | 4 | **3.40** | 终端党最快上手；没有 GUI / 手机 / 深度 review |
| 7 | **Vibe Kanban** | 4 | 4 | 4 | 3 | 4 | 3 | 2 | 5 | **3.65** | 看板品类定义者，星标第二；母公司已死，团队采用要打折 |
| 8 | **Mux** | 3 | 3 | 5 | 3 | 3 | 4 | 4 | 3 | **3.45** | 本地+远端隔离强，但更偏自研 Agent loop，不是「把你现有 CLI 全装进来」 |
| 9 | **AgentsRoom** | 4 | 4 | 3 | 4 | 3 | 5 | 3 | 2 | **3.50** | 桌面+手机+多项目磁贴完整，闭源且缺主仓，证据多来自自家博客 |
| 10 | **Parallel Code** | 4 | 3 | 4 | 2 | 3 | 3 | 4 | 2 | **3.15** | 小而完整的开源分屏工作台 |
| 11 | **Opcode** | 2 | 2 | 2 | 2 | 3 | 2 | 2 | 5 | **2.40** | Claude Code GUI，不是多 Agent 编排器；维护状态有争议 |
| 12 | **Acepe** | 4 | 3 | 4 | 2 | 3 | 3 | 3 | 1 | **2.95** | 方向对，体量太早 |
| 13 | **Paneflow** | 3 | 3 | 3 | 2 | 2 | 3 | 3 | 1 | **2.55** | 终端控制室雏形 |
| 14 | **Sculptor** | 2 | 2 | 5 | 2 | 3 | 2 | 3 | 2 | **2.60** | 容器隔离最好，但不是 CLI 包装 ADE |

说明：Emdash 加权 3.80，按分数应高于 Conductor。主榜名次把「公开可验证的产品完成度」和「Mac 体验口碑」拆开：

- 若只看加权分：1 Orca → 2 CodeG → 3 Emdash → 4 Conductor。
- 上表名次把 Conductor 放第 3，是因为多家独立评测（agentsroom、munderdifflin、nimbalyst blog、broomva）把它当作 Mac 体验标杆，而 Emdash 官网本次抓取超时，功能细项多为仓库描述，**完成度未核实**。采用 Emdash 前应自己打开应用核对 worktree / review / Agent 列表。

更干净的分数序（纯加权，不掺口碑修正）：

1. Orca 4.75
2. CodeG 4.20
3. Emdash 3.80
4. Conductor 3.75
5. Vibe Kanban 3.65
6. AgentsRoom 3.50
7. Nimbalyst 3.45 / Mux 3.45
8. Claude Squad 3.40
9. Parallel Code 3.15
10. Acepe 2.95
11. Sculptor 2.60
12. Paneflow 2.55
13. Opcode 2.40

### 6.2 按场景选，不要只看总分

| 你的瓶颈 | 首选 | 次选 | 不要选 |
| --- | --- | --- | --- |
| 要一个能装下所有 CLI Agent 的 ADE | **Orca** | Emdash、CodeG | Opcode、Sculptor |
| 要 Claude 写、Codex 审、同一线程回流 | **CodeG** | Orca fan-out（人来合并） | Conductor（无异构委托） |
| 只要 Mac 上最好看的 review | **Conductor** | Orca、Nimbalyst | Claude Squad |
| 终端 / SSH 服务器 | **Claude Squad** | Orca SSH worktree、Paneflow | Conductor |
| 看板管任务，不是管终端 | **Vibe Kanban**（接受社区维护） | Nimbalyst | Happy |
| 离开工位还要批权限、续跑 | **Orca 或 CodeG 移动端** | Happy、Omnara、AgentsRoom | Conductor、Claude Squad |
| 飞书 / Telegram / 微信驱动 | **CodeG** | 无同等公开竞品 | 其余多数 |
| 必须自托管、数据不出机房 | **CodeG Server/Docker** | Vibe Kanban 本地、Orca 自建桌面 | Conductor Cloud、Cursor Cloud |
| Agent 可能乱跑命令，要沙箱 | **Sculptor 或 Container Use** | Mux | 纯 worktree 方案 |
| 要 Agent 自己协调、共享记忆 | **Munder Difflin / qm**（证据弱） | 无成熟替代 | 把 ADE 误当成 hive |
| 只要手机遥控本机 Claude/Codex | **Happy** | Omnara | 为这个需求上完整 ADE |

### 6.3 对 Orca vs CodeG 的直接结论

两者是同类，不是替代关系里的「一个碾压另一个」。

| | Orca | CodeG |
| --- | --- | --- |
| 更强 | Agent 覆盖、Design Mode、SSH、GitHub/Linear、社区、日更速度 | 会话聚合、@异构协作、IM 通道、Docker 自托管、办公/科研扩展 |
| 更弱 | 中文 IM、历史会话统一检索、主从委托 | 生态声量、Design Mode、公开评测、编排可视化 |
| 适合 | 已经在用 3 个以上 CLI Agent、要舰队 + 手机盯盘 | 要多 Agent 在同一任务里协作，或要飞书/Telegram/私有化 |

同时装两个不冲突：Orca 当 ADE 外壳，CodeG 当会话总线 + 协作层。重复的是 worktree 与桌面壳，不重复的是聚合与 @委托。

---

## 7. 不要拿来和 Orca / CodeG 比的东西

这些常出现在「多 Agent 编排」搜索结果里，但问题不同。

### 7.1 框架（写代码才能编排）

LangGraph、CrewAI、Microsoft Agent Framework（原 AutoGen）、Semantic Kernel、MetaGPT、ChatDev、CAMEL、OpenAI Swarm。

它们是库。Orca / CodeG 是装好就能指挥现有 CLI 的应用。框架适合造自己的 Agent 系统；不适合「今天下午并排跑 5 个 Claude Code」。

### 7.2 单 Agent 或 IDE

Cursor、Windsurf、Zed Agent、Continue、Cline、Roo、OpenCode、Amp、Claude Code Desktop。这些是 L1。Orca 把它们当工人，不和它们抢「谁写代码」。

### 7.3 云端 Agent 平台

Cursor Cloud Agents、Devin、Codex Cloud、Factory。隔离在别人的 VM 上，按任务计费，不是「用你已有的 Claude Max / Codex 订阅在本机并行」。和 ADE 可互补：笔记本跑 Orca，长任务丢云。

### 7.4 工作流自动化

Dify、Coze、n8n、Flowise。通用 Agent / RAG / 业务流，不是 git worktree 编码舰队。

---

## 8. 证据索引

### 8.1 仓库元数据（2026-08-14，`gh repo view`）

| 仓库 | stars | forks | license | updatedAt | createdAt |
| --- | --- | --- | --- | --- | --- |
| stablyai/orca | 45298 | 3165 | MIT | 2026-08-14 | 2026-03-17 |
| BloopAI/vibe-kanban | 27790 | 2961 | Apache-2.0 | 2026-08-14 | 2025-06-14 |
| slopus/happy | 23343 | 1968 | MIT | 2026-08-14 | — |
| winfunc/opcode | 22363 | 1725 | AGPL-3.0 | 2026-08-14 | — |
| smtg-ai/claude-squad | 8309 | 602 | AGPL-3.0 | 2026-08-14 | 2025-03-09 |
| generalaction/emdash | 5405 | 559 | Apache-2.0 | 2026-08-14 | — |
| dagger/container-use | 4009 | 202 | Apache-2.0 | 2026-08-14 | — |
| stravu/crystal | 3110 | 196 | MIT | 2026-08-12 | 2025-06-05 |
| omnara-ai/omnara | 2723 | 208 | Apache-2.0 | 2026-08-14 | — |
| xintaofei/codeg | 2721 | 337 | Apache-2.0 | 2026-08-14 | 2026-02-09 |
| coder/mux | 1968 | 131 | AGPL-3.0 | 2026-08-14 | — |
| nimbalyst/nimbalyst | 1480 | 207 | MIT | 2026-08-14 | — |
| johannesjo/parallel-code | 970 | 126 | MIT | 2026-08-13 | 2026-02-18 |
| imbue-ai/sculptor | 216 | 15 | MIT | 2026-08-14 | — |
| flazouh/acepe | 89 | 7 | MIT | 2026-08-08 | 2026-03-22 |
| arthjean/paneflow | 56 | 8 | GPL-3.0 | 2026-08-10 | — |
| yangmain/codeg | 0 | 0 | Apache-2.0 | 2026-07-29 | 2026-03-07 |

### 8.2 页面来源

- Orca 官网：https://www.onorca.dev/
- Orca 仓库：https://github.com/stablyai/orca
- CodeG 文档：https://docs.codeg.app/
- CodeG 仓库：https://github.com/xintaofei/codeg
- Conductor：https://conductor.build/ ；YC：https://www.ycombinator.com/companies/conductor
- Vibe Kanban：https://vibekanban.com/
- Nimbalyst 评测（含 Crystal 更名、Bloop 关停、定价）：https://nimbalyst.com/blog/best-agent-management-tools-2026/
- Munder Difflin 分类：https://munderdiffl.in/blog/best-claude-code-multi-agent-tools/
- AgentsRoom 对比（含 Bloop 关停、Sculptor、Omnara）：https://agentsroom.dev/blog/best-multi-agent-coding-tools
- Augment Code 开源编排器综述：https://www.augmentcode.com/tools/open-source-agent-orchestrators
- broomva Agent Cockpit Wars：https://broomva.tech/writing/agent-cockpit-wars
- Conductor 中文介绍：https://codepick.dev/zh/guides/conductor-build-intro/
- Orca 中文介绍：https://dibi8.com/zh/resources/ai-tools/orca-ai-agent-ide-parallel-worktrees-2026/
- Claude Code worktree 官方文档：https://code.claude.com/docs/en/worktrees
- Emdash 仓库：https://github.com/generalaction/emdash （官网 emdash.com 本次抓取超时）

第三方评测都带产品私货（Nimbalyst、AgentsRoom、Munder Difflin、Augment Cosmos）。事实（关停日期、许可、平台）交叉后采用；「谁最好用」只当观点。

---

## 9. 缺口与未核实

1. 未安装、未实测任何一个桌面应用。分数不是跑分。
2. Emdash 官网超时，平台矩阵与 Agent 列表未核实。
3. Conductor Cloud、定价页、企业功能未逐页核对。
4. AgentsRoom、qm、Superset 缺稳定公开主仓或本次未解析到，功能按自家站点转述。
5. Opcode 是否停更：评测与仓库活跃冲突，未下结论。
6. 没有 Reddit / 小红书 / 知乎一手样本。
7. 没有 Tavily / Exa / Firecrawl 深度抓取；遗漏小众产品的概率存在。
8. orcaorchestrator.com 是另一个「Orca」：本机 Claude Code 舰队脚本，与 stablyai/orca 不是同一产品。Orca Security 是云安全，无关。

缺口：以上 8 项。主榜产品清单与星标、许可、关停事件已用 `gh` 与官方页核对；体验分与 Emdash / AgentsRoom 细项标为未核实。
