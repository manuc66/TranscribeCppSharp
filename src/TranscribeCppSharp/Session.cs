#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using TranscribeCppSharp.Interop;

namespace TranscribeCppSharp;

/// <summary>
/// A transcription session bound to a model. Disposing this frees the native session.
/// Not thread-safe: use multiple sessions or synchronize access for concurrency.
/// </summary>
/// <remarks>
/// The session keeps its parent <see cref="Model"/> alive for the session's lifetime.
/// Do not dispose the model before all sessions created from it.
/// </remarks>
public sealed class Session : IDisposable
{
    private readonly Model model;
    private readonly SessionHandle handle;

    private Session(SessionHandle handle, Model model)
    {
        this.handle = handle;
        this.model = model;
    }

    internal static Session Create(ModelHandle modelHandle, Model model, Action<SessionParamsBuilder>? configure = null)
    {
        using var sessionParams = new SessionParamsBuilder();
        configure?.Invoke(sessionParams);

        var outSession = Marshal.AllocHGlobal(IntPtr.Size);
        try
        {
            var status = NativeMethods.SessionInit(modelHandle, sessionParams.Build(), outSession);
            if (status != Status.Ok)
            {
                throw new TranscribeException(status, nameof(NativeMethods.SessionInit));
            }

            var handle = new SessionHandle(Marshal.ReadIntPtr(outSession));
            return new Session(handle, model);
        }
        finally
        {
            Marshal.FreeHGlobal(outSession);
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }

    private bool disposed;

    /// <summary>Create a streaming session bound to this session handle.</summary>
    public StreamSession CreateStream()
    {
        ThrowIfDisposed();
        return new StreamSession(handle, this);
    }

    /// <summary>
    /// Transcribe a PCM buffer (16 kHz mono f32, samples in [-1, 1]).
    /// Returns a Transcript with FullText and DetectedLanguage eagerly loaded.
    /// Call ReadSegments(), ReadWords(), ReadTokens() to get detailed results.
    /// Supports cancellation via <paramref name="ct"/>.
    /// </summary>
    public Transcript Run(ReadOnlySpan<float> pcm, Action<RunParamsBuilder>? configure = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (ct.CanBeCanceled)
        {
            var previousCallback = abortCallback;
            SetAbortCallback(() => ct.IsCancellationRequested);
            try
            {
                RunNative(pcm, configure);
            }
            catch (TranscribeException ex) when (ex.StatusCode == Status.ErrAborted)
            {
                throw new OperationCanceledException(ct);
            }
            finally
            {
                if (previousCallback != null)
                {
                    SetAbortCallback(previousCallback);
                }
                else
                {
                    ClearAbortCallback();
                }
            }
        }
        else
        {
            RunNative(pcm, configure);
        }

        return ReadResults();
    }

    /// <summary>Get the full transcription text after a run.</summary>
    public string FullText
    {
        get
        {
            ThrowIfDisposed();
            var ptr = NativeMethods.FullText(handle);
            var result = ptr == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUTF8(ptr) ?? string.Empty;
            GC.KeepAlive(this);
            return result;
        }
    }

    /// <summary>Number of segments in the last result.</summary>
    public int SegmentCount
    {
        get
        {
            ThrowIfDisposed();
            var count = NativeMethods.NSegments(handle);
            GC.KeepAlive(this);
            return count;
        }
    }

    /// <summary>Number of words in the last result.</summary>
    public int WordCount
    {
        get
        {
            ThrowIfDisposed();
            var count = NativeMethods.NWords(handle);
            GC.KeepAlive(this);
            return count;
        }
    }

    /// <summary>Number of tokens in the last result.</summary>
    public int TokenCount
    {
        get
        {
            ThrowIfDisposed();
            var count = NativeMethods.NTokens(handle);
            GC.KeepAlive(this);
            return count;
        }
    }

    /// <summary>Check if the session was aborted.</summary>
    public bool WasAborted
    {
        get
        {
            ThrowIfDisposed();
            var result = NativeMethods.WasAborted(handle);
            GC.KeepAlive(this);
            return result;
        }
    }

    /// <summary>Check if the output was truncated due to buffer limits.</summary>
    public bool WasTruncated
    {
        get
        {
            ThrowIfDisposed();
            var result = NativeMethods.WasTruncated(handle);
            GC.KeepAlive(this);
            return result;
        }
    }

    /// <summary>Get the kind of timestamps returned by the session.</summary>
    public TimestampKind ReturnedTimestampKind
    {
        get
        {
            ThrowIfDisposed();
            var result = NativeMethods.ReturnedTimestampKind(handle);
            GC.KeepAlive(this);
            return result;
        }
    }

    /// <summary>Resource limits for this session.</summary>
    public record SessionLimitsInfo(int EffectiveNCtx, long EffectiveMaxAudioMs, long MaxKvBytes);

    /// <summary>Get resource limits for this session.</summary>
    public SessionLimitsInfo GetLimits()
    {
        ThrowIfDisposed();
        var size = (int)NativeMethods.AbiStructSize(AbiStruct.AbiSessionLimits);
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            NativeMethods.SessionLimitsInit(ptr);
            var status = NativeMethods.SessionGetLimits(handle, ptr);
            if (status != Status.Ok)
            {
                throw new TranscribeException(status, nameof(NativeMethods.SessionGetLimits));
            }

            var limits = Marshal.PtrToStructure<Interop.SessionLimits>(ptr);
            return new SessionLimitsInfo(
                EffectiveNCtx: limits.effectiveNCtx,
                EffectiveMaxAudioMs: limits.effectiveMaxAudioMs,
                MaxKvBytes: limits.maxKvBytes);
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
        NativeMethods.ResetTimings(handle);
    }

    /// <summary>Read segments from the last run result.</summary>
    public IReadOnlyList<SegmentResult> ReadSegments()
    {
        ThrowIfDisposed();
        return ReadItems<Interop.Segment, SegmentResult>(
            count: NativeMethods.NSegments(handle),
            abi: AbiStruct.AbiSegment,
            init: NativeMethods.SegmentInit,
            get: (i, ptr) => NativeMethods.GetSegment(handle, i, ptr),
            getMethodName: nameof(NativeMethods.GetSegment),
            map: static seg => new SegmentResult(
                Start: TimeSpan.FromMilliseconds(seg.t0Ms),
                End: TimeSpan.FromMilliseconds(seg.t1Ms),
                Text: Marshal.PtrToStringUTF8(seg.text) ?? string.Empty));
    }

    /// <summary>Read words from the last run result.</summary>
    public IReadOnlyList<WordResult> ReadWords()
    {
        ThrowIfDisposed();
        return ReadItems<Interop.Word, WordResult>(
            count: NativeMethods.NWords(handle),
            abi: AbiStruct.AbiWord,
            init: NativeMethods.WordInit,
            get: (i, ptr) => NativeMethods.GetWord(handle, i, ptr),
            getMethodName: nameof(NativeMethods.GetWord),
            map: static w => new WordResult(
                Start: TimeSpan.FromMilliseconds(w.t0Ms),
                End: TimeSpan.FromMilliseconds(w.t1Ms),
                Text: Marshal.PtrToStringUTF8(w.text) ?? string.Empty));
    }

    /// <summary>Read tokens from the last run result.</summary>
    public IReadOnlyList<TokenResult> ReadTokens()
    {
        ThrowIfDisposed();
        return ReadItems<Interop.Token, TokenResult>(
            count: NativeMethods.NTokens(handle),
            abi: AbiStruct.AbiToken,
            init: NativeMethods.TokenInit,
            get: (i, ptr) => NativeMethods.GetToken(handle, i, ptr),
            getMethodName: nameof(NativeMethods.GetToken),
            map: static t => new TokenResult(
                Id: t.id,
                Probability: t.p,
                Start: TimeSpan.FromMilliseconds(t.t0Ms),
                End: TimeSpan.FromMilliseconds(t.t1Ms),
                Text: Marshal.PtrToStringUTF8(t.text) ?? string.Empty));
    }

    /// <summary>Print timing information to the log.</summary>
    public void PrintTimings()
    {
        ThrowIfDisposed();
        NativeMethods.PrintTimings(handle);
    }

    /// <summary>
    /// Set a cancellation callback. Return true from the callback to abort transcription.
    /// The callback is invoked periodically during long-running operations.
    /// </summary>
    public void SetAbortCallback(Func<bool> abortCallback)
    {
        ThrowIfDisposed();
        this.abortCallback = abortCallback ?? throw new ArgumentNullException(nameof(abortCallback));
        interopAbortCallback = _ => this.abortCallback();
        NativeMethods.SetAbortCallback(handle, interopAbortCallback, IntPtr.Zero);
    }

    /// <summary>
    /// Clear the cancellation callback.
    /// </summary>
    public void ClearAbortCallback()
    {
        ThrowIfDisposed();

        // Set a no-op callback to disable abort checks while keeping
        // the delegate rooted to prevent GC.
        abortCallback = null;
        interopAbortCallback = _ => false;
        NativeMethods.SetAbortCallback(handle, interopAbortCallback, IntPtr.Zero);
    }

    /// <summary>
    /// Get the current abort callback, if any.
    /// </summary>
    internal Func<bool>? GetAbortCallback()
    {
        return abortCallback;
    }

    private Func<bool>? abortCallback;
    private Interop.AbortCallback? interopAbortCallback;

    /// <summary>
    /// Internal method for batch transcription with proper thread-safety.
    /// </summary>
    internal Status RunBatchInternal(IntPtr pcmPtrArray, IntPtr sampleCountArray, int n, IntPtr runParams)
    {
        ThrowIfDisposed();
        return NativeMethods.RunBatch(handle, pcmPtrArray, sampleCountArray, n, runParams);
    }

    /// <summary>
    /// Get the number of batch results.
    /// </summary>
    internal int GetBatchResultCount()
    {
        ThrowIfDisposed();
        return NativeMethods.BatchNResults(handle);
    }

    /// <summary>
    /// Get the status of a batch result.
    /// </summary>
    internal Status GetBatchResultStatus(int index)
    {
        ThrowIfDisposed();
        return NativeMethods.BatchStatus(handle, index);
    }

    /// <summary>
    /// Get the full text of a batch result.
    /// </summary>
    internal string GetBatchResultFullText(int index)
    {
        ThrowIfDisposed();
        var ptr = NativeMethods.BatchFullText(handle, index);
        return ptr == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUTF8(ptr) ?? string.Empty;
    }

    /// <summary>
    /// Get the detected language of a batch result.
    /// </summary>
    internal string GetBatchResultDetectedLanguage(int index)
    {
        ThrowIfDisposed();
        var ptr = NativeMethods.BatchDetectedLanguage(handle, index);
        return ptr == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUTF8(ptr) ?? string.Empty;
    }

    internal IReadOnlyList<SegmentResult> GetBatchSegments(int batchIndex)
    {
        ThrowIfDisposed();
        return ReadItems<Interop.Segment, SegmentResult>(
            count: NativeMethods.BatchNSegments(handle, batchIndex),
            abi: AbiStruct.AbiSegment,
            init: NativeMethods.SegmentInit,
            get: (j, ptr) => NativeMethods.BatchGetSegment(handle, batchIndex, j, ptr),
            getMethodName: nameof(NativeMethods.BatchGetSegment),
            map: static seg => new SegmentResult(
                Start: TimeSpan.FromMilliseconds(seg.t0Ms),
                End: TimeSpan.FromMilliseconds(seg.t1Ms),
                Text: Marshal.PtrToStringUTF8(seg.text) ?? string.Empty));
    }

    internal IReadOnlyList<WordResult> GetBatchWords(int batchIndex)
    {
        ThrowIfDisposed();
        return ReadItems<Interop.Word, WordResult>(
            count: NativeMethods.BatchNWords(handle, batchIndex),
            abi: AbiStruct.AbiWord,
            init: NativeMethods.WordInit,
            get: (j, ptr) => NativeMethods.BatchGetWord(handle, batchIndex, j, ptr),
            getMethodName: nameof(NativeMethods.BatchGetWord),
            map: static w => new WordResult(
                Start: TimeSpan.FromMilliseconds(w.t0Ms),
                End: TimeSpan.FromMilliseconds(w.t1Ms),
                Text: Marshal.PtrToStringUTF8(w.text) ?? string.Empty));
    }

    internal IReadOnlyList<TokenResult> GetBatchTokens(int batchIndex)
    {
        ThrowIfDisposed();
        return ReadItems<Interop.Token, TokenResult>(
            count: NativeMethods.BatchNTokens(handle, batchIndex),
            abi: AbiStruct.AbiToken,
            init: NativeMethods.TokenInit,
            get: (j, ptr) => NativeMethods.BatchGetToken(handle, batchIndex, j, ptr),
            getMethodName: nameof(NativeMethods.BatchGetToken),
            map: static t => new TokenResult(
                Id: t.id,
                Probability: t.p,
                Start: TimeSpan.FromMilliseconds(t.t0Ms),
                End: TimeSpan.FromMilliseconds(t.t1Ms),
                Text: Marshal.PtrToStringUTF8(t.text) ?? string.Empty));
    }

    internal SessionHandle Handle => handle;

    internal TimingsResult? GetBatchTimings(int batchIndex)
    {
        ThrowIfDisposed();
        var timingsSize = (int)NativeMethods.AbiStructSize(AbiStruct.AbiTimings);
        TimingsResult? result = null;
        StackAllocHelper.RunWithBuffer(timingsSize, timingsPtr =>
        {
            NativeMethods.TimingsInit(timingsPtr);
            if (NativeMethods.BatchGetTimings(handle, batchIndex, timingsPtr) == Status.Ok)
            {
                var t = Marshal.PtrToStructure<Interop.Timings>(timingsPtr);
                result = new TimingsResult(t.loadMs, t.melMs, t.encodeMs, t.decodeMs);
            }
        });
        return result;
    }

    private void RunNative(ReadOnlySpan<float> pcm, Action<RunParamsBuilder>? configure)
    {
        ThrowIfDisposed();

        using var runParams = new RunParamsBuilder();
        configure?.Invoke(runParams);

        var status = NativeMethods.Run(handle, pcm, pcm.Length, runParams.Build());
        if (status != Status.Ok)
        {
            throw new TranscribeException(status, nameof(Run));
        }
    }

    private Transcript ReadResults()
    {
        var timings = ReadTimings();
        var langPtr = NativeMethods.DetectedLanguage(handle);
        var lang = langPtr == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUTF8(langPtr) ?? string.Empty;

        var segments = ReadSegments();
        var words = ReadWords();
        var tokens = ReadTokens();

        return new Transcript
        {
            FullText = FullText,
            DetectedLanguage = lang,
            WasAborted = WasAborted,
            WasTruncated = WasTruncated,
            Timing = timings,
            Segments = segments,
            Words = words,
            Tokens = tokens,
        };
    }

    private TimingsResult? ReadTimings()
    {
        var timingsSize = (int)NativeMethods.AbiStructSize(AbiStruct.AbiTimings);
        TimingsResult? result = null;
        StackAllocHelper.RunWithBuffer(timingsSize, timingsPtr =>
        {
            NativeMethods.TimingsInit(timingsPtr);
            if (NativeMethods.GetTimings(handle, timingsPtr) == Status.Ok)
            {
                var t = Marshal.PtrToStructure<Interop.Timings>(timingsPtr);
                result = new TimingsResult(t.loadMs, t.melMs, t.encodeMs, t.decodeMs);
            }
        });
        return result;
    }

    /// <summary>
    /// Fetch <paramref name="count"/> items of native type <typeparamref name="TNative"/>
    /// into a fresh buffer (one init + fetch per index) and map each to the managed
    /// result type <typeparamref name="TResult"/>. Shared by the session and batch
    /// readers to avoid six copies of the same loop.
    /// </summary>
    private static List<TResult> ReadItems<TNative, TResult>(
        int count,
        AbiStruct abi,
        Action<IntPtr> init,
        Func<int, IntPtr, Status> get,
        string getMethodName,
        Func<TNative, TResult> map)
        where TNative : struct
    {
        var results = new List<TResult>(count);
        if (count == 0)
        {
            return results;
        }

        var size = (int)NativeMethods.AbiStructSize(abi);
        StackAllocHelper.RunWithBuffer(size, ptr =>
        {
            for (int i = 0; i < count; i++)
            {
                init(ptr);
                var status = get(i, ptr);
                if (status != Status.Ok)
                {
                    throw new TranscribeException(status, getMethodName);
                }

                results.Add(map(Marshal.PtrToStructure<TNative>(ptr)!));
            }
        });
        return results;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!disposed)
        {
            handle.Dispose();
            disposed = true;
        }
    }
}
