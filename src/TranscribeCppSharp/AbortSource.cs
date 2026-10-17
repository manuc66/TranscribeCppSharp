namespace TranscribeCppSharp;

using System;
using System.Threading;
using TranscribeCppSharp.Interop;

/// <summary>
/// Owns the single interop abort delegate a session registers with the native
/// side and switches it between "a <see cref="CancellationToken"/> is
/// installed" and "do not abort". Shared by the diarize, langid and vad
/// sessions, which each expose only a cancellation token (the ASR session
/// additionally supports a user callback).
/// </summary>
/// <remarks>
/// The delegate is created once and reused so a repeated cancellable call
/// allocates nothing here; it is rooted for the session's lifetime because the
/// native library holds its pointer.
/// </remarks>
internal sealed class AbortSource(Action<AbortCallback, IntPtr> setCallback)
{
    private AbortCallback? callback;
    private CancellationToken token;
    private bool usesToken;

    /// <summary>Install <paramref name="ct"/> as the abort source.</summary>
    public void UseToken(CancellationToken ct)
    {
        token = ct;
        usesToken = true;
        setCallback(EnsureCallback(), IntPtr.Zero);
    }

    /// <summary>Stop aborting (the callback always answers false).</summary>
    public void Clear()
    {
        token = CancellationToken.None;
        usesToken = false;
        setCallback(EnsureCallback(), IntPtr.Zero);
    }

    private AbortCallback EnsureCallback()
        => callback ??= _ => usesToken && token.IsCancellationRequested;
}
