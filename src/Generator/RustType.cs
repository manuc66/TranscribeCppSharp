namespace TranscribeCppSharp.Generator;

/// <summary>
/// Structural model of a Rust type from FFI. Replaces string-based Contains/StartsWith.
/// </summary>
public abstract record RustType;

public record VoidType : RustType;

public record BoolType : RustType;

public record PrimitiveType(string Name) : RustType;

public record PointerType(PointerMutability Mut, RustType Inner) : RustType;

public record OpaqueHandleType(string RustName) : RustType;

public record StructType(string RustName) : RustType;

public record SliceType(RustType ElementType) : RustType;

public record UnknownType(string RawText) : RustType;

public enum PointerMutability
{
    Const,
    Mutable,
}
