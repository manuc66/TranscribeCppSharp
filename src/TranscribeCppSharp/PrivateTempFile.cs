namespace TranscribeCppSharp;

using System;
using System.IO;

/// <summary>
/// Creates a temporary file that only the current user can read.
/// </summary>
/// <remarks>
/// <see cref="File.Create(string)"/> uses 0666 masked by the process umask, which
/// is 0644 with the stock 0022: an audio upload being transcribed, or the PCM
/// decoded from it, would be readable by every local user for the whole run.
/// On Unix the file is created 0600. On Windows the ACL inherited from the temp
/// directory already limits it to the user, and the Unix-only option is not set.
/// </remarks>
internal static class PrivateTempFile
{
    /// <summary>
    /// Creates or truncates <paramref name="path"/> for writing, owner-only on Unix.
    /// </summary>
    /// <param name="path">The file to create.</param>
    /// <returns>The opened stream; the caller owns it.</returns>
    internal static FileStream Create(string path)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.Create,
            Access = FileAccess.Write,
            Share = FileShare.None,
        };

        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        return new FileStream(path, options);
    }
}
