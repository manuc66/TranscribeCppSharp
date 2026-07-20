using TranscribeCppSharp.Generator;

var ffiPath = args.Length > 0 ? args[0] : "rust/transcribe_sys.rs";
var outputDir = args.Length > 1 ? args[1] : "generated/TranscribeCppSharp.Interop";

if (!File.Exists(ffiPath))
{
    Console.Error.WriteLine($"Rust FFI file not found: {ffiPath}");
    Console.Error.WriteLine("Usage: Generator <path/to/transcribe_sys.rs> [output-dir]");
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

var generator = new CSharpGenerator();
var code = generator.Generate(parser);

Directory.CreateDirectory(outputDir);
var outputPath = Path.Combine(outputDir, "NativeMethods.cs");
File.WriteAllText(outputPath, code);
Console.WriteLine($"Generated: {outputPath}");
Console.WriteLine("Done.");
return 0;
