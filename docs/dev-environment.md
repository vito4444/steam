# 开发环境搭建

本项目在无图形界面的 Linux 云端虚拟机上开发，并交叉构建 Windows 可执行文件。
以下步骤在 Ubuntu 24.04 上验证通过。

## 1. 系统依赖

```bash
sudo apt-get update
sudo apt-get install -y xvfb ffmpeg
pip3 install --break-system-packages pillow numpy
```

`xvfb` 提供虚拟显示。Unity 编辑器即使以 `-batchmode -nographics` 运行，
初始化阶段仍会访问 X 相关接口，因此所有 Unity 命令都需要包一层 `xvfb-run`。

Mesa 的 `llvmpipe` 软件光栅化器负责实际渲染。本机实测可用的图形上下文为
`OpenGL 4.5 (Core Profile) Mesa 25.2.8`，足以运行 URP 并输出 1920×1080 截图。

## 2. Unity Hub

```bash
wget -qO- https://hub.unity3d.com/linux/keys/public \
  | gpg --dearmor \
  | sudo tee /usr/share/keyrings/Unity_Technologies_ApS.gpg > /dev/null
echo "deb [signed-by=/usr/share/keyrings/Unity_Technologies_ApS.gpg] https://hub.unity3d.com/linux/repos/deb stable main" \
  | sudo tee /etc/apt/sources.list.d/unityhub.list
sudo apt-get update
sudo apt-get install -y unityhub
```

## 3. Unity 编辑器与 Windows 构建模块

```bash
xvfb-run -a unityhub --headless install \
  --version 6000.3.22f1 \
  --module windows-mono \
  --childModules
```

安装到 `~/Unity/Hub/Editor/6000.3.22f1/`，占用约 9 GB。
`windows-mono` 是从 Linux 交叉构建 Windows 版本所需的模块。

Unity Hub 3.x 的命令行不再提供登录与授权子命令，授权需要直接调用授权客户端。

## 4. 许可证激活

```bash
cd ~/Unity/Hub/Editor/6000.3.22f1/Editor/Data/Resources/Licensing/Client/
./Unity.Licensing.Client --activate-ulf --username "<邮箱>" --password "<密码>"
```

成功时输出 `Successfully activated ULF license for user <邮箱>`。
Personal 版许可证不需要 `--serial` 参数；Pro 版需要追加 `--serial <序列号>`。

查看当前授权状态：

```bash
./Unity.Licensing.Client --showEntitlements
```

## 5. 工程初始化

首次拉取仓库后需要还原包依赖并生成 URP 资产。包依赖会由 Unity 在首次打开工程时
自动还原；URP 资产与验证场景已入库，无需重新生成。

若需要从零重建这两者：

```bash
source tools/env.sh
maner_unity -executeMethod Maner.EditorTools.ManerSetup.InstallPackages -logFile /tmp/pkg.log
maner_unity -executeMethod Maner.EditorTools.ManerBootstrap.Run -logFile /tmp/bootstrap.log
```

## 6. 已验证的管线

| 环节 | 命令 | 实测结果 |
| --- | --- | --- |
| Linux 构建 | `tools/build.sh linux` | 成功，用时 35.7 s，体积 88.7 MB |
| Windows 构建 | `tools/build.sh windows` | 成功，用时 42.6 s，体积 92.1 MB，产物为 `PE32+ executable (GUI) x86-64` |
| 无人值守运行与截图 | `tools/selfcheck.sh` | 输出 3 张 1920×1080 PNG，附帧时间统计 |
| 画面差距量化 | `tools/compare.py` | 输出并排对比图与中文差距报告 |

## 7. 无头环境下踩过的坑

以下问题的共同特征是**静默失效**：不报错、不警告，在编辑器里也完全看不出来，
只有在构建产物的实机截图里才会暴露。它们几乎占掉了控制舱视觉实现的全部调试时间，
记录在此以免重蹈。

| 现象 | 根因 | 处理 |
| --- | --- | --- |
| 全场材质变成白色默认材质 | 运行时用 `Shader.Find` 取的 shader 没有被任何资产引用，构建时不打包 | 写入 Graphics Settings 的 Always Included Shaders |
| 整间屋子被冲成惨白 | 改了 `RenderSettings.ambient*` 但没调用 `DynamicGI.UpdateEnvironment()`，引擎继续用场景默认的天空环境光 | 修改后显式刷新环境 |
| 灯亮着但对画面零贡献，平行光却正常 | URP 的着色器变体剥离把附加光源与自发光变体整批裁掉，因为没有资产用到它们 | 关闭 URP Global Settings 里的变体剥离 |
| 主光源完全不出光 | 光源位于灯泡玻璃球体的球心，被自己的灯泡投影遮蔽 | 灯泡渲染器关闭投影 |
| 表盘只剩一圈框和一根针 | 用代码把 URP Lit 切成透明模式没生效，玻璃罩以不透明状态盖住了表盘面 | 移除玻璃罩；如需透明优先用 Unlit |
| 面板与表盘面整个不可见 | `MeshBuilder` 的四边形绕序反向，面被背面剔除 | 修正绕序为 0-1-2 / 0-2-3 |
| 丝印文字看着像被镜像 | 位图缓冲区按「左上为原点、Y 向下」绘制，与 Unity 纹理坐标原点在左下相反 | 上传前垂直翻转缓冲区 |
| 贴图正确但显示为一块纯色 | 运行时创建的材质其 `_BaseMap_ST` 未必是 (1,1,0,0) | 显式设置 tiling 与 offset |
| 点光源强度调 20 倍几乎无变化 | Unity 6 URP 使用物理光照单位，点光源以坎德拉计量，衰减远比预期陡 | 小空间内改用平行光承担主照明 |
| 构建报告成功但产物是旧代码 | 脚本有编译错误时 Unity 会退回上一次成功编译的程序集继续出包，且返回码为 0 | `tools/build.sh` 显式检查日志中的 `error CS` |

诊断这类问题的有效手段有三个，都已固化到仓库里：

1. `tools/calibrate_lighting.py`——在同一份构建里扫描参数组合，避免每改一个数就重新构建。
2. `Maner.EditorTools.ManerDiagnostics`——生成只有一块地、一个盒子、一盏灯的最小场景，
   把变量降到最低来判断某个引擎特性到底是否工作。
3. 运行时命令行开关（`-manerNoPostFX`、`-manerNoFill`、`-manerPanelDebug` 等）——
   在一份构建里做 A/B 二分。

## 8. 已知限制

1. **IL2CPP 无法交叉构建。** Windows 目标的 IL2CPP 后端必须在 Windows 机器上构建。
   开发期使用 Mono 后端，发行前需要在 Windows 上做一次 IL2CPP 构建并重新验证。
2. **无音频输出设备。** 云端环境没有声卡，运行时日志会出现
   `FMOD failed to initialize the output device` 与 ALSA 相关报错。这不影响构建与渲染，
   但意味着**音频效果无法在本环境中验证，必须由人工试玩确认**。
3. **软件渲染的性能数据不可作为目标机性能参考。** `llvmpipe` 的帧时间只能用于
   发现「相对退化」（例如某次改动让帧时间翻倍），不能用于判断真实硬件上的绝对帧率。
4. **手感、乐趣、恐怖感、社交体验无法自动化评估。** 这些必须通过人工试玩闭环。
