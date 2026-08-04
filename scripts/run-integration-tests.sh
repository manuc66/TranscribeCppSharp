#!/bin/bash
set -e

# Script to run integration tests with a real model
# Downloads a tiny GGUF model and runs all tests

echo "=== TranscribeCppSharp Integration Tests ==="
echo ""

# Configuration
MODEL_DIR="./test-models"
MODEL_FILE="$MODEL_DIR/ggml-tiny.bin"
MODEL_URL="https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-tiny.bin"

AUDIO_DIR="./test-audio"
AUDIO_FILE="$AUDIO_DIR/jfk.wav"
AUDIO_URL="https://github.com/ggerganov/whisper.cpp/raw/master/samples/jfk.wav"

# Detect platform
OS="$(uname -s)"
ARCH="$(uname -m)"

case "$OS" in
  Linux)  RID="linux" ;;
  Darwin) RID="osx" ;;
  *)      echo "Unsupported OS: $OS"; exit 1 ;;
esac

case "$ARCH" in
  x86_64|amd64)  RID="${RID}-x64" ;;
  aarch64|arm64) RID="${RID}-arm64" ;;
  *)             echo "Unsupported architecture: $ARCH"; exit 1 ;;
esac

NATIVE_DIR="native-packages/${RID}/runtimes/${RID}/native"

# Create directories
mkdir -p "$MODEL_DIR"
mkdir -p "$AUDIO_DIR"

# Download model if not exists
if [ ! -f "$MODEL_FILE" ]; then
    echo "Downloading Whisper tiny model..."
    curl -fSL --proto '=https' --proto-redir '=https' -o "$MODEL_FILE" "$MODEL_URL"
    echo "Model downloaded to $MODEL_FILE"
else
    echo "Model already exists at $MODEL_FILE"
fi

# Download audio if not exists
if [ ! -f "$AUDIO_FILE" ]; then
    echo "Downloading test audio..."
    curl -fSL --proto '=https' --proto-redir '=https' -o "$AUDIO_FILE" "$AUDIO_URL"
    echo "Audio downloaded to $AUDIO_FILE"
else
    echo "Audio already exists at $AUDIO_FILE"
fi

# Set library path for native library
export LD_LIBRARY_PATH="$PWD/${NATIVE_DIR}:$LD_LIBRARY_PATH"
export DYLD_LIBRARY_PATH="$PWD/${NATIVE_DIR}:$DYLD_LIBRARY_PATH"

# Check if native library exists — fetch if missing
if [ ! -f "${NATIVE_DIR}/libtranscribe.so" ] && [ ! -f "${NATIVE_DIR}/libtranscribe.dylib" ]; then
    echo "Native library not found in ${NATIVE_DIR}. Fetching..."
    if [ -f "tools/FetchNative/FetchNative.csproj" ]; then
        dotnet run --project tools/FetchNative
    elif [ -f "scripts/fetch-native.sh" ]; then
        bash scripts/fetch-native.sh
    else
        echo "Error: No fetch tool found. Place native libs in ${NATIVE_DIR}."
        exit 1
    fi
    # Re-check after fetch
    if [ ! -f "${NATIVE_DIR}/libtranscribe.so" ] && [ ! -f "${NATIVE_DIR}/libtranscribe.dylib" ]; then
        echo "Error: Fetch failed. Native library still missing in ${NATIVE_DIR}."
        exit 1
    fi
fi

echo ""
echo "Running integration tests (RID: ${RID})..."
echo ""

# Run tests with the model path. Coverlet collects line coverage and enforces a
# 70% total-line threshold by default (measured ~76% across Generator + wrapper
# + Interop; the threshold applies to the mean, not per module — the generated
# Interop P/Invoke surface sits lower by design). Coverlet resolves the output
# path relative to the test project directory, so use an absolute path to land
# it in ./test-results at the repo root (the CI upload expects it there).
#
# The format and threshold are overridable for consumers that need a different
# report (e.g. SonarCloud uses OpenCover and no threshold):
#   COVERLET_FORMAT=cobertura (default) | opencover | ...
#   COVERLET_THRESHOLD=70 (default; empty disables the gate)
COVERLET_FORMAT="${COVERLET_FORMAT:-cobertura}"
COVERLET_THRESHOLD="${COVERLET_THRESHOLD:-70}"
mkdir -p "$(pwd)/test-results"
THRESHOLD_ARGS=()
if [ -n "$COVERLET_THRESHOLD" ]; then
  THRESHOLD_ARGS=(/p:Threshold="$COVERLET_THRESHOLD" /p:ThresholdType=line /p:ThresholdStat=total)
fi
dotnet test --logger "console;verbosity=detailed" \
  /p:CollectCoverage=true \
  "/p:CoverletOutputFormat=$COVERLET_FORMAT" \
  "/p:CoverletOutput=$(pwd)/test-results/coverage.$COVERLET_FORMAT.xml" \
  "${THRESHOLD_ARGS[@]}"

echo ""
echo "=== Integration tests completed ==="
