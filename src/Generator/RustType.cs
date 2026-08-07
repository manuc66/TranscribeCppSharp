namespace TranscribeCppSharp.Generator;

/// <summary>
/// Structural model of a Rust type from FFI. Replaces string-based Contains/StartsWith.
/// Empty by design: it is the closed marker base of the type-node union used in
/// pattern matching (VoidType, BoolType, PointerType, ...).
/// </summary>
// NOSONAR S2094: deliberate empty base of a sealed type hierarchy.
public abstract record RustType;
