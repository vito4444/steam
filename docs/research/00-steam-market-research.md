# Steam 市场调研报告（项目代号 decoder）

**调研日期**：2026-08-13
**数据来源**：Steam 官方商店搜索接口、Steam Store API（`appdetails` / `appreviews` / `storesearch`）、公开行业分析文章
**采集方式**：直接抓取 Steam 官方接口，全部数字为抓取当日实测值，未经估算
**用途**：为代号 decoder 的新项目提供选型依据

---

## 1. 调研方法与可信度声明

本报告分为两类内容，请区分对待：

- **事实**：直接来自 Steam 官方接口的原始数据（评论数、好评率、价格、发行日期、标签、开发者名）。这些数字是 2026-08-13 当日抓取的快照，随时间变化。
- **推测**：由事实推导出的判断（销量估算、品类趋势、竞争强度）。凡属推测处均明确标注。

销量估算使用业界通行的 Boxleiter 方法（评论数 × 系数）。该系数在不同年份、不同品类差异很大，业界常用区间为 25–50 倍。**本报告中所有销量与流水数字均为推测，不是 Steam 官方披露数据。**

原始抓取数据保存在 `docs/research/data/` 目录下，可复核。

---

## 2. 顶层观察：谁在赚钱

### 2.1 全球畅销榜（Top Sellers，2026-08-13 抓取）

榜单前 24 位基本被长青运营型产品和大厂新作占据，与新项目无竞争关系：

| 排名 | 游戏 | 价格 | 好评 | 评论数 |
|---|---|---|---|---|
| 1 | HELLDIVERS™ 2 | $29.99 | Very Positive 80% | 630,699 |
| 2 | Counter-Strike 2 | Free | Very Positive 86% | 2,593,222 |
| 3 | Hell Let Loose: Vietnam | $35.99 | 新品未评级 | — |
| 5 | Big Walk | $14.99 | Very Positive 94% | 9,251 |
| 6 | Marvel Rivals | Free | Mostly Positive 77% | 295,419 |
| 24 | Palworld | $29.99 | Overwhelmingly Positive 95% | 176,315 |
| **25** | **IRON NEST: Heavy Turret Simulator** | **$14.99** | **Overwhelmingly Positive 98%** | **4,613** |

**关键发现**：畅销榜第 25 位的 IRON NEST 是一位**单人开发者**（Steam 页面开发商栏为个人姓名 `Nick Nieuwoudt`）在 2026-08-06 发行的作品，售价 $14.99。它在发行 7 天内挤进全球畅销榜前 25，与 Palworld、GTA V 同屏。这是本次调研中最强的单一信号。

### 2.2 2026 年 7–8 月发行的小体量爆款（实测数据）

这三款是当前最值得逐帧拆解的样本，全部为小团队或单人作品，全部在发行两周内累积数千条评论：

| 游戏 | 发行日 | 价格 | 好评率 | 评论数 | 开发商 |
|---|---|---|---|---|---|
| IRON NEST: Heavy Turret Simulator | 2026-08-06 | $14.99 | 98% | 8,138 | Nick Nieuwoudt（个人） |
| Shift At Midnight | 2026-07-22 | $9.99 | 88% | 8,592 | Bun Muen（个人） |
| ReStory: Chill Electronics Repairs | 2026-08-06 | $17.99 | 96% | 4,954 | Mandragora（小工作室） |

按 Boxleiter 系数 30 倍**推测**，IRON NEST 约售出 24 万份，毛流水约 360 万美元；ReStory 约 15 万份，毛流水约 270 万美元。**此为推测值，非官方数据。** 即使按最保守的 15 倍系数，这几款仍属于明确的商业成功。

### 2.3 即将发行热门榜（Popular Upcoming，按愿望单排序）

这份榜单反映的是**玩家当下正在期待什么**，对新项目立项比畅销榜更有指导意义。前 20 位：

```
 1. Low-Budget Repairs              11. Servant of the Lake
 2. Hell Let Loose: Vietnam         12. STAR WARS Zero Company
 3. DISCIPLINE SIMULATOR            13. Duskfade
 4. Clawed                          14. BOMBANANA!
 5. Sandustry                       15. Moo Who?
 6. Mortal Shell II                 16. Last Pirates: Die Together
 7. Sort Them Ducks                 17. Static Dread: The Submarine
 8. TV Archive: Tidy Up Together    18. Tidy Up Together
 9. WE ARE SO DEAD                  19. WARDOGS
10. Lootbound                       20. The Lantern of the Laughless Saint
```

榜单更靠后的位置还有 `Emergency Room Simulator`（33）、`Scratch the Ticket`（35）、`Vacation Cafe Simulator`（39）、`FD 27: Direct Your Football Club`（38）。

**关键发现**：40 席中有 9 席是"某某模拟器 / 整理 / 修理 / 打工"类的小体量单点玩法游戏，且第 1 名 `Low-Budget Repairs`（90 年代公寓装修，玩法是偷工减料赚快钱）就属于这一类。这是当下 Steam 最拥挤、同时也最容易被算法推荐的赛道。

---

## 3. 三条可执行的品类信号

### 信号 A：单点机械深度模拟（IRON NEST 范式）

**代表作实测数据**：IRON NEST: Heavy Turret Simulator，$14.99，98% 好评，8,138 评论，单人开发。

**Steam 标签**：`Military` `Simulation` `Realistic` `3D` `War` `Alternate History` `Indie` `Immersive Sim` `Action` `First-Person` `Steampunk` `Historical` `Atmospheric` `World War I`

**官方商店描述原文**：
> IRON NEST is a brutal dieselpunk heavy-artillery game where you dominate the battlefield through a colossal war machine. Take map measurements based on the received coordinates, strain the hydraulics, ...

**画面视角（已下载官方截图核实）**：第一人称、摄像机基本固定在一个封闭炮塔舱内。玩家正面是一整面仪表墙——两个红色手轮（标注 `Left Gun Elevation` / `Right Gun Elevation`）、中央绿色仰角表盘、顶部四个机械计数器（`Left Gun Hydraulic System Pressure`、`Left Gun Elevation Error` 等）、右侧带绿色指示灯与数字显示（`0945`）的控制面板、右上方装填机构、挂在管道上的作业图纸夹板。左侧前景是一枚标注 `HIGH EXPLOSIVE / STANDARD BURST LOAD` 的巨型炮弹。舱室中央有一个小观察窗，透出外面的战场。整体是红绿双色照明的柴油朋克重工业质感。

**为什么这个范式重要**：
1. **玩法内核就是解码**——接收一串抽象坐标，在地图上测量换算，把结果翻译成手轮圈数、液压压力、装药量，最后开火验证。信息 → 操作的转译过程本身就是游戏。
2. **技术负担极低**——场景就是一个房间，没有开放世界、没有 NPC 寻路、没有联网同步、没有复杂物理。渲染压力集中在材质与光照，几何体数量小。
3. **单人可完成**——已有实证。
4. **98% 好评说明这套设计在体验上没有明显短板**，玩家买账。

**参考截图**：`docs/research/refshots/iron_nest_heavy_turret_simulator_0.jpg`、`_1.jpg`
**商店页**：https://store.steampowered.com/app/2950790/

---

### 信号 B：桌面界面调查／解码推理

**代表作实测数据**：

| 游戏 | 发行 | 价格 | 好评率 | 评论数 |
|---|---|---|---|---|
| Return of the Obra Dinn | 2018-10-18 | $19.99 | 97% | 34,940 |
| Chants of Sennaar | 2023-09-05 | $19.99 | 98% | 33,688 |
| The Case of the Golden Idol | 2022-10-13 | $17.99 | 98% | 10,887 |
| No Case Should Remain Unsolved | 2024-01-17 | $4.89 | 96% | 10,485 |
| The Roottrees are Dead | 2025-01-15 | $19.99 | 97% | 9,654 |
| The Operator | 2024-07-22 | $13.99 | 92% | 8,777 |
| Papers, Please | 2013-08-08 | $4.99 | 97% | 80,031 |
| Do Not Feed the Monkeys | 2018-10-23 | $15.99 | 94% | 12,652 |
| Not For Broadcast | 2022-01-25 | $6.24 | 94% | 12,602 |
| Lorelei and the Laser Eyes | 2024-05-16 | $24.99 | 94% | 3,199 |

**画面视角（已下载官方截图核实）**：以 The Operator 为例，整屏是一套伪造的操作系统界面，深蓝配色。顶部是任务栏（`APPLICATIONS`、当前任务 `Find killer's name`、三个联系人头像、时钟 `09:22`）；桌面左上角是文件夹图标（`Documents` / `Cases` / `Bar`）；中间浮着多个可拖动窗口——`VIDEO PLAYER - CAM_02`（监控录像带帧号、编解码器、分辨率等元数据）、`VICTIM`（绿色线框标注的尸体分析）、`HUMANDB`（联邦情报部人员档案，含 `ERROR 403 Access Forbidden` 权限拦截）；右侧是与外勤特工的即时通讯栏。**全部是 2D UI，零 3D 场景。**

**这一品类的特征**：
- **技术负担全场最低**——本质是一套 UI 系统加一套内容数据库。
- **内容与谜题设计负担全场最高**——好不好玩完全取决于谜题链条设计和文本质量，代码写完只是开始。
- **好评率极高且稳定**（92%–98%），说明这一品类的玩家宽容度高、口碑传播强。
- **单人开发实证充足**：The Roottrees are Dead 由 Robin Ward（`Evil Trout Inc.`）单人起家，Return of the Obra Dinn 由 Lucas Pope 单人完成。

**参考截图**：`docs/research/refshots/the_operator_0.jpg`、`the_roottrees_are_dead_0.jpg`、`return_of_the_obra_dinn_0.jpg`、`the_case_of_the_golden_idol_0.jpg`、`papers_please_0.jpg`、`not_for_broadcast_0.jpg`

---

### 信号 C：Cozy 整理／修理打工模拟

**代表作实测数据**：

| 游戏 | 状态 | 价格 | 好评率 | 评论数 |
|---|---|---|---|---|
| ReStory: Chill Electronics Repairs | 2026-08-06 发行 | $17.99 | 96% | 4,954 |
| Low-Budget Repairs | 未发行 | — | — | 愿望单榜第 1 |
| Sort Them Ducks | 未发行 | — | — | 愿望单榜第 7 |
| TV Archive: Tidy Up Together | 未发行 | — | — | 愿望单榜第 8 |
| Shelves and Sorcery | 已发行 | $4.79 | 96% | 108 |
| Unpacking | 2021-11-01 | $19.99 | 93% | 40,589 |
| A Little to the Left | 2022-11-08 | $5.99 | 92% | 18,856 |
| PowerWash Simulator 2 | 2025-10-23 | $24.99 | 91% | 11,612 |

**ReStory 的 Steam 标签**：`Job Simulator` `Cozy` `Management` `Economy` `Singleplayer` `Building` `Indie` `Relaxing` `Crafting` `Shop Keeper` `Simulation` `Dialogue Heavy` `First-Person` `Choices Matter`

**画面视角**：第一人称工作台视角，玩家低头面对桌面上的待修设备，周围是店铺环境。

**评估**：这条赛道当下最热，但也**最拥挤**——愿望单榜前 20 里挤了 5 款同类。同质化严重意味着后来者需要在美术品质或题材新意上明显超出平均线才能出头。另外这类游戏的成本结构偏"资产量"：需要大量可交互道具模型和贴图，对没有美术团队的项目是硬约束。

**参考截图**：`docs/research/refshots/restory_chill_electronics_repairs_0.jpg`

---

### 信号 D：小队合作恐怖（评估后不推荐作为首作）

**代表作**：Shift At Midnight，$9.99，88% 好评，8,592 评论，单人开发，2026-07-22 发行。
**Steam 标签**：`Horror` `Online Co-Op` `Multiplayer` `Retro` `3D` `Co-op` `Simulation` `First-Person` `Survival Horror` `Comedy` `Mystery` `Investigation` `Detective` `1990's`
**描述原文**：
> An online co-op detective horror for up to 3 players. Work together across randomly generated shifts to investigate your customers, as some are only pretending to be human.

**为什么不推荐作为首作**：需要网络同步、大厅匹配、反作弊与持续运维。这会让技术复杂度翻倍，且发行后如果在线人数不足，游戏会直接"死亡"（没人组队 = 无法游玩 = 差评）。同类的 88% 好评率也明显低于单机品类的 96%–98%，说明联机问题拖累了体验评价。

---

## 4. 关键市场参数（含来源）

以下数字来自公开行业分析文章，非 Steam 官方披露，不同来源口径不一致，此处如实并列：

| 指标 | 数值 | 来源 |
|---|---|---|
| 愿望单→购买转化（首周） | 10%–25% | [steamforecast.app](https://steamforecast.app/guides/steam-wishlist-conversion-rate) |
| 愿望单→购买转化（终身） | 20%–40% | 同上 |
| 愿望单→购买转化（行业均值） | 5%–10%（较 2018 年的约 20% 下滑） | [immutable.com](https://www.immutable.com/resources/insights/steam-wishlist-conversion-rates) |
| 25,000+ 愿望单游戏的首周转化中位数 | 0.15x | GameDiscoverCo，转引自 [forgivengames.com](https://forgivengames.com/good-steam-conversion-benchmark-for-indies/) |
| 售价 £10 以上游戏的转化中位数 | 0.10x | 同上 |
| 脱离算法"隐形层"所需评论数 | 50+ | [metricusapp.com](https://metricusapp.com/blog/indie-game-distribution-user-acquisition-painpoints-2025-2026/) |
| 越过可见度门槛所需愿望单 | 7,000–10,000 | 同上 |
| 有机愿望单 vs 付费愿望单转化差距 | 有机高 2–4 倍 | steamforecast.app |
| 独立游戏定价甜点区 | $15–$25 | steamforecast.app |
| 折扣期购买占 Steam 总购买比例 | 60% | forgivengames.com |
| 季节性促销占独立游戏年收入比例 | 35% | 同上 |

**三条来源一致的结论**：

1. **愿望单数量本身不重要，愿望单的"质量"和"增长形状"才重要。** 线性平稳增长的愿望单转化率明显高于靠 Next Fest 或病毒视频砸出来的尖峰式增长——后者混入大量"等打折"的观光客。
2. **Capsule（商店头图）是开发者可控范围内影响最大的单一变量。** 它决定点击率，而点击率决定整条漏斗。
3. **标签准确性直接决定曝光。** Steam 用标签把游戏投进对应的推荐队列，标签写歪了等于自断流量。

---

## 5. 对 decoder 项目的结论

### 5.1 应该做什么类型

**推荐范式：信号 A（单点机械深度模拟）为骨架，融合信号 B（解码推理）为内核。**

理由：

1. **市场验证最新鲜**：IRON NEST 在 7 天前刚证明这条路走得通，而且是单人走通的。信号 B 的品类则有跨越 13 年（Papers Please 2013 → Roottrees 2025）的持续验证，说明它不是短期风口。
2. **技术负担与本项目的开发条件匹配**：单房间场景 + 大量精细交互控件 + 音频驱动，几何体数量可控，不需要开放世界、联网、NPC AI 或复杂物理。这一点对"在无 GPU 的云虚拟环境里迭代"是决定性的（详见技术文档）。
3. **与项目代号 decoder 天然契合**：解码就是"把抽象信息翻译成具体操作"，既能做成机械操作（IRON NEST 式），也能做成推理填空（Obra Dinn 式），两者可以同一套系统实现。
4. **规避了最拥挤的赛道**：Cozy 整理类（信号 C）当下愿望单榜前 20 里有 5 款同类，后来者需要额外的美术投入才能突围。

### 5.2 应该避开什么

- **联机合作**：技术复杂度翻倍，且有"在线人数不足即死亡"的结构性风险。
- **开放世界／大量场景**：资产成本超出小团队能力，且在无 GPU 环境里几乎无法做视觉迭代。
- **拟真人物角色**：Static Dread 那种带 3D 人物立绘的表现力需要角色建模、绑定、动画和面部表情，是小团队最容易翻车的成本项。若需要人物，应采用照片处理、剪影、纯文字或非写实风格替代。
- **定价低于 $9.99**：多份来源指出低价会触发"低质量"预期，反而压低转化。

### 5.3 目标定价区间

**$14.99–$19.99。** 依据：IRON NEST $14.99、ReStory $17.99、Golden Idol $17.99、Roottrees $19.99、Obra Dinn $19.99、Chants of Sennaar $19.99 全部落在此区间，且这些游戏好评率均在 96% 以上。定价甜点区的行业建议同样指向 $15–$25。

---

## 6. 原始数据文件

| 文件 | 内容 |
|---|---|
| `data/steam_raw.json` | 五份 Steam 榜单各 40 条原始抓取结果 |
| `data/refs.json` | 56 款参考游戏的价格、发行日、分类、评论数详情 |
| `data/tags.json` | 17 款重点游戏的完整 Steam 标签与描述 |
| `data/shots.json` | 20 款参考游戏的官方截图与商店页链接 |
| `refshots/*.jpg` | 15 张已下载的官方截图，用于视角对标与后续画面比对基准 |
