// Resolves the --model argument to a local file, downloading known models from
// HuggingFace on first use and caching them. Model weights are never bundled
// with the tool. Three forms are accepted:
//
//   1. a path to a local file (offline / custom model);
//   2. a known alias (see models.json, generated from the official GGUF repos)
//      optionally combined with --quant to pick another quantization;
//   3. a HuggingFace file spec "<owner>/<repo>/<file>[@<revision>]" for any GGUF
//      that transcribe.cpp supports, not just the curated aliases.
//
// Everything is downloaded once from a pinned revision and verified by sha256;
// the sha256 of an arbitrary HF file is read from the HuggingFace LFS metadata
// (never trusted from the download itself).

using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TranscribeCppSharp.Cli;

internal static class ModelStore
{
    private const string UserAgent = "TranscribeCppSharp.Cli (+https://github.com/manuc66/TranscribeCppSharp)";

    private sealed class Manifest
    {
        public string DefaultQuant { get; set; } = "Q5_K_M";

        public SortedDictionary<string, ModelInfo> Models { get; set; } = new(StringComparer.Ordinal);
    }

    private sealed class ModelInfo
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

    private sealed record HfFile(string Path, string Sha256, long Size);

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static Manifest? _manifest;

    private static Manifest LoadedManifest => _manifest ??= JsonSerializer.Deserialize<Manifest>(ReadManifest(), JsonOptions)
        ?? throw new InvalidOperationException("Embedded model manifest 'models.json' is invalid.");

    /// <summary>
    /// How the HuggingFace client is built. The download, the sha256
    /// verification and the cache are the part of the CLI a user hits on a
    /// first run, so they need tests; a seam here is what makes them reachable
    /// without the network. Production never sets it.
    /// </summary>
    internal static Func<HttpClient> HttpClientFactory { get; set; } = NewHttpClient;

    /// <summary>
    /// Where models are cached. Set only by tests, to keep their downloads out
    /// of the user's real cache; null means the platform default.
    /// </summary>
    internal static string? CacheRootOverride { get; set; }

    /// <summary>
    /// Resolves the model argument: an existing path is used as-is; a known alias
    /// or a HuggingFace repo/file spec is fetched (once) and cached.
    /// </summary>
    /// <param name="argument">A file path, a curated alias or an HF spec.</param>
    /// <param name="quant">Optional quantization override for a curated alias.</param>
    /// <param name="error">Where the download progress is reported.</param>
    public static string Resolve(string argument, string? quant, TextWriter error)
    {
        if (File.Exists(argument))
        {
            return Path.GetFullPath(argument);
        }

        // Alias (curated manifest), possibly with an overridden quantization.
        if (LoadedManifest.Models.TryGetValue(argument, out ModelInfo? info))
        {
            if (quant is null || string.Equals(quant, info.Quant, StringComparison.OrdinalIgnoreCase))
            {
                return EnsureLocal(info.Repo, info.Revision, info.File, info.Sha256, info.Size, info.License, info.LicenseUrl, error);
            }

            using var http = HttpClientFactory();
            HfFile file = ResolveHfFile(http, info.Repo, info.Revision, quant, isSuffix: true)
                ?? throw new FileNotFoundException($"No '{quant}' quantization found for '{argument}' in {info.Repo}.");
            return EnsureLocal(info.Repo, info.Revision, file.Path, file.Sha256, file.Size, info.License, info.LicenseUrl, error);
        }

        // HuggingFace spec: "<owner>/<repo>/<file>[@<revision>]".
        string[] parts = argument.Split('/');
        if (parts.Length >= 3)
        {
            string repo = $"{parts[0]}/{parts[1]}";
            string file = string.Join('/', parts[2..]);
            string? revision = null;
            int at = file.LastIndexOf('@');
            if (at >= 0)
            {
                revision = file[(at + 1)..];
                file = file[..at];
            }

            using var http = HttpClientFactory();
            revision ??= ResolveRevision(http, repo);
            HfFile resolved = ResolveHfFile(http, repo, revision, file, isSuffix: false)
                ?? throw new FileNotFoundException($"File '{file}' not found in {repo}@{revision}.");
            return EnsureLocal(repo, revision, resolved.Path, resolved.Sha256, resolved.Size, license: "see the model card", licenseUrl: $"https://huggingface.co/{repo}", error);
        }

        throw new FileNotFoundException(
            $"Model '{argument}' is neither an existing file, a known alias, nor a '<owner>/<repo>/<file>' spec." +
            $"{Environment.NewLine}Known aliases: run 'transcribe --list-models'." +
            $"{Environment.NewLine}Or pass a model file path, or a HuggingFace spec like 'handy-computer/whisper-tiny-gguf/whisper-tiny-Q5_K_M.gguf'.");
    }

    public static void List(TextWriter writer)
    {
        Manifest manifest = LoadedManifest;
        writer.WriteLine($"{"alias",-40} {"quant",-8} {"license",-16} {"size",8}");
        foreach ((string alias, ModelInfo info) in manifest.Models)
        {
            string license = IsNonCommercial(info.License) ? $"{info.License} !" : info.License;
            writer.WriteLine($"{alias,-40} {info.Quant,-8} {license,-16} {info.Size / (1024 * 1024),6} MB");
        }

        writer.WriteLine();
        writer.WriteLine("! = non-commercial license; verify before any commercial use.");
        writer.WriteLine($"Default quantization: {manifest.DefaultQuant}. Override with --quant <Q4_K_M|Q5_K_M|Q8_0|...>.");
        writer.WriteLine("Details for one alias: --model-info <alias>.");
        writer.WriteLine("Any other GGUF supported by transcribe.cpp: --model <owner>/<repo>/<file.gguf>[@<revision>].");
    }

    public static bool Info(string alias, TextWriter writer, TextWriter error)
    {
        if (!LoadedManifest.Models.TryGetValue(alias, out ModelInfo? info))
        {
            error.WriteLine($"Unknown alias '{alias}'. Run 'transcribe --list-models'.");
            return false;
        }

        writer.WriteLine(alias);
        writer.WriteLine($"  repo       : {info.Repo}");
        writer.WriteLine($"  revision   : {info.Revision}");
        writer.WriteLine($"  quant      : {info.Quant}");
        writer.WriteLine($"  file       : {info.File}");
        writer.WriteLine($"  size       : {info.Size / (1024 * 1024)} MB");
        writer.WriteLine($"  license    : {info.License}{(IsNonCommercial(info.License) ? "  (non-commercial!)" : string.Empty)}");
        writer.WriteLine($"  license url: {info.LicenseUrl}");
        return true;
    }

    private static bool IsNonCommercial(string license)
        => license.Contains("-nc", StringComparison.OrdinalIgnoreCase)
        || license.Contains("noncommercial", StringComparison.OrdinalIgnoreCase)
        || license.Contains("non-commercial", StringComparison.OrdinalIgnoreCase);

    private static string EnsureLocal(string repo, string revision, string file, string sha256, long size, string license, string licenseUrl, TextWriter error)
    {
        string dir = Path.Combine(CacheRoot(), repo.Replace('/', '_'), revision);
        string dest = Path.Combine(dir, file.Replace('/', '_'));
        if (File.Exists(dest))
        {
            return dest;
        }

        Directory.CreateDirectory(dir);
        string url = $"https://huggingface.co/{repo}/resolve/{revision}/{file}";
        error.WriteLine($"Downloading '{file}' ({(size > 0 ? $"{size / (1024 * 1024)} MB, " : string.Empty)}{license}) from HuggingFace...");
        error.WriteLine($"  {url}");
        if (licenseUrl.Length > 0)
        {
            error.WriteLine($"  license: {licenseUrl}");
        }

        string tmp = dest + ".part-" + Guid.NewGuid().ToString("N");
        try
        {
            DownloadAsync(url, tmp).GetAwaiter().GetResult();

            string actual = Sha256(tmp);
            if (!string.Equals(actual, sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Checksum mismatch for {file}: expected sha256:{sha256}, got sha256:{actual}. Aborting.");
            }

            File.Move(tmp, dest, overwrite: true);
            error.WriteLine($"Model cached at {dest}");
            return dest;
        }
        finally
        {
            if (File.Exists(tmp))
            {
                File.Delete(tmp);
            }
        }
    }

    private static HfFile? ResolveHfFile(HttpClient http, string repo, string revision, string fileOrQuant, bool isSuffix)
    {
        string treeUrl = $"https://huggingface.co/api/models/{repo}/tree/{revision}?recursive=true&expand=true";
        using JsonDocument doc = JsonDocument.Parse(http.GetStringAsync(treeUrl).GetAwaiter().GetResult());

        foreach (JsonElement entry in doc.RootElement.EnumerateArray())
        {
            string? path = entry.GetProperty("path").GetString();
            if (path is null || !path.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string name = path[(path.LastIndexOf('/') + 1)..];
            bool match = isSuffix
                ? name.EndsWith($"-{fileOrQuant}.gguf", StringComparison.OrdinalIgnoreCase)

                // A spec may name a file in a subdirectory ("owner/repo/q4/f.gguf"),
                // so the full path counts as well as the bare file name.
                : string.Equals(name, fileOrQuant, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(path, fileOrQuant, StringComparison.OrdinalIgnoreCase);
            if (!match)
            {
                continue;
            }

            if (!entry.TryGetProperty("lfs", out JsonElement lfs) || !lfs.TryGetProperty("oid", out JsonElement oid))
            {
                throw new InvalidDataException(
                    $"'{name}' on {repo} is not stored as an LFS object, so its sha256 cannot be verified. " +
                    "Pass a pinned revision/file whose hash is known, or use a curated alias.");
            }

            long size = lfs.TryGetProperty("size", out JsonElement s) ? s.GetInt64() : 0;
            return new HfFile(path, oid.GetString()!, size);
        }

        return null;
    }

    private static string ResolveRevision(HttpClient http, string repo)
    {
        string url = $"https://huggingface.co/api/models/{repo}";
        using JsonDocument doc = JsonDocument.Parse(http.GetStringAsync(url).GetAwaiter().GetResult());
        return doc.RootElement.GetProperty("sha").GetString()
            ?? throw new InvalidDataException($"Could not resolve the current revision of {repo}.");
    }

    private static async Task DownloadAsync(string url, string destination)
    {
        using var http = HttpClientFactory();
        using HttpResponseMessage response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        await using Stream source = await response.Content.ReadAsStreamAsync();
        await using FileStream target = File.Create(destination);
        await source.CopyToAsync(target);
    }

    private static HttpClient NewHttpClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        return http;
    }

    private static string Sha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string CacheRoot()
    {
        if (CacheRootOverride is { } root)
        {
            return root;
        }

        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TranscribeCppSharp", "models");
        }

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string? xdg = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        string cache = !string.IsNullOrEmpty(xdg)
            ? xdg
            : OperatingSystem.IsMacOS()
                ? Path.Combine(home, "Library", "Caches")
                : Path.Combine(home, ".cache");

        return Path.Combine(cache, "TranscribeCppSharp", "models");
    }

    private static string ReadManifest()
    {
        using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("models.json");
        if (stream is null)
        {
            throw new InvalidOperationException("Embedded model manifest 'models.json' is missing from the assembly.");
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
