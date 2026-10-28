#!/usr/bin/env bash
# Cloudflare Workers 版のビルドと、手元（wrangler dev）での起動。
# 使い方: scripts/workers.sh build
#         scripts/workers.sh dev [wrangler dev に渡す引数...]
#
# NativeAOT-LLVM の実験版コンパイラには macOS 向けがないので、macOS では Linux（arm64）の .NET SDK コンテナの中でビルドする。
# WASI SDK は .cache/wasi-sdk/ にダウンロードして使う（コミットしない）。
set -euo pipefail

ROOT=$(cd "$(dirname "$0")/.." && pwd)
PROJECT="$ROOT/src/CycleSharingMcp.Workers"
WASI_SDK_VERSION=29.0
WASI_SDK_TAG=wasi-sdk-29
SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:10.0.402

# GitHub Release の asset の sha256
wasi_sdk_sha256() {
  case "$1" in
    wasi-sdk-29.0-x86_64-linux) echo 87d1d1a2879d139cdc624b968efad3d4a97b8078cdff95e63ac88ecafd1a0171 ;;
    wasi-sdk-29.0-arm64-linux) echo 052ad773397dc9e5aa99fb4cfef694175e6b1e81bb2ad1d3c8e7b3fc81441b7c ;;
    *) echo "対応していない WASI SDK です: $1" >&2; return 1 ;;
  esac
}

# Linux 向けの WASI SDK を用意してパスを返す（macOS でもコンテナの中で使うので Linux 向け）
wasi_sdk() {
  local arch=$1
  local name="wasi-sdk-${WASI_SDK_VERSION}-${arch}-linux"
  local dir="$ROOT/.cache/wasi-sdk/$name"
  if [[ ! -x "$dir/bin/clang" ]]; then
    mkdir -p "$ROOT/.cache/wasi-sdk"
    local archive="$ROOT/.cache/wasi-sdk/$name.tar.gz"
    echo "WASI SDK $WASI_SDK_VERSION ($arch) をダウンロードします" >&2
    curl -fsSL -o "$archive" "https://github.com/WebAssembly/wasi-sdk/releases/download/$WASI_SDK_TAG/$name.tar.gz"
    local expected actual
    expected=$(wasi_sdk_sha256 "$name")
    actual=$( (sha256sum "$archive" 2>/dev/null || shasum -a 256 "$archive") | cut -d' ' -f1)
    if [[ "$actual" != "$expected" ]]; then
      echo "WASI SDK の sha256 が一致しません（期待 $expected、実際 $actual）" >&2
      rm -f "$archive"
      return 1
    fi
    tar -xzf "$archive" -C "$ROOT/.cache/wasi-sdk"
    rm -f "$archive"
  fi
  echo "$dir"
}

build() {
  case "$(uname -s)" in
    Linux)
      local arch
      case "$(uname -m)" in
        x86_64) arch=x86_64 ;;
        aarch64 | arm64) arch=arm64 ;;
        *) echo "対応していない CPU です: $(uname -m)" >&2; return 1 ;;
      esac
      WASI_SDK_PATH=$(wasi_sdk "$arch") dotnet publish "$PROJECT" -c Release
      ;;
    Darwin)
      local sdk nuget
      sdk=$(wasi_sdk arm64)
      nuget="${NUGET_PACKAGES:-$HOME/.nuget/packages}"
      mkdir -p "$nuget"
      # リポジトリと NuGet のキャッシュをコンテナにも同じパスでつなぐ（obj/ の中のパスが手元と食い違わないように）
      docker run --rm --platform linux/arm64 \
        -v "$ROOT:$ROOT" -v "$nuget:$nuget" \
        -e NUGET_PACKAGES="$nuget" -e WASI_SDK_PATH="$sdk" -e DOTNET_CLI_TELEMETRY_OPTOUT=1 \
        -w "$PROJECT" "$SDK_IMAGE" dotnet publish -c Release
      ;;
    *)
      echo "この OS には対応していません: $(uname -s)（Linux か macOS で実行してください）" >&2
      return 1
      ;;
  esac
}

case "${1:-}" in
  build)
    build
    ;;
  dev)
    shift
    build
    cd "$PROJECT"
    exec wrangler dev "$@"
    ;;
  *)
    echo "使い方: $0 build | dev [wrangler dev の引数...]" >&2
    exit 2
    ;;
esac
