// Regenerates the CLI model manifest (src/TranscribeCppSharp.Cli/models.json)
// from the official GGUF repositories published on HuggingFace. One alias per
// repository, using a default quantization; every entry pins the repository
// revision and the file's sha256 (from the HF LFS "oid") and records the
// upstream license reported by the model card.
//
// Usage: dotnet run --project tools/UpdateModelManifest [-- --author <org>] [--quant <Q>] [--out <path>]
// BCL only (HttpClient + System.Text.Json).

using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

string author = ArgAfter("--author") ?? "handy-computer";
string quant = ArgAfter("--quant") ?? "Q5_K_M";
string outPath = ArgAfter("--out") ?? Path.Combine(FindRepoRoot(), "src", "TranscribeCppSharp.Cli", "models.json");

using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
http.DefaultRequestHeaders.UserAgent.ParseAdd("TranscribeCppSharp.UpdateModelManifest/1.0");

Console.Error.WriteLine($"Listing GGUF repositories for author '{author}'...");
List<string> repos = await ListGgufReposAsync(http, author);
Console.Error.WriteLine($"Found {repos.Count} '-gguf' repositories.");

var models = new SortedDictionary<string, ModelEntry>(StringComparer.Ordinal);
foreach (string repo in repos)
{
    ModelEntry? entry = await BuildEntryAsync(http, repo, quant);
    if (entry is null)
    {
        Console.Error.WriteLine($"  ! {repo}: no usable .gguf file, skipped");
        continue;
    }

    string alias = repo[(repo.LastIndexOf('/') + 1)..];
    if (alias.EndsWith("-gguf", StringComparison.Ordinal))
    {
        alias = alias[..^"-gguf".Length];
    }

    alias = alias.ToLowerInvariant();
    models[alias] = entry;
    Console.Error.WriteLine($"  + {alias}  ({repo}, {entry.Quant})");
}

var manifest = new Manifest { DefaultQuant = quant, Models = models };
string json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions
{
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
});
await File.WriteAllTextAsync(outPath, json + Environment.NewLine);
Console.Error.WriteLine($"Wrote {models.Count} models to {outPath}");

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

    HfFile? chosen = Pick(files, modelStem, quant);
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
    };
}

static HfFile? Pick(List<HfFile> files, string modelStem, string preferredQuant)
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
