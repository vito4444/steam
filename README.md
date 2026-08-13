# 项目 Hunter

一款面向 Steam 发行的 Windows 平台 3D 游戏，使用 Unity 6 LTS 开发。

## 当前阶段

**方案选型中。** 市场调研与五个候选方案已完成，等待确认走哪条路线。

## 文档

| 文档 | 内容 |
| --- | --- |
| [`docs/00-market-research.md`](docs/00-market-research.md) | Steam 市场调研：周销榜实况、标签收入中位数、类型饱和度、五款可对标竞品的逐项拆解 |
| [`docs/01-game-concepts.md`](docs/01-game-concepts.md) | 五个游戏方案，含视角相机参数、Steam 画面参考、美术方向、商业定位、技术路径、可行性评估 |
| [`docs/02-technical-constraints.md`](docs/02-technical-constraints.md) | 开发环境实况、Unity 配置、Windows 构建链路、自检与截图方案 |

## 五个候选方案速览

| 方案 | 类型 | 视角 | 推荐度 |
| --- | --- | --- | --- |
| A《金雾猎场》 | PvE 搜打撤动作 RPG | 第三人称越肩 | ★★★★★ |
| B《巨兽契约》 | co-op 巨兽狩猎 | 第一人称 | ★★★★☆ |
| C《兽径》 | PS1 复古氛围恐怖 | 第一人称 | ★★★★☆ |
| D《赏金回路》 | 动作 roguelite | 第三人称拉远 | ★★★☆☆ |
| E《荒野猎屋》 | 狩猎模拟 + 经营 | 第一人称 / 第三人称 | ★★★☆☆ |

概念图见 [`concepts/`](concepts/)。

## 目录结构

```
docs/          设计文档与调研
concepts/      概念图（作为画面目标基准）
screenshots/   各里程碑实机截图（与概念图对比用）
tools/         环境安装与构建脚本
```

## 环境

- 引擎：Unity 6000.0.81f1（Unity 6.0 LTS）
- 目标平台：Windows x64
- 开发机：Linux，无 GPU，通过 Xvfb + Mesa 软件渲染做无头运行与截图

安装脚本：`tools/install_unity.sh`
