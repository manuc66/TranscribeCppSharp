namespace TranscribeCppSharp.Generator;

public record RustFunction(string Name, RustType ReturnType, List<RustParam> Parameters);
