using System.Formats.Tar;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

if (args.Length > 0 && args[0] is "--help" or "-h")
{
    Console.WriteLine("Usage: FetchNative [--all] [--update-hashes]");
    Console.WriteLine("  (no args)         Fetch native libs for the current platform only");
    Console.WriteLine("  --all             Fetch native libs for all platforms");
    Console.WriteLine("  --update-hashes   Update build/native-sha256.json from downloaded archives");
    return 0;
}

var fetchAll = args.Contains("--all");
var updateHashes = args.Contains("--update-hashes");
var repoRoot = FindRepoRoot();
var version = (await File.ReadAllTextAsync(Path.Combine(repoRoot, "build", "TRANSCRIBE_VERSION"))).Trim();
var baseUrl = $"https://github.com/handy-computer/transcribe.cpp/releases/download/v{version}";
var dest = Path.Combine(repoRoot, "native-packages");
var hashesPath = Path.Combine(repoRoot, "build", "native-sha256.json");

var archives = new Dictionary<string, string>
{
    ["win-x64"]    = $"transcribe-native-{version}-windows-x86_64-cpu-vulkan.tar.gz",
    ["linux-x64"]  = $"transcribe-native-{version}-linux-x86_64-cpu-vulkan.tar.gz",
    ["linux-arm64"] = $"transcribe-native-{version}-linux-aarch64-cpu-vulkan.tar.gz",
    ["osx-arm64"]  = $"transcribe-native-{version}-macos-arm64-metal.tar.gz",
    ["osx-x64"]    = $"transcribe-native-{version}-macos-x86_64-cpu.tar.gz",
};

var expectedHashes = LoadExpectedHashes(hashesPath, version);

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
    await FetchOneAsync(http, rid, archive, baseUrl, dest, expectedHashes, updateHashes);
}

Console.WriteLine("Done.");

if (updateHashes)
{
    SaveHashes(hashesPath, version, expectedHashes);
    Console.WriteLine($"Updated {hashesPath}. Review the values before committing (they came from the download URL itself).");
}

return 0;

static async Task FetchOneAsync(
    HttpClient http,
    string rid,
    string archive,
    string baseUrl,
    string dest,
    Dictionary<string, string> expectedHashes,
    bool updateHashes)
{
    var target = Path.Combine(dest, rid, "runtimes", rid, "native");
    var doneFile = Path.Combine(target, ".done");
    var libFile = Path.Combine(target, NativeLibFileName());

    if (!updateHashes && File.Exists(doneFile) && File.Exists(libFile))
    {
        Console.WriteLine($"Already have {rid}");
        return;
    }

    var url = $"{baseUrl}/{archive}";
    Console.WriteLine($"Downloading {url}");

    Directory.CreateDirectory(target);

    var tmpGz = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
    var tmpTar = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
    try
    {
        var bytes = await http.GetByteArrayAsync(url);

        // Verify integrity before writing anything.
        var actualHash = VerifyChecksum(bytes, rid, expectedHashes, updateHashes);
        if (updateHashes)
        {
            expectedHashes[rid] = actualHash;
        }

        await File.WriteAllBytesAsync(tmpGz, bytes);
        await DecompressToTarAsync(tmpGz, tmpTar);
        await ExtractToTargetAsync(tmpTar, target);

        await File.WriteAllTextAsync(doneFile, $"fetched {DateTime.UtcNow:O}");
        Console.WriteLine($"Installed {rid}");
    }
    finally
    {
        if (File.Exists(tmpGz)) File.Delete(tmpGz);
        if (File.Exists(tmpTar)) File.Delete(tmpTar);
    }
}

static string NativeLibFileName()
{
    if (OperatingSystem.IsWindows()) return "transcribe.dll";
    if (OperatingSystem.IsMacOS()) return "libtranscribe.dylib";
    return "libtranscribe.so";
}

static string VerifyChecksum(
    byte[] bytes,
    string rid,
    Dictionary<string, string> expectedHashes,
    bool updateHashes)
{
    var actualHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    if (expectedHashes.TryGetValue(rid, out var expected))
    {
        if (!string.Equals(actualHash, expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Checksum mismatch for {rid}: expected sha256:{expected}, got sha256:{actualHash}. " +
                "The downloaded archive does not match build/native-sha256.json. Aborting.");
        }

        Console.WriteLine($"Checksum OK for {rid} (sha256:{actualHash})");
    }
    else if (!updateHashes)
    {
        throw new InvalidDataException(
            $"No expected checksum for {rid} in build/native-sha256.json. " +
            "Run with --update-hashes to add it (review the value before committing).");
    }

    return actualHash;
}

static async Task DecompressToTarAsync(string tmpGz, string tmpTar)
{
    // Decompress gzip -> tar
    await using (var gzStream = File.OpenRead(tmpGz))
    await using (var gzip = new GZipStream(gzStream, CompressionMode.Decompress))
    await using (var tarOut = File.Create(tmpTar))
    {
        await gzip.CopyToAsync(tarOut);
    }
}

static async Task ExtractToTargetAsync(string tmpTar, string target)
{
    var tmpExtract = Path.Combine(Path.GetTempPath(), "fetchnative-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(tmpExtract);
    try
    {
        await TarFile.ExtractToDirectoryAsync(tmpTar, tmpExtract, overwriteFiles: true);
        MoveContentsToTarget(tmpExtract, target);
    }
    finally
    {
        if (Directory.Exists(tmpExtract)) Directory.Delete(tmpExtract, recursive: true);
    }
}

static void MoveContentsToTarget(string tmpExtract, string target)
{
    // Extract tar (strip top-level directory like tar --strip-components=1)
    var entries = Directory.GetFileSystemEntries(tmpExtract);
    if (entries.Length == 1 && Directory.Exists(entries[0]))
    {
        foreach (var srcEntry in Directory.GetFileSystemEntries(entries[0], "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(entries[0], srcEntry);
            var destPath = Path.Combine(target, relative);
            if (File.Exists(srcEntry))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destPath));
                File.Copy(srcEntry, destPath, overwrite: true);
            }
        }
    }
}

static Dictionary<string, string> LoadExpectedHashes(string path, string version)
{
    if (!File.Exists(path))
    {
        return new Dictionary<string, string>();
    }

    using var doc = JsonDocument.Parse(File.ReadAllText(path));
    if (!doc.RootElement.TryGetProperty(version, out var versionEntry))
    {
        return new Dictionary<string, string>();
    }

    var result = new Dictionary<string, string>();
    foreach (var prop in versionEntry.EnumerateObject())
    {
        // Stored as "sha256:<hex>"; normalize to just the hex for comparison.
        var value = prop.Value.GetString() ?? string.Empty;
        if (value.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
        {
            value = value.Substring("sha256:".Length);
        }

        result[prop.Name] = value;
    }

    return result;
}

static void SaveHashes(string path, string version, Dictionary<string, string> hashes)
{
    var sorted = hashes.OrderBy(kv => kv.Key, StringComparer.Ordinal)
        .ToDictionary(kv => kv.Key, kv => "sha256:" + kv.Value);
    var root = new Dictionary<string, object> { [version] = sorted };
    File.WriteAllText(path, JsonSerializer.Serialize(root, JsonOptions.Instance) + Environment.NewLine);
}

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

file static class JsonOptions
{
    public static readonly System.Text.Json.JsonSerializerOptions Instance = new() { WriteIndented = true };
}
