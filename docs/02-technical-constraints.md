# 项目 Hunter — 技术约束与开发环境

版本：v1
最后更新：2026-08-13

本文档记录开发环境的真实能力边界。它约束着方案选型——`docs/01-game-concepts.md` 中每个方案的「可行性评估」一节，依据的就是这里的数据。

---

## 一、开发机实况

这是一台云端 Linux 虚拟机，不是开发工作站。它的能力边界如下：

| 项目 | 实测值 | 影响 |
| --- | --- | --- |
| 操作系统 | Linux 6.12.94（Ubuntu 24.04 基础） | Unity 需用 Linux 编辑器 |
| CPU | 4 核 | 构建与光照烘焙偏慢 |
| 内存 | 15 GB | 够用，但大场景导入需注意 |
| 磁盘 | 252 GB，可用 235 GB | 充足 |
| **GPU** | **无独立显卡，无 CUDA** | **决定性约束，见第二节** |
| 显示 | Xvfb 虚拟帧缓冲（DISPLAY=:1） | 可跑图形程序并截图 |
| 视频工具 | ffmpeg 已安装 | 可录制运行过程视频 |
| 网络出口 | 无限制（已确认 egress restricted = false） | 可自由下载依赖 |

## 二、无 GPU 意味着什么（以及不意味着什么）

**它不意味着做不了 3D 游戏。** Unity 在 Linux 上可以通过 Mesa 的 llvmpipe 软件渲染器运行，配合 Xvfb 虚拟显示，能够完成：

- 编辑器无头（batchmode）操作：导入资产、修改场景、执行编辑器脚本
- 运行游戏并截图：用于自检画面效果
- 构建 Windows 播放器
- 自动化测试：Unity Test Framework 在 batchmode 下运行

**它确实意味着以下限制：**

1. **帧率不能作为性能指标。** 软件渲染下的帧率与真实 GPU 上的帧率没有可比性。性能优化只能依靠**间接指标**：Draw Call 数量、三角面数、批处理合并率、材质数量、纹理内存占用。这些都可以通过 Unity 的 Profiler API 在 batchmode 下采集。
2. **重度后处理效果无法有效验证。** 屏幕空间反射、体积雾的高质量档、大量粒子在软件渲染下要么极慢要么表现不一致。
3. **开阔大场景的画面验证困难。** 这直接决定了方案 E（狩猎模拟，开阔自然场景）的可行性评分最低，而方案 A（浓雾限制视距）与方案 C（20 米绘制距离）评分最高。

**结论**：方案选型必须优先考虑「画面在低可见度、封闭或半封闭空间内」的类型。这不是妥协，方案 A 与 C 的美术方向本身就把这一点转化成了风格优势。

## 三、Unity 环境

### 3.1 版本选择

选定 **Unity 6000.0.81f1（Unity 6.0 LTS）**，changeset `6238fec1e98f`，2026-08-06 发布。

选择理由：Unity 6.0 LTS 分支已经过长时间打磨，是当前 Steam 上商业项目使用最广的版本，社区资料与第三方插件兼容性最好。更新的 6000.3.x LTS 分支（最新 6000.3.22f1 发布于 2026-08-13，即今日）特性更多但打磨时间短，不适合以「可发行」为目标的项目。

版本数据来源：Unity 官方 Release API `https://services.api.unity.com/unity/editor/release/v1/releases`

### 3.2 安装方式

Unity Hub 的 AppImage 官方地址（`public-cdn.cloud.unity3d.com/hub/prod/UnityHub.AppImage`）目前返回 404，改为**直接下载编辑器压缩包**并解压，绕过 Hub：

```
编辑器：https://download.unity3d.com/download_unity/6238fec1e98f/LinuxEditorInstaller/Unity-6000.0.81f1.tar.xz（4.2 GB）
安装位置：/opt/unity/6000.0.81f1/Editor
```

安装脚本见 `tools/install_unity.sh`。

### 3.3 Windows 构建能力（关键结论）

Unity 官方 Release API 确认 `windows-mono`（Windows Build Support (Mono)）模块对本编辑器版本可用。这意味着：

**可以做到**：在这台 Linux 机器上构建出 Windows x64 可执行文件（`.exe` + `_Data` 目录），使用 **Mono** 脚本后端。这个产物可以直接上传到 Steam 发行。Steam 上有大量商业游戏使用 Mono 后端。

**做不到**：在 Linux 上构建 **Windows + IL2CPP** 后端的版本。IL2CPP 会把 C# 转成 C++ 再用平台原生编译器编译，Windows 目标需要 MSVC 工具链，只能在 Windows 主机上完成。Unity 官方提供的是反方向的跨平台工具链（从 Windows/macOS 构建 Linux IL2CPP），没有从 Linux 构建 Windows IL2CPP 的路径。

来源：Unity 手册《Linux IL2CPP cross-compiler》<https://docs.unity3d.com/6000.6/Documentation/Manual/linux-il2cpp-crosscompiler.html>、《Introduction to scripting back ends》<https://docs.unity3d.com/6/Documentation/Manual/scripting-backends-intro.html>

**Mono 与 IL2CPP 的实际差异，以及对本项目的影响：**

| 维度 | Mono | IL2CPP | 对本项目 |
| --- | --- | --- | --- |
| 运行性能 | 略低 | 较高（AOT 编译） | 开发期无影响；发售版建议切 IL2CPP |
| 启动速度 | 较慢 | 较快 | 可接受 |
| 构建时间 | 快 | 慢很多 | 开发期 Mono 迭代更快，是优势 |
| 代码保护 | 弱，DLL 可被反编译 | 强 | 单机游戏影响有限；若做联机需注意作弊 |
| Steam 发行 | 完全支持 | 完全支持 | 两者都能上架 |

**发行期的解决方案（三选一，需要在发售前确定）：**

1. 直接用 Mono 后端发行。技术上完全可行，缺点是代码易被反编译。
2. 在发售前找一台 Windows 机器（或云 Windows 实例）用 IL2CPP 重新构建。项目工程完全通用，只是换机器执行构建命令。
3. 配置 GitHub Actions 的 Windows runner 做发行构建。GameCI 提供现成的 Unity 构建 Action，需要在 GitHub Secrets 中配置 Unity 许可证。这是最自动化的方案，推荐。

日常开发与自检全部用 Mono 后端在本机完成，不受影响。

### 3.4 许可证

用户提供的 Unity 账号（2941529694@qq.com）用于激活 Unity Personal 许可证。激活方式：

```
Unity -batchmode -nographics -quit \
  -username <账号> -password <密码> \
  -serial <可选，Personal 版不需要> \
  -logFile /dev/stdout
```

Unity Personal 的适用条件：过去 12 个月收入与融资总额低于 20 万美元。若项目商业成功超过该门槛，需要升级到 Unity Pro。Unity 已于 2024 年取消 Runtime Fee，当前按席位订阅收费，不抽成收入。

**注意**：许可证凭据属于敏感信息，不写入代码仓库。激活在环境搭建时一次性完成，凭据通过环境变量传入。

## 四、自检与截图链路（用户明确要求的能力）

目标：让开发过程中能够「边运行边自检、截图对比目标与现状」。设计如下四层：

### 第一层：静态检查（每次提交）

无需运行游戏，直接对工程做检查：

- C# 编译错误与警告（`Unity -batchmode -quit -executeMethod` 触发编译）
- 资产规范检查：纹理尺寸、模型面数、材质数量、命名规范
- 场景引用完整性：是否有丢失的预制件引用

### 第二层：自动化测试（每次提交）

Unity Test Framework 的 EditMode 与 PlayMode 测试，在 batchmode 下运行：

```
Unity -runTests -batchmode -projectPath . -testPlatform EditMode -testResults results.xml
```

覆盖对象是纯逻辑系统：背包容量与重量计算、掉落表概率、词缀叠加规则、存档序列化往返、AI 决策函数的输入输出。这些是最能通过测试守护的部分。

### 第三层：渲染截图（每个里程碑）

在 Xvfb 虚拟显示下运行构建产物或编辑器，用 Unity 的 `ScreenCapture.CaptureScreenshot` 在预设机位拍摄固定画面。要点：

- 建立**固定的截图机位集**（例如「营地全景」「雾区入口」「战斗中」「UI 界面」），每次都从同样的角度拍，这样跨版本对比才有意义
- 截图与概念图并排存档在 `screenshots/` 目录，按里程碑编号
- 每张截图附带当时的关键指标（三角面数、Draw Call、材质数）

### 第四层：视频录制（阶段性）

用 ffmpeg 抓取 Xvfb 的帧缓冲，录制一段游戏运行过程。软件渲染下帧率低，需要按固定时间步长录制再调整播放速率。这一层用于向你展示实际的玩法节奏。

### 差距对比方法

用户要求「截图看自己和目标的差距」。具体做法：

1. 概念图（本次已产出 5 张）作为目标基准
2. 每个里程碑产出同机位的实机截图
3. 逐项对比清单：构图、色彩分布、光照方向与强度、剪影可读性、细节密度、氛围感
4. 把差距写成具体的、可执行的改进项（例如「雾的浓度不足，概念图中 25 米处已完全不可见，实机 40 米仍可见轮廓 → 调整 URP 雾的密度参数」），而不是「感觉不够好」这类无法执行的描述

---

## 五、项目工程结构（选定方案后建立）

```
/workspace
├── docs/                   # 设计文档、调研、里程碑记录
├── concepts/               # 概念图（目标基准）
├── screenshots/            # 各里程碑实机截图（与概念图对比）
├── tools/                  # 安装脚本、构建脚本、自检脚本
├── HunterGame/             # Unity 工程本体
│   ├── Assets/
│   │   ├── Scripts/
│   │   ├── Scenes/
│   │   ├── Prefabs/
│   │   ├── Materials/
│   │   ├── Settings/       # URP 配置
│   │   └── Tests/          # EditMode / PlayMode 测试
│   ├── Packages/
│   └── ProjectSettings/
└── Builds/                 # 构建产物（不入库）
```

`.gitignore` 需要排除 Unity 的 `Library/`、`Temp/`、`Logs/`、`Builds/` 以及 `*.csproj`、`*.sln`。

---

## 六、当前状态

| 事项 | 状态 |
| --- | --- |
| 市场调研 | 已完成，见 `docs/00-market-research.md` |
| 五个方案 | 已完成，见 `docs/01-game-concepts.md` |
| 概念图 | 已完成 5 张，见 `concepts/` |
| Unity 编辑器安装 | 已完成，`/opt/unity/6000.0.81f1`，7.9 GB |
| Windows 构建模块 | 已完成，从 Mac pkg 提取 PlaybackEngine，811 MB |
| 许可证激活 | 已完成，entitlement 已解析 |
| 无头渲染与截图链路验证 | 已完成，三轮迭代实测，见 `docs/03-milestone-M0.md` |
| Windows x64 构建验证 | 已完成，产物为 PE32+ 可执行文件 |
| 方案选型 | **等待用户决策** |
| Steamworks 账号 | **需要用户处理，硬阻塞项** |

M0 验证的完整证据与差距分析见 [`docs/03-milestone-M0.md`](03-milestone-M0.md)。
