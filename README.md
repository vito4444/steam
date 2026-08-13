# decoder

一个面向 Steam 发行的 Windows 单机游戏项目，当前处于**立项选型阶段**。

引擎 Unity 6000.0.81f1 LTS，目标平台 Windows x64。

---

## 当前状态

项目尚未选定最终方案。已完成的是选型所需的全部依据，以及一条可以立刻投入使用的开发管线。

| 阶段 | 状态 |
|---|---|
| Steam 市场调研 | 完成，基于 2026-08-13 抓取的一手数据 |
| 游戏方案设计 | 完成，5 个方案待选 |
| 云端 Unity 环境 | 完成并验证 |
| 构建、截图、自检管线 | 完成并跑通 |
| 美术方向验证 | 完成，探针场景已证明方案 A 的画面方向成立 |
| 玩法实现 | **未开始，等待方案选定** |

---

## 文档

| 文档 | 内容 |
|---|---|
| [`docs/research/00-steam-market-research.md`](docs/research/00-steam-market-research.md) | Steam 市场调研报告。从榜单一手数据中识别出三条可执行的品类信号，含关键市场参数与来源 |
| [`docs/concepts/01-game-concepts.md`](docs/concepts/01-game-concepts.md) | 5 个完整游戏方案。每个含定位、核心循环、画面视角对标、美术方向、技术负担、定价、风险，文末有横向对比矩阵与推荐 |
| [`docs/tech/02-pipeline-and-shipping.md`](docs/tech/02-pipeline-and-shipping.md) | 技术管线与 Steam 发行方案。含环境条件、管线用法、发行清单、已验证项与已知缺口 |

原始调研数据在 `docs/research/data/`，参考截图在 `docs/research/refshots/`。

---

## 目录结构

```
docs/
  research/     市场调研报告、原始抓取数据、参考截图
  concepts/     游戏方案
  tech/         技术管线与发行方案
tools/          自动化脚本
unity/Decoder/  Unity 工程
artifacts/      构建产物、截图、自检报告、配平记录（构建产物不入库）
```

---

## 环境准备

云端环境已经装好，本节供在新机器上复现。

**依赖**：Ubuntu 24.04、Xvfb、Mesa（软件渲染）、Python 3 与 PIL/numpy、7z、cpio。

**Unity**：
- 编辑器 `6000.0.81f1`，安装到 `/opt/unity/6000.0.81f1`
- Windows Build Support (Mono) 模块，解包到 `Editor/Data/PlaybackEngines/WindowsStandaloneSupport`
- 许可证激活：`Unity -batchmode -nographics -quit -username <邮箱> -password <密码>`
- 验证：`Editor/Data/Resources/Licensing/Client/Unity.Licensing.Client --showEntitlements` 应输出 `Unity Personal`

路径可通过 `UNITY_ROOT` 环境变量覆盖，见 `tools/unity-env.sh`。

---

## 常用命令

```bash
# 程序化重建美术探针场景（约 6 秒）
./tools/build-probe-scene.sh

# 无头渲染场景中所有机位到 artifacts/screenshots/（约 6 秒，5 个 1920x1080 机位）
./tools/capture.sh

# 交叉构建 Windows x64 可执行文件（约 16 秒）
./tools/build-windows.sh

# 画面自检：构图对标参考截图，配色对标项目档案
python3 tools/compare_frames.py \
  --shot artifacts/screenshots/probe_front.png \
  --reference docs/research/refshots/iron_nest_heavy_turret_simulator_0.jpg \
  --profile night-watch

# 列出内置配色档案
python3 tools/compare_frames.py --list-profiles

# 光照自动配平：按自检结果迭代调参，把最优解写回配置
python3 tools/auto_tune_lighting.py --iterations 10 \
  --reference docs/research/refshots/iron_nest_heavy_turret_simulator_0.jpg \
  --profile night-watch
```

---

## 两个必须知道的限制

**云端没有 GPU。** 截图走 Xvfb + Mesa llvmpipe 软件渲染。探针场景这种规模下渲染 1920×1080 约 1 秒一张，够用；场景复杂度上去之后会明显变慢。

**Windows 构建只能用 Mono 后端。** Unity 不提供 Linux 版的 Windows IL2CPP 模块。Mono 后端可以正常发行到 Steam，但正式发行版建议在 Windows 机器上用 IL2CPP 重新构建。
