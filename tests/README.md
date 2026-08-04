# Integration Tests

This project contains unit tests and integration tests to validate the C# wrapper for transcribe.cpp.

## Prerequisites

All tests require the native transcribe.cpp library. It is auto-fetched during build if missing.
To fetch manually:

```bash
dotnet run --project tools/FetchNative
```

## Unit Tests

Unit tests validate code generation, enum parity, and type structure.
Builder tests require the native library (auto-fetched at build time).

Run:
```bash
dotnet test
```

## Integration Tests

Integration tests additionally require a GGUF model and test audio.

### Setup

1. The native library (auto-fetched, or manually via `dotnet run --project tools/FetchNative`).

2. Download a GGUF model (e.g., tiny model):
```bash
mkdir -p test-models
curl -L -o test-models/ggml-tiny.bin https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-tiny.bin
```

3. Download test audio:
```bash
mkdir -p test-audio
curl -L -o test-audio/jfk.wav https://github.com/ggerganov/whisper.cpp/raw/master/samples/jfk.wav
```

### Running

Use the integration script:
```bash
./scripts/run-integration-tests.sh
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
