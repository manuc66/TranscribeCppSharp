namespace TranscribeCppSharp.UI.Services;

using System;
using System.IO;
using System.Text;

/// <summary>
/// The files the app keeps between runs: saved transcripts, the settings and the
/// log.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="File.WriteAllText(string, string)"/> and
/// <see cref="File.AppendAllText(string, string)"/> create files 0666 masked by
/// the process umask, which is 0644 with the stock 0022 — on a shared host every
/// local account could read every saved transcript. On Unix the directories this
/// class creates are 0700 and the files 0600. On Windows the ACL inherited from
/// the profile already limits them to the user, and there is nothing to set.
/// </para>
/// <para>
/// A directory that already exists is **not** re-moded: the app directory can be
/// a shared, pre-provisioned cache (an <c>XDG_CACHE_HOME</c> under a shared
/// path), and changing it would cut other accounts off. The files themselves are
/// tightened whether they are new or old (<see cref="EnsureFile"/>), which is
/// what holds the transcript.
/// </para>
/// </remarks>
internal static class PrivateFile
{
    private const UnixFileMode OwnerDirectory =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private const UnixFileMode OwnerFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    /// <summary>UTF-8 without a byte-order mark, like <see cref="File.WriteAllText(string, string)"/>.</summary>
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Creates <paramref name="path"/> when it is missing, owner-only on Unix.
    /// </summary>
    /// <param name="path">The directory.</param>
    internal static void EnsureDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            return;
        }

        Directory.CreateDirectory(path);

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, OwnerDirectory);
        }
    }

    /// <summary>
    /// Makes an existing file owner-only, and does nothing when it is missing.
    /// </summary>
    /// <remarks>
    /// The migration path: a settings file or a saved transcript written by a
    /// version that used 0644 is corrected when it is read, not only when it is
    /// rewritten. A file with no read path (the log) is fixed on its next append.
    /// </remarks>
    /// <param name="path">The file to tighten.</param>
    internal static void EnsureFile(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.SetUnixFileMode(path, OwnerFile);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not ours to tighten (a shared or read-only file): reading it still
            // works, and failing here would turn a permission detail into a
            // broken tab.
        }
    }

    /// <summary>
    /// Replaces <paramref name="path"/> with <paramref name="content"/>.
    /// </summary>
    /// <remarks>
    /// Written to a sibling and renamed into place, so a crash leaves either the
    /// old file or the new one, never a truncated document. The sibling is
    /// created owner-only, and the rename carries that mode onto the result.
    /// </remarks>
    /// <param name="path">Where the document goes.</param>
    /// <param name="content">What to write.</param>
    internal static void WriteAllText(string path, string content)
    {
        ArgumentNullException.ThrowIfNull(path);
        EnsureDirectory(Path.GetDirectoryName(path)!);

        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, Options(FileMode.Create)))
            using (var writer = new StreamWriter(stream, Utf8))
            {
                writer.Write(content);
            }

            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A leftover .tmp is harmless; letting the cleanup throw would
                // hide the write failure that sent us to this finally.
            }
        }
    }

    /// <summary>
    /// Appends <paramref name="content"/> to <paramref name="path"/>, creating
    /// the file and its directory when they are missing.
    /// </summary>
    /// <param name="path">The file to append to.</param>
    /// <param name="content">What to append.</param>
    internal static void AppendAllText(string path, string content)
    {
        ArgumentNullException.ThrowIfNull(path);
        EnsureDirectory(Path.GetDirectoryName(path)!);

        using var stream = new FileStream(path, Options(FileMode.Append));
        if (!OperatingSystem.IsWindows())
        {
            // UnixCreateMode only applies to a file this call creates; an
            // existing log from an earlier version is fixed here.
            File.SetUnixFileMode(path, OwnerFile);
        }

        using var writer = new StreamWriter(stream, Utf8);
        writer.Write(content);
    }

    private static FileStreamOptions Options(FileMode mode)
    {
        var options = new FileStreamOptions
        {
            Mode = mode,
            Access = FileAccess.Write,
            Share = mode == FileMode.Append ? FileShare.Read : FileShare.None,
        };

        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = OwnerFile;
        }

        return options;
    }
}
