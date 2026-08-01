namespace TranscribeCppSharp.Generator;

public record RustStructLayout(string Name, ulong Size, ulong Align, List<RustStructLayoutField> Fields);
