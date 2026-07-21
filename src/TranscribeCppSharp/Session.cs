#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using TranscribeCppSharp.Interop;

namespace TranscribeCppSharp;

/// <summary>
/// A transcription session bound to a model. Disposing this frees the native session.
/// Thread-safe: Dispose waits for all in-flight operations to complete before freeing the handle.
/// </summary>
public sealed class Session : IDisposable
{
    private SessionHandle _handle;
    private int _useCount;
    private int _disposed; // 0 = alive, 1 = disposed

    private Session(SessionHandle handle) => _handle = handle;

    internal static Session Create(ModelHandle model, Action<SessionParamsBuilder>? configure = null)
    {
        using var sessionParams = new SessionParamsBuilder();
        configure?.Invoke(sessionParams);

        var outSession = Marshal.AllocHGlobal(IntPtr.Size);
        try
        {
            var status = NativeMethods.SessionInit(model, sessionParams.Handle, outSession);
            if (status != Status.Ok)
                throw new TranscribeException(status, nameof(NativeMethods.SessionInit));

            var handle = Marshal.PtrToStructure<SessionHandle>(outSession);
            return new Session(handle);
        }
        finally
        {
            Marshal.FreeHGlobal(outSession);
        }
    }

    private void BeginUse()
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(Session));

        Interlocked.Increment(ref _useCount);

        // Re-check after increment: dispose may have happened between our check and increment
        if (Volatile.Read(ref _disposed) != 0)
        {
            Interlocked.Decrement(ref _useCount);
            throw new ObjectDisposedException(nameof(Session));
        }
    }

    private void EndUse()
    {
        Interlocked.Decrement(ref _useCount);
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
        BeginUse();
        try
        {
            var status = NativeMethods.Run(_handle, pcmPtr, nSamples, runParams.Handle);
            if (status != Status.Ok)
                throw new TranscribeException(status, nameof(Run));

            return ReadResults();
        }
        finally
        {
            EndUse();
        }
    }

    /// <summary>Get the full transcription text after a run.</summary>
    public string FullText
    {
        get
        {
            BeginUse();
            try
            {
                var ptr = NativeMethods.FullText(_handle);
                return ptr == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(ptr) ?? "";
            }
            finally
            {
                EndUse();
            }
        }
    }

    /// <summary>Number of segments in the last result.</summary>
    public int SegmentCount
    {
        get
        {
            BeginUse();
            try
            {
                return NativeMethods.NSegments(_handle);
            }
            finally
            {
                EndUse();
            }
        }
    }

    /// <summary>Number of words in the last result.</summary>
    public int WordCount
    {
        get
        {
            BeginUse();
            try
            {
                return NativeMethods.NWords(_handle);
            }
            finally
            {
                EndUse();
            }
        }
    }

    /// <summary>Read segments from the last run result.</summary>
    public unsafe IReadOnlyList<SegmentResult> ReadSegments()
    {
        BeginUse();
        try
        {
            var segments = new List<SegmentResult>();
            var segCount = NativeMethods.NSegments(_handle);
            if (segCount == 0) return segments;

            var segSize = (int)NativeMethods.AbiStructSize(AbiStruct.AbiSegment);
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
        finally
        {
            EndUse();
        }
    }

    /// <summary>Read words from the last run result.</summary>
    public unsafe IReadOnlyList<WordResult> ReadWords()
    {
        BeginUse();
        try
        {
            var words = new List<WordResult>();
            var wordCount = NativeMethods.NWords(_handle);
            if (wordCount == 0) return words;

            var wordSize = (int)NativeMethods.AbiStructSize(AbiStruct.AbiWord);
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
        finally
        {
            EndUse();
        }
    }

    /// <summary>Read tokens from the last run result.</summary>
    public unsafe IReadOnlyList<TokenResult> ReadTokens()
    {
        BeginUse();
        try
        {
            var tokens = new List<TokenResult>();
            var tokenCount = NativeMethods.NTokens(_handle);
            if (tokenCount == 0) return tokens;

            var tokenSize = (int)NativeMethods.AbiStructSize(AbiStruct.AbiToken);
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
        finally
        {
            EndUse();
        }
    }

    /// <summary>Print timing information to the log.</summary>
    public void PrintTimings()
    {
        BeginUse();
        try
        {
            NativeMethods.PrintTimings(_handle);
        }
        finally
        {
            EndUse();
        }
    }

    internal SessionHandle Handle => _handle;

    private unsafe void RunNative(ReadOnlySpan<float> pcm, Action<RunParamsBuilder>? configure)
    {
        BeginUse();
        try
        {
            using var runParams = new RunParamsBuilder();
            configure?.Invoke(runParams);

            fixed (float* pPcm = pcm)
            {
                var status = NativeMethods.Run(_handle, (IntPtr)pPcm, pcm.Length, runParams.Handle);
                if (status != Status.Ok)
                    throw new TranscribeException(status, nameof(Run));
            }
        }
        finally
        {
            EndUse();
        }
    }

    private Transcript ReadResults()
    {
        var timings = ReadTimings();
        var langPtr = NativeMethods.DetectedLanguage(_handle);
        var lang = langPtr == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(langPtr) ?? "";

        return new Transcript
        {
            FullText = FullText,
            DetectedLanguage = lang,
            Timing = timings,
        };
    }

    private unsafe TimingsResult? ReadTimings()
    {
        var timingsSize = (int)NativeMethods.AbiStructSize(AbiStruct.AbiTimings);
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

    ~Session()
    {
        Volatile.Write(ref _disposed, 1);
        NativeMethods.SessionFree(_handle);
        _handle = SessionHandle.Null;
    }

    public void Dispose()
    {
        Volatile.Write(ref _disposed, 1);

        // Spin-wait for all in-flight operations to complete
        var sw = new SpinWait();
        while (Volatile.Read(ref _useCount) > 0)
            sw.SpinOnce();

        NativeMethods.SessionFree(_handle);
        _handle = SessionHandle.Null;
        GC.SuppressFinalize(this);
    }
}
