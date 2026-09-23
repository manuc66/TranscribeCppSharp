// Resolves the --model argument to a local file, downloading known models from
// HuggingFace on first use and caching them. Model weights are never bundled
// with the tool: the user either passes a path, or a known name (see models.json)
// which is fetched once at a pinned revision and verified by sha256.

using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace TranscribeCppSharp.Cli;

internal static class ModelStore
{
    private const string UserAgent = "TranscribeCppSharp.Cli (+https://github.com/manuc66/TranscribeCppSharp)";

    private sealed class ModelInfo
    {
        public string Repo { get; set; } = string.Empty;
        public string Revision { get; set; } = string.Empty;
        public string File { get; set; } = string.Empty;
        public string Sha256 { get; set; } = string.Empty;
        public long Size { get; set; }
        public string License { get; set; } = string.Empty;
        public string LicenseUrl { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }

    /// <summary>
    /// Resolves the model argument: an existing path is used as-is (offline or
    /// custom model); otherwise it must be a known model name, which is fetched
    /// and cached if needed.
    /// </summary>
    public static string Resolve(string argument)
    {
        if (File.Exists(argument))
        {
            return Path.GetFullPath(argument);
        }

        if (TryLoad(argument, out ModelInfo? info))
        {
            return EnsureLocal(info!);
        }

        throw new FileNotFoundException(
            $"Model '{argument}' is neither an existing file nor a known model name." +
            $"{Environment.NewLine}Known names: {string.Join(", ", KnownNames())}." +
            $"{Environment.NewLine}Or pass the path to a model file directly.");
    }

    public static IReadOnlyList<string> KnownNames()
    {
        using JsonDocument doc = JsonDocument.Parse(ReadManifest());
        return doc.RootElement.GetProperty("models").EnumerateObject().Select(p => p.Name).ToList();
    }

    private static bool TryLoad(string name, out ModelInfo? info)
    {
        info = null;
        using JsonDocument doc = JsonDocument.Parse(ReadManifest());
        if (!doc.RootElement.GetProperty("models").TryGetProperty(name, out JsonElement element))
        {
            return false;
        }

        info = element.Deserialize<ModelInfo>(JsonOptions)!;
        return true;
    }

    private static string EnsureLocal(ModelInfo info)
    {
        string dir = Path.Combine(CacheRoot(), info.Repo.Replace('/', '_'), info.Revision);
        string dest = Path.Combine(dir, info.File);
        if (File.Exists(dest))
        {
            return dest;
        }

        Directory.CreateDirectory(dir);
        string url = $"https://huggingface.co/{info.Repo}/resolve/{info.Revision}/{info.File}";
        Console.Error.WriteLine(
            $"Downloading model '{info.File}' ({info.Size / (1024 * 1024)} MB, {info.License}) from HuggingFace...");
        Console.Error.WriteLine($"  {url}");

        string tmp = dest + ".part-" + Guid.NewGuid().ToString("N");
        try
        {
            DownloadAsync(url, tmp).GetAwaiter().GetResult();

            string actual = Sha256(tmp);
            if (!string.Equals(actual, info.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Checksum mismatch for {info.File}: expected sha256:{info.Sha256}, got sha256:{actual}. " +
                    "The downloaded file does not match the pinned model. Aborting.");
            }

            // Atomic publish: only a fully downloaded, verified file becomes dest.
            File.Move(tmp, dest, overwrite: true);
            Console.Error.WriteLine($"Model cached at {dest}");
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

    private static async Task DownloadAsync(string url, string destination)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);

        using HttpResponseMessage response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        await using Stream source = await response.Content.ReadAsStreamAsync();
        await using FileStream target = File.Create(destination);
        await source.CopyToAsync(target);
    }

    private static string Sha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string CacheRoot()
    {
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

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
}
