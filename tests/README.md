# Integration Tests

This project contains unit tests and integration tests to validate the C# wrapper for transcribe.cpp.

## Unit Tests

Unit tests do not require the native library or GGUF model. They validate:
- Code generation
- Enum parity
- Type structure

Run:
```bash
dotnet test
```

## Integration Tests

Integration tests require:
1. The native transcribe.cpp library
2. A Whisper GGUF model

### Setup

1. Download the native library:
```bash
./fetch-native.sh
```

2. Download a GGUF model (e.g., tiny model):
```bash
mkdir -p test-models
curl -L -o test-models/ggml-tiny.bin https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-tiny.bin
```

3. Create a test audio file:
```bash
mkdir -p test-audio
# Use ffmpeg or another tool to create a 16kHz mono WAV file
ffmpeg -f lavfi -i "sine=frequency=440:duration=1" -ar 16000 -ac 1 test-audio/test.wav
```

### Running

Use the integration script:
```bash
./run-integration-tests.sh
```

Or run tests directly:
```bash
export LD_LIBRARY_PATH="$PWD/native-packages/linux-x64/runtimes/linux-x64/native:$LD_LIBRARY_PATH"
dotnet test --filter "FullyQualifiedName~HighLevelApiTests"
```

## Test Structure

- `EnumParityTest.cs`: Verifies enum parity between Rust and C#
- `GoldenFileTest.cs`: Verifies generated code matches reference file
- `HighLevelApiTests.cs`: High-level API integration tests

## Notes

- Tests requiring the native library are marked with `Skip = "Requires native library"`
- Tests requiring a GGUF model are marked with `Skip = "Requires integration test environment"`
- Tests are automatically skipped if dependencies are not present
