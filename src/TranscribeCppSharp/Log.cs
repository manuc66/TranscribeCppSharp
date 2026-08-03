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
    /// <remarks>
    /// Per the upstream transcribe.cpp library (its public header, see
    /// transcribe_log_set): the handler may be invoked from any thread,
    /// including ggml worker threads, so it must be safe for concurrent access
    /// and must not assume it runs on the caller thread. Call once at process
    /// startup, before any model is loaded.
    /// </remarks>
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
