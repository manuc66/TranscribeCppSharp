#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using TranscribeCppSharp.Interop;

namespace TranscribeCppSharp;

/// <summary>
/// A transcription session bound to a model. Disposing this frees the native session.
/// Thread-safe: SafeHandle ensures the native handle is released correctly.
/// </summary>
public sealed class Session : IDisposable
{
    private SessionHandle _handle;

    private Session(SessionHandle handle) => _handle = handle;

    internal static Session Create(ModelHandle model, Action<SessionParamsBuilder>? configure = null)
    {
        using var sessionParams = new SessionParamsBuilder();
        configure?.Invoke(sessionParams);

        var outSession = Marshal.AllocHGlobal(IntPtr.Size);
        try
        {
            var status = NativeMethods.SessionInit(model, sessionParams.Build(), outSession);
            if (status != Status.Ok)
                throw new TranscribeException(status, nameof(NativeMethods.SessionInit));

            var handle = new SessionHandle(Marshal.ReadIntPtr(outSession));
            return new Session(handle);
        }
        finally
        {
            Marshal.FreeHGlobal(outSession);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_handle.IsInvalid)
            throw new ObjectDisposedException(nameof(Session));
    }

    /// <summary>Create a streaming session bound to this session handle.</summary>
    public StreamSession CreateStream()
    {
        ThrowIfDisposed();
        return new StreamSession(_handle);
    }

    /// <summary>
    /// Transcribe a PCM buffer (16 kHz mono f32, samples in [-1, 1]).
    /// Returns a Transcript with FullText and DetectedLanguage eagerly loaded.
    /// Call ReadSegments(), ReadWords(), ReadTokens() to get detailed results.
    /// </summary>
    public Transcript Run(ReadOnlySpan<float> pcm, Action<RunParamsBuilder>? configure = null)
    {
        RunNative(pcm, configure);
        return ReadResults();
    }

    /// <summary>
    /// Transcribe a PCM buffer with pre-allocated params (no closure allocation).
    /// </summary>
    public Transcript Run(IntPtr pcmPtr, int nSamples, RunParamsBuilder runParams)
    {
        ThrowIfDisposed();
        var status = NativeMethods.Run(_handle, pcmPtr, nSamples, runParams.Build());
        if (status != Status.Ok)
            throw new TranscribeException(status, nameof(Run));

        return ReadResults();
    }

    /// <summary>Get the full transcription text after a run.</summary>
    public string FullText
    {
        get
        {
            ThrowIfDisposed();
            var ptr = NativeMethods.FullText(_handle);
            return ptr == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(ptr) ?? "";
        }
    }

    /// <summary>Number of segments in the last result.</summary>
    public int SegmentCount
    {
        get
        {
            ThrowIfDisposed();
            return NativeMethods.NSegments(_handle);
        }
    }

    /// <summary>Number of words in the last result.</summary>
    public int WordCount
    {
        get
        {
            ThrowIfDisposed();
            return NativeMethods.NWords(_handle);
        }
    }

    /// <summary>Number of tokens in the last result.</summary>
    public int TokenCount
    {
        get
        {
            ThrowIfDisposed();
            return NativeMethods.NTokens(_handle);
        }
    }

    /// <summary>Check if the session was aborted.</summary>
    public bool WasAborted
    {
        get
        {
            ThrowIfDisposed();
            return NativeMethods.WasAborted(_handle);
        }
    }

    /// <summary>Check if the output was truncated due to buffer limits.</summary>
    public bool WasTruncated
    {
        get
        {
            ThrowIfDisposed();
            return NativeMethods.WasTruncated(_handle);
        }
    }

    /// <summary>Get the kind of timestamps returned by the session.</summary>
    public TimestampKind ReturnedTimestampKind => NativeMethods.ReturnedTimestampKind(_handle);

    /// <summary>Get resource limits for this session.</summary>
    public SessionLimits GetLimits()
    {
        ThrowIfDisposed();
        var size = (int)NativeMethods.AbiStructSize(AbiStruct.AbiSessionLimits);
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            NativeMethods.SessionLimitsInit(ptr);
            var status = NativeMethods.SessionGetLimits(_handle, ptr);
            if (status != Status.Ok)
                throw new TranscribeException(status, nameof(NativeMethods.SessionGetLimits));

            return Marshal.PtrToStructure<SessionLimits>(ptr);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    /// <summary>Reset timing statistics for this session.</summary>
    public void ResetTimings()
    {
        ThrowIfDisposed();
        NativeMethods.ResetTimings(_handle);
    }

    /// <summary>Read segments from the last run result.</summary>
    public unsafe IReadOnlyList<SegmentResult> ReadSegments()
    {
        ThrowIfDisposed();

        var segments = new List<SegmentResult>();
        var segCount = NativeMethods.NSegments(_handle);
        if (segCount == 0) return segments;

        var segSize = (int)NativeMethods.AbiStructSize(AbiStruct.AbiSegment);
        StackAllocHelper.ThrowIfTooLarge(segSize, nameof(Segment));
        Span<byte> segBuffer = stackalloc byte[segSize];
        fixed (byte* pSegBuffer = segBuffer)
        {
            var segPtr = (IntPtr)pSegBuffer;
            for (int i = 0; i < segCount; i++)
            {
                NativeMethods.SegmentInit(segPtr);
                var status = NativeMethods.GetSegment(_handle, i, segPtr);
                if (status != Status.Ok) continue;

                var seg = Marshal.PtrToStructure<Interop.Segment>(segPtr);
                var text = Marshal.PtrToStringUTF8(seg.text) ?? "";
                segments.Add(new SegmentResult(
                    Start: TimeSpan.FromMilliseconds(seg.t0Ms),
                    End: TimeSpan.FromMilliseconds(seg.t1Ms),
                    Text: text
                ));
            }
        }

        return segments;
    }

    /// <summary>Read words from the last run result.</summary>
    public unsafe IReadOnlyList<WordResult> ReadWords()
    {
        ThrowIfDisposed();

        var words = new List<WordResult>();
        var wordCount = NativeMethods.NWords(_handle);
        if (wordCount == 0) return words;

        var wordSize = (int)NativeMethods.AbiStructSize(AbiStruct.AbiWord);
        StackAllocHelper.ThrowIfTooLarge(wordSize, nameof(Word));
        Span<byte> wordBuffer = stackalloc byte[wordSize];
        fixed (byte* pWordBuffer = wordBuffer)
        {
            var wordPtr = (IntPtr)pWordBuffer;
            for (int i = 0; i < wordCount; i++)
            {
                NativeMethods.WordInit(wordPtr);
                var status = NativeMethods.GetWord(_handle, i, wordPtr);
                if (status != Status.Ok) continue;

                var w = Marshal.PtrToStructure<Interop.Word>(wordPtr);
                var text = Marshal.PtrToStringUTF8(w.text) ?? "";
                words.Add(new WordResult(
                    Start: TimeSpan.FromMilliseconds(w.t0Ms),
                    End: TimeSpan.FromMilliseconds(w.t1Ms),
                    Text: text
                ));
            }
        }

        return words;
    }

    /// <summary>Read tokens from the last run result.</summary>
    public unsafe IReadOnlyList<TokenResult> ReadTokens()
    {
        ThrowIfDisposed();

        var tokens = new List<TokenResult>();
        var tokenCount = NativeMethods.NTokens(_handle);
        if (tokenCount == 0) return tokens;

        var tokenSize = (int)NativeMethods.AbiStructSize(AbiStruct.AbiToken);
        StackAllocHelper.ThrowIfTooLarge(tokenSize, nameof(Token));
        Span<byte> tokenBuffer = stackalloc byte[tokenSize];
        fixed (byte* pTokenBuffer = tokenBuffer)
        {
            var tokenPtr = (IntPtr)pTokenBuffer;
            for (int i = 0; i < tokenCount; i++)
            {
                NativeMethods.TokenInit(tokenPtr);
                var status = NativeMethods.GetToken(_handle, i, tokenPtr);
                if (status != Status.Ok) continue;

                var t = Marshal.PtrToStructure<Interop.Token>(tokenPtr);
                var text = Marshal.PtrToStringUTF8(t.text) ?? "";
                tokens.Add(new TokenResult(
                    Id: t.id,
                    Probability: t.p,
                    Start: TimeSpan.FromMilliseconds(t.t0Ms),
                    End: TimeSpan.FromMilliseconds(t.t1Ms),
                    Text: text
                ));
            }
        }

        return tokens;
    }

    /// <summary>Print timing information to the log.</summary>
    public void PrintTimings()
    {
        ThrowIfDisposed();
        NativeMethods.PrintTimings(_handle);
    }

    /// <summary>
    /// Set a cancellation callback. Return true from the callback to abort transcription.
    /// The callback is invoked periodically during long-running operations.
    /// </summary>
    public void SetAbortCallback(Interop.AbortCallback callback)
    {
        ThrowIfDisposed();
        _abortCallback = callback;
        NativeMethods.SetAbortCallback(_handle, callback, IntPtr.Zero);
    }

    /// <summary>
    /// Clear the cancellation callback.
    /// </summary>
    public void ClearAbortCallback()
    {
        ThrowIfDisposed();
        // Store a no-op callback to keep the delegate rooted
        _abortCallback = _ => false;
        NativeMethods.SetAbortCallback(_handle, _abortCallback, IntPtr.Zero);
    }


    private Interop.AbortCallback? _abortCallback;

    /// <summary>
    /// Internal method for batch transcription with proper thread-safety.
    /// </summary>
    internal unsafe Status RunBatchInternal(IntPtr pcmPtrArray, IntPtr sampleCountArray, int n, IntPtr runParams)
    {
        ThrowIfDisposed();
        return NativeMethods.RunBatch(_handle, pcmPtrArray, sampleCountArray, n, runParams);
    }

    /// <summary>
    /// Get the number of batch results.
    /// </summary>
    internal int GetBatchResultCount()
    {
        ThrowIfDisposed();
        return NativeMethods.BatchNResults(_handle);
    }

    /// <summary>
    /// Get the status of a batch result.
    /// </summary>
    internal Status GetBatchResultStatus(int index)
    {
        ThrowIfDisposed();
        return NativeMethods.BatchStatus(_handle, index);
    }

    /// <summary>
    /// Get the full text of a batch result.
    /// </summary>
    internal string GetBatchResultFullText(int index)
    {
        ThrowIfDisposed();
        var ptr = NativeMethods.BatchFullText(_handle, index);
        return ptr == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(ptr) ?? "";
    }

    /// <summary>
    /// Get the detected language of a batch result.
    /// </summary>
    internal string GetBatchResultDetectedLanguage(int index)
    {
        ThrowIfDisposed();
        var ptr = NativeMethods.BatchDetectedLanguage(_handle, index);
        return ptr == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(ptr) ?? "";
    }

    internal SessionHandle Handle => _handle;

    private unsafe void RunNative(ReadOnlySpan<float> pcm, Action<RunParamsBuilder>? configure)
    {
        ThrowIfDisposed();

        using var runParams = new RunParamsBuilder();
        configure?.Invoke(runParams);

        fixed (float* pPcm = pcm)
        {
            var status = NativeMethods.Run(_handle, (IntPtr)pPcm, pcm.Length, runParams.Build());
            if (status != Status.Ok)
                throw new TranscribeException(status, nameof(Run));
        }
    }

    private Transcript ReadResults()
    {
        var timings = ReadTimings();
        var langPtr = NativeMethods.DetectedLanguage(_handle);
        var lang = langPtr == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(langPtr) ?? "";

        var segments = ReadSegments();
        var words = WordCount > 0 ? ReadWords() : Array.Empty<WordResult>();
        var tokens = NativeMethods.NTokens(_handle) > 0 ? ReadTokens() : Array.Empty<TokenResult>();

        return new Transcript
        {
            FullText = FullText,
            DetectedLanguage = lang,
            Timing = timings,
            Segments = segments,
            Words = words,
            Tokens = tokens,
        };
    }

    private unsafe TimingsResult? ReadTimings()
    {
        var timingsSize = (int)NativeMethods.AbiStructSize(AbiStruct.AbiTimings);
        StackAllocHelper.ThrowIfTooLarge(timingsSize, nameof(Timings));
        Span<byte> timingsBuffer = stackalloc byte[timingsSize];
        fixed (byte* pTimingsBuffer = timingsBuffer)
        {
            var timingsPtr = (IntPtr)pTimingsBuffer;
            NativeMethods.TimingsInit(timingsPtr);
            if (NativeMethods.GetTimings(_handle, timingsPtr) == Status.Ok)
            {
                var t = Marshal.PtrToStructure<Interop.Timings>(timingsPtr);
                return new TimingsResult(t.loadMs, t.melMs, t.encodeMs, t.decodeMs);
            }
        }
        return null;
    }

    public void Dispose()
    {
        _handle.Dispose();
    }
}
