using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;

if (args.Length > 0 && args[0] is "--help" or "-h")
{
    Console.WriteLine("Usage: FetchNative [--all]");
    Console.WriteLine("  (no args)  Fetch native libs for the current platform only");
    Console.WriteLine("  --all      Fetch native libs for all platforms");
    return 0;
}

var fetchAll = args.Contains("--all");
var repoRoot = FindRepoRoot();
var version = File.ReadAllText(Path.Combine(repoRoot, "build", "TRANSCRIBE_VERSION")).Trim();
var baseUrl = $"https://github.com/handy-computer/transcribe.cpp/releases/download/v{version}";
var dest = Path.Combine(repoRoot, "native-packages");

var archives = new Dictionary<string, string>
{
    ["win-x64"]    = $"transcribe-native-{version}-windows-x86_64-cpu-vulkan.tar.gz",
    ["linux-x64"]  = $"transcribe-native-{version}-linux-x86_64-cpu-vulkan.tar.gz",
    ["linux-arm64"] = $"transcribe-native-{version}-linux-aarch64-cpu-vulkan.tar.gz",
    ["osx-arm64"]  = $"transcribe-native-{version}-macos-arm64-metal.tar.gz",
    ["osx-x64"]    = $"transcribe-native-{version}-macos-x86_64-cpu.tar.gz",
};

var currentRid = GetCurrentRid();
var toFetch = fetchAll
    ? archives
    : archives.Where(kv => kv.Key == currentRid).ToDictionary(kv => kv.Key, kv => kv.Value);

if (toFetch.Count == 0)
{
    Console.WriteLine($"No native archive for current platform ({currentRid}). Use --all to fetch all.");
    return 1;
}

using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

foreach (var (rid, archive) in toFetch)
{
    var target = Path.Combine(dest, rid, "runtimes", rid, "native");
    var doneFile = Path.Combine(target, ".done");
    var libFile = Path.Combine(target, OperatingSystem.IsWindows() ? "transcribe.dll"
        : OperatingSystem.IsMacOS() ? "libtranscribe.dylib"
        : "libtranscribe.so");

    if (File.Exists(doneFile) && File.Exists(libFile))
    {
        Console.WriteLine($"Already have {rid}");
        continue;
    }

    var url = $"{baseUrl}/{archive}";
    Console.WriteLine($"Downloading {url}");

    Directory.CreateDirectory(target);

    var tmpGz = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
    var tmpTar = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
    try
    {
        var bytes = await http.GetByteArrayAsync(url);
        await File.WriteAllBytesAsync(tmpGz, bytes);

        // Decompress gzip -> tar
        await using (var gzStream = File.OpenRead(tmpGz))
        await using (var gzip = new GZipStream(gzStream, CompressionMode.Decompress))
        await using (var tarOut = File.Create(tmpTar))
        {
            await gzip.CopyToAsync(tarOut);
        }

        // Extract tar (strip top-level directory like tar --strip-components=1)
        var tmpExtract = Path.Combine(Path.GetTempPath(), "fetchnative-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpExtract);
        try
        {
            TarFile.ExtractToDirectory(tmpTar, tmpExtract, overwriteFiles: true);
            // Move contents from the single top-level dir to target
            var entries = Directory.GetFileSystemEntries(tmpExtract);
            if (entries.Length == 1 && Directory.Exists(entries[0]))
            {
                foreach (var srcEntry in Directory.GetFileSystemEntries(entries[0], "*", SearchOption.AllDirectories))
                {
                    var relative = Path.GetRelativePath(entries[0], srcEntry);
                    var destPath = Path.Combine(target, relative);
                    if (File.Exists(srcEntry))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                        File.Copy(srcEntry, destPath, overwrite: true);
                    }
                }
            }
        }
        finally
        {
            if (Directory.Exists(tmpExtract)) Directory.Delete(tmpExtract, recursive: true);
        }
        File.WriteAllText(doneFile, $"fetched {DateTime.UtcNow:O}");
        Console.WriteLine($"Installed {rid}");
    }
    finally
    {
        if (File.Exists(tmpGz)) File.Delete(tmpGz);
        if (File.Exists(tmpTar)) File.Delete(tmpTar);
    }
}

Console.WriteLine("Done.");
return 0;

static string FindRepoRoot()
{
    var dir = AppContext.BaseDirectory;
    while (dir != null)
    {
        if (File.Exists(Path.Combine(dir, "TranscribeCppSharp.slnx")))
            return dir;
        dir = Path.GetDirectoryName(dir);
    }
    throw new DirectoryNotFoundException("Could not find repo root (TranscribeCppSharp.slnx)");
}

static string GetCurrentRid()
{
    if (OperatingSystem.IsWindows())
        return Environment.Is64BitProcess ? "win-x64" : throw new PlatformNotSupportedException("win-x86 not supported");
    if (OperatingSystem.IsLinux())
        return RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "linux-arm64" : "linux-x64";
    if (OperatingSystem.IsMacOS())
        return RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "osx-arm64" : "osx-x64";
    throw new PlatformNotSupportedException($"Unsupported OS: {RuntimeInformation.OSDescription}");
}
