#!/usr/bin/env bash
# 构建 maner 的独立可执行版本。
#
#   tools/build.sh windows    构建 Windows 64 位（Mono 后端，可从 Linux 交叉构建）
#   tools/build.sh linux      构建 Linux 64 位（供本机自检运行使用）
#   tools/build.sh all        两个都构建

set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/env.sh"

build_one() {
  local platform="$1" method output log
  case "$platform" in
    windows)
      method="Maner.EditorTools.ManerBuild.Windows64"
      output="$MANER_BUILD_DIR/windows"
      ;;
    linux)
      method="Maner.EditorTools.ManerBuild.Linux64"
      output="$MANER_BUILD_DIR/linux"
      ;;
    *)
      echo "未知平台: $platform（可选 windows / linux / all）" >&2
      return 2
      ;;
  esac

  log="$MANER_LOG_DIR/build-$platform.log"
  echo "== 构建 $platform -> $output"
  maner_unity -executeMethod "$method" -manerBuildPath "$output" -logFile "$log"
  local code=$?

  grep -E '^\[ManerBuild\]' "$log" || true
  if [[ $code -ne 0 ]]; then
    echo "构建失败，完整日志: $log" >&2
    grep -E 'error CS|BuildFailedException' "$log" | head -20 >&2 || true
    return $code
  fi
  echo "构建完成: $output"
}

targets=("${1:-all}")
if [[ "${targets[0]}" == "all" ]]; then
  targets=(linux windows)
fi

for t in "${targets[@]}"; do
  build_one "$t"
done
