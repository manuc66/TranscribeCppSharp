namespace TranscribeCppSharp.SharedSource;

using System;
using System.IO;

/// <summary>
/// Confines a path that a build-time tool reads, writes or deletes to the
/// directory that tool owns.
/// </summary>
/// <remarks>
/// Shared source, not a shared project: the generator and the two tools are
/// separate executables and none of them ships as a package, so a linked file
/// gives one guard with no dependency added. The same rule is applied by
/// <c>ModelStore.Contain</c> to the model cache; this one covers the build side.
/// <para>
/// It is a backstop, not a privilege boundary: a developer or CI runs these
/// tools against a checkout, and the paths normally come from the repository
/// itself. What it stops is a value read from a file (a RID, a version, a
/// manifest entry) or a mistyped argument reaching outside the tree the tool was
/// pointed at — the recursive delete in <c>FetchNative</c> is why it matters.
/// </para>
/// </remarks>
internal static class BuildPath
{
    /// <summary>
    /// Returns <paramref name="candidate"/> as a full path, or throws when it is
    /// not inside <paramref name="root"/>.
    /// </summary>
    /// <param name="root">The directory the caller owns.</param>
    /// <param name="candidate">A path, absolute or relative to the current directory.</param>
    /// <returns>The normalized full path.</returns>
    /// <exception cref="InvalidOperationException">The path resolves outside <paramref name="root"/>.</exception>
    internal static string Contain(string root, string candidate)
    {
        string rootFull = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string full = Path.GetFullPath(candidate);
        if (!full.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !string.Equals(full, rootFull, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"refusing to use '{full}': it is outside '{rootFull}'.");
        }

        return full;
    }
}
