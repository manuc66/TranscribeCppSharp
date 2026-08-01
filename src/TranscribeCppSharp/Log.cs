#nullable enable

using System;
using TranscribeCppSharp.Interop;

namespace TranscribeCppSharp;

/// <summary>
/// Configure native log output. Call before Backends.InitDefault().
/// </summary>
public static class Log
{
    private static LogCallback? callback;

    /// <summary>
    /// Set a handler for native log messages.
    /// Pass null to disable logging.
    /// </summary>
    public static void Configure(Action<LogLevel, string>? handler)
    {
        if (handler == null)
        {
            callback = null;
#pragma warning disable CS8625 // Intentional: null disables native logging
            NativeMethods.LogSet(null, IntPtr.Zero);
#pragma warning restore CS8625
            return;
        }

        callback = (level, msg, _) => handler(level, msg);
        NativeMethods.LogSet(callback, IntPtr.Zero);
    }
}
