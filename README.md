# TranscribeCppSharp

Auto-generated C# bindings for [transcribe.cpp](https://github.com/handy-computer/transcribe.cpp).

## Architecture

```
TranscribeCppSharp/
├── rust/
│   └── transcribe_sys.rs              # Rust FFI (from transcribe.cpp, committed)
├── src/
│   └── Generator/                     # CLI tool: parses Rust FFI → generates C#
│       ├── Program.cs
│       ├── RustFfiParser.cs           # Parses transcribe_sys.rs into RustType model
│       ├── RustType.cs                # Structural type model (no string matching)
│       └── CSharpGenerator.cs         # Pattern-matches RustType → C# declarations
├── generated/
│   └── TranscribeCppSharp.Interop/
│       ├── TranscribeCppSharp.Interop.csproj   # SYSLIB1051/1054/1055 as errors
│       └── NativeMethods.cs           # Auto-generated P/Invoke (do not edit)
├── tests/
│   └── TranscribeCppSharp.Interop.Tests/
│       ├── GoldenFileTest.cs          # Snapshot: catches generator drift
│       └── EnumParityTest.cs          # Rust enum values ↔ C# enum ints
└── TranscribeCppSharp.slnx
```

## How it works

1. **transcribe.cpp** provides `bindings/rust/sys/src/transcribe_sys.rs` — bindgen output from the C header
2. **Generator** parses this Rust FFI file into a structural `RustType` model, then pattern-matches to produce C# `LibraryImport` declarations
3. **generated/NativeMethods.cs** is committed so consumers don't need Rust tooling

## Regenerate

When transcribe.cpp updates its Rust bindings:

```bash
# Update the Rust FFI source
curl -sL -o rust/transcribe_sys.rs \
  https://raw.githubusercontent.com/handy-computer/transcribe.cpp/main/bindings/rust/sys/src/transcribe_sys.rs

# Regenerate C# bindings
dotnet run --project src/Generator
```

## Tests

### Unit Tests
```bash
dotnet test
```

### Integration Tests (with real model)
Requires the native libraries to be fetched first.
```bash
./fetch-native.sh
./run-integration-tests.sh
```

### Smoke Test
```bash
dotnet run --project samples/SmokeTest -- test-models/ggml-tiny.bin test-audio/jfk.wav
```

| Test | What it catches |
|---|---|
| `GeneratedOutput_MatchesCommittedSnapshot` | Any silent drift in generated output |
| `AllEnumValues_MatchRustValues` | Enum reordering, renaming, or value drift |
| `AbiStructSize_IsNonZero` | Struct layout mismatch (requires native lib) |

The interop project enforces `SYSLIB1051`/`SYSLIB1054`/`SYSLIB1055` as build errors, catching `StringMarshalling`/`MarshalAs` conflicts at compile time.

## Coverage

| Category | Count |
|---|---|
| Enums | 16 (87 values) |
| Functions | 97 |
| Structs | 23 |

Full coverage of the transcribe.cpp C API — generated from the same source as the official Rust, Python, TypeScript, and Swift bindings.

## Status

The generated P/Invoke layer is **complete and usable as-is**. Typed handles, correct marshalling, callback delegates — no wrapper required for direct usage. See `tests/TranscribeCppSharp.Interop.Tests/` for usage examples.
