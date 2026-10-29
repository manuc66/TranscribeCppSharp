namespace TranscribeCppSharp.Cli;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TranscribeCppSharp.Interop;
using TranscribeCppSharp.Models;
using TranscribeCppSharp.SharedSource;

/// <summary>How one <c>--doctor</c> check went.</summary>
internal enum DoctorStatus
{
    Ok,
    Warn,
    Fail,
}

/// <summary>One line of the diagnosis.</summary>
/// <param name="Name">The short stable name used by <c>--json</c>.</param>
/// <param name="Status">Whether this is fine, worth knowing, or broken.</param>
/// <param name="Detail">What was found, in one sentence.</param>
internal readonly record struct DoctorCheck(string Name, DoctorStatus Status, string Detail);

/// <summary>
/// <c>transcribe --doctor</c>: what this machine can actually do, and what is
/// missing.
/// </summary>
/// <remarks>
/// <para>
/// Every failure this project has a support question about is a first-run
/// environment problem: no native package for the platform, a native library and
/// a wrapper from different versions, no ffmpeg for a non-WAV input, a GPU that
/// does not initialise, a model file that is the wrong bytes. Each of them
/// surfaces one at a time, while transcribing. This collects them in one pass,
/// changes nothing, and prints the fix.
/// </para>
/// <para>
/// It never downloads: the cache is read, not filled, so it is safe to run
/// offline and on a metered connection.
/// </para>
/// </remarks>
internal static class Doctor
{
    /// <summary>Indented, and reused: one instance per process, not per call.</summary>
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>Runs every check and prints the report.</summary>
    /// <param name="stdout">Where the report goes.</param>
    /// <param name="stderr">Where native warnings go, so stdout stays parseable.</param>
    /// <param name="json">True to print one JSON object instead of the lines.</param>
    /// <param name="verifyCache">True to re-hash every cached model.</param>
    /// <returns>0 when no check failed, 1 when at least one did.</returns>
    internal static int Run(TextWriter stdout, TextWriter stderr, bool json, bool verifyCache)
    {
        var checks = new List<DoctorCheck>();

        // Native logging goes to stderr: the default writes to the process'
        // stdout, which would corrupt the JSON document.
        Log.Configure((level, message) =>
        {
            if (level is LogLevel.LogLevelWarn or LogLevel.LogLevelError)
            {
                stderr.WriteLine(message);
            }
        });

        try
        {
            checks.Add(new DoctorCheck("tool", DoctorStatus.Ok, TranscribeCommand.ToolVersion));

            bool native = CheckNative(checks);
            if (native)
            {
                CheckAbi(checks);
            }

            CheckTool(checks, "ffmpeg", why: "non-WAV input is decoded with it");
            CheckTool(checks, "ffplay", why: "only the desktop app plays excerpts");
            CheckBackends(checks);
            CheckCache(checks, verifyCache);
            CheckSavedData(checks);
        }
        finally
        {
            // Leave logging off rather than pointing at the (closed) writers.
            Log.Configure(null);
        }

        return Report(checks, stdout, json);
    }

    /// <summary>Whether the native library loads, and which version it is.</summary>
    private static bool CheckNative(List<DoctorCheck> checks)
    {
        try
        {
            string version = Backends.Version;
            string commit = Backends.VersionCommit;
            if (string.IsNullOrEmpty(version))
            {
                checks.Add(new DoctorCheck("native", DoctorStatus.Warn, "the library loaded but reports no version"));
                return true;
            }

            checks.Add(new DoctorCheck(
                "native",
                DoctorStatus.Ok,
                string.IsNullOrEmpty(commit) ? $"transcribe.cpp {version}" : $"transcribe.cpp {version} ({commit})"));
            return true;
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        {
            checks.Add(new DoctorCheck(
                "native",
                DoctorStatus.Fail,
                "not found; add the TranscribeCppSharp.Native package for this platform "
                + "(or set the native library on the loader's path)"));
            return false;
        }
    }

    /// <summary>
    /// Builds the two param structs the tool uses, which is what triggers the
    /// size check against the native library.
    /// </summary>
    private static void CheckAbi(List<DoctorCheck> checks)
    {
        try
        {
            using var run = new RunParamsBuilder();
            using var load = new ModelLoadParamsBuilder();

            checks.Add(new DoctorCheck(
                "abi",
                DoctorStatus.Ok,
                "RunParams and ModelLoadParams match the native library"));
        }
        catch (Exception ex) when (ex is InvalidOperationException or TranscribeException)
        {
            checks.Add(new DoctorCheck(
                "abi",
                DoctorStatus.Fail,
                $"{ex.Message} -- the wrapper and the native package are from different versions"));
        }
    }

    /// <summary>Whether an external tool is on PATH.</summary>
    private static void CheckTool(List<DoctorCheck> checks, string name, string why)
    {
        string? path = FindOnPath(name);
        if (path is not null)
        {
            checks.Add(new DoctorCheck(name, DoctorStatus.Ok, path));
            return;
        }

        checks.Add(new DoctorCheck(name, DoctorStatus.Warn, $"not found on PATH; {why}"));
    }

    /// <summary>Which compute devices the native layer sees.</summary>
    private static void CheckBackends(List<DoctorCheck> checks)
    {
        try
        {
            Backends.InitDefault();
            IReadOnlyList<BackendDevice> devices = Backends.EnumerateDevices();
            if (devices.Count == 0)
            {
                checks.Add(new DoctorCheck("backends", DoctorStatus.Fail, "no compute device was reported"));
                return;
            }

            var names = new StringBuilder();
            foreach (BackendDevice device in devices)
            {
                if (names.Length > 0)
                {
                    names.Append(", ");
                }

                names.Append(device.Name);
            }

            checks.Add(new DoctorCheck("backends", DoctorStatus.Ok, names.ToString()));
        }
        catch (Exception ex) when (ex is TranscribeException or DllNotFoundException or BadImageFormatException)
        {
            checks.Add(new DoctorCheck("backends", DoctorStatus.Fail, ex.Message));
        }
    }

    /// <summary>Where the models live, how many are there, and optionally whether they are intact.</summary>
    private static void CheckCache(List<DoctorCheck> checks, bool verifyCache)
    {
        ModelDescriptor[] cached = [.. ModelStore.Catalog.Where(ModelStore.IsCached)];
        long bytes = 0;
        foreach (ModelDescriptor descriptor in cached)
        {
            bytes += ModelStore.CachedSize(descriptor);
        }

        checks.Add(new DoctorCheck(
            "cache",
            DoctorStatus.Ok,
            $"{ModelStore.CacheRoot()}: {cached.Length} model(s), {ModelSizeFormat.Format(bytes)}"));

        if (!verifyCache)
        {
            return;
        }

        if (cached.Length == 0)
        {
            checks.Add(new DoctorCheck("cache-integrity", DoctorStatus.Ok, "nothing cached to verify"));
            return;
        }

        var bad = new List<string>();
        foreach (ModelDescriptor descriptor in cached)
        {
            if (!string.Equals(Sha256Of(ModelStore.CachedPath(descriptor)), descriptor.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                bad.Add(descriptor.Alias);
            }
        }

        checks.Add(bad.Count == 0
            ? new DoctorCheck(
                "cache-integrity",
                DoctorStatus.Ok,
                $"{cached.Length} cached model(s) match their recorded sha256")
            : new DoctorCheck(
                "cache-integrity",
                DoctorStatus.Fail,
                $"these do not match their recorded sha256: {string.Join(", ", bad)} "
                + "-- delete them with --remove-model and fetch again"));
    }

    /// <summary>
    /// Whether the desktop app's saved transcripts and settings are owner-only.
    /// </summary>
    /// <remarks>
    /// The CLI does not write these files; it checks them because the audit found
    /// them world-readable and the fix only applies on the next write. Silent on
    /// Windows, where the profile ACL already limits them.
    /// </remarks>
    private static void CheckSavedData(List<DoctorCheck> checks)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string appDirectory = Path.GetDirectoryName(ModelStore.CacheRoot())!;
        var paths = new List<string>
        {
            Path.Combine(appDirectory, "ui-settings.json"),
            Path.Combine(appDirectory, "logs", "ui.log"),
        };

        string history = Path.Combine(appDirectory, "history");
        if (Directory.Exists(history))
        {
            paths.Add(history);
            paths.AddRange(Directory.EnumerateFiles(history, "*.json"));
        }

        string[] exposed = [.. paths.Where(IsReadableByOthers).Select(Path.GetFileName)!];

        checks.Add(exposed.Length == 0
            ? new DoctorCheck("privacy", DoctorStatus.Ok, "settings, history and log are owner-only")
            : new DoctorCheck(
                "privacy",
                DoctorStatus.Warn,
                $"readable by other local accounts: {string.Join(", ", exposed)}"));
    }

    /// <summary>Whether another local account can read this file or directory.</summary>
    private static bool IsReadableByOthers(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            // No mode bits: the profile ACL limits these files to the user.
            return false;
        }

        try
        {
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                return false;
            }

            UnixFileMode mode = File.GetUnixFileMode(path);
            return (mode & (UnixFileMode.GroupRead | UnixFileMode.OtherRead)) != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not ours to inspect; the report says nothing about it.
            return false;
        }
    }

    /// <summary>Prints the report and returns the exit code.</summary>
    private static int Report(List<DoctorCheck> checks, TextWriter stdout, bool json)
    {
        bool failed = checks.Any(c => c.Status == DoctorStatus.Fail);
        int warnings = checks.Count(c => c.Status == DoctorStatus.Warn);

        if (json)
        {
            var document = new
            {
                tool = TranscribeCommand.ToolVersion,
                ok = !failed,
                checks = checks.Select(c => new
                {
                    name = c.Name,
                    status = c.Status.ToString().ToLowerInvariant(),
                    detail = c.Detail,
                }),
            };

            stdout.WriteLine(JsonSerializer.Serialize(document, Json));
        }
        else
        {
            int width = checks.Max(c => c.Name.Length);
            foreach (DoctorCheck check in checks)
            {
                string status = check.Status switch
                {
                    DoctorStatus.Ok => "ok",
                    DoctorStatus.Warn => "warn",
                    _ => "fail",
                };

                stdout.WriteLine($"{status,-5} {check.Name.PadRight(width)}  {check.Detail}");
            }

            stdout.WriteLine();
            stdout.WriteLine(failed
                ? "at least one check failed; see the lines above"
                : warnings == 0
                    ? "all checks passed"
                    : $"{warnings.ToString(CultureInfo.InvariantCulture)} warning(s); nothing blocking");
        }

        return failed ? 1 : 0;
    }

    /// <summary>The full path of <paramref name="name"/> on PATH, or null.</summary>
    private static string? FindOnPath(string name)
    {
        string executable = OperatingSystem.IsWindows() ? name + ".exe" : name;
        foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = Path.Combine(directory, executable);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>The sha256 of a file, as lowercase hex.</summary>
    private static string Sha256Of(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
