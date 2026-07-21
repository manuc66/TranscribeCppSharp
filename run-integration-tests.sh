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

# Create directories
mkdir -p "$MODEL_DIR"
mkdir -p "$AUDIO_DIR"

# Download model if not exists
if [ ! -f "$MODEL_FILE" ]; then
    echo "Downloading Whisper tiny model..."
    curl -L -o "$MODEL_FILE" "$MODEL_URL"
    echo "Model downloaded to $MODEL_FILE"
else
    echo "Model already exists at $MODEL_FILE"
fi

# Download audio if not exists
if [ ! -f "$AUDIO_FILE" ]; then
    echo "Downloading test audio..."
    curl -L -o "$AUDIO_FILE" "$AUDIO_URL"
    echo "Audio downloaded to $AUDIO_FILE"
else
    echo "Audio already exists at $AUDIO_FILE"
fi

# Set library path for native library
export LD_LIBRARY_PATH="$PWD/native-packages/linux-x64/runtimes/linux-x64/native:$LD_LIBRARY_PATH"

# Check if native library exists
if [ ! -f "native-packages/linux-x64/runtimes/linux-x64/native/libtranscribe.so" ]; then
    echo "Error: Native library not found. Run ./fetch-native.sh first."
    exit 1
fi

echo ""
echo "Running integration tests..."
echo ""

# Run tests with the model path
dotnet test --filter "FullyQualifiedName~HighLevelApiTests" --logger "console;verbosity=detailed"

echo ""
echo "=== Integration tests completed ==="
