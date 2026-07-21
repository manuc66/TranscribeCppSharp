#nullable enable

using System;
using System.Runtime.InteropServices;
using TranscribeCppSharp.Interop;

namespace TranscribeCppSharp;

/// <summary>
/// Result of a StreamFeed or StreamFinalize call.
/// </summary>
public record StreamUpdateResult(
    bool ResultChanged,
    bool IsFinal,
    int Revision,
    TimeSpan InputReceived,
    TimeSpan AudioCommitted,
    TimeSpan Buffered);

/// <summary>
/// Current text state of a streaming session.
/// </summary>
public record StreamTextResult(
    string FullText,
    string CommittedText,
    string TentativeText);

/// <summary>
/// Real-time streaming transcription session.
/// Feed PCM audio chunks incrementally and read partial/final results.
/// Thread-safe: SafeHandle on the parent Session ensures correct handle management.
/// </summary>
public sealed class StreamSession : IDisposable
{
    private readonly SessionHandle _session;
    private bool _disposed;

    internal StreamSession(SessionHandle session) => _session = session;

    private void ThrowIfDisposed()
    {
        if (_disposed || _session.IsInvalid)
            throw new ObjectDisposedException(nameof(StreamSession));
    }

    /// <summary>
    /// Start a new streaming transcription.
    /// </summary>
    public void Begin(
        Action<RunParamsBuilder>? runConfig = null,
        Action<StreamParamsBuilder>? streamConfig = null)
    {
        ThrowIfDisposed();

        using var runParams = new RunParamsBuilder();
        runConfig?.Invoke(runParams);
        using var streamParams = new StreamParamsBuilder();
        streamConfig?.Invoke(streamParams);

        var status = NativeMethods.StreamBegin(_session, runParams.Build(), streamParams.Build());
        if (status != Status.Ok)
            throw new TranscribeException(status, nameof(NativeMethods.StreamBegin));
    }

    /// <summary>
    /// Feed a chunk of PCM audio (16 kHz mono f32).
    /// Returns an update indicating whether results changed.
    /// </summary>
    public unsafe StreamUpdateResult Feed(ReadOnlySpan<float> pcm)
    {
        ThrowIfDisposed();

        var updateSize = (int)NativeMethods.AbiStructSize(AbiStruct.AbiStreamUpdate);
        StackAllocHelper.ThrowIfTooLarge(updateSize, nameof(StreamUpdate));
        Span<byte> updateBuffer = stackalloc byte[updateSize];
        fixed (byte* pBuffer = updateBuffer)
        {
            var updatePtr = (IntPtr)pBuffer;
            NativeMethods.StreamUpdateInit(updatePtr);

            fixed (float* pPcm = pcm)
            {
                var status = NativeMethods.StreamFeed(_session, (IntPtr)pPcm, pcm.Length, updatePtr);
                if (status != Status.Ok)
                    throw new TranscribeException(status, nameof(NativeMethods.StreamFeed));
            }

            var u = Marshal.PtrToStructure<Interop.StreamUpdate>(updatePtr);
            return new StreamUpdateResult(
                ResultChanged: u.resultChanged,
                IsFinal: u.isFinal,
                Revision: u.revision,
                InputReceived: TimeSpan.FromMilliseconds(u.inputReceivedMs),
                AudioCommitted: TimeSpan.FromMilliseconds(u.audioCommittedMs),
                Buffered: TimeSpan.FromMilliseconds(u.bufferedMs));
        }
    }

    /// <summary>
    /// Finalize the stream. No more audio can be fed after this.
    /// </summary>
    public unsafe StreamUpdateResult Finalize()
    {
        ThrowIfDisposed();

        var updateSize = (int)NativeMethods.AbiStructSize(AbiStruct.AbiStreamUpdate);
        StackAllocHelper.ThrowIfTooLarge(updateSize, nameof(StreamUpdate));
        Span<byte> updateBuffer = stackalloc byte[updateSize];
        fixed (byte* pBuffer = updateBuffer)
        {
            var updatePtr = (IntPtr)pBuffer;
            NativeMethods.StreamUpdateInit(updatePtr);

            var status = NativeMethods.StreamFinalize(_session, updatePtr);
            if (status != Status.Ok)
                throw new TranscribeException(status, nameof(NativeMethods.StreamFinalize));

            var u = Marshal.PtrToStructure<Interop.StreamUpdate>(updatePtr);
            return new StreamUpdateResult(
                ResultChanged: u.resultChanged,
                IsFinal: u.isFinal,
                Revision: u.revision,
                InputReceived: TimeSpan.FromMilliseconds(u.inputReceivedMs),
                AudioCommitted: TimeSpan.FromMilliseconds(u.audioCommittedMs),
                Buffered: TimeSpan.FromMilliseconds(u.bufferedMs));
        }
    }

    /// <summary>
    /// Reset the stream to start a new transcription (keeps the session).
    /// </summary>
    public void Reset()
    {
        ThrowIfDisposed();
        NativeMethods.StreamReset(_session);
    }

    /// <summary>Read the current streaming text (full, committed, tentative).</summary>
    public unsafe StreamTextResult CurrentText
    {
        get
        {
            ThrowIfDisposed();

            var textSize = (int)NativeMethods.AbiStructSize(AbiStruct.AbiStreamText);
            StackAllocHelper.ThrowIfTooLarge(textSize, nameof(StreamText));
            Span<byte> textBuffer = stackalloc byte[textSize];
            fixed (byte* pBuffer = textBuffer)
            {
                var textPtr = (IntPtr)pBuffer;
                NativeMethods.StreamTextInit(textPtr);

                var status = NativeMethods.StreamGetText(_session, textPtr);
                if (status != Status.Ok)
                    throw new TranscribeException(status, nameof(NativeMethods.StreamGetText));

                var t = Marshal.PtrToStructure<Interop.StreamText>(textPtr);
                var fullText = t.fullText != IntPtr.Zero && t.fullTextBytes > 0
                    ? Marshal.PtrToStringUTF8(t.fullText, (int)t.fullTextBytes) ?? ""
                    : "";
                var committedText = t.committedText != IntPtr.Zero && t.committedTextBytes > 0
                    ? Marshal.PtrToStringUTF8(t.committedText, (int)t.committedTextBytes) ?? ""
                    : "";
                var tentativeText = t.tentativeText != IntPtr.Zero && t.tentativeTextBytes > 0
                    ? Marshal.PtrToStringUTF8(t.tentativeText, (int)t.tentativeTextBytes) ?? ""
                    : "";

                return new StreamTextResult(fullText, committedText, tentativeText);
            }
        }
    }

    /// <summary>Current state of the streaming session.</summary>
    public StreamState State
    {
        get
        {
            ThrowIfDisposed();
            return NativeMethods.StreamGetState(_session);
        }
    }

    /// <summary>Number of committed segments.</summary>
    public int CommittedSegmentCount
    {
        get
        {
            ThrowIfDisposed();
            return NativeMethods.StreamNCommittedSegments(_session);
        }
    }

    /// <summary>Number of committed words.</summary>
    public int CommittedWordCount
    {
        get
        {
            ThrowIfDisposed();
            return NativeMethods.StreamNCommittedWords(_session);
        }
    }

    /// <summary>Number of committed tokens.</summary>
    public int CommittedTokenCount
    {
        get
        {
            ThrowIfDisposed();
            return NativeMethods.StreamNCommittedTokens(_session);
        }
    }

    /// <summary>Last status of the streaming session.</summary>
    public Status LastStatus
    {
        get
        {
            ThrowIfDisposed();
            return NativeMethods.StreamLastStatus(_session);
        }
    }

    /// <summary>Stream revision (incremented on each commit).</summary>
    public int Revision
    {
        get
        {
            ThrowIfDisposed();
            return NativeMethods.StreamRevision(_session);
        }
    }

    public void Dispose()
    {
        _disposed = true;
    }
}
