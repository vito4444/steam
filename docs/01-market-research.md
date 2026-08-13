# 项目 CODER — Steam 市场调研报告

调研时间：2026 年 8 月 13 日
调研范围：Steam 全球热销榜、独立游戏收入分布、品类需求趋势、小团队成功范式、发行流程要求
数据来源：见文末「来源清单」，正文中关键数据就近标注出处

---

## 一、结论先行

如果目标是「一款由极小团队（含 AI 协作）在 Unity 上做出、能在 Steam 上正式发行并取得实际销量的 3D Windows 游戏」，市场数据指向的最优解具备以下五个特征：

1. **场景小、系统深。** 用一个高密度的封闭场景替代大世界，把开发预算全部压在交互深度和系统涌现上，而不是场景面积和资产数量上。
2. **视角选第一人称或近距离第三人称。** 摄像机离得近，玩家看到的东西少，需要制作的资产就少，同时单个资产的质感回报最高。
3. **品类落在「模拟 / 自动化 / 管理 / roguelite / 恐怖」这几个收入中位数最高的区间**，避开平台跳跃、视觉小说、步行模拟这类收入天花板低的品类。
4. **定价 8–20 美元，走 Early Access 或先发 Demo 攒愿望单。**
5. **游玩过程要「可被剪辑」**——玩家自发产生的短视频是当前独立游戏唯一还在起效的免费流量入口。

下文用数据支撑这五条。

---

## 二、Steam 独立游戏的收入分布：先认清基准线

这是所有方案讨论的前提。不认清这个分布，任何方案都是空谈。

### 2.1 整体分布（终身总收入，扣平台抽成前）

| 分位 | 终身总收入 |
| --- | --- |
| 后 50% | 低于 15,000 美元 |
| 50–75 分位 | 15,000 – 75,000 美元 |
| 75–90 分位 | 75,000 – 300,000 美元 |
| 90–95 分位 | 300,000 – 1,000,000 美元 |
| 前 5% | 100 万美元以上 |
| 前 1% | 500 万美元以上 |

中位数落在 5,000 – 15,000 美元区间，扣掉 Steam 30% 抽成后开发者实收约 3,500 – 10,500 美元。（来源：Steam Page Analyzer《Indie Game Revenue Data 2026》）

另一组交叉数据：Steam 上带 `Indie` 标签的游戏共 93,930 款，总收入约 160 亿美元，**平均**每款 22 万美元，**中位数**只有 570 美元。平均数和中位数差 386 倍，说明收入高度集中在极少数头部。（来源：Fungies.io《Indie Developer Market 2026》）

**这两个数字必须一起看。** 中位数 570 美元的那个统计口径包含了大量从未认真做完的项目；5,000–15,000 美元那个口径更接近「真的做完并上架了」的项目。我们的目标应该对标的不是中位数，而是「评测数过 100 且好评率 Very Positive」那一档——这一档的典型收入是 75,000 – 300,000 美元。

### 2.2 分品类收入中位数（限评测数 100+ 的游戏）

| 品类 | 收入中位数 | 对项目 CODER 的适配度 |
| --- | --- | --- |
| 工厂 / 自动化 | 200,000 – 500,000+ 美元 | 高：内容可程序化生成，美术需求低 |
| 殖民地模拟 / 管理 | 150,000 – 400,000 美元 | 高：系统驱动，美术需求低 |
| Roguelite | 100,000 – 300,000 美元 | 高：程序化生成天然契合 |
| 生存建造 | 100,000 – 350,000 美元 | 中：需要大地图，资产量大 |
| 城市建造 | 75,000 – 250,000 美元 | 中：需要大量建筑资产 |
| 类银河恶魔城 | 50,000 – 150,000 美元 | 低：通常 2D，且手工关卡量大 |
| 平台跳跃 | 30,000 – 100,000 美元 | 低：饱和严重 |
| 叙事驱动 | 25,000 – 100,000 美元 | 中：依赖配音和文本量 |
| 解谜 | 20,000 – 80,000 美元 | 中：价格敏感 |
| 视觉小说（非成人） | 10,000 – 40,000 美元 | 低 |
| 步行模拟 / 探索 | 10,000 – 30,000 美元 | 低：天花板明显 |
| 复古街机 | 5,000 – 20,000 美元 | 低：受众已迁往移动端 |

（来源：Steam Page Analyzer《Indie Game Revenue Data 2026》）

注意「工厂/自动化」和「殖民地模拟」这两项——它们恰好是**内容可以由系统生成、美术资产可以模块化复用**的品类，与小团队 + AI 协作的能力结构高度吻合。这不是巧合：这两个品类的价值来自规则组合的深度，而规则组合正是代码能高效产出的东西。

### 2.3 愿望单与首月收入的对应关系

| 发售时愿望单数 | 首月收入区间 |
| --- | --- |
| 低于 5,000 | 低于 15,000 美元 |
| 5,000 – 10,000 | 15,000 – 40,000 美元 |
| 10,000 – 25,000 | 40,000 – 100,000 美元 |
| 25,000 – 50,000 | 100,000 – 250,000 美元 |
| 50,000 以上 | 通常超过 250,000 美元 |

（来源：Steam Page Analyzer《Indie Game Revenue Data 2026》）

首月收入通常占第一年收入的 30–50%，首周单独占第一年的 15–25%。这意味着：**发售前的愿望单积累比发售后的任何补救都重要。** 商店页面（Coming Soon）上线得越早越好。有数据显示，愿望单转化率最高的那批游戏，Coming Soon 页面平均在发售前 214 天就已经挂出。（来源：Immutable《How to Publish a Game on Steam: 2026 Guide》）

### 2.4 Boxleiter 法：从评测数反推收入

行业通用的估算公式：

```
估算总收入 = 评测数 × 销量倍率 × 售价
```

销量倍率随品类在 20–60 之间浮动，多数独立游戏取 30 左右。策略类等「评测意愿高」的受众倍率偏低（20–30），休闲受众倍率偏高。

举例：一款 14.99 美元的 roguelite 拿到 500 条评测，保守估算是 `500 × 25 × 14.99 ≈ 187,000` 美元总收入。

这个公式后面用来给各方案做收入预估，全部按**保守倍率**计算。

---

## 三、当前 Steam 热销榜实况（2026 年 8 月）

### 3.1 第 33 周（截至 8 月 11 日）付费榜前十

| 排名 | 游戏 | 说明 |
| --- | --- | --- |
| 1 | Big Walk | 新上榜。House House 开发，Panic 发行，开放世界合作冒险 |
| 2 | Marvel Tōkon: Fighting Souls | 新上榜，格斗 |
| 3 | Cyberpunk 2077 | 长尾 |
| 4 | **IRON NEST: Heavy Turret Simulator** | **新上榜，两人团队** |
| 5 | **ReStory: Chill Electronics Repairs** | **新上榜，cozy 维修模拟** |
| 6 | Palworld | 长尾 |
| 7 | Gears of War: E-Day | 预购 |
| 8 | Mistfall Hunter | 撤离射击，黑暗奇幻 |
| 9 | Ghost Recon Wildlands | 长尾 |
| 10 | Rust | 长尾 |

（来源：VGChartz Steam Weekly Week 33, 2026）

### 3.2 前一周（第 32 周）付费榜前十

Mistfall Hunter（新，撤离射击）、Cyberpunk 2077、Halo: Campaign Evolved、Palworld、Marvel's Spider-Man 2、Baldur's Gate 3、Shift At Midnight、Corsair Cove（新）、Spider-Man Remastered、Battlefield 6。

（来源：VGChartz Steam Weekly Week 32, 2026）

### 3.3 从榜单里能读出的三件事

**第一，小团队做的高聚焦模拟游戏正在硬挤进前五。**

IRON NEST 是这次调研里信息量最大的样本。它是**两个人**（Nick Nieuwoudt、Dominik Latos）做的柴油朋克第一人称模拟器，玩家扮演一门 5,000 吨移动重炮的操作员。整个游戏的内容就是：从电传打字机接收司令部坐标 → 在战术地图上用红铅笔画方位线求交点 → 把方位和距离输入弹道计算机 → 选炮弹类型 → 拧发射药旋钮 → 装填 → 转动炮塔 → 开火。玩家从头到尾没有离开过炮塔内部。

它在 2026 年 6 月的 Steam Next Fest 上凭 Demo 拿到「Overwhelmingly Positive（好评如潮）」，正式版上线直接进热销第 4 名。（来源：Inven Global《Steam Next Fest: What Are Global Gamers Choosing?》、Global Esport News、Steam 社区攻略 id=3673768517）

**这个样本证明了一件事：一个封闭场景 + 一套足够深的机械交互流程，足以支撑一款商业上成功的 3D 游戏。** 它不需要开放世界，不需要角色动画，不需要过场动画，不需要多人联机。它需要的是一套设计得极其扎实的操作流程，以及把每一个旋钮、每一张纸、每一支铅笔都做到有手感。

**第二，cozy 职业模拟同样在榜。**

ReStory: Chill Electronics Repairs 由 Mandragora 开发、tinyBuild 发行，玩家在秋叶原开一家小电器店，修 2000 年代中期的电子产品（包含 Atari 官方授权主机），一边焊电路板一边和顾客聊天，故事有分支和多结局。它同样是「第一人称 + 固定工位 + 深度物品交互」的结构。

**第三，撤离玩法（extraction）仍然有效但门槛在抬高。**

Mistfall Hunter（Bellring Games 开发，Skystone Games 发行）连续两周在榜。撤离玩法的核心张力（带着战利品活着走出去）是很强的设计，但完整的撤离射击通常需要多人联网、大地图、AI 敌人、装备经济，体量远超小团队。

---

## 四、需求趋势：哪些方向的愿望单在暴涨

Game Oracle 用「相似度聚类 + 90 天愿望单/销量变化」的方法给 Steam 全库做了需求热力图，2026 年识别出三个高需求聚类：

| 聚类 | 90 天愿望单增长 | 备注 |
| --- | --- | --- |
| 心理 / 氛围恐怖 | **+572%** | 全图最强信号 |
| 放置 / 增量式模拟策略 | **+358%**（销量增长 25%） | Steam 平均愿望单增长约 1%，即 300 倍于平均 |
| 动漫回合制 RPG | 高 | 美术成本高，不适合小团队 |

（来源：Game Oracle《Top 3 Steam Trends in 2026 (So Far)》）

关于心理恐怖聚类的具体定义，原文描述为：「以氛围张力、第一人称探索、解谜、潜行为核心，而非战斗。用理智值、昏暗光照、变化的环境营造持续的不安感，而不是靠 jump scare。音效设计是关键的差异化点。」

**这段描述对我们特别有利。** 「不靠战斗、不靠 jump scare、靠氛围和音效」意味着：不需要复杂的战斗系统，不需要大量敌人 AI 和动画，不需要武器手感调校。第一人称 + 昏暗光照还能天然掩盖低多边形资产的短板——暗处不需要细节。

关于放置/增量聚类：「围绕资源采集、被动进度、生产链、长期战略建造。核心玩家幻想始终一致：从小处起步，看着自己的系统成长，即使离开一会儿也能感受到进度的满足感。」

---

## 五、小团队成功范式拆解

### 5.1 范式 A：单场景深度模拟（IRON NEST 模型）

- 代表作：IRON NEST、PVKK: Planetenverteidigungskanonenkommandant、Hardspace: Shipbreaker（体量更大）
- 团队规模：1–3 人
- 场景数量：1 个主场景 + 少量变体
- 深度来源：真实感的操作流程、多步骤依赖、可犯错且错误有后果
- 视角：第一人称，站姿或坐姿工位
- 优势：美术资产总量可控在数十个模型以内；不需要角色动画；不需要联网
- 劣势：玩法门槛高，需要教学设计；内容长度依赖任务变体的组合数

### 5.2 范式 B：合作恐怖（Lethal Company / REPO 模型）

- 代表作：Lethal Company、Content Warning、REPO、Phasmophobia
- 传播机制：近距离语音（proximity voice）+ 物理系统 + 不可预测的怪物行为 = 大量可剪辑的社死瞬间，创作者自发做分发
- 数据参照：REPO 上线后收入一度超过《文明 7》，近 2,000 条评测拿到「Overwhelmingly Positive」；Lethal Company 历史峰值同时在线 240,817 人（2023 年 12 月 3 日）
- **但要注意衰减：** Lethal Company 当前同时在线约 3,946 人，比峰值低约 98%。（来源：SmartCDKeys 关于 The Mound 的报道）
- PC Gamer 的原话很直接：「『Lethal Company 克隆』已经是 Steam 上仅剩的三大品类之一。」这个赛道现在极度拥挤。
- 结构性问题：一旦玩家群体摸清怪物行为和最优路线，恐怖就退化成剧本化的喜剧。这是这个品类的天花板。

### 5.3 范式 C：Cozy 职业模拟（ReStory / PowerWash 模型）

- 代表作：ReStory、PowerWash Simulator、PC Building Simulator
- 深度来源：任务变体 + 拆解/修复的过程质感 + 轻叙事
- 视角：第一人称工位，或可自由走动的小场景
- 优势：场景极小；「cozy」标签本身有稳定受众和社区活跃度
- 劣势：对美术调性（配色、材质、光照、UI 手感）要求高，这恰好是纯代码最难补的一块

### 5.4 通用规律：Phasmophobia 给出的方法论

Kinetic Games 的做法值得逐条抄：

- **范围纪律：** 先把一个循环（辨认鬼的种类）做完，再加地图和系统。把化妆品经济和庞大叙事全部推后。
- **速度楔子：** Unity + Early Access，从第一天就上 VR 支持；高频更新本身就是营销节点。
- **分发取巧：** 近距离语音 + 不可预测的追猎 = 每一局都自带可剪辑素材，创作者变成买量引擎。
- **明确的「不做」清单：** 不做战斗通行证、不做付费 DLC 节奏、不投广告、不建大型 live-ops 团队。公开说「不做什么」本身就是在保护核心循环。

（来源：Solo Unicorn Club、Birdor Blog 的 Phasmophobia 案例研究）

### 5.5 定价策略

Solo 开发者的主流建议是 **3–10 美元**，把游戏定位成「一份小点心」而不是「一顿正餐」，对应 20,000 – 150,000 美元的收入预期。

但 2025–2026 年出现了反例：Hollow Knight: Silksong 和 Schedule 1 这样 20 美元的独立游戏冲进了 Steam 白金层（收入前 12），和《怪物猎人：荒野》《EA Sports FC 26》这种十亿美元级的作品并列。为了做到同样的总收入，20 美元的独立游戏卖出了近 4 倍于 70 美元 3A 游戏的份数。（来源：Fungies.io）

**我们的取值：** 首发 12.99–17.99 美元区间。理由是本项目的目标品类（模拟/自动化/恐怖）受众对深度内容的付费意愿高于休闲受众，定价过低反而会让玩家怀疑内容量。具体数字在方案选定后按内容量再定。

---

## 六、Steam 发行流程与硬性时间约束

这一节的每一条都是发行日程表的硬约束，不能压缩。

| 步骤 | 要求 | 耗时 |
| --- | --- | --- |
| 注册 Steamworks | 完成公司/个人身份认证、签 NDA 和分发协议 | — |
| 税务信息验证 | 银行 + 税务 + 身份验证 | 2–7 个工作日 |
| 支付 Steam Direct 费用 | **每款产品 100 美元**，不可用 Steam 钱包余额支付 | 即时 |
| 强制等待期 | 付费后到可以发售之间 **强制等待 30 天**，无法加速 | 30 天 |
| 商店页面审核 | Valve 审核商店页完整性 | 3–5 个工作日 |
| Coming Soon 页面公开 | 发售前**至少公开挂满 14 天** | ≥14 天 |
| 构建审核 | Valve 实际运行你的 Windows 可执行文件，检查能否正常启动、有无恶意行为。这是技术审核，不评判游戏质量 | 1–5 个工作日 |
| 手动点击发售 | 全部检查项通过后，由开发者手动点 Release App | — |

（来源：Steamworks 官方文档 appfee / onboarding、Steam Direct 页面、Immutable 2026 指南、The Game Marketer 2026 指南）

补充要点：

- 100 美元费用**不退**，但在产品累计调整后总收入达到 1,000 美元后会在月度报表里作为单独条目返还。
- **每个独立 App 都要单独付 100 美元**，包括独立上架的 Demo 和独立上架的 DLC。
- 构建审核必须在商店页审核通过**之后**才能提交。两个审核是串行的，不能并行。
- 上传用 SteamPipe。图形界面工具（SteamPipeGUI）适合首次上传；自动化流程用 SteamCMD + VDF 脚本。
- 上传构建**不等于**发售，只是让构建在 Steamworks 后台可用。

**关键路径推论：** 从「决定发行」到「可以按下发售按钮」，即使一切顺利、零返工，中间也不可能少于约 30 天（受强制等待期约束），而且商店页要在此之前就准备好。因此**商店页和 Coming Soon 应该在游戏做完之前很久就上线**，这既是流程要求，也是愿望单积累的唯一途径。

---

## 七、对项目 CODER 的直接启示

把上面所有数据压成一组设计约束：

| 维度 | 结论 | 依据 |
| --- | --- | --- |
| 场景规模 | 1 个主场景，或 3–5 个模块化小场景 | IRON NEST 单场景进热销第 4 |
| 视角 | 第一人称优先；次选近距离第三人称 | 资产量最省，质感回报最高；恐怖和模拟两大热门聚类都是第一人称 |
| 美术风格 | 风格化 / 低多边形 / 强光影对比 | 低多边形单个资产 40 分钟 vs 写实资产两周（Polylusion 的实测对比）；且软件渲染环境下可开发 |
| 品类 | 模拟 / 自动化 / 管理 / roguelite / 心理恐怖 | 收入中位数最高的几档 |
| 内容来源 | 系统涌现 + 程序化生成 > 手工关卡 | 与「代码高效、手工美术低效」的能力结构匹配 |
| 联网 | 单人优先。多人合作作为高风险高回报的备选 | 联网会让开发和自动化测试的复杂度翻倍 |
| 定价 | 12.99 – 17.99 美元 | 目标受众为深度玩家 |
| 可剪辑性 | 必须刻意设计「值得录下来的瞬间」 | 当前唯一有效的免费流量入口 |
| 发行准备 | 商店页远早于游戏完成就上线 | 30 天强制等待 + 14 天 Coming Soon + 愿望单积累周期 |

---

## 来源清单

1. VGChartz —《Big Walk Debuts in 1st on the Steam Charts》（2026 年第 33 周榜单）
   https://www.vgchartz.com/article/468707/steam-weekly-week-33-2026/
2. VGChartz —《Mistfall Hunter Tops the Steam Charts》（2026 年第 32 周榜单）
   https://www.vgchartz.com/article/468646/steam-weekly-week-32-2026/
3. Steam Page Analyzer —《Indie Game Revenue Data 2026: $5K-$15K Median, $1M Top 5%》
   https://www.steampageanalyzer.com/blog/indie-game-revenue-data
4. Fungies.io —《Indie Developer Market 2026: The Complete Industry Analysis》
   https://fungies.io/indie-developer-market-2026-complete-analysis-data-trends-forecasts-7/
5. Game Oracle —《Top 3 Steam Trends in 2026 (So Far)》
   https://www.game-oracle.com/blog/top-trends-2026
6. Inven Global —《Steam Next Fest: What Are Global Gamers Choosing?》（IRON NEST 玩法拆解）
   https://www.invenglobal.com/articles/23154/steam-next-fest-what-are-global-gamers-choosing
7. Global Esport News — IRON NEST / ReStory 周报导
   https://www.global-esports.news/general/new-on-steam-fans-have-been-waiting-15-years-for-this-marvel-game/
8. Steam 社区攻略 —《Iron Nest: An operator's guide to the basics》（操作流程细节）
   https://steamcommunity.com/sharedfiles/filedetails/?id=3673768517
9. GameGrin —《Weekly Top Selling Games on Steam (3rd–9th of August 2026)》（榜单游戏标签与开发者信息）
   https://www.gamegrin.com/news/weekly-top-selling-games-on-steam-3rd9th-of-august-2026/
10. PC Gamer —《An $8 cooperative horror game is rocketing up Steam's top sellers list》（REPO）
    https://www.pcgamer.com/games/horror/an-usd8-cooperative-horror-game-is-rocketing-up-steams-top-sellers-list-never-occurred-to-me-lethal-company-was-missing-physics-until-i-played-this/
11. SmartCDKeys —《The Mound: Omen of Cthulhu Launches July 15》（Lethal Company 在线人数衰减数据）
    https://smartcdkeys.com/en/blog/the-mound-omen-of-cthulhu-launches-july-15-to-tackle-co-op-horror-s-core-problem
12. Solo Unicorn Club — Phasmophobia 案例研究
    https://www.solounicorn.club/blog/day-64-phasmophobia
13. Birdor Blog — Phasmophobia 产品案例研究
    https://blog.birdor.com/phasmophobia-voice-coop-horror-product-case-study/
14. Polylusion Games —《Polylusion Art Style》《Low Poly Graphics》（低多边形工作流实测数据）
    https://polylusion.com/blog/polylusion-art-style
15. Steamworks 官方文档 — Steam Direct Fee / Onboarding
    https://partner.steamgames.com/doc/gettingstarted/appfee
    https://partner.steamgames.com/doc/gettingstarted/onboarding
16. Immutable —《How to Publish a Game on Steam: 2026 Guide》
    https://www.immutable.com/guides/how-to-publish-a-game-on-steam
17. The Game Marketer —《How to Publish Your Game on Steam in 2026》
    https://www.thegamemarketer.com/insight-posts/how-to-publish-your-game-on-steam-guide

---

## 调研范围说明

本报告覆盖的是**公开可查的市场数据与已上架游戏的公开信息**。以下内容不在本次调研范围内，如需补充请提出：

- 具体竞品的实际销量数据（需要 SteamSpy / VG Insights 等付费数据源）
- 中国区市场的差异化数据（本次数据以全球榜为主）
- 主机平台移植的市场空间
- 具体发行商的接洽条件

所有引用的数字均直接取自来源原文，未做估算或换算。收入预估部分明确标注为「按 Boxleiter 公式保守估算」，属于推算而非事实。
