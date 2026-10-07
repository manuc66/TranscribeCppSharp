using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace TranscribeCppSharp.Interop.Tests;

/// <summary>
/// The pinned upstream catalog is exactly what was recorded, byte for byte.
/// </summary>
/// <remarks>
/// The files are upstream's, fetched once and committed. Git already fixes what is
/// in the repository, so this test is not guarding against corruption — it is the
/// record that the pin still says what it said: re-zipping from a different tag, or
/// regenerating the checksums without the data, fails here rather than silently
/// swapping the numbers the CLI and the GUI display.
/// </remarks>
public class CatalogPinningTests
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static string CatalogDir => Path.Combine(RepoRoot, "catalog");

    [Fact]
    public void ThePinnedRecordIsPresent()
    {
        string pinPath = Path.Combine(CatalogDir, "PINNED.json");
        Assert.True(File.Exists(pinPath), $"missing {pinPath}");

        using JsonDocument pin = JsonDocument.Parse(File.ReadAllText(pinPath));
        Assert.Equal("v0.3.0", pin.RootElement.GetProperty("tag").GetString());

        // Same version as the native library this project binds, or the catalog
        // would describe models the pinned transcribe.cpp build has never seen.
        string version = File.ReadAllText(
            Path.Combine(RepoRoot, "build", "TRANSCRIBE_VERSION")).Trim();
        Assert.Equal($"v{version}", pin.RootElement.GetProperty("tag").GetString());
    }

    [Fact]
    public void EveryEntryMatchesItsRecordedChecksum()
    {
        string zipPath = Path.Combine(CatalogDir, "catalog.zip");
        Assert.True(File.Exists(zipPath), $"missing {zipPath}");

        Dictionary<string, string> expected = ReadSums();
        Assert.NotEmpty(expected);

        using var zip = ZipFile.OpenRead(zipPath);
        var actual = zip.Entries
            .Where(e => !string.IsNullOrEmpty(e.Name))
            .ToDictionary(e => e.FullName, e => Hash(e), StringComparer.Ordinal);

        string[] missing = expected.Keys.Except(actual.Keys, StringComparer.Ordinal).ToArray();
        string[] extra = actual.Keys.Except(expected.Keys, StringComparer.Ordinal).ToArray();
        Assert.True(missing.Length == 0, $"missing entries: {string.Join(", ", missing)}");
        Assert.True(extra.Length == 0, $"unrecorded entries: {string.Join(", ", extra)}");

        string[] changed = expected
            .Where(kv => actual[kv.Key] != kv.Value)
            .Select(kv => kv.Key)
            .ToArray();
        Assert.True(changed.Length == 0,
            $"{changed.Length} entry(ies) differ from the pin: {string.Join(", ", changed.Take(10))}");
    }

    [Fact]
    public void TheRecordCoversEveryModelInTheCatalog()
    {
        // A model added to the manifest but absent from the pinned catalog would
        // show an empty detail pane with no indication that upstream simply has no
        // record — better said by the test than left to be discovered on screen.
        using JsonDocument manifest = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(RepoRoot, "src", "TranscribeCppSharp", "models.json")));

        Dictionary<string, string> sums = ReadSums();
        string[] absent = manifest.RootElement.GetProperty("models").EnumerateObject()
            .Select(p => p.Name)
            .Where(alias => !sums.ContainsKey($"{UpstreamName(alias)}.json"))
            .ToArray();

        Assert.True(absent.Length == 0,
            $"no upstream record for: {string.Join(", ", absent)}");
    }

    /// <summary>
    /// Our alias is derived from the repository name, upstream's is theirs, and one
    /// of the seventy-two does not agree (<c>sensevoicesmall</c> vs
    /// <c>sensevoice-small</c>). The join key in this project is the alias, so the
    /// mapping is spelled out rather than guessed at each call site.
    /// </summary>
    private static string UpstreamName(string alias)
        => alias == "sensevoicesmall" ? "sensevoice-small" : alias;

    private static Dictionary<string, string> ReadSums()
    {
        string path = Path.Combine(CatalogDir, "SHA256SUMS");
        Assert.True(File.Exists(path), $"missing {path}");

        var sums = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string line in File.ReadAllLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            string[] parts = line.Split("  ", StringSplitOptions.None);
            Assert.True(parts.Length == 2, $"malformed checksum line: '{line}'");
            sums[parts[1]] = parts[0];
        }

        return sums;
    }

    private static string Hash(ZipArchiveEntry entry)
    {
        using Stream stream = entry.Open();
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }
}