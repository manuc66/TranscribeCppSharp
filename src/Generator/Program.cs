using TranscribeCppSharp.Generator;

var ffiPath = args.Length > 0 ? args[0] : "ffi/rust/transcribe_sys.rs";
var headerPath = args.Length > 1 ? args[1] : "ffi/c/transcribe.h";
var outputDir = args.Length > 2 ? args[2] : "generated/TranscribeCppSharp.Interop";

if (!File.Exists(ffiPath))
{
    await Console.Error.WriteLineAsync($"Rust FFI file not found: {ffiPath}");
    await Console.Error.WriteLineAsync("Usage: Generator <path/to/transcribe_sys.rs> [path/to/transcribe.h] [output-dir]");
    return 1;
}

Console.WriteLine($"Parsing: {ffiPath}");
var parser = RustFfiParser.FromFile(ffiPath);

var enums = parser.ParseEnums();
var functions = parser.ParseFunctions();
var structs = parser.ParseStructs();

Console.WriteLine($"  {enums.Count} enums ({enums.Sum(e => e.Values.Count)} values)");
Console.WriteLine($"  {functions.Count} functions");
Console.WriteLine($"  {structs.Count} structs");

CHeaderDoc? headerDoc = null;
if (File.Exists(headerPath))
{
    headerDoc = CHeaderDoc.FromFile(headerPath);
    Console.WriteLine($"Docs from: {headerPath}");
}
else
{
    Console.WriteLine($"Warning: C header not found at {headerPath}; generating without doc comments.");
}

var generator = new CSharpGenerator();
var code = generator.Generate(parser, headerDoc);

Directory.CreateDirectory(outputDir);
var outputPath = Path.Combine(outputDir, "NativeMethods.cs");
await File.WriteAllTextAsync(outputPath, code);
Console.WriteLine($"Generated: {outputPath}");

Console.WriteLine("Done.");
return 0;
