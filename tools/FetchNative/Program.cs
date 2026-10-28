using System.Formats.Tar;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

if (args.Length > 0 && args[0] is "--help" or "-h")
{
    Console.WriteLine("Usage: FetchNative [--all] [--update-hashes] [--verify] [--update-libs-hashes]");
    Console.WriteLine("  (no args)             Fetch native libs for the current platform only");
    Console.WriteLine("  --all                 Fetch native libs for all platforms");
    Console.WriteLine("  --update-hashes       Update build/native-sha256.json from downloaded archives");
    Console.WriteLine("  --verify              Re-hash the installed libraries and compare them with");
    Console.WriteLine("                        build/native-libs-sha256.json; downloads nothing");
    Console.WriteLine("  --update-libs-hashes  Record the installed libraries in that file");
    return 0;
}

var fetchAll = args.Contains("--all");
var updateHashes = args.Contains("--update-hashes");
var verify = args.Contains("--verify");
var updateLibHashes = args.Contains("--update-libs-hashes");
var repoRoot = FindRepoRoot();
var version = (await File.ReadAllTextAsync(Path.Combine(repoRoot, "build", "TRANSCRIBE_VERSION"))).Trim();
var baseUrl = $"https://github.com/handy-computer/transcribe.cpp/releases/download/v{version}";
var dest = Path.Combine(repoRoot, "native-packages");
var hashesPath = Path.Combine(repoRoot, "build", "native-sha256.json");
var libsHashesPath = Path.Combine(repoRoot, "build", "native-libs-sha256.json");

var archives = new Dictionary<string, string>
{
    ["win-x64"]    = $"transcribe-native-{version}-windows-x86_64-cpu-vulkan.tar.gz",
    ["linux-x64"]  = $"transcribe-native-{version}-linux-x86_64-cpu-vulkan.tar.gz",
    ["linux-arm64"] = $"transcribe-native-{version}-linux-aarch64-cpu-vulkan.tar.gz",
    ["osx-arm64"]  = $"transcribe-native-{version}-macos-arm64-metal.tar.gz",
    ["osx-x64"]    = $"transcribe-native-{version}-macos-x86_64-cpu.tar.gz",
};

var expectedHashes = LoadExpectedHashes(hashesPath, version);

if (verify)
{
    // Offline on purpose: verification is what a build runs after restoring a
    // cache, and a missing library must fail instead of being fetched silently.
    return VerifyInstalled(dest, version, libsHashesPath) ? 0 : 1;
}

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

if (updateLibHashes)
{
    UpdateLibHashes(dest, version, libsHashesPath);
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
                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                File.Copy(srcEntry, destPath, overwrite: true);
            }
        }
    }
}

// ── Installed-file integrity ────────────────────────────────────────────────
//
// build/native-sha256.json pins the archive that is downloaded; this pins the
// files that end up in the package. They are not the same check: FetchNative
// skips a download when the ".done" marker and the library are present, so a
// restored CI cache (or a tampered tree) never passes through the archive hash
// at all. The build re-hashes the installed files instead, which is also the
// octet that ships.

static string InstalledRoot(string dest, string rid)
    => Path.Combine(dest, rid, "runtimes", rid, "native");

static SortedDictionary<string, string> HashInstalledFiles(string root)
{
    var files = new SortedDictionary<string, string>(StringComparer.Ordinal);
    foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
    {
        var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
        if (relative is ".done" or "native-libs-sha256.json")
        {
            continue;
        }

        using var stream = File.OpenRead(path);
        files[relative] = "sha256:" + Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    return files;
}

static void UpdateLibHashes(string dest, string version, string path)
{
    var byVersion = LoadLibHashes(path);
    var installed = new Dictionary<string, SortedDictionary<string, string>>(StringComparer.Ordinal);
    foreach (var rid in Directory.GetDirectories(dest).Select(Path.GetFileName).OrderBy(x => x, StringComparer.Ordinal))
    {
        if (rid is null)
        {
            continue;
        }

        var root = InstalledRoot(dest, rid);
        if (Directory.Exists(root))
        {
            installed[rid] = HashInstalledFiles(root);
        }
    }

    if (installed.Count == 0)
    {
        Console.Error.WriteLine($"No installed libraries under {dest}; run without --update-libs-hashes first.");
        return;
    }

    // Keep every version already recorded, and every RID of this version that is
    // not installed here: a machine with one platform must not drop the others.
    byVersion.TryGetValue(version, out var existing);
    existing ??= new Dictionary<string, SortedDictionary<string, string>>(StringComparer.Ordinal);
    foreach (var (rid, files) in installed)
    {
        existing[rid] = files;
    }

    byVersion[version] = existing;

    var ordered = byVersion
        .OrderBy(kv => Version.TryParse(kv.Key, out var parsed) ? parsed : new Version(0, 0))
        .ToDictionary(kv => kv.Key, kv => (object)kv.Value);
    File.WriteAllText(path, JsonSerializer.Serialize(ordered, FetchNative.JsonOptions.Instance) + Environment.NewLine);
    Console.WriteLine($"Updated {path} for {string.Join(", ", installed.Keys)}.");
    Console.WriteLine("Review the values before committing: they describe what was installed, not what upstream published.");
}

static Dictionary<string, Dictionary<string, SortedDictionary<string, string>>> LoadLibHashes(string path)
{
    var result = new Dictionary<string, Dictionary<string, SortedDictionary<string, string>>>(StringComparer.Ordinal);
    if (!File.Exists(path))
    {
        return result;
    }

    using var doc = JsonDocument.Parse(File.ReadAllText(path));
    foreach (var versionEntry in doc.RootElement.EnumerateObject())
    {
        var rids = new Dictionary<string, SortedDictionary<string, string>>(StringComparer.Ordinal);
        foreach (var ridEntry in versionEntry.Value.EnumerateObject())
        {
            var files = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var fileEntry in ridEntry.Value.EnumerateObject())
            {
                files[fileEntry.Name] = fileEntry.Value.GetString() ?? string.Empty;
            }

            rids[ridEntry.Name] = files;
        }

        result[versionEntry.Name] = rids;
    }

    return result;
}

static bool VerifyInstalled(string dest, string version, string path)
{
    var byVersion = LoadLibHashes(path);
    if (!byVersion.TryGetValue(version, out var expected))
    {
        Console.Error.WriteLine($"No entry for {version} in {path}.");
        Console.Error.WriteLine("Run: dotnet run --project tools/FetchNative -- --all --update-libs-hashes");
        return false;
    }

    var problems = new List<string>();
    var verified = 0;
    foreach (var rid in Directory.GetDirectories(dest).Select(Path.GetFileName).OrderBy(x => x, StringComparer.Ordinal))
    {
        if (rid is null || !Directory.Exists(InstalledRoot(dest, rid)))
        {
            continue;
        }

        if (!expected.TryGetValue(rid, out var expectedFiles))
        {
            problems.Add($"{rid}: installed but has no recorded hashes");
            continue;
        }

        var actualFiles = HashInstalledFiles(InstalledRoot(dest, rid));
        foreach (var (relative, expectedHash) in expectedFiles)
        {
            if (!actualFiles.TryGetValue(relative, out var actualHash))
            {
                problems.Add($"{rid}/{relative}: missing");
            }
            else if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                problems.Add($"{rid}/{relative}: expected {expectedHash}, got {actualHash}");
            }
        }

        foreach (var relative in actualFiles.Keys.Except(expectedFiles.Keys, StringComparer.Ordinal))
        {
            problems.Add($"{rid}/{relative}: present but not recorded");
        }

        verified++;
    }

    if (verified == 0)
    {
        Console.Error.WriteLine($"No installed libraries under {dest} to verify.");
        return false;
    }

    if (problems.Count > 0)
    {
        Console.Error.WriteLine($"Installed native libraries do not match {path}:");
        foreach (var problem in problems)
        {
            Console.Error.WriteLine($"  {problem}");
        }

        Console.Error.WriteLine("Delete native-packages/*/runtimes and re-run without --verify, or update the hashes deliberately.");
        return false;
    }

    Console.WriteLine($"Verified {verified} RID(s) against {path}.");
    return true;
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
    // Keep the hashes of every version already recorded: the pin moves forward,
    // but a checkout pinned to an older release still needs its checksums to
    // verify what it downloads. Overwriting the file with only the current
    // version would silently drop them.
    var byVersion = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
    if (File.Exists(path))
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            byVersion[prop.Name] = JsonSerializer.Deserialize<Dictionary<string, string>>(prop.Value.GetRawText())
                ?? new Dictionary<string, string>();
        }
    }

    byVersion[version] = hashes.OrderBy(kv => kv.Key, StringComparer.Ordinal)
        .ToDictionary(kv => kv.Key, kv => "sha256:" + kv.Value);

    var ordered = byVersion
        .OrderBy(kv => Version.TryParse(kv.Key, out var parsed) ? parsed : new Version(0, 0))
        .ToDictionary(kv => kv.Key, kv => (object)kv.Value);
    File.WriteAllText(path, JsonSerializer.Serialize(ordered, FetchNative.JsonOptions.Instance) + Environment.NewLine);
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

namespace FetchNative
{
    file static class JsonOptions
    {
        public static readonly System.Text.Json.JsonSerializerOptions Instance = new() { WriteIndented = true };
    }
}
