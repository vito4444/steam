# Steam 市场调研报告（2026 年 8 月）

> 项目代号：**maner**
> 调研日期：2026-08-13
> 调研目的：为一款面向 Steam 平台发行的 Windows 3D 游戏确定品类方向、画面视角、定价区间与可行的开发规模。
> 数据口径说明：本报告中的所有数字均来自下方列出的公开来源，并在正文中就近标注。凡属推测或来自间接估算的内容，均已显式标注为「推测」或「估算」。

---

## 1. 结论摘要

1. **Steam 的分布极度不平等，绝大多数新游戏收入接近于零。** 2026 年上半年 Steam 上线新游戏接近 12,000 款，总营收超过 19 亿美元，但收入中位数只有 196 美元，67% 的游戏收入低于 1,000 美元，只有 145 款（1.2%）超过 100 万美元；第 95 百分位的收入是 13 万美元，第 30 百分位只有 21 美元。【App2top，2026 上半年 Steam 品类基准报告】
   这意味着：**「做一个还行的游戏」在统计上等价于失败。** 项目必须在选题阶段就绑定一个具体、狭窄、有明确买家的品类，而不是做一个泛泛的「3D 动作冒险」。

2. **宽标签是最差的竞争位置，窄标签才有中位收入。** Singleplayer、Indie 这类标签下各有数万款游戏，中位收入约在一千美元量级；而 Extraction Shooter 这样只有约 106 款游戏的窄标签，中位收入在数万美元量级（约 33,000 美元，中位价 9.99 美元）。Open World Survival Craft 有 710 款，中位收入约 22,100 美元。规律非常一致：**描述「这款游戏具体是什么」的标签，胜过描述「它大致像什么」的标签。**【profitable.app，Steam Tags by Revenue，2026 年 8 月】

3. **按中位收入排序，工厂/自动化与殖民地/管理模拟是独立开发者收益最高的品类。** 达到 100 条以上评价的工厂与自动化类游戏，典型收入区间为 20 万至 50 万美元以上；殖民地模拟与管理类紧随其后，为 15 万至 40 万美元。原因是这类玩家会在单款游戏里投入数百小时、留下评价、并主动向他人安利，形成「高参与度 → 高评价数 → 算法推荐 → 更多销量」的正循环。【Steam Page Analyzer，Steam Revenue by Genre 2026】

4. **合作（Co-op）是 2026 年最强的增长引擎，但它买的是「社交」而不是「机制深度」。** Co-op 标签的中位拥有量约 55 万，为全平台各标签最高（对比 Singleplayer 约 35 万）。【dev.to，The State of Steam 2026】近两年的爆款 PEAK、R.E.P.O.、Lethal Company、Content Warning、Webfishing 被玩家统称为 "Friendslop"，其共同点是低价、机制简单直观、专门设计用来制造可分享的搞笑与戏剧性瞬间，从而驱动直播与口碑传播。【Game Developer，What developers can learn from the indie social co-op】

5. **合作生存的商业效率被反复验证。** PEAK 由 Aggro Crab 与 Landfall 合作开发，主体开发时间约四周，总成本估算低于 20 万美元，上线数小时回本，一周内 160 万份，截至 2026 年 8 月销量接近 1,000 万份（Alinea Analytics 估算），峰值同时在线 170,759 人。R.E.P.O. 是第一款达到千万级销量的「预算独立游戏」。【Game Developer，How co-op climbing hit Peak achieved 2 million sales for less than $200,000；Dexerto；Game Developer 社交合作分析】

6. **「单场景 + 高密度机械交互」是 2026 年被验证的小团队路径。** IRON NEST: Heavy Turret Simulator 由两人（Nick Nieuwoudt、Dominik Latos）自研自发，2026 年 8 月 6 日上线，定价 19.99 美元，首周即进入 Steam 周销榜第 4 名。玩法是第一人称手动操作一座巨型柴油朋克炮塔：调刻度盘、转瞄准轮、算弹道、从 30 种弹药中选型、截听无线电、判读航拍侦察照片、执行火力任务。【vgchartz Week 33 2026；GameGrin 周榜；IRON NEST 官方商店描述与预告片】
   这条路径的关键价值在于：**它把制作成本从「广度」转移到了「深度」——只有一个场景，但那个场景里的每一个物件都可交互。** 对美术产能受限的团队，这是最优的成本结构。

7. **Cozy 修理/整理类同样在榜。** ReStory: Chill Electronics Repairs（开发 Mandragora，发行 tinyBuild）2026 年 8 月 6 日上线，标签为 Job Simulator / Cozy / Management，首周登上销榜第 5 名。【vgchartz；GameGrin】

8. **定价梯度清晰。** Open World 是中位价最高的社区标签（付费游戏中位 11.99 美元），Multiplayer、Co-op、Simulation、Story Rich 在 9 至 10 美元区间，Puzzle、Casual、Indie 在 4.99 美元。【dev.to，The State of Steam 2026】
   实际榜单上的新品定价为：Big Walk 19.99、IRON NEST 19.99、Mistfall Hunter 24.99、Machine Party 7.99、PEAK 7.99（当前 50% 折扣至 3.99）。【Steam Global Top Sellers 页面，2026-08-13 抓取】

---

## 2. 榜单快照

### 2.1 Steam 周销榜（第 33 周，截至 2026-08-11，按营收，不含免费游戏）

| 排名 | 游戏 | 状态 |
| --- | --- | --- |
| 1 | Big Walk | 新上榜 |
| 2 | Marvel Tōkon: Fighting Souls | 新上榜 |
| 3 | Cyberpunk 2077 | 下降 1 位 |
| 4 | IRON NEST: Heavy Turret Simulator | 新上榜 |
| 5 | ReStory: Chill Electronics Repairs | 新上榜 |
| 6 | Palworld | 下降 2 位 |
| 7 | Gears of War: E-Day（预购） | 新上榜 |
| 8 | Mistfall Hunter | 由第 1 跌至第 8 |
| 9 | Tom Clancy's Ghost Recon Wildlands | — |
| 10 | Rust | — |

来源：vgchartz，Steam Weekly Week 33 2026。榜单按营收排序，包含预购与硬件，同一游戏的不同版本会重复出现。

**值得注意的一点：** 前十里有四款是新上榜，其中 IRON NEST（两人团队）与 ReStory（小团队 + 中型发行商）都属于小规模制作。这说明榜单头部对小团队并非封闭，但需要极强的品类辨识度。

### 2.2 同期在售新品定价参考

| 游戏 | 上线日期 | 原价（美元） |
| --- | --- | --- |
| Big Walk | 2026-08-04 | 19.99 |
| IRON NEST: Heavy Turret Simulator | 2026-08-06 | 19.99 |
| ReStory（含 2 款游戏的合集） | 2026-08-06 | 合集 25.18 |
| Mistfall Hunter | 2026-07-29 | 24.99 |
| Machine Party | 2026-07-30 | 7.99 |
| Pax Autocratica | 2026-08-10 | 29.99 |
| PEAK | 2025-06-16 | 7.99（现折 3.99） |

来源：Steam Global Top Sellers 搜索页，2026-08-13 抓取。

---

## 3. 品类经济性对比

### 3.1 按中位收入（越窄越赚）

| 标签 | 在架数量 | 中位收入 | 中位价 |
| --- | --- | --- | --- |
| Villain Protagonist | 280 | 39,800 美元 | 7.9x 美元 |
| Mature | 1,500 | 37,300 美元 | 6.99 美元 |
| Extraction Shooter | 106 | 33,000 美元 | 9.99 美元 |
| Open World Survival Craft | 710 | 22,100 美元 | 10.49 美元 |
| Sequel | 249 | 22,000 美元 | 7.99 美元 |
| Singleplayer / Indie（宽标签） | 数万 | 约 1,000 美元量级 | — |

来源：profitable.app，Steam Tags by Revenue（2026 年 8 月）。表中部分数字在源页面上被截断，已只保留可完整读取的条目。

### 3.2 按品类整体（Steam Page Analyzer 口径，指达到 100+ 评价的游戏）

| 品类 | 典型收入区间 |
| --- | --- |
| 工厂 / 自动化 | 20 万 – 50 万美元以上 |
| 殖民地模拟 / 管理 | 15 万 – 40 万美元 |
| 生存建造 | 依赖差异化，多人支持显著抬高上限；Early Access 接受度高 |

来源：Steam Page Analyzer，Steam Revenue by Genre 2026。

### 3.3 参与度与算法飞轮

高参与度品类（roguelite、生存、自动化）每笔销量产生的评价数更多，评价数直接喂给 Steam 的推荐算法，形成正反馈。这是这些品类「以小博大」的结构性原因，而不仅仅是运气。【Steam Page Analyzer】

---

## 4. 爆款拆解：小团队做对了什么

### 4.1 PEAK（合作攀爬，2025-06 上线）

- 团队：Aggro Crab + Landfall 两家小工作室联合。
- 制作：主体在四周内完成，团队集中到首尔封闭开发一个月。
- 成本：估算低于 20 万美元，主要是几个月薪水加上机票、住宿、餐饮。
- 定价：首发低于 5 美元，被形容为「极具诱惑力的冲动消费」。
- 结果：上线数小时回本；一周 160 万份；至 2026 年 8 月接近 1,000 万份；峰值同时在线 170,759。
- 关键动因：两家工作室的社区经理在 TikTok、YouTube、Twitter、Steam 上已经耕耘多年，发行时有现成的传播启动能量。开发者自己的说法是「如果你已经知道会有那个火星，剩下的就只看游戏够不够好，能不能把火星烧成篝火」。

来源：Game Developer 两篇报道、Dexerto 报道。

**可迁移的经验：** 制作周期短、成本低、定价低、机制单点极致、天然产生可剪辑的名场面。
**不可迁移的部分：** 现成的社群基本盘。这是 PEAK 数据里最容易被误读的一项，新项目不具备。

### 4.2 Lethal Company（单人开发，2023-10 Early Access）

产品分析指出其真正的护城河不是某个机制，而是「玩家会把游戏内的经历带到游戏外」：讲故事、建 wiki、做 mod、剪片、教新人、更新后回流。低价 + 快速生成故事的定位非常清晰，而当小团队试图「做给所有人玩」时，产品通常会变得更软、更慢、更不容易被记住。
报告同时指出对单人开发者的现实风险：病毒式传播会带来 bug 报告、服务器预期、内容需求、社区管理问题，创作者本人容易成为一切需求的瓶颈。【Birdor Blog，Lethal Company 产品案例研究】

### 4.3 IRON NEST（两人团队，2026-08 上线，19.99 美元）

- 单一场景：全部玩法发生在一座炮塔内部。
- 交互密度极高：刻度、瞄准轮、液压、弹道计算、30 种弹药（穿甲弹、烟幕弹、毒气弹等）。
- 信息层：截听无线电、判读航拍侦察照片、接收上级火力任务与前线请求。
- 感官反馈是核心卖点：玩家评价强调「你能看见、听见、感受到机器随着你的每一个操作嘎吱作响」，并把最终拉下击发杆的那一声 THUNK/BOOM 称为「游戏史上最爽的一下」。
- 时代与世界观：1920 年代西班牙内战前夕的架空历史，柴油朋克。

来源：IRON NEST Steam 社区页与官方描述、Secret Sauce Showcase 2026 预告、GameGrin 周榜。

**可迁移的经验：** **两个人，一个房间，19.99 美元，Steam 周销榜第 4。** 对于美术产能受限的团队，这是当前调研中投入产出比最高的范式。

### 4.4 "Friendslop" 现象的本质

这个词最初来自一条调侃推文，把 Lethal Company、Content Warning、R.E.P.O. 三张截图并列，称其「存在的唯一目的就是刷朋友」。批评者认为若剥离同伴则内容单薄；支持者认为社交体验本身就是游戏，机制只是把人聚在一起的载体。
更有价值的观察是：过去多年 AAA 发行商逐步撤出小规模合作体验，留下了一个巨大的空缺，独立团队填了进去。玩家的需求分成两类且互不替代——一类要竞技与精通，另一类只想和朋友待几小时、笑一场、一起完成点什么、下线时带走几个能复述的故事。【Game Developer；LinkedIn 行业讨论】

---

## 5. Steam 发行流程与硬性要求

| 项目 | 内容 |
| --- | --- |
| Steam Direct 费用 | 每款产品 100 美元，不可退款；当该产品调整后总收入达到 1,000 美元后可回收 |
| Valve 分成 | 30%，超过 1,000 万美元后降至 25%，超过 5,000 万美元后降至 20% |
| 最低构建要求 | Windows 原生 .exe；不接受网页/浏览器游戏；必须能独立运行，不能要求玩家手动安装额外软件 |
| 商店页 | 必须先以「即将推出」状态上线，且在发售前至少保持 2 周，以便玩家加入愿望单 |
| 审核 | 商店页与游戏构建分两次审核，必须先过商店页再提交构建；每次 1–5 个工作日 |
| 上传方式 | Steamworks SDK 中的 SteamPipe；命令行为 `steamcmd +login <user> +run_app_build <script.vdf> +quit`；需配置 app_build.vdf 与 depot 配置 |
| 账号权限 | 上传需要具备 "Edit App Metadata" 与 "Publish App Changes To Steam" 权限；官方建议单独建一个只有这两项权限的构建专用账号 |
| 法务与财务 | 需完成公司/个人法定身份认证、税务与银行信息、签署 NDA 与 Steam 分发协议 |
| 愿望单期 | 建议在发售前 2–6 个月开始积累 |

来源：Steamworks 官方文档（Onboarding、Release Process、Uploading to Steam）、Steam Direct 页面、Summer Engine 2026 发行指南。

**测试要求提醒：** 官方与实践经验都强调，必须在一台没有安装任何开发工具的干净机器上测试构建。最常见的上线故障是缺少 DLL 或运行时依赖——这些东西在开发机上存在，在玩家机器上不存在。

---

## 6. 对 maner 项目的直接推论

1. **不做宽品类。** 目标标签必须能写成一句具体的话，例如「柴油朋克单房间机械操作模拟」，而不是「3D 冒险」。
2. **优先考虑「深度 > 广度」的成本结构。** 一个做到极致的场景，胜过十个平庸场景。IRON NEST 已经证明这条路能进周销榜前五。
3. **定价锚点。** 单场景高密度模拟类锚定 14.99–19.99 美元；社交合作类锚定 6.99–9.99 美元；自动化/管理类锚定 19.99–24.99 美元。
4. **Early Access 是默认选项，不是退路。** 生存与自动化品类玩家对 EA 接受度高，并且愿意参与迭代。
5. **必须在选题时就考虑「可传播性」。** 游戏是否天然产生可截图、可剪辑、可复述的瞬间，直接决定零预算发行的天花板。
6. **不要把 PEAK 的数字当作预期值。** 那条曲线由既有社群启动，新项目没有该变量，把它当基准会导致严重的规划失真。合理的规划基准应该是：进入品类的第 75–95 百分位（即数万美元量级），把百万级当作上行情形而非计划。

---

## 7. 来源清单

| 编号 | 来源 | URL |
| --- | --- | --- |
| 1 | vgchartz — Steam Weekly Week 33 2026 | https://www.vgchartz.com/article/468707/steam-weekly-week-33-2026/ |
| 2 | Steam Global Top Sellers | https://store.steampowered.com/search?filter=globaltopsellers |
| 3 | Steam Page Analyzer — Steam Revenue by Genre 2026 | https://www.steampageanalyzer.com/blog/steam-revenue-by-genre |
| 4 | COGconnected — Co-Op Survival Games Won't Stop Eating Steam's Charts | https://cogconnected.com/2026/07/co-op-survival-games-wont-stop-eating-steams-charts/ |
| 5 | profitable.app — Steam Tags by Revenue（2026 年 8 月） | https://profitable.app/steam/tags |
| 6 | App2top — Steam Genre Benchmark（2026 上半年） | https://app2top.com/analytics/steam-genre-benchmark-inequality-index-oversaturated-niches-top-grossing-categories-anomalies-and-perennial-underdogs-295050.html |
| 7 | dev.to — The State of Steam 2026: What 10,000 Games Reveal | https://dev.to/tonywangca/the-state-of-steam-2026-what-10000-games-reveal-516o |
| 8 | Game Developer — What developers can learn from the indie social co-op | https://www.gamedeveloper.com/design/what-developers-can-learn-from-the-indie-social-co-op-games-topping-the-steam-charts |
| 9 | Game Developer — How co-op climbing hit Peak achieved 2 million sales | https://www.gamedeveloper.com/production/how-co-op-climbing-hit-peak-achieved-2-million-sales-for-less-than-200-000- |
| 10 | Birdor Blog — Lethal Company 产品案例研究 | https://blog.birdor.com/lethal-company-social-horror-product-case-study/ |
| 11 | Dexerto — Peak 四周开发报道 | https://www.dexerto.com/gaming/viral-co-op-game-peak-was-mostly-made-in-just-four-weeks-we-locked-in-3218842/ |
| 12 | GameGrin — Weekly Top Selling Games on Steam (3–9 Aug 2026) | https://www.gamegrin.com/news/weekly-top-selling-games-on-steam-3rd9th-of-august-2026/ |
| 13 | IRON NEST Steam 社区页 | https://steamcommunity.com/app/2950790 |
| 14 | Steamworks — Onboarding | https://partner.steamgames.com/doc/gettingstarted/onboarding |
| 15 | Steamworks — Release Process | https://partner.steamgames.com/doc/store/releasing |
| 16 | Steamworks — Uploading to Steam | https://partner.steamgames.com/doc/sdk/uploading |
| 17 | Steam Direct | https://partner.steamgames.com/steamdirect/ |
| 18 | Summer Engine — Publish Your Game on Steam: 2026 Guide | https://www.summerengine.com/blog/how-to-publish-game-on-steam |

### 未能核实的事项

- Big Walk 的开发商与具体玩法形态。本次检索只确认了它 2026-08-04 上线、定价 19.99 美元、位列周销榜第 1，未取得可引用的玩法与视角描述，因此本报告不对其做任何形态判断。
- 「2026 年 Steam 上合作类游戏总营收 41 亿美元（Alinea Analytics 估算）」这一数字在来源页面上被截断，仅作方向性参考，不作为决策依据。
