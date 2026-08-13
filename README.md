# 项目 CODER

一款面向 Steam 发行的 Windows 3D 游戏。Unity 引擎，全新项目，不复用任何既有代码或设计。

**当前阶段：方案评审。** 市场调研已完成，五个候选方案已给出，开发环境已搭建并完成端到端技术验证。**等待你选定方案后进入开发。**

---

## 先读这四份文档

| 文档 | 内容 | 建议阅读顺序 |
| --- | --- | --- |
| [市场调研报告](docs/01-market-research.md) | Steam 热销榜实况、独立游戏收入分布、品类需求趋势、小团队成功范式拆解、发行流程要求。所有数据附来源链接 | 1 |
| [五个游戏方案](docs/02-game-concepts.md) | 五个完整方案，含核心循环、摄像机参数、画面参考、美术方向、内容体量、系统设计、风险清单、市场定位、收入预估 | **2（最重要）** |
| [技术可行性与自检流水线](docs/03-tech-feasibility.md) | 开发环境实况、已验证的能力与边界、四层自动化自检流水线设计 | 3 |
| [Steam 发行计划](docs/04-steam-release-plan.md) | 发行关键路径、需要你本人完成的事项、技术接入清单、发售前检查表 | 4 |

---

## 五个方案速览

| 编号 | 代号 | 视角 | 品类 | 概念图 |
| --- | --- | --- | --- | --- |
| A | ABYSSAL（深渊） | 第一人称固定工位 | 硬核模拟 + 氛围恐怖 | [看图](concept-art/concept-A-abyssal.png) |
| **B** | **OVERCLOCK（超频）** | **斜俯视可缩放** | **自动化 + roguelite** | [看图](concept-art/concept-B-overclock.png) |
| C | NIGHT SHIFT（夜班） | 第一人称自由移动 | 4 人合作恐怖 | [看图](concept-art/concept-C-nightshift.png) |
| D | SALVAGE（打捞） | 第一人称零重力 | Cozy 拆解修复 | [看图](concept-art/concept-D-salvage.png) |
| E | DEAD DROP（死信） | 第三人称越肩 | 单人撤离 roguelite | [看图](concept-art/concept-E-deaddrop.png) |

**推荐方案 B，次选方案 A。** 详细理由见[方案文档的推荐章节](docs/02-game-concepts.md#推荐)。

不推荐在当前条件下选 C 和 E：C 需要多人联机，当前云环境无法做有效的自动化测试；E 需要 60–80 个角色动画和无法自动化验证的射击手感。

---

## 已完成的技术验证

全部在当前这台**没有独立显卡**的 Linux 云开发机上实测通过：

| 能力 | 状态 | 实测数据 |
| --- | --- | --- |
| Unity 6000.0.81f1 LTS 编辑器安装 | 通过 | 7.9 GB，装在 `/opt/unity/6000.0.81f1` |
| Windows 构建支持模块 | 通过 | `win64_player_nondevelopment_mono` 变体到位 |
| Unity 许可证激活 | 通过 | `Successfully activated ULF license` |
| 无头创建 Unity 项目 | 通过 | 12 秒 |
| 软件渲染截图（Xvfb + llvmpipe） | 通过 | 6.4 秒出一张 1280×720 PNG，透视/光照/深度全部正确 |
| **Windows 64 位交叉编译** | **通过** | `result=Succeeded errors=0`，产出 82 MB 完整发行目录 |

这意味着「在虚拟环境里跑任务、边运行边自检、截图看差距」这条链路是**真的能跑通**的，不是纸上谈兵。

已知边界（无法在本机完成，需要你或有 GPU 的机器）：帧率与性能测试、Windows 产物的实际运行验证、手感调校、多人联机测试、IL2CPP 构建、音效质量判断。详见[技术可行性文档第七节](docs/03-tech-feasibility.md#七当前环境无法覆盖的部分必须诚实列出)。

---

## 需要你拍板的五件事

在开工之前，这五个问题会实质改变架构和交付物，必须先定：

1. **选哪个方案？**（推荐 B，次选 A）
2. **交付目标是哪一档？** 可玩垂直切片 / Steam Demo / Early Access 首发 / 正式版。这四档的工作量是数量级递增的
3. **美术资产从哪来？** 程序化生成（零成本，只有方案 B 完全可行）/ 采购 Asset Store / 外包委托
4. **音效和配音怎么办？** 方案 A 需要 120–180 条音效 + 约 300 行配音；方案 B 几乎不需要
5. **现在启动 Steam Direct 流程吗？** 建议现在就启动，30 天强制等待期与开发进度无关，纯粹的日历时间

完整说明见[方案文档末尾](docs/02-game-concepts.md#需要你拍板的问题)。

---

## 仓库结构

```
docs/                 调研报告与方案文档
concept-art/          五个方案的概念图
tools/                环境搭建与自动化自检脚本
```

方案选定后会增加 `Unity/`（Unity 工程）和 `selfcheck/`（自检产出的截图与对比报告）。

---

## 工具

### 开发环境重建

```bash
export UNITY_EMAIL="你的 Unity 账号"
export UNITY_PASSWORD="你的密码"
bash tools/setup-unity.sh
```

装系统依赖、下载 Unity Linux 编辑器、装 Windows 构建模块、激活许可证、验证软件渲染。凭据只从环境变量读取，不入库。

### 视觉差距量化

这是「截图看自己和目标的差距」的实现。

```bash
# 与概念图对比，看美术方向差多远
python3 tools/visual_diff.py 当前截图.png concept-art/concept-B-overclock.png --out selfcheck/0001

# 与上一版截图对比，检测视觉回归
python3 tools/visual_diff.py 新截图.png 旧截图.png --out selfcheck/0001 --regression
```

产出并排对比图（底部带主色板条）、逐像素差异热力图、六项量化指标（亮度、对比度、饱和度、暗部占比、边缘密度、视觉重心）和差距报告。

工具已实测：同图自比对全部差值为 0、SSIM 为 1.0；拿 Unity 软渲染的灰调测试场景对比方案 B 的高饱和概念图时，正确报出 4 项超阈值差距（亮度偏高 0.146、饱和度偏低 0.504、暗部占比偏低 0.637、细节量偏低 0.426）。

**指标只负责发现问题，定位原因必须看图。** 用法和差距清单的写法见[技术可行性文档第五、六节](docs/03-tech-feasibility.md#五自动化自检流水线设计)。

### Unity 能力探针

`tools/unity-probe/ProbeBuild.cs` 放进 Unity 工程的 `Assets/Editor/`，可以从命令行验证程序化建场景、软件渲染截图、Windows/Linux 交叉编译三项能力。用法见文件头注释。
