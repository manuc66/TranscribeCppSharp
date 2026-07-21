#nullable enable

using System;
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
    /// <item><description><see cref="Status.ErrModelLoad"/> (4): Failed to load or parse the model file.</description></item>
    /// <item><description><see cref="Status.ErrBackend"/> (8): Hardware acceleration backend failure.</description></item>
    /// <item><description><see cref="Status.ErrAborted"/> (13): Operation was cancelled via CancellationToken.</description></item>
    /// </list>
    /// </summary>
    public Status StatusCode { get; }

    /// <summary>The raw integer value of the status code.</summary>
    public int ErrorCode { get; }

    /// <summary>The native method that failed, if known.</summary>
    public string? FailedMethod { get; }

    public TranscribeException(Status status, string? failedMethod = null)
        : base(BuildMessage(status, failedMethod))
    {
        StatusCode = status;
        ErrorCode = (int)status;
        FailedMethod = failedMethod;
    }

    private static string BuildMessage(Status status, string? failedMethod)
    {
        var nativeMessage = NativeMethods.StatusString((int)status);
        var method = failedMethod != null ? $" in {failedMethod}" : "";
        return $"transcribe native error{method}: {status} (code {nativeMessage})";
    }
}
