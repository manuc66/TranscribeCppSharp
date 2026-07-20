#!/bin/bash
set -e

VERSION="0.1.3"
BASE_URL="https://github.com/handy-computer/transcribe.cpp/releases/download/v${VERSION}"
DEST="native-packages"

declare -A ARCHIVES=(
  ["win-x64"]="transcribe-native-${VERSION}-windows-x86_64-cpu-vulkan.tar.gz"
  ["linux-x64"]="transcribe-native-${VERSION}-linux-x86_64-cpu-vulkan.tar.gz"
  ["linux-arm64"]="transcribe-native-${VERSION}-linux-aarch64-cpu-vulkan.tar.gz"
  ["osx-arm64"]="transcribe-native-${VERSION}-macos-arm64-metal.tar.gz"
  ["osx-x64"]="transcribe-native-${VERSION}-macos-x86_64-cpu.tar.gz"
)

for RID in "${!ARCHIVES[@]}"; do
  ARCHIVE="${ARCHIVES[$RID]}"
  TARGET="${DEST}/${RID}/runtimes/${RID}/native"

  if [ -f "${TARGET}/.done" ]; then
    echo "Already have ${RID}"
    continue
  fi

  echo "Downloading ${ARCHIVE} -> ${TARGET}"
  mkdir -p "${TARGET}"
  curl -fSL "${BASE_URL}/${ARCHIVE}" | tar xz -C "${TARGET}" --strip-components=1
  touch "${TARGET}/.done"
  echo "Installed ${RID}"
done

echo "All native libraries installed."
echo "Pack with: dotnet pack native-packages/<rid>/"
