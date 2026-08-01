#nullable enable

using System;
using System.Runtime.InteropServices;
using TranscribeCppSharp.Interop;

namespace TranscribeCppSharp;

/// <summary>
/// Real-time streaming transcription session.
/// Feed PCM audio chunks incrementally and read partial/final results.
/// </summary>
/// <remarks>
/// <b>Lifecycle:</b> <c>Begin()</c> -> <c>Feed()*</c> -> <c>Complete()</c>.
/// <br/>
/// This class is a view over a <see cref="Session"/> and shares its state. 
/// It is <b>not thread-safe</b> for concurrent access, but the underlying 
/// handle management is safe.
/// </remarks>
public sealed class StreamSession : IDisposable
{
    private readonly SessionHandle session;
    private bool disposed;

    internal StreamSession(SessionHandle session) => this.session = session;

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed || session.IsClosed, this);
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

        var status = NativeMethods.StreamBegin(session, runParams.Build(), streamParams.Build());
        if (status != Status.Ok)
        {
            throw new TranscribeException(status, nameof(NativeMethods.StreamBegin));
        }
    }

    /// <summary>
    /// Feed a chunk of PCM audio (16 kHz mono f32).
    /// Returns an update indicating whether results changed.
    /// </summary>
    public StreamUpdateResult Feed(ReadOnlySpan<float> pcm)
    {
        ThrowIfDisposed();

        var updateSize = (int)NativeMethods.AbiStructSize(AbiStruct.AbiStreamUpdate);
        return FeedPinned(pcm, updateSize);
    }

    private unsafe StreamUpdateResult FeedPinned(ReadOnlySpan<float> pcm, int updateSize)
    {
        var nSamples = pcm.Length;
        fixed (float* pPcm = pcm)
        {
            var pcmPtr = (IntPtr)pPcm;
            return StackAllocHelper.RunWithBuffer(
                updateSize,
                updatePtr => FeedCore(pcmPtr, nSamples, updatePtr));
        }
    }

    private unsafe StreamUpdateResult FeedCore(IntPtr pPcm, int nSamples, IntPtr updatePtr)
    {
        NativeMethods.StreamUpdateInit(updatePtr);
        var status = NativeMethods.StreamFeed(session, pPcm, nSamples, updatePtr);
        if (status != Status.Ok)
        {
            throw new TranscribeException(status, nameof(NativeMethods.StreamFeed));
        }

        var u = Marshal.PtrToStructure<Interop.StreamUpdate>(updatePtr);
        return ToStreamUpdateResult(u);
    }

    /// <summary>
    /// Complete the stream. No more audio can be fed after this.
    /// This should be called before <see cref="Dispose"/> to ensure all 
    /// buffered audio is processed and final results are generated.
    /// </summary>
    /// <returns>Result indicating if the final transcription changed or is final.</returns>
    /// <exception cref="ObjectDisposedException">Thrown if called after Dispose.</exception>
    public StreamUpdateResult Complete()
    {
        ThrowIfDisposed();

        var updateSize = (int)NativeMethods.AbiStructSize(AbiStruct.AbiStreamUpdate);
        return StackAllocHelper.RunWithBuffer(updateSize, updatePtr =>
        {
            NativeMethods.StreamUpdateInit(updatePtr);

            var status = NativeMethods.StreamFinalize(session, updatePtr);
            if (status != Status.Ok)
            {
                throw new TranscribeException(status, nameof(NativeMethods.StreamFinalize));
            }

            var u = Marshal.PtrToStructure<Interop.StreamUpdate>(updatePtr);
            return ToStreamUpdateResult(u);
        });
    }

    private static StreamUpdateResult ToStreamUpdateResult(Interop.StreamUpdate u)
    {
        return new StreamUpdateResult(
            ResultChanged: u.resultChanged,
            IsFinal: u.isFinal,
            Revision: u.revision,
            InputReceived: TimeSpan.FromMilliseconds(u.inputReceivedMs),
            AudioCommitted: TimeSpan.FromMilliseconds(u.audioCommittedMs),
            Buffered: TimeSpan.FromMilliseconds(u.bufferedMs),
            CommittedChanged: u.committedChanged,
            TentativeChanged: u.tentativeChanged);
    }

    /// <summary>
    /// Reset the stream to start a new transcription (keeps the session).
    /// </summary>
    public void Reset()
    {
        ThrowIfDisposed();
        NativeMethods.StreamReset(session);
    }

    /// <summary>Read the current streaming text (full, committed, tentative).</summary>
    public StreamTextResult CurrentText
    {
        get
        {
            ThrowIfDisposed();

            var textSize = (int)NativeMethods.AbiStructSize(AbiStruct.AbiStreamText);
            return StackAllocHelper.RunWithBuffer(textSize, textPtr =>
            {
                NativeMethods.StreamTextInit(textPtr);

                var status = NativeMethods.StreamGetText(session, textPtr);
                if (status != Status.Ok)
                {
                    throw new TranscribeException(status, nameof(NativeMethods.StreamGetText));
                }

                var t = Marshal.PtrToStructure<Interop.StreamText>(textPtr);
                var fullText = t.fullText != IntPtr.Zero && t.fullTextBytes > 0
                    ? Marshal.PtrToStringUTF8(t.fullText, (int)t.fullTextBytes) ?? string.Empty
                    : string.Empty;
                var committedText = t.committedText != IntPtr.Zero && t.committedTextBytes > 0
                    ? Marshal.PtrToStringUTF8(t.committedText, (int)t.committedTextBytes) ?? string.Empty
                    : string.Empty;
                var tentativeText = t.tentativeText != IntPtr.Zero && t.tentativeTextBytes > 0
                    ? Marshal.PtrToStringUTF8(t.tentativeText, (int)t.tentativeTextBytes) ?? string.Empty
                    : string.Empty;

                return new StreamTextResult(fullText, committedText, tentativeText);
            });
        }
    }

    /// <summary>Current state of the streaming session.</summary>
    public StreamState State
    {
        get
        {
            ThrowIfDisposed();
            var result = NativeMethods.StreamGetState(session);
            GC.KeepAlive(this);
            return result;
        }
    }

    /// <summary>Number of committed segments.</summary>
    public int CommittedSegmentCount
    {
        get
        {
            ThrowIfDisposed();
            var count = NativeMethods.StreamNCommittedSegments(session);
            GC.KeepAlive(this);
            return count;
        }
    }

    /// <summary>Number of committed words.</summary>
    public int CommittedWordCount
    {
        get
        {
            ThrowIfDisposed();
            var count = NativeMethods.StreamNCommittedWords(session);
            GC.KeepAlive(this);
            return count;
        }
    }

    /// <summary>Number of committed tokens.</summary>
    public int CommittedTokenCount
    {
        get
        {
            ThrowIfDisposed();
            var count = NativeMethods.StreamNCommittedTokens(session);
            GC.KeepAlive(this);
            return count;
        }
    }

    /// <summary>Last status of the streaming session.</summary>
    public Status LastStatus
    {
        get
        {
            ThrowIfDisposed();
            var result = NativeMethods.StreamLastStatus(session);
            GC.KeepAlive(this);
            return result;
        }
    }

    /// <summary>Stream revision (incremented on each commit).</summary>
    public int Revision
    {
        get
        {
            ThrowIfDisposed();
            var rev = NativeMethods.StreamRevision(session);
            GC.KeepAlive(this);
            return rev;
        }
    }

    /// <summary>
    /// Disposes the streaming view. 
    /// If streaming was started via <see cref="Begin"/>, resets the native
    /// stream state. It is recommended to call <see cref="Complete"/> before
    /// Disposing to get the last transcription results.
    /// </summary>
    public void Dispose()
    {
        if (!disposed && !session.IsClosed)
        {
            NativeMethods.StreamReset(session);
        }

        disposed = true;
    }
}
