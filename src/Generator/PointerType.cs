namespace TranscribeCppSharp.Generator;

public record PointerType(PointerMutability Mut, RustType Inner) : RustType;
