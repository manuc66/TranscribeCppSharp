// NOT auto-generated — this file is part of the TranscribeCppSharp.Interop
// assembly and survives regeneration of NativeMethods.cs. It registers a
// DllImportResolver so the native library is found without LD_LIBRARY_PATH,
// custom DLL search paths, or manual copying, both in published apps and in
// plain "dotnet run" scenarios. It searches the app output directory —
// including the runtimes/<rid>/native/ layout where .NET places runtime-package
// binaries — and the NuGet global packages folder.
//
// Every candidate is gated on transcribe_version(): the bindings are generated
// against one upstream transcribe.cpp release and struct sizes move between
// them (transcribe_model_load_params grew from 24 to 32 bytes in v0.4.1), so a
// native left over from another release must not be loaded. The NuGet cache
// keeps every version a machine ever restored, and its enumeration order is
// filesystem-dependent; without the gate a stale package silently wins and the
// first ABI check fails with a message that blames the bindings.
//
// On failure it throws a DllNotFoundException that lists the exact package to
// add and the paths searched, plus any candidate rejected for its version.

namespace TranscribeCppSharp.Interop;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using static System.Globalization.CultureInfo;

internal static partial class NativeMethods
{
    /// <summary>
    /// The transcribe.cpp version these bindings were generated against,
    /// injected by the Interop project from <c>build/TRANSCRIBE_VERSION</c> as
    /// assembly metadata. Empty when the assembly was built without it, in
    /// which case the version gate is skipped rather than rejecting everything.
    /// </summary>
    internal static readonly string ExpectedNativeVersion = ReadExpectedNativeVersion();

    /// <summary>
    /// The RID of the native package that matches this OS and architecture,
    /// used for the "add this package" line of the failure message.
    /// <see cref="RuntimeInformation.RuntimeIdentifier"/> is not usable for that:
    /// outside .NET's RID graph it returns a distro-specific value (Arch Linux
    /// reports <c>arch-x64</c>), and no such package is published.
    /// </summary>
    internal static readonly string SupportedRid = GetSupportedRid();

    static NativeMethods()
    {
        NativeLibrary.SetDllImportResolver(typeof(NativeMethods).Assembly, ResolveNativeLibrary);
    }

    /// <summary>
    /// Path of the native library the resolver accepted, or null before the
    /// first successful load. Used by the wrapper's ABI diagnostics so a struct
    /// mismatch names the library that is actually loaded.
    /// </summary>
    internal static string? LoadedNativeLibraryPath { get; private set; }

    /// <summary>
    /// Version reported by the accepted native library, or null when the
    /// version gate was skipped or no library has been loaded yet.
    /// </summary>
    internal static string? LoadedNativeVersion { get; private set; }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr NativeVersionFn();

    private static IntPtr ResolveNativeLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName != "transcribe")
        {
            return IntPtr.Zero;
        }

        var candidates = EnumerateCandidates().ToList();
        var rejected = new List<(string Path, string Reason)>();
        foreach (string candidate in candidates)
        {
            if (TryLoadAccepted(candidate, rejected, out IntPtr handle))
            {
                return handle;
            }
        }

        // Fail-fast with an actionable message instead of the cryptic
        // DllNotFoundException that .NET would otherwise produce.
        throw new DllNotFoundException(BuildNotFoundMessage(candidates, rejected));
    }

    /// <summary>
    /// Builds the error message shown when the native library cannot be located.
    /// Exposed internally for tests.
    /// </summary>
    internal static string BuildNotFoundMessage(
        List<string> candidates,
        IReadOnlyList<(string Path, string Reason)>? rejected = null)
    {
        var rid = SupportedRid;
        var searched = candidates.Count == 0
            ? "  (no candidates — app output dir and NuGet packages folder not resolvable)"
            : string.Join(Environment.NewLine, candidates.Select(c => $"  {c}"));

        var message = new StringBuilder();
        message.Append(InvariantCulture, $"The native 'transcribe' library was not found for RID '{rid}'.");
        if (ExpectedNativeVersion.Length > 0)
        {
            message.Append(InvariantCulture, $" These bindings target transcribe.cpp {ExpectedNativeVersion}.");
        }

        message.AppendLine();
        message.AppendLine("Searched:");
        message.AppendLine(searched);

        if (rejected is { Count: > 0 })
        {
            message.AppendLine("Rejected candidates:");
            foreach (var (path, reason) in rejected)
            {
                message.AppendLine(InvariantCulture, $"  {path} ({reason})");
            }
        }

        message.AppendLine("To fix, add the native package for your platform:");
        var versionArgument = ExpectedNativeVersion.Length > 0 ? $" --version {ExpectedNativeVersion}" : string.Empty;
        message.AppendLine(InvariantCulture, $"  dotnet add package TranscribeCppSharp.Native.{rid}{versionArgument}");
        if (rejected is { Count: > 0 })
        {
            message.AppendLine(
                "A rejected path under the NuGet packages folder is a stale package: remove that version, " +
                "or let the package manager restore the one named above.");
        }

        message.Append("For musl/Alpine Linux or custom native builds, see the 'Building from source' section of the README.");
        return message.ToString();
    }

    internal static IEnumerable<string> EnumerateCandidates()
        => EnumerateCandidates(AppContext.BaseDirectory, ResolvePackagesFolder());

    /// <summary>
    /// Enumerates candidate native-library paths, most specific first.
    /// <paramref name="baseDirectory"/> is the app output directory and
    /// <paramref name="packagesFolder"/> the NuGet global packages folder
    /// (null disables the NuGet fallback). Exposed internally for tests.
    /// </summary>
    internal static IEnumerable<string> EnumerateCandidates(string baseDirectory, string? packagesFolder)
    {
        string fileName = GetNativeFileName();

        // 1. App output directory (dotnet publish / build output) root.
        yield return Path.Combine(baseDirectory, fileName);

        // 2. App output runtimes/<rid>/native — the standard .NET layout where
        //    runtime packages place their binaries. The subfolder carries the
        //    concrete RID (e.g. linux-x64), which differs from the portable RID
        //    RuntimeInformation.RuntimeIdentifier reports (e.g. arch-x64) when no
        //    <RuntimeIdentifier> is set — so enumerate every runtimes/*/native.
        string runtimesDir = Path.Combine(baseDirectory, "runtimes");
        if (Directory.Exists(runtimesDir))
        {
            foreach (string nativeDir in EnumerateNativeDirs(runtimesDir))
            {
                yield return Path.Combine(nativeDir, fileName);
            }
        }

        // 3. NuGet global packages folder:
        //    ~/.nuget/packages/transcribecppsharp.native.<rid>/<version>/runtimes/<rid>/native/
        //    Enumerate every native.<rid> package and every runtimes/*/native it
        //    ships, since the runtime reports the portable RID while package and
        //    runtimes folders use the concrete RID. Every cached version is
        //    listed on purpose: the resolver accepts the one whose
        //    transcribe_version() matches the bindings, the rest are reported as
        //    rejected. The order is not significant for correctness, only for
        //    which compatible copy wins when several are present.
        if (string.IsNullOrEmpty(packagesFolder) || !Directory.Exists(packagesFolder))
        {
            yield break;
        }

        foreach (string packageDir in Directory.EnumerateDirectories(
                     packagesFolder, "transcribecppsharp.native.*", SearchOption.TopDirectoryOnly))
        {
            foreach (string versionDir in GetVersionDirs(packageDir))
            {
                string pkgRuntimes = Path.Combine(versionDir, "runtimes");
                if (!Directory.Exists(pkgRuntimes))
                {
                    continue;
                }

                foreach (string nativeDir in EnumerateNativeDirs(pkgRuntimes))
                {
                    yield return Path.Combine(nativeDir, fileName);
                }
            }
        }
    }

    private static string? ResolvePackagesFolder()
    {
        string? packagesFolder = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (string.IsNullOrEmpty(packagesFolder))
        {
            string? home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(home))
            {
                packagesFolder = Path.Combine(home, ".nuget", "packages");
            }
        }

        return packagesFolder;
    }

    private static IEnumerable<string> EnumerateNativeDirs(string runtimesRoot)
    {
        foreach (string ridDir in Directory.EnumerateDirectories(runtimesRoot, "*", SearchOption.TopDirectoryOnly))
        {
            string nativeDir = Path.Combine(ridDir, "native");
            if (Directory.Exists(nativeDir))
            {
                yield return nativeDir;
            }
        }
    }

    private static IEnumerable<string> GetVersionDirs(string packageRoot)
    {
        foreach (string dir in Directory.EnumerateDirectories(packageRoot))
        {
            string name = Path.GetFileName(dir);
            if (name.StartsWith('.') || name.StartsWith('_'))
            {
                continue;
            }

            yield return dir;
        }
    }

    private static string GetNativeFileName()
    {
        if (OperatingSystem.IsWindows())
        {
            return "transcribe.dll";
        }

        if (OperatingSystem.IsMacOS())
        {
            return "libtranscribe.dylib";
        }

        return "libtranscribe.so";
    }

    private static string GetSupportedRid()
    {
        // Only 64-bit RIDs are published. Windows ships one package, so it is
        // named whatever the process architecture is — on an ARM64 host the
        // x64 build is the one that exists.
        if (OperatingSystem.IsWindows())
        {
            return "win-x64";
        }

        if (OperatingSystem.IsMacOS())
        {
            return RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "osx-arm64" : "osx-x64";
        }

        if (OperatingSystem.IsLinux())
        {
            return RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "linux-arm64" : "linux-x64";
        }

        // A platform with no package: the message is still built, and the
        // portable RID is the best description available.
        return RuntimeInformation.RuntimeIdentifier;
    }

    private static bool TryLoadAccepted(
        string candidate,
        List<(string Path, string Reason)> rejected,
        out IntPtr handle)
    {
        handle = IntPtr.Zero;
        if (!File.Exists(candidate))
        {
            return false;
        }

        try
        {
            handle = NativeLibrary.Load(candidate);
        }
        catch (Exception)
        {
            // Wrong architecture, missing libggml* dependency, not a native
            // library at all: try the next candidate.
            return false;
        }

        if (ExpectedNativeVersion.Length > 0)
        {
            string? version = ReadNativeVersion(handle);
            if (version is null)
            {
                rejected.Add((candidate, "has no readable transcribe_version()"));
                Unload(handle);
                handle = IntPtr.Zero;
                return false;
            }

            if (!string.Equals(version, ExpectedNativeVersion, StringComparison.Ordinal))
            {
                rejected.Add((candidate, $"reports transcribe.cpp {version}"));
                Unload(handle);
                handle = IntPtr.Zero;
                return false;
            }

            LoadedNativeVersion = version;
        }

        LoadedNativeLibraryPath = candidate;
        return true;
    }

    private static string? ReadNativeVersion(IntPtr handle)
    {
        try
        {
            var export = NativeLibrary.GetExport(handle, "transcribe_version");
            var version = Marshal.GetDelegateForFunctionPointer<NativeVersionFn>(export)();
            return Marshal.PtrToStringUTF8(version);
        }
        catch (Exception)
        {
            // A library without the entry point cannot be checked and is not
            // accepted: the bindings call it, so it would fail later anyway.
            return null;
        }
    }

    /// <summary>
    /// Best-effort unload of a rejected candidate. The handle is not reused, but
    /// leaving it mapped would keep its libggml* siblings resident and the next
    /// candidate could bind to them.
    /// </summary>
    private static void Unload(IntPtr handle)
    {
        try
        {
            NativeLibrary.Free(handle);
        }
        catch (Exception)
        {
            // Not fatal: the candidate is simply never used.
        }
    }

    private static string ReadExpectedNativeVersion()
    {
        foreach (AssemblyMetadataAttribute attribute in typeof(NativeMethods).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
        {
            if (attribute.Key == "TranscribeNativeVersion" && !string.IsNullOrWhiteSpace(attribute.Value))
            {
                return attribute.Value.Trim();
            }
        }

        return string.Empty;
    }
}
