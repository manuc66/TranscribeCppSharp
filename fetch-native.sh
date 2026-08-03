#!/bin/bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
VERSION=$(cat "${SCRIPT_DIR}/TRANSCRIBE_VERSION")
BASE_URL="https://github.com/handy-computer/transcribe.cpp/releases/download/v${VERSION}"
DEST="${SCRIPT_DIR}/native-packages"

# Portable: no associative arrays (works on macOS stock bash 3.2)
fetch_rid() {
  local rid="$1"
  local archive="$2"
  local target="${DEST}/${rid}/runtimes/${rid}/native"

  if [ -f "${target}/.done" ]; then
    echo "Already have ${rid}"
    return
  fi

  echo "Downloading ${archive} -> ${target}"
  mkdir -p "${target}"
  curl -fSL --proto '=https' --proto-redir '=https' --retry 3 "${BASE_URL}/${archive}" | tar xz -C "${target}" --strip-components=1
  touch "${target}/.done"
  echo "Installed ${rid}"
}

fetch_rid "win-x64"   "transcribe-native-${VERSION}-windows-x86_64-cpu-vulkan.tar.gz"
fetch_rid "linux-x64"  "transcribe-native-${VERSION}-linux-x86_64-cpu-vulkan.tar.gz"
fetch_rid "linux-arm64" "transcribe-native-${VERSION}-linux-aarch64-cpu-vulkan.tar.gz"
fetch_rid "osx-arm64"  "transcribe-native-${VERSION}-macos-arm64-metal.tar.gz"
fetch_rid "osx-x64"    "transcribe-native-${VERSION}-macos-x86_64-cpu.tar.gz"

echo "All native libraries installed."
echo "Pack with: dotnet pack native-packages/<rid>/"
