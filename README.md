# TranscribeCppSharp

Auto-generated C# bindings for [transcribe.cpp](https://github.com/handy-computer/transcribe.cpp).

## Architecture

```
TranscribeCppSharp/
├── rust/
│   └── transcribe_sys.rs          # Rust FFI (from transcribe.cpp, committed)
├── src/
│   └── Generator/                 # CLI tool: parses Rust FFI → generates C#
│       ├── RustFfiParser.cs
│       ├── CSharpGenerator.cs
│       └── Program.cs
├── generated/
│   └── TranscribeCppSharp.Interop/
│       └── NativeMethods.cs       # Auto-generated P/Invoke (do not edit)
└── README.md
```

## How it works

1. **transcribe.cpp** provides `bindings/rust/sys/src/transcribe_sys.rs` — bindgen output from the C header
2. **Generator** parses this Rust FFI file and produces C# `DllImport` declarations
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

## Coverage

| Category | Count |
|---|---|
| Enums | 16 (87 values) |
| Functions | 97 |
| Structs | 23 |

Full coverage of the transcribe.cpp C API — generated from the same source as the official Rust, Python, TypeScript, and Swift bindings.

## Status

This generates the **raw P/Invoke layer**. A higher-level C# wrapper (Model, Session, Transcript classes) should be built on top of this.
