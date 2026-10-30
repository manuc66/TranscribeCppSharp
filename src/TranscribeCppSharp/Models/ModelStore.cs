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

namespace TranscribeCppSharp.Models;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

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
    {
        // Read the root once for the whole call. CacheRootOverride is a test
        // seam, and reading it twice let a test that moved it between the two
        // reads build the directory under one root and check it against the
        // other, which Contain then correctly rejected.
        string root = CacheRoot();
        string dir = Contain(root, CacheDirectory(root, descriptor.Repo, descriptor.Revision));
        return ContainResolved(root, Path.Combine(dir, FlatFileName(descriptor.File)));
    }

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
    /// <param name="cancellationToken">Stops a download in flight.</param>
    /// <returns>An absolute path to a local model file.</returns>
    /// <exception cref="OperationCanceledException">The token was cancelled; the partial download is deleted, so nothing corrupt is cached.</exception>
    public static string Resolve(
        string argument,
        string? quant,
        TextWriter error,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
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
                return EnsureLocal(info.Repo, info.Revision, info.File, info.Sha256, info.Size, info.License, info.LicenseUrl, error, progress, cancellationToken);
            }

            using var http = HttpClientFactory();
            HfFile file = ResolveHfFile(http, info.Repo, info.Revision, quant, isSuffix: true, cancellationToken)
                ?? throw new FileNotFoundException($"No '{quant}' quantization found for '{argument}' in {info.Repo}.");
            return EnsureLocal(info.Repo, info.Revision, file.Path, file.Sha256, file.Size, info.License, info.LicenseUrl, error, progress, cancellationToken);
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
                // The revision is free text in a spec and ends up as a directory
                // name: "..", an absolute path or an embedded separator would
                // move the download (and the overwrite) outside the cache.
                revision = PathSegment(file[(at + 1)..], "revision");
                file = file[..at];
            }

            using var http = HttpClientFactory();
            revision ??= ResolveRevision(http, repo, cancellationToken);
            HfFile resolved = ResolveHfFile(http, repo, revision, file, isSuffix: false, cancellationToken)
                ?? throw new FileNotFoundException($"File '{file}' not found in {repo}@{revision}.");
            return EnsureLocal(repo, revision, resolved.Path, resolved.Sha256, resolved.Size, license: "see the model card", licenseUrl: $"https://huggingface.co/{repo}", error, progress, cancellationToken);
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
        writer.WriteLine($"{"alias",-40} {"quant",-8} {"license",-16} {"size",8} unit");
        foreach (ModelDescriptor model in Catalog)
        {
            string license = model.IsNonCommercialLicense ? $"{model.License} !" : model.License;

            // The number is padded on its own and the unit follows, so the column
            // stays aligned while the unit can change per row. Printing "GB" in a
            // fixed column and padding the whole cell would not.
            ModelSize size = ModelSizeFormat.Format(model.Size);
            writer.WriteLine($"{model.Alias,-40} {model.Quant,-8} {license,-16} {size.Number(8)} {size.Unit}");
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
        if (model.Family is not null)
        {
            writer.WriteLine($"  family     : {model.Family}");
        }

        if (model.Params > 0)
        {
            writer.WriteLine($"  params     : {model.Params:N0}");
        }

        if (model.UpstreamRepo is not null)
        {
            writer.WriteLine($"  upstream   : {model.UpstreamRepo} @ {model.UpstreamCommit}");
        }

        writer.WriteLine($"  repo       : {model.Repo}");
        writer.WriteLine($"  revision   : {model.Revision}");
        writer.WriteLine($"  quant      : {model.Quant}");
        writer.WriteLine($"  file       : {model.File}");
        writer.WriteLine($"  size       : {ModelSizeFormat.Format(model.Size)}");
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
            entry.Size,
            entry.Languages,
            entry.Family,
            entry.Params,
            entry.UpstreamRepo,
            entry.UpstreamCommit);

    private static string CacheDirectory(string root, string repo, string revision)
        => Path.Combine(root, repo.Replace('/', '_'), revision);

    /// <summary>
    /// The file name used inside the per-revision cache directory.
    /// </summary>
    /// <remarks>
    /// Both separators are replaced. A HuggingFace path uses '/', but a
    /// repository can hold a file whose name contains '\', which Windows reads
    /// as a separator: flattening it is what keeps the write inside the cache.
    /// </remarks>
    private static string FlatFileName(string file) => file.Replace('/', '_').Replace('\\', '_');

    /// <summary>
    /// A single path component that cannot leave the cache.
    /// </summary>
    /// <remarks>
    /// <see cref="Path.Combine(string, string, string)"/> discards the earlier
    /// segments when a later one is absolute, and ".." walks up, so a value that
    /// comes from the user is refused unless it is one plain segment.
    /// </remarks>
    internal static string PathSegment(string value, string what)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value is "." or ".."
            || Path.IsPathRooted(value)
            || value.IndexOfAny(['/', '\\', ':', '\0']) >= 0)
        {
            throw new ArgumentException(
                $"'{what}' is not a single path segment: '{value}'. "
                + "Use a commit sha or a tag name, without a slash or a leading path.",
                what);
        }

        return value;
    }

    /// <summary>
    /// Returns <paramref name="candidate"/> as a full path, or refuses it when it
    /// is not inside <paramref name="root"/>.
    /// </summary>
    /// <remarks>
    /// Kept separate from <see cref="PathSegment"/> on purpose: this is the
    /// backstop, whatever produced the value (a spec, a manifest entry, or a file
    /// name read from the HuggingFace API).
    /// </remarks>
    internal static string Contain(string root, string candidate)
    {
        string full = Path.GetFullPath(candidate);
        string prefix = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"refusing to use '{full}': it is outside the model cache '{root}'.");
        }

        return full;
    }

    /// <summary>
    /// <see cref="Contain"/> plus a check on where the path really leads.
    /// </summary>
    /// <remarks>
    /// <see cref="Contain"/> compares strings, and a symbolic link is not a string
    /// difference: a revision directory that points outside the cache passes it
    /// while the write lands elsewhere (verified by linking one at a cache the
    /// app then wrote through). Both sides are resolved component by component, so
    /// a symlinked cache root — a legitimate way to keep weights on another disk —
    /// still matches, and a link that leaves it does not.
    /// </remarks>
    internal static string ContainResolved(string root, string candidate)
    {
        string full = Contain(root, candidate);
        string realRoot = RealPath(root);
        string real = RealPath(full);

        if (!string.Equals(real, realRoot, StringComparison.Ordinal)
            && !real.StartsWith(
                realRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"refusing to use '{full}': it resolves outside the model cache ({real}).");
        }

        return full;
    }

    /// <summary>
    /// The path with every symbolic link resolved, component by component.
    /// </summary>
    /// <remarks>
    /// <see cref="Path.GetFullPath(string)"/> normalises ".." but does not follow
    /// links, and <see cref="FileSystemInfo.ResolveLinkTarget(bool)"/> only
    /// resolves one when the path named <em>is</em> the link: a symlinked parent
    /// directory would otherwise pass a containment check. Each component is
    /// resolved before the next one is appended.
    /// </remarks>
    private static string RealPath(string path)
    {
        string full = Path.GetFullPath(path);
        string root = Path.GetPathRoot(full) ?? string.Empty;
        if (root.Length == 0)
        {
            return full;
        }

        string current = root;
        string[] parts = full[root.Length..].Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

        foreach (string part in parts)
        {
            current = Path.Combine(current, part);
            try
            {
                FileSystemInfo info = Directory.Exists(current)
                    ? new DirectoryInfo(current)
                    : new FileInfo(current);
                if (!info.Exists)
                {
                    // Missing, or a broken link: nothing left to follow.
                    continue;
                }

                // A chain of links is followed to the end, not one hop.
                current = info.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? current;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Unreadable component: keep the lexical path; the write itself
                // reports the problem.
            }
        }

        return current;
    }

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
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        string root = CacheRoot();
        string dir = Contain(root, CacheDirectory(root, repo, revision));
        string dest = Contain(dir, Path.Combine(dir, FlatFileName(file)));
        if (File.Exists(dest))
        {
            // The download verifies the sha256, but a cache written by an older
            // build or interrupted by a disk error can be truncated. The size is
            // in the manifest/LFS metadata and costs one stat, so compare it and
            // fetch again when it disagrees. A same-size corruption is not caught
            // here: hashing multi-GB weights on every resolve would cost more than
            // the failure it prevents, and the load reports it.
            long cached = new FileInfo(dest).Length;
            if (size <= 0 || cached == size)
            {
                return dest;
            }

            error.WriteLine(
                $"Cached '{file}' is {cached} bytes, expected {size}; downloading it again.");
        }

        Directory.CreateDirectory(dir);

        // Now that the directory exists, check where it really leads. Both sides
        // are measured against the cache root, not against each other: a revision
        // directory linked elsewhere is inside itself and outside the cache.
        ContainResolved(root, dir);
        dest = ContainResolved(root, dest);

        string url = $"https://huggingface.co/{repo}/resolve/{revision}/{file}";
        error.WriteLine($"Downloading '{file}' ({(size > 0 ? $"{ModelSizeFormat.Format(size)}, " : string.Empty)}{license}) from HuggingFace...");
        error.WriteLine($"  {url}");
        if (licenseUrl.Length > 0)
        {
            error.WriteLine($"  license: {licenseUrl}");
        }

        string tmp = dest + ".part-" + Guid.NewGuid().ToString("N");
        try
        {
            DownloadAsync(url, tmp, file, size, progress, cancellationToken).GetAwaiter().GetResult();

            cancellationToken.ThrowIfCancellationRequested();

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

    private static HfFile? ResolveHfFile(
        HttpClient http,
        string repo,
        string revision,
        string fileOrQuant,
        bool isSuffix,
        CancellationToken cancellationToken)
    {
        string treeUrl = $"https://huggingface.co/api/models/{repo}/tree/{revision}?recursive=true&expand=true";
        using JsonDocument doc = JsonDocument.Parse(
            http.GetStringAsync(treeUrl, cancellationToken).GetAwaiter().GetResult());

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

    private static string ResolveRevision(HttpClient http, string repo, CancellationToken cancellationToken)
    {
        string url = $"https://huggingface.co/api/models/{repo}";
        using JsonDocument doc = JsonDocument.Parse(
            http.GetStringAsync(url, cancellationToken).GetAwaiter().GetResult());
        return doc.RootElement.GetProperty("sha").GetString()
            ?? throw new InvalidDataException($"Could not resolve the current revision of {repo}.");
    }

    /// <summary>
    /// Downloads <paramref name="url"/> to <paramref name="destination"/>.
    /// </summary>
    /// <param name="url">Where to read from.</param>
    /// <param name="destination">The file to write.</param>
    /// <param name="file">The name to report in errors.</param>
    /// <param name="expectedSize">
    /// The size the metadata announced, or 0 when it is unknown. The sha256 is only
    /// checked once the whole file is on disk, so this is what stops a server that
    /// keeps sending from filling the disk first.
    /// </param>
    /// <param name="progress">Reports 0..1.</param>
    /// <param name="cancellationToken">Stops the transfer.</param>
    private static async Task DownloadAsync(
        string url,
        string destination,
        string file,
        long expectedSize,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        using var http = HttpClientFactory();
        using HttpResponseMessage response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        long total = response.Content.Headers.ContentLength ?? -1L;
        await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using FileStream target = File.Create(destination);

        // Copy in chunks so progress can be reported; CopyToAsync has no way to
        // surface how far it has got, and a multi-GB model gives the UI nothing
        // to show for minutes otherwise. The token reaches the read and the write,
        // so a cancelled download stops at the next chunk instead of at the end.
        byte[] buffer = new byte[81920];
        long received = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            received += read;

            if (expectedSize > 0 && received > expectedSize)
            {
                throw new InvalidDataException(
                    $"'{file}' was announced as {expectedSize} bytes but the server sent more; "
                    + "aborting instead of filling the disk.");
            }

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

        /// <summary>Languages the model card declares, or null when it declares none.</summary>
        public string[]? Languages { get; set; }

        /// <summary>Model family as upstream records it, or null.</summary>
        public string? Family { get; set; }

        /// <summary>Parameter count as upstream records it, or 0.</summary>
        public long Params { get; set; }

        /// <summary>Checkpoint the GGUF was converted from.</summary>
        public string? UpstreamRepo { get; set; }

        /// <summary>Exact commit of that checkpoint.</summary>
        public string? UpstreamCommit { get; set; }
    }

    private sealed record HfFile(string Path, string Sha256, long Size);
}
