#!/usr/bin/env bash
#
# 在无 GPU 的 Linux 机器上搭建项目 CODER 的 Unity 开发环境。
#
# 完成后具备的能力：
#   - Unity 6000.0.81f1 LTS 编辑器（Linux）
#   - StandaloneWindows64 交叉编译（Mono 后端）
#   - StandaloneLinux64 构建（用于自动化测试）
#   - Xvfb + Mesa llvmpipe 软件渲染，可在无显卡环境下截图
#
# 凭据从环境变量读取，不写入仓库：
#   UNITY_EMAIL     Unity 账号邮箱
#   UNITY_PASSWORD  Unity 账号密码
#
# 用法：
#   UNITY_EMAIL=... UNITY_PASSWORD=... bash tools/setup-unity.sh
#
set -euo pipefail

UNITY_VERSION="${UNITY_VERSION:-6000.0.81f1}"
CHANGESET="${CHANGESET:-6238fec1e98f}"
BASE="https://download.unity3d.com/download_unity/${CHANGESET}"
INSTALL_ROOT="${INSTALL_ROOT:-/opt/unity/${UNITY_VERSION}}"
DL="${DL:-/tmp/unity-dl}"
EDITOR="${INSTALL_ROOT}/Editor/Unity"
WINDIR="${INSTALL_ROOT}/Editor/Data/PlaybackEngines/WindowsStandaloneSupport"

log() { echo "[$(date +%H:%M:%S)] $*"; }
die() { echo "ERROR: $*" >&2; exit 1; }

# ---------------------------------------------------------------- 1. 系统依赖

log "1/5 安装系统依赖"
export DEBIAN_FRONTEND=noninteractive
sudo apt-get update -qq
sudo apt-get install -y -qq --no-install-recommends \
  xz-utils p7zip-full cpio curl ca-certificates \
  libgtk-3-0t64 libnss3 libasound2t64 libxtst6 libxss1 libgbm1 \
  libglu1-mesa libgl1 libgl1-mesa-dri mesa-utils libegl1 libvulkan1 \
  libunwind8 libc6-dev libncurses6 libstdc++6 \
  libnotify4 libxcursor1 libxrandr2 libxi6 libxinerama1 \
  xvfb x11-utils libxkbcommon-x11-0 libsecret-1-0

# ---------------------------------------------------------------- 2. 编辑器

log "2/5 下载并解压 Unity ${UNITY_VERSION} 编辑器"
mkdir -p "$DL"
sudo mkdir -p "$INSTALL_ROOT"
sudo chown -R "$(id -u):$(id -g)" "$(dirname "$INSTALL_ROOT")"

if [ ! -f "$DL/editor.tar.xz" ]; then
  curl -fL --retry 4 --retry-delay 5 -o "$DL/editor.tar.xz.part" \
    "${BASE}/LinuxEditorInstaller/Unity-${UNITY_VERSION}.tar.xz"
  mv "$DL/editor.tar.xz.part" "$DL/editor.tar.xz"
fi

if [ ! -x "$EDITOR" ]; then
  tar -xf "$DL/editor.tar.xz" -C "$INSTALL_ROOT"
fi
[ -x "$EDITOR" ] || die "编辑器二进制不存在：$EDITOR"

# ---------------------------------------------------------------- 3. Windows 模块
#
# Unity 官方只为 Windows 构建支持模块提供 macOS 的 .pkg 包，但内容与平台无关。
# .pkg 是 xar 容器，里面的 Payload~ 是 cpio 归档，解开后的目录结构就是
# PlaybackEngines/WindowsStandaloneSupport 的内容。

log "3/5 安装 Windows 构建支持模块"
if [ ! -f "$DL/win-mono.pkg" ]; then
  curl -fL --retry 4 --retry-delay 5 -o "$DL/win-mono.pkg.part" \
    "${BASE}/MacEditorTargetInstaller/UnitySetup-Windows-Mono-Support-for-Editor-${UNITY_VERSION}.pkg"
  mv "$DL/win-mono.pkg.part" "$DL/win-mono.pkg"
fi

if [ ! -d "$WINDIR/Variations/win64_player_nondevelopment_mono" ]; then
  rm -rf "$DL/winpkg"
  mkdir -p "$DL/winpkg/extracted"
  ( cd "$DL/winpkg" && 7z x -y "$DL/win-mono.pkg" > /dev/null )
  PAYLOAD=$(find "$DL/winpkg" -maxdepth 2 -name "Payload*" -type f | head -1)
  [ -n "$PAYLOAD" ] || die "在 .pkg 里找不到 Payload"
  ( cd "$DL/winpkg/extracted" && cpio -idm --quiet < "$PAYLOAD" )
  mkdir -p "$(dirname "$WINDIR")"
  rm -rf "$WINDIR"
  cp -r "$DL/winpkg/extracted" "$WINDIR"
fi
[ -d "$WINDIR/Variations/win64_player_nondevelopment_mono" ] \
  || die "Windows 发行版构建变体缺失"

# ---------------------------------------------------------------- 4. 许可证

log "4/5 激活 Unity 许可证"
LIC="${INSTALL_ROOT}/Editor/Data/Resources/Licensing/Client/Unity.Licensing.Client"
[ -x "$LIC" ] || die "许可证客户端不存在：$LIC"

if [ -z "${UNITY_EMAIL:-}" ] || [ -z "${UNITY_PASSWORD:-}" ]; then
  die "需要设置 UNITY_EMAIL 和 UNITY_PASSWORD 环境变量"
fi

# 输出里可能回显账号，过滤后再打印。
"$LIC" --activate-ulf --username "$UNITY_EMAIL" --password "$UNITY_PASSWORD" \
  > /tmp/unity-activate.log 2>&1 || die "许可证激活失败，详见 /tmp/unity-activate.log"
sed -E "s/${UNITY_PASSWORD//\//\\/}/***/g" /tmp/unity-activate.log | tail -3

# ---------------------------------------------------------------- 5. 验证

log "5/5 验证软件渲染"
export LIBGL_ALWAYS_SOFTWARE=1
export GALLIUM_DRIVER=llvmpipe
RENDERER=$(xvfb-run -a -s "-screen 0 1920x1080x24" glxinfo 2>/dev/null \
  | grep -i "OpenGL renderer string" || true)
echo "  $RENDERER"
echo "$RENDERER" | grep -qi llvmpipe || die "软件渲染不可用"

cat <<EOF

环境就绪。

  编辑器：      $EDITOR
  Windows 模块：$WINDIR
  图形栈：      Xvfb + Mesa llvmpipe（OpenGL 4.5 Core）

使用要点：
  - 截图任务必须去掉 -nographics，否则 Camera.Render() 得到全黑
  - 构建任务可以加 -nographics 以加快速度
  - 运行前 export LIBGL_ALWAYS_SOFTWARE=1 GALLIUM_DRIVER=llvmpipe

示例：
  xvfb-run -a -s "-screen 0 1920x1080x24" "$EDITOR" \\
    -batchmode -quit -projectPath <项目> -executeMethod <方法> -logFile <日志>
EOF
