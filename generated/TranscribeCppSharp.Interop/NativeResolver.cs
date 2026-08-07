// NOT auto-generated — this file is part of the TranscribeCppSharp.Interop
// assembly and survives regeneration of NativeMethods.cs. It registers a
// DllImportResolver so the native library is found without LD_LIBRARY_PATH,
// custom DLL search paths, or manual copying, both in published apps and in
// plain "dotnet run" scenarios.
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

    private static IEnumerable<string> EnumerateCandidates()
    {
        string fileName = GetNativeFileName();

        // 1. App output directory (dotnet publish / build output).
        yield return Path.Combine(AppContext.BaseDirectory, fileName);

        // 2. NuGet global packages folder:
        //    ~/.nuget/packages/transcribecppsharp.native.<rid>/<version>/runtimes/<rid>/native/
        string? packagesFolder = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (string.IsNullOrEmpty(packagesFolder))
        {
            string? home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(home))
            {
                packagesFolder = Path.Combine(home, ".nuget", "packages");
            }
        }

        string rid = RuntimeInformation.RuntimeIdentifier;
        if (string.IsNullOrEmpty(packagesFolder) || string.IsNullOrEmpty(rid))
        {
            yield break;
        }

        string packageRoot = Path.Combine(packagesFolder, $"transcribecppsharp.native.{rid.ToLowerInvariant()}");
        if (!Directory.Exists(packageRoot))
        {
            yield break;
        }

        foreach (string versionDir in GetVersionDirs(packageRoot))
        {
            yield return Path.Combine(versionDir, "runtimes", rid, "native", fileName);
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
