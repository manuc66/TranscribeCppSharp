using TranscribeCppSharp.Generator;
using TranscribeCppSharp.SharedSource;

// The generator only reads what the repository vendors (ffi/rust, ffi/c) and
// writes generated/. The boundary is the checkout, so a path read from a file
// or mistyped on the command line cannot reach outside it.
string repoRoot = FindRepoRoot();
var ffiPath = Resolve(args.Length > 0 ? args[0] : "ffi/rust/transcribe_sys.rs");
var headerPath = Resolve(args.Length > 1 ? args[1] : "ffi/c/transcribe.h");
var outputDir = Resolve(args.Length > 2 ? args[2] : "generated/TranscribeCppSharp.Interop");

string Resolve(string path)
    => BuildPath.Contain(repoRoot, Path.IsPathRooted(path) ? path : Path.Combine(repoRoot, path));

if (!File.Exists(ffiPath))
{
    await Console.Error.WriteLineAsync($"Rust FFI file not found: {ffiPath}");
    await Console.Error.WriteLineAsync("Usage: Generator <path/to/transcribe_sys.rs> [path/to/transcribe.h] [output-dir]");
    return 1;
}

Console.WriteLine($"Parsing: {ffiPath}");
var parser = new RustFfiParser(await File.ReadAllTextAsync(ffiPath));

var enums = parser.ParseEnums();
var functions = parser.ParseFunctions();
var structs = parser.ParseStructs();

Console.WriteLine($"  {enums.Count} enums ({enums.Sum(e => e.Values.Count)} values)");
Console.WriteLine($"  {functions.Count} functions");
Console.WriteLine($"  {structs.Count} structs");

CHeaderDoc? headerDoc = null;
if (Directory.Exists(headerPath))
{
    // Upstream splits the public surface across transcribe.h and
    // transcribe/*.h; merge them so extension, VAD, LANGID and diarize
    // functions get their doc comments too.
    string[] headers = HeaderFiles(headerPath);
    if (headers.Length > 0)
    {
        headerDoc = CHeaderDoc.FromContents(headers.Select(File.ReadAllText));
        Console.WriteLine($"Docs from: {headerPath} ({headers.Length} header(s))");
    }
    else
    {
        Console.WriteLine($"Warning: no C headers found under {headerPath}; generating without doc comments.");
    }
}
else if (File.Exists(headerPath))
{
    headerDoc = CHeaderDoc.FromContents([await File.ReadAllTextAsync(headerPath)]);
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

// Kept in step with GoldenFileTest.HeaderContents: the golden snapshot depends
// on this order, so a drift between the two fails that test rather than shipping.
static string[] HeaderFiles(string directory)
{
    var paths = new List<string>();
    string rootHeader = Path.Combine(directory, "transcribe.h");
    if (File.Exists(rootHeader))
    {
        paths.Add(rootHeader);
    }

    string subHeaderDir = Path.Combine(directory, "transcribe");
    if (Directory.Exists(subHeaderDir))
    {
        paths.AddRange(Directory.GetFiles(subHeaderDir, "*.h").OrderBy(p => p, StringComparer.Ordinal));
    }

    return [.. paths];
}

static string FindRepoRoot()
{
    var dir = AppContext.BaseDirectory;
    while (dir is not null)
    {
        if (File.Exists(Path.Combine(dir, "TranscribeCppSharp.slnx")))
        {
            return dir;
        }

        dir = Path.GetDirectoryName(dir);
    }

    throw new DirectoryNotFoundException("Could not find the repository root (TranscribeCppSharp.slnx).");
}
