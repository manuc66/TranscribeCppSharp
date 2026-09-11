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

# Opt-in heavy asset for the diarization tests (MOSS Q4_K_M, ~617 MB).
# Only fetched when WITH_DIARIZATION_MODEL=1; the diarization test skips
# otherwise. Pinned to the upstream release the wrapper binds to.
MOSS_FILE="$MODEL_DIR/moss-transcribe-diarize-q4-k-m.gguf"
MOSS_URL="https://huggingface.co/handy-computer/MOSS-Transcribe-Diarize-gguf/resolve/main/MOSS-Transcribe-Diarize-Q4_K_M.gguf"

# curl: restrict both direct and redirected URLs to HTTPS
CURL_PROTO="=https"

# Detect platform
OS="$(uname -s)"
ARCH="$(uname -m)"

case "$OS" in
  Linux)  RID="linux" ;;
  Darwin) RID="osx" ;;
  MINGW*|MSYS*|CYGWIN*)  RID="win" ;;
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
if [[ ! -f "$MODEL_FILE" ]]; then
    echo "Downloading Whisper tiny model..."
    curl -fSL --proto "$CURL_PROTO" --proto-redir "$CURL_PROTO" -o "$MODEL_FILE" "$MODEL_URL"
    echo "Model downloaded to $MODEL_FILE"
else
    echo "Model already exists at $MODEL_FILE"
fi

# Download audio if not exists
if [[ ! -f "$AUDIO_FILE" ]]; then
    echo "Downloading test audio..."
    curl -fSL --proto "$CURL_PROTO" --proto-redir "$CURL_PROTO" -o "$AUDIO_FILE" "$AUDIO_URL"
    echo "Audio downloaded to $AUDIO_FILE"
else
    echo "Audio already exists at $AUDIO_FILE"
fi

# Download diarization model only on explicit opt-in (large asset)
if [[ "${WITH_DIARIZATION_MODEL:-0}" == "1" ]]; then
    if [[ ! -f "$MOSS_FILE" ]]; then
        echo "Downloading MOSS diarization model (~617 MB)..."
        curl -fSL --proto "$CURL_PROTO" --proto-redir "$CURL_PROTO" -o "$MOSS_FILE" "$MOSS_URL"
        echo "Model downloaded to $MOSS_FILE"
    else
        echo "Diarization model already exists at $MOSS_FILE"
    fi
else
    echo "Skipping diarization model (set WITH_DIARIZATION_MODEL=1 to fetch it)"
fi

# Set library path for native library (the CopyNativeLib target also places the
# libs in the app output dir, which the DllImportResolver checks first; these env
# vars are a belt-and-braces fallback and are only meaningful on Unix).
case "$OS" in
  MINGW*|MSYS*|CYGWIN*)
    # Windows: rely on the resolver (output dir / NuGet cache); no LD_LIBRARY_PATH.
    ;;
  Darwin)
    export DYLD_LIBRARY_PATH="$PWD/${NATIVE_DIR}:$DYLD_LIBRARY_PATH"
    ;;
  *)
    export LD_LIBRARY_PATH="$PWD/${NATIVE_DIR}:$LD_LIBRARY_PATH"
    ;;
esac

# Check if native library exists — fetch if missing
LIBFILE="${NATIVE_DIR}/libtranscribe.so"
if [[ "$OS" = "Darwin" ]]; then LIBFILE="${NATIVE_DIR}/libtranscribe.dylib"; fi
case "$OS" in
  MINGW*|MSYS*|CYGWIN*) LIBFILE="${NATIVE_DIR}/transcribe.dll" ;;
  *) ;;
esac

if [[ ! -f "$LIBFILE" ]]; then
    echo "Native library not found in ${NATIVE_DIR}. Fetching..."
    dotnet run --project tools/FetchNative
    # Re-check after fetch
    if [[ ! -f "$LIBFILE" ]]; then
        echo "Error: Fetch failed. Native library still missing in ${NATIVE_DIR}." >&2
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
if [[ -n "$COVERLET_THRESHOLD" ]]; then
  THRESHOLD_ARGS=(-p:Threshold="$COVERLET_THRESHOLD" -p:ThresholdType=line -p:ThresholdStat=total)
fi
dotnet test --logger "console;verbosity=detailed" \
  -p:CollectCoverage=true \
  "-p:CoverletOutputFormat=$COVERLET_FORMAT" \
  "-p:CoverletOutput=$(pwd)/test-results/coverage.$COVERLET_FORMAT.xml" \
  "${THRESHOLD_ARGS[@]}"

echo ""
echo "=== Integration tests completed ==="
