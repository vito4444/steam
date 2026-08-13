# MANER

面向 Steam 发行的 Windows 3D 游戏项目，代号 **maner**。引擎为 Unity 6.3 LTS，渲染管线为 URP。

## 当前状态

**方案选型阶段。** 已完成市场调研与候选方案设计，等待确定具体方案后进入垂直切片开发。
工程骨架、构建管线与无人值守自检管线已搭建完成并验证通过，与具体方案无关，选定任何方案都可直接使用。

| 交付物 | 位置 | 状态 |
| --- | --- | --- |
| Steam 市场调研报告 | [`docs/research/steam-market-research-2026.md`](docs/research/steam-market-research-2026.md) | 完成 |
| 五个候选游戏方案 | [`docs/concepts/maner-game-proposals.md`](docs/concepts/maner-game-proposals.md) | 完成，待决策 |
| 方案概念图 | [`docs/concepts/images/`](docs/concepts/images/) | 完成 |
| Unity 工程骨架 | [`game/`](game/) | 完成 |
| Windows 构建管线 | [`tools/build.sh`](tools/build.sh) | 已验证产出 PE32+ 可执行文件 |
| 无人值守自检管线 | [`tools/selfcheck.sh`](tools/selfcheck.sh) | 已验证输出 1920×1080 截图与帧统计 |
| 画面差距量化工具 | [`tools/compare.py`](tools/compare.py) | 已验证 |

## 目录结构

```
docs/
  research/     市场调研
  concepts/     游戏方案与概念图
  dev-environment.md   开发环境搭建说明
  asset-licenses.md    第三方资产来源与许可登记
game/           Unity 工程
  Assets/Editor/       批处理构建与工程初始化脚本
  Assets/Scripts/      运行时代码
  Assets/Scenes/       场景
tools/          构建、自检、对比脚本
build/          构建产物（不入库）
screenshots/    自检截图（不入库）
artifacts/      日志与对比图（不入库）
```

## 快速开始

环境搭建见 [`docs/dev-environment.md`](docs/dev-environment.md)。环境就绪后：

```bash
# 构建（windows / linux / all）
tools/build.sh all

# 在虚拟显示环境中运行 Linux 构建，自动截图并输出帧时间统计
tools/selfcheck.sh 1.0,2.5,4.0

# 量化实机画面与目标概念图的差距
python3 tools/compare.py \
  --target docs/concepts/images/maner-concept-A-control-cabin.jpg \
  --actual screenshots/shot_00_t1.00s.png \
  --out artifacts/compare/latest.png
```

## 构建后端说明

开发期使用 **Mono** 脚本后端，因为它支持从 Linux 交叉构建 Windows 可执行文件。
IL2CPP 的 Windows 目标无法在 Linux 上交叉编译，**发行版本必须在一台 Windows 机器上完成最终构建**。
这是发行前的明确前置条件，已记录在方案文档的发行清单中。
