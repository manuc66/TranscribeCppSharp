// Resolves a model argument to a local file, downloading known models from
// HuggingFace on first use and caching them. Model weights are never bundled
// with the wrapper. Three forms are accepted:
//
//   1. a path to a local file (offline / custom model);
//   2. a known alias (see models.json, generated from the official GGUF repos)
//      optionally combined with a quantization to pick another one;
//   3. a HuggingFace file spec "<owner>/<repo>/<file>[@<revision>]" for any GGUF
//      that transcribe.cpp supports, not just the curated aliases.
//
// Everything is downloaded once from a pinned revision and verified by sha256;
// the sha256 of an arbitrary HF file is read from the HuggingFace LFS metadata
// (never trusted from the download itself).
//
// Lives in the wrapper rather than in one front end because the CLI and the
// GUI resolve models the same way and must agree on the cache layout: a model
// downloaded by one is a cache hit for the other.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace TranscribeCppSharp.Models;

/// <summary>
/// Model resolution and caching, shared by the CLI and the GUI.
/// </summary>
public static class ModelStore
{
    private const string UserAgent = "TranscribeCppSharp (+https://github.com/manuc66/TranscribeCppSharp)";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static Manifest? cachedManifest;

    /// <summary>
    /// How the HuggingFace client is built. The download, the sha256
    /// verification and the cache are what a user hits on a first run, so they
    /// need tests; a seam here is what makes them reachable without the network.
    /// Production never sets it.
    /// </summary>
    public static Func<HttpClient> HttpClientFactory { get; set; } = NewHttpClient;

    /// <summary>
    /// Where models are cached. Set only by tests, to keep their downloads out
    /// of the user's real cache; null means the platform default.
    /// </summary>
    public static string? CacheRootOverride { get; set; }

    private static Manifest LoadedManifest => cachedManifest ??= JsonSerializer.Deserialize<Manifest>(ReadManifest(), JsonOptions)
        ?? throw new InvalidOperationException("Embedded model manifest 'models.json' is invalid.");

    /// <summary>Quantization the manifest defaults to when an alias does not say.</summary>
    public static string DefaultQuant => LoadedManifest.DefaultQuant;

    /// <summary>
    /// Every curated alias, ordered by name. This is the catalogue a UI lists;
    /// it reads no files and touches no network.
    /// </summary>
    public static IReadOnlyList<ModelDescriptor> Catalog
    {
        get
        {
            List<ModelDescriptor> all = new();
            foreach (string alias in LoadedManifest.Models.Keys)
            {
                all.Add(Describe(alias, LoadedManifest.Models[alias]));
            }

            // Sorted here rather than trusted from the manifest's dictionary:
            // the JSON deserializer replaces SortedDictionary with an instance
            // using Comparer<string>.Default, which is culture-sensitive. That
            // made the order depend on the machine's locale ("-" and "_" sort
            // differently under one collation than another), so two users could
            // see the list in a different order for no visible reason.
            all.Sort(static (left, right) => string.CompareOrdinal(left.Alias, right.Alias));
            return all;
        }
    }

    /// <summary>Looks up one alias, or null when the manifest does not have it.</summary>
    /// <param name="alias">The alias to find.</param>
    /// <returns>The descriptor, or null.</returns>
    public static ModelDescriptor? Find(string alias)
        => LoadedManifest.Models.TryGetValue(alias, out ManifestEntry? entry)
            ? Describe(alias, entry)
            : null;

    /// <summary>Where models are cached on this machine.</summary>
    /// <returns>An absolute directory path. Created on demand, not here.</returns>
    public static string CacheRoot()
    {
        if (CacheRootOverride is { } root)
        {
            return root;
        }

        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TranscribeCppSharp",
                "models");
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

    /// <summary>
    /// Local path an alias would use, whether or not it has been downloaded.
    /// </summary>
    /// <param name="descriptor">The alias to locate.</param>
    /// <returns>An absolute path under <see cref="CacheRoot"/>.</returns>
    public static string CachedPath(ModelDescriptor descriptor)
        => Path.Combine(CacheDirectory(descriptor.Repo, descriptor.Revision), descriptor.File.Replace('/', '_'));

    /// <summary>
    /// Whether the alias is already downloaded.
    /// </summary>
    /// <param name="descriptor">The alias to check.</param>
    /// <returns>true when the file is present.</returns>
    public static bool IsCached(ModelDescriptor descriptor) => File.Exists(CachedPath(descriptor));

    /// <summary>
    /// Size on disk of a cached alias, or 0 when it is not downloaded.
    /// </summary>
    /// <param name="descriptor">The alias to measure.</param>
    /// <returns>Size in bytes.</returns>
    public static long CachedSize(ModelDescriptor descriptor)
    {
        string path = CachedPath(descriptor);
        return File.Exists(path) ? new FileInfo(path).Length : 0;
    }

    /// <summary>
    /// Deletes a cached alias from disk.
    /// </summary>
    /// <param name="descriptor">The alias to remove.</param>
    /// <returns>
    /// true when a file was deleted; false when it was not cached, or when the
    /// delete failed (an unreadable cache is not worth failing a UI over).
    /// </returns>
    /// <remarks>
    /// Only the file itself is removed. The per-revision directory is left
    /// alone unless it ends up empty, because several aliases can share one
    /// repository and revision.
    /// </remarks>
    public static bool Delete(ModelDescriptor descriptor)
    {
        string path = CachedPath(descriptor);
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        TryPruneEmptyDirectory(Path.GetDirectoryName(path));
        return true;
    }

    /// <summary>
    /// Resolves the model argument: an existing path is used as-is; a known alias
    /// or a HuggingFace repo/file spec is fetched (once) and cached.
    /// </summary>
    /// <param name="argument">A file path, a curated alias or an HF spec.</param>
    /// <param name="quant">Optional quantization override for a curated alias.</param>
    /// <param name="error">Where progress and diagnostics are reported.</param>
    /// <param name="progress">Optional download progress, 0 to 1.</param>
    /// <returns>An absolute path to a local model file.</returns>
    public static string Resolve(string argument, string? quant, TextWriter error, IProgress<double>? progress = null)
    {
        if (File.Exists(argument))
        {
            return Path.GetFullPath(argument);
        }

        // Alias (curated manifest), possibly with an overridden quantization.
        if (LoadedManifest.Models.TryGetValue(argument, out ManifestEntry? info))
        {
            if (quant is null || string.Equals(quant, info.Quant, StringComparison.OrdinalIgnoreCase))
            {
                return EnsureLocal(info.Repo, info.Revision, info.File, info.Sha256, info.Size, info.License, info.LicenseUrl, error, progress);
            }

            using var http = HttpClientFactory();
            HfFile file = ResolveHfFile(http, info.Repo, info.Revision, quant, isSuffix: true)
                ?? throw new FileNotFoundException($"No '{quant}' quantization found for '{argument}' in {info.Repo}.");
            return EnsureLocal(info.Repo, info.Revision, file.Path, file.Sha256, file.Size, info.License, info.LicenseUrl, error, progress);
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
            return EnsureLocal(repo, revision, resolved.Path, resolved.Sha256, resolved.Size, license: "see the model card", licenseUrl: $"https://huggingface.co/{repo}", error, progress);
        }

        throw new FileNotFoundException(
            $"Model '{argument}' is neither an existing file, a known alias, nor a '<owner>/<repo>/<file>' spec." +
            $"{Environment.NewLine}Known aliases: run 'transcribe --list-models'." +
            $"{Environment.NewLine}Or pass a model file path, or a HuggingFace spec like 'handy-computer/whisper-tiny-gguf/whisper-tiny-Q5_K_M.gguf'.");
    }

    /// <summary>
    /// Writes the curated aliases, one per line, with quantization, license and size.
    /// </summary>
    /// <param name="writer">Where the table is written.</param>
    public static void List(TextWriter writer)
    {
        writer.WriteLine($"{"alias",-40} {"quant",-8} {"license",-16} {"size",8}");
        foreach (ModelDescriptor model in Catalog)
        {
            string license = model.IsNonCommercialLicense ? $"{model.License} !" : model.License;
            writer.WriteLine($"{model.Alias,-40} {model.Quant,-8} {license,-16} {model.Size / (1024 * 1024),6} MB");
        }

        writer.WriteLine();
        writer.WriteLine("! = non-commercial license; verify before any commercial use.");
        writer.WriteLine($"Default quantization: {DefaultQuant}. Override with --quant <Q4_K_M|Q5_K_M|Q8_0|>.");
        writer.WriteLine("Details for one alias: --model-info <alias>.");
        writer.WriteLine("Any other GGUF supported by transcribe.cpp: --model <owner>/<repo>/<file.gguf>[@<revision>].");
    }

    /// <summary>
    /// Writes the detail of one alias: repo, revision, quantization, size, license.
    /// </summary>
    /// <param name="alias">The alias to describe.</param>
    /// <param name="writer">Where the detail is written.</param>
    /// <param name="error">Where an unknown alias is reported.</param>
    /// <returns>true when the alias is known.</returns>
    public static bool Info(string alias, TextWriter writer, TextWriter error)
    {
        if (Find(alias) is not { } model)
        {
            error.WriteLine($"Unknown alias '{alias}'. Run 'transcribe --list-models'.");
            return false;
        }

        writer.WriteLine(alias);
        writer.WriteLine($"  repo       : {model.Repo}");
        writer.WriteLine($"  revision   : {model.Revision}");
        writer.WriteLine($"  quant      : {model.Quant}");
        writer.WriteLine($"  file       : {model.File}");
        writer.WriteLine($"  size       : {model.Size / (1024 * 1024)} MB");
        writer.WriteLine($"  license    : {model.License}{(model.IsNonCommercialLicense ? "  (non-commercial!)" : string.Empty)}");
        writer.WriteLine($"  license url: {model.LicenseUrl}");
        return true;
    }

    private static ModelDescriptor Describe(string alias, ManifestEntry entry)
        => new(
            alias,
            entry.Repo,
            entry.Revision,
            entry.License,
            entry.LicenseUrl,
            entry.Quant,
            entry.File,
            entry.Sha256,
            entry.Size);

    private static string CacheDirectory(string repo, string revision)
        => Path.Combine(CacheRoot(), repo.Replace('/', '_'), revision);

    private static void TryPruneEmptyDirectory(string? directory)
    {
        if (string.IsNullOrEmpty(directory))
        {
            return;
        }

        try
        {
            if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Directory.Delete(directory);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An empty leftover directory is harmless.
        }
    }

    private static string EnsureLocal(
        string repo,
        string revision,
        string file,
        string sha256,
        long size,
        string license,
        string licenseUrl,
        TextWriter error,
        IProgress<double>? progress)
    {
        string dir = CacheDirectory(repo, revision);
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
            DownloadAsync(url, tmp, progress).GetAwaiter().GetResult();

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

    private static async Task DownloadAsync(string url, string destination, IProgress<double>? progress)
    {
        using var http = HttpClientFactory();
        using HttpResponseMessage response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        long total = response.Content.Headers.ContentLength ?? -1L;
        await using Stream source = await response.Content.ReadAsStreamAsync();
        await using FileStream target = File.Create(destination);

        // Copy in chunks so progress can be reported; CopyToAsync has no way to
        // surface how far it has got, and a multi-GB model gives the UI nothing
        // to show for minutes otherwise.
        byte[] buffer = new byte[81920];
        long received = 0;
        int read;
        while ((read = await source.ReadAsync(buffer)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read));
            received += read;
            if (total > 0)
            {
                progress?.Report((double)received / total);
            }
        }

        progress?.Report(1.0);
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

    private sealed class Manifest
    {
        public string DefaultQuant { get; set; } = "Q5_K_M";

        public SortedDictionary<string, ManifestEntry> Models { get; set; } = new(StringComparer.Ordinal);
    }

    private sealed class ManifestEntry
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
}
