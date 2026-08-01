#nullable enable

using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using TranscribeCppSharp.Interop;

namespace TranscribeCppSharp;

/// <summary>
/// Exception thrown when a transcribe.cpp native call returns an error status.
/// </summary>
public sealed class TranscribeException : Exception
{
    /// <summary>
    /// The native status code returned by the library.
    /// Common values include:
    /// <list type="bullet">
    /// <item><description><see cref="Status.Ok"/> (0): Success.</description></item>
    /// <item><description><see cref="Status.ErrInvalidArg"/> (1): Invalid argument provided.</description></item>
    /// <item><description><see cref="Status.ErrGguf"/> (4): Failed to load or parse the model file.</description></item>
    /// <item><description><see cref="Status.ErrBackend"/> (8): Hardware acceleration backend failure.</description></item>
    /// <item><description><see cref="Status.ErrAborted"/> (13): Operation was cancelled via CancellationToken.</description></item>
    /// </list>
    /// </summary>
    public Status StatusCode { get; }

    /// <summary>The raw integer value of the status code.</summary>
    public int ErrorCode { get; }

    /// <summary>The native method that failed, if known.</summary>
    public string? FailedMethod { get; }

    /// <summary>
    /// Create an exception for a failed native call.
    /// </summary>
    /// <param name="status">The native status code returned by the library.</param>
    /// <param name="failedMethod">The native method name that failed, if known.</param>
    public TranscribeException(Status status, string? failedMethod = null)
        : base(BuildMessage(status, failedMethod))
    {
        StatusCode = status;
        ErrorCode = (int)status;
        FailedMethod = failedMethod;
    }

    private static string BuildMessage(Status status, string? failedMethod)
    {
        var method = failedMethod != null ? $" in {failedMethod}" : string.Empty;
        var nativeMsg = GetNativeStatusMessage((int)status);
        return $"transcribe native error{method}: {status} ({(int)status}){nativeMsg}";
    }

    /// <summary>
    /// Best-effort native status description, memoized per status code so the
    /// exception path hits P/Invoke at most once per code — and never crashes
    /// (e.g. when the native library cannot be loaded).
    /// </summary>
    private static readonly ConcurrentDictionary<int, string> SStatusMessages = new();

    private static string GetNativeStatusMessage(int status)
    {
        if (SStatusMessages.TryGetValue(status, out var cached))
        {
            return cached;
        }

        var msg = string.Empty;
        try
        {
            var ptr = NativeMethods.StatusString(status);
            if (ptr != IntPtr.Zero)
            {
                var str = Marshal.PtrToStringUTF8(ptr);
                if (!string.IsNullOrWhiteSpace(str))
                {
                    msg = $" — {str}";
                }
            }
        }
        catch
        {
            // StatusString is best-effort; don't crash the exception constructor
        }

        SStatusMessages[status] = msg;
        return msg;
    }
}
