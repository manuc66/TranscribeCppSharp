// Regenerates the CLI model manifest (src/TranscribeCppSharp.Cli/models.json)
// from the official GGUF repositories published on HuggingFace. One alias per
// repository, using a default quantization; every entry pins the repository
// revision and the file's sha256 (from the HF LFS "oid") and records the
// upstream license reported by the model card.
//
// Usage: dotnet run --project tools/UpdateModelManifest [-- --author <org>] [--quant <Q>] [--out <path>]
//        dotnet run --project tools/UpdateModelManifest -- --languages-only <path>
// BCL only (HttpClient + System.Text.Json).

using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using TranscribeCppSharp.Tools.UpdateModelManifest;

string author = ArgAfter("--author") ?? "handy-computer";
string quant = ArgAfter("--quant") ?? "Q5_K_M";
string outPath = ArgAfter("--out") ?? Path.Combine(FindRepoRoot(), "src", "TranscribeCppSharp", "models.json");

using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
http.DefaultRequestHeaders.UserAgent.ParseAdd("TranscribeCppSharp.UpdateModelManifest/1.0");

// Languages first, because they are a separate question from the catalogue: the
// pins, the sizes and the checksums are what a manifest exists for, and re-listing
// them just to add a display column would re-pin 72 revisions. This reads an
// existing manifest and writes back only the language lists.
string? languagesOnly = ArgAfter("--languages-only");
if (languagesOnly is not null)
{
    await EnrichLanguagesAsync(http, languagesOnly);
    return;
}

await Console.Error.WriteLineAsync($"Listing GGUF repositories for author '{author}'...");
List<string> repos = await ListGgufReposAsync(http, author);
await Console.Error.WriteLineAsync($"Found {repos.Count} '-gguf' repositories.");

var models = new SortedDictionary<string, ModelEntry>(StringComparer.Ordinal);
foreach (string repo in repos)
{
    ModelEntry? entry = await BuildEntryAsync(http, repo, quant);
    if (entry is null)
    {
        await Console.Error.WriteLineAsync($"  ! {repo}: no usable .gguf file, skipped");
        continue;
    }

    string alias = repo[(repo.LastIndexOf('/') + 1)..];
    if (alias.EndsWith("-gguf", StringComparison.Ordinal))
    {
        alias = alias[..^"-gguf".Length];
    }

    alias = alias.ToLowerInvariant();
    models[alias] = entry;
    await Console.Error.WriteLineAsync($"  + {alias}  ({repo}, {entry.Quant})");
}

var manifest = new Manifest { DefaultQuant = quant, Models = models };
await WriteManifestAsync(outPath, manifest);
await Console.Error.WriteLineAsync($"Wrote {models.Count} models to {outPath}");

static async Task<List<string>> ListGgufReposAsync(HttpClient http, string author)
{
    string url = $"https://huggingface.co/api/models?author={Uri.EscapeDataString(author)}&limit=1000";
    using JsonDocument doc = JsonDocument.Parse(await http.GetStringAsync(url));
    var repos = new List<string>();
    foreach (JsonElement model in doc.RootElement.EnumerateArray())
    {
        string? id = model.GetProperty("id").GetString();
        if (id is not null && id.EndsWith("-gguf", StringComparison.OrdinalIgnoreCase))
        {
            repos.Add(id);
        }
    }

    repos.Sort(StringComparer.Ordinal);
    return repos;
}

static async Task<ModelEntry?> BuildEntryAsync(HttpClient http, string repo, string quant)
{
    string modelUrl = $"https://huggingface.co/api/models/{repo}";
    using JsonDocument modelDoc = JsonDocument.Parse(await http.GetStringAsync(modelUrl));
    JsonElement model = modelDoc.RootElement;

    string revision = model.GetProperty("sha").GetString()!;
    string license = ExtractLicense(model);
    string[] languages = ExtractLanguages(model);

    string treeUrl = $"https://huggingface.co/api/models/{repo}/tree/{revision}?recursive=true&expand=true";
    using JsonDocument treeDoc = JsonDocument.Parse(await http.GetStringAsync(treeUrl));

    var files = new List<HfFile>();
    foreach (JsonElement file in treeDoc.RootElement.EnumerateArray())
    {
        string? path = file.GetProperty("path").GetString();
        if (path is null || !path.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        string? sha256 = file.TryGetProperty("lfs", out JsonElement lfs) && lfs.TryGetProperty("oid", out JsonElement oid)
            ? oid.GetString()
            : null;
        long size = file.TryGetProperty("lfs", out JsonElement lfs2) && lfs2.TryGetProperty("size", out JsonElement lfsSize)
            ? lfsSize.GetInt64()
            : file.TryGetProperty("size", out JsonElement plainSize) ? plainSize.GetInt64() : 0;

        if (sha256 is not null)
        {
            files.Add(new HfFile(path, sha256, size));
        }
    }

    if (files.Count == 0)
    {
        return null;
    }

    string modelStem = repo[(repo.LastIndexOf('/') + 1)..];
    modelStem = modelStem.EndsWith("-gguf", StringComparison.Ordinal) ? modelStem[..^"-gguf".Length] : modelStem;

    HfFile? chosen = Pick(files, quant);
    if (chosen is null)
    {
        return null;
    }

    return new ModelEntry
    {
        Repo = repo,
        Revision = revision,
        License = license,
        LicenseUrl = $"https://huggingface.co/{repo}",
        Quant = chosen.Quant,
        File = chosen.Path,
        Sha256 = chosen.Sha256,
        Size = chosen.Size,
        Languages = languages.Length > 0 ? languages : null,
    };
}

static HfFile? Pick(List<HfFile> files, string preferredQuant)
{
    // Preferred quantization, then sensible fallbacks, then anything. Match on
    // the file name suffix so files nested in a sub folder (e.g. bundle/…) work.
    string[] order = { preferredQuant, "Q5_K_M", "Q8_0", "Q4_K_M", "F16", "F32", "Q6_K" };
    foreach (string q in order)
    {
        HfFile? match = files.FirstOrDefault(f => f.Name.EndsWith($"-{q}.gguf", StringComparison.OrdinalIgnoreCase));
        if (match is not null)
        {
            return match;
        }
    }

    return files[0];
}

/// <summary>
/// Reads a model card's <c>language</c> list out of an existing manifest.
/// </summary>
/// <remarks>
/// Separate from the catalogue pass on purpose: adding a display column must not
/// re-pin a single revision, sha256 or size. Everything else in the file is read
/// and written back untouched, through the same DTO and the same serializer the
/// catalogue pass uses, so the formatting cannot drift from it.
/// </remarks>
static async Task EnrichLanguagesAsync(HttpClient http, string path)
{
    Manifest existing = JsonSerializer.Deserialize<Manifest>(
        await File.ReadAllTextAsync(path), ManifestJson.Read)
        ?? throw new InvalidDataException($"{path} is not a model manifest.");

    // SortedDictionary deserializes with its default comparer, which is
    // culture-sensitive, while the catalogue pass writes with StringComparer.Ordinal.
    // Writing it back as loaded would reorder keys that merely happen to be written
    // the other way round — churn in a file whose whole point is stable pins.
    var ordinal = new SortedDictionary<string, ModelEntry>(StringComparer.Ordinal);
    foreach (KeyValuePair<string, ModelEntry> pair in existing.Models)
    {
        ordinal[pair.Key] = pair.Value;
    }

    existing = new Manifest { DefaultQuant = existing.DefaultQuant, Models = ordinal };

    await Console.Error.WriteLineAsync($"Reading languages for {existing.Models.Count} entries...");

    int withLanguages = 0;
    foreach (KeyValuePair<string, ModelEntry> pair in existing.Models)
    {
        string[] languages = await FetchLanguagesAsync(http, pair.Value.Repo);
        pair.Value.Languages = languages.Length > 0 ? languages : null;
        withLanguages += languages.Length > 0 ? 1 : 0;
        await Console.Error.WriteLineAsync(
            $"  + {pair.Key}  ({languages.Length} languages){(languages.Length == 0 ? "  ! none declared" : string.Empty)}");
    }

    await WriteManifestAsync(path, existing);
    await Console.Error.WriteLineAsync(
        $"Wrote {existing.Models.Count} models to {path} ({withLanguages} declare at least one language).");
}

/// <summary>
/// The languages the model card declares.
/// </summary>
/// <remarks>
/// Read from <c>cardData.language</c>, not the top-level <c>language</c>: the HF
/// API leaves that null for every repository this project tracks, while the card
/// carries the codes. The card is documentation about the model, not a guarantee
/// about what it accepts — the loaded model answers that, and the manifest is only
/// what a reader can see before downloading.
/// </remarks>
static async Task<string[]> FetchLanguagesAsync(HttpClient http, string repo)
{
    using JsonDocument doc = JsonDocument.Parse(await http.GetStringAsync($"https://huggingface.co/api/models/{repo}"));
    return ExtractLanguages(doc.RootElement);
}

static string[] ExtractLanguages(JsonElement model)
{
    if (!model.TryGetProperty("cardData", out JsonElement card)
        || !card.TryGetProperty("language", out JsonElement lang)
        || lang.ValueKind != JsonValueKind.Array)
    {
        return Array.Empty<string>();
    }

    return lang.EnumerateArray()
        .Select(e => e.GetString())
        .Where(s => !string.IsNullOrEmpty(s))
        .Select(s => s!)
        .ToArray();
}

static async Task WriteManifestAsync(string path, Manifest manifest)
{
    string json = JsonSerializer.Serialize(manifest, ManifestJson.Write);
    await File.WriteAllTextAsync(path, json + Environment.NewLine);
}

static string ExtractLicense(JsonElement model)
{
    if (!model.TryGetProperty("cardData", out JsonElement card) || !card.TryGetProperty("license", out JsonElement lic))
    {
        return "unknown";
    }

    if (lic.ValueKind == JsonValueKind.String)
    {
        return lic.GetString() ?? "unknown";
    }

    if (lic.ValueKind == JsonValueKind.Array)
    {
        return string.Join(",", lic.EnumerateArray().Select(e => e.GetString()));
    }

    return "unknown";
}

static string? ArgAfter(string flag)
{
    for (int i = 0; i < Environment.GetCommandLineArgs().Length - 1; i++)
    {
        if (Environment.GetCommandLineArgs()[i] == flag)
        {
            return Environment.GetCommandLineArgs()[i + 1];
        }
    }

    return null;
}

static string FindRepoRoot()
{
    string? dir = AppContext.BaseDirectory;
    while (dir is not null)
    {
        if (File.Exists(Path.Combine(dir, "TranscribeCppSharp.slnx")))
        {
            return dir;
        }

        dir = Path.GetDirectoryName(dir);
    }

    throw new DirectoryNotFoundException("Could not find repo root (TranscribeCppSharp.slnx)");
}

// The manifest DTOs live in a named namespace rather than the global one
// (SonarCloud rule S3903). A block-scoped namespace is used because a
// file-scoped one would have to precede the top-level statements.
namespace TranscribeCppSharp.Tools.UpdateModelManifest
{
    /// <summary>Shared so neither pass allocates them per manifest write.</summary>
    internal static class ManifestJson
    {
        public static readonly JsonSerializerOptions Read = new() { PropertyNameCaseInsensitive = true };

        public static readonly JsonSerializerOptions Write = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
    }

    internal sealed class Manifest
    {
        public string DefaultQuant { get; set; } = string.Empty;

        public SortedDictionary<string, ModelEntry> Models { get; set; } = new(StringComparer.Ordinal);
    }

    internal sealed class ModelEntry
    {
        public string Repo { get; set; } = string.Empty;

        public string Revision { get; set; } = string.Empty;

        public string License { get; set; } = string.Empty;

        public string LicenseUrl { get; set; } = string.Empty;

        public string Quant { get; set; } = string.Empty;

        public string File { get; set; } = string.Empty;

        public string Sha256 { get; set; } = string.Empty;

        public long Size { get; set; }

        /// <summary>Languages the model card declares, or null when it declares none.</summary>
        public string[]? Languages { get; set; }
    }

    internal sealed record HfFile(string Path, string Sha256, long Size)
    {
        public string Name => Path[(Path.LastIndexOf('/') + 1)..];

        public string Quant
        {
            get
            {
                int dash = Name.LastIndexOf('-');
                return dash >= 0 && Name.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase)
                    ? Name[(dash + 1)..^".gguf".Length]
                    : "unknown";
            }
        }
    }
}
