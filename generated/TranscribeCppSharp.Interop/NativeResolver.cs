// NOT auto-generated — this file is part of the TranscribeCppSharp.Interop
// assembly and survives regeneration of NativeMethods.cs. It registers a
// DllImportResolver so the native library is found without LD_LIBRARY_PATH,
// custom DLL search paths, or manual copying, both in published apps and in
// plain "dotnet run" scenarios. It searches the app output directory —
// including the runtimes/<rid>/native/ layout where .NET places runtime-package
// binaries — and the NuGet global packages folder. On failure it throws a
// DllNotFoundException that lists the exact package to add and the paths searched.
#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace TranscribeCppSharp.Interop;

internal static partial class NativeMethods
{
    static NativeMethods()
    {
        NativeLibrary.SetDllImportResolver(typeof(NativeMethods).Assembly, ResolveNativeLibrary);
    }

    private static IntPtr ResolveNativeLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName != "transcribe")
        {
            return IntPtr.Zero;
        }

        var candidates = EnumerateCandidates().ToList();
        foreach (string candidate in candidates)
        {
            if (TryLoadNativeLibrary(candidate, out IntPtr handle))
            {
                return handle;
            }
        }

        // Fail-fast with an actionable message instead of the cryptic
        // DllNotFoundException that .NET would otherwise produce.
        throw new DllNotFoundException(BuildNotFoundMessage(candidates));
    }

    /// <summary>
    /// Builds the error message shown when the native library cannot be located.
    /// Exposed internally for tests.
    /// </summary>
    internal static string BuildNotFoundMessage(List<string> candidates)
    {
        var rid = RuntimeInformation.RuntimeIdentifier;
        var searched = candidates.Count == 0
            ? "  (no candidates — app output dir and NuGet packages folder not resolvable)"
            : string.Join(Environment.NewLine, candidates.Select(c => $"  {c}"));

        return $"The native 'transcribe' library was not found for RID '{rid}'.{Environment.NewLine}" +
               $"Searched:{Environment.NewLine}{searched}{Environment.NewLine}" +
               $"To fix, add the native package for your platform:{Environment.NewLine}" +
               $"  dotnet add package TranscribeCppSharp.Native.{rid}{Environment.NewLine}" +
               "For musl/Alpine Linux or custom native builds, see the 'Building from source' section of the README.";
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
        //    runtimes folders use the concrete RID.
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

    private static bool TryLoadNativeLibrary(string candidate, out IntPtr handle)
    {
        if (!File.Exists(candidate))
        {
            handle = IntPtr.Zero;
            return false;
        }

        try
        {
            handle = NativeLibrary.Load(candidate);
            return true;
        }
        catch (Exception)
        {
            // Try the next candidate.
        }

        handle = IntPtr.Zero;
        return false;
    }
}
