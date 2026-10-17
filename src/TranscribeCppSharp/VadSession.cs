namespace TranscribeCppSharp;

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using TranscribeCppSharp.Interop;

/// <summary>
/// Voice activity detection, for a model that serves
/// <see cref="TranscribeRole.Vad"/> (e.g. Silero VAD v6.2).
/// </summary>
/// <remarks>
/// Introduced by upstream transcribe.cpp 0.4. Audio must be 16 kHz mono f32.
/// Not thread-safe. The session keeps its parent <see cref="Model"/> alive.
/// </remarks>
public sealed class VadSession : IDisposable
{
    // Unread by design: roots the parent Model for the session's lifetime.
    // NOSONAR S4487
    private readonly Model model;
    private readonly IntPtr handle;
    private readonly AbortSource abort;
    private bool disposed;

    private VadSession(IntPtr handle, Model model)
    {
        this.handle = handle;
        this.model = model;
        abort = new AbortSource((cb, ud) => NativeMethods.VadSetAbortCallback(handle, cb, ud));
    }

    /// <summary>Facts about a loaded voice-activity-detection model.</summary>
    /// <param name="SampleRate">Expected input sample rate (Hz).</param>
    /// <param name="FrameSamples">Frame size the model scores, in samples.</param>
    public record VadInfo(int SampleRate, int FrameSamples);

    /// <summary>A speech segment as sample indices <c>[StartSample, EndSample)</c>.</summary>
    public record VadSegmentResult(long StartSample, long EndSample);

    /// <summary>
    /// The result of a VAD call: the speech probability per frame, the index of
    /// the first returned frame, and (after an offline run) the speech segments.
    /// </summary>
    public record VadResult(IReadOnlyList<float> Probabilities, long FirstFrame, IReadOnlyList<VadSegmentResult> Segments);

    internal static VadSession Create(ModelHandle modelHandle, Model model, Action<VadSessionParamsBuilder>? configure)
    {
        using var sessionParams = new VadSessionParamsBuilder();
        configure?.Invoke(sessionParams);

        var outSession = Marshal.AllocHGlobal(IntPtr.Size);
        try
        {
            var status = NativeMethods.VadSessionInit(modelHandle, sessionParams.Build(), outSession);
            if (status != Status.Ok)
            {
                throw new TranscribeException(status, nameof(NativeMethods.VadSessionInit));
            }

            return new VadSession(Marshal.ReadIntPtr(outSession), model);
        }
        finally
        {
            Marshal.FreeHGlobal(outSession);
        }
    }

    /// <summary>
    /// Score a whole PCM buffer (16 kHz mono f32) and return the per-frame
    /// probabilities and the speech segments. Supports cancellation via
    /// <paramref name="ct"/>.
    /// </summary>
    public VadResult Run(
        ReadOnlySpan<float> pcm,
        Action<VadParamsBuilder>? configure = null,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ct.ThrowIfCancellationRequested();

        using var runParams = new VadParamsBuilder();
        configure?.Invoke(runParams);

        if (ct.CanBeCanceled)
        {
            abort.UseToken(ct);
            try
            {
                RunNative(pcm, runParams);
            }
            catch (TranscribeException ex) when (ex.StatusCode == Status.ErrAborted)
            {
                throw new OperationCanceledException(ct);
            }
            finally
            {
                abort.Clear();
            }
        }
        else
        {
            RunNative(pcm, runParams);
        }

        return ReadResult();
    }

    /// <summary>
    /// Score one streaming block. Probabilities become readable via
    /// <see cref="ReadResult"/>; segments are not produced by a feed.
    /// </summary>
    public void Feed(ReadOnlySpan<float> pcm)
        => Check(NativeMethods.VadStreamFeed(handle, pcm, pcm.Length), nameof(NativeMethods.VadStreamFeed));

    /// <summary>Flush the streaming buffer; the remaining probabilities become readable.</summary>
    public void Flush() => Check(NativeMethods.VadStreamFlush(handle), nameof(NativeMethods.VadStreamFlush));

    /// <summary>Reset the streaming state.</summary>
    public void Reset()
    {
        ThrowIfDisposed();
        NativeMethods.VadStreamReset(handle);
    }

    /// <summary>Read the result of the last run, feed or flush.</summary>
    public VadResult ReadResult()
    {
        ThrowIfDisposed();
        var size = (int)NativeMethods.AbiStructSize(AbiStruct.AbiVadResult);
        var probabilities = new List<float>();
        var segments = new List<VadSegmentResult>();
        long firstFrame = 0;

        StackAllocHelper.RunWithBuffer(size, ptr =>
        {
            NativeMethods.VadResultInit(ptr);
            var status = NativeMethods.VadGetResult(handle, ptr);
            if (status != Status.Ok)
            {
                throw new TranscribeException(status, nameof(NativeMethods.VadGetResult));
            }

            var result = Marshal.PtrToStructure<Interop.VadResult>(ptr);
            firstFrame = result.firstFrame;
            probabilities = ReadProbabilities(result.nProbs);
            segments = ReadSegments(result.nSegments);
        });

        return new VadResult(probabilities, firstFrame, segments);
    }

    /// <summary>Timing statistics for the last run, or null when unavailable.</summary>
    public TimingsResult? GetTimings()
    {
        ThrowIfDisposed();
        var timingsSize = (int)NativeMethods.AbiStructSize(AbiStruct.AbiTimings);
        TimingsResult? result = null;
        StackAllocHelper.RunWithBuffer(timingsSize, ptr =>
        {
            NativeMethods.TimingsInit(ptr);
            if (NativeMethods.VadGetTimings(handle, ptr) == Status.Ok)
            {
                var t = Marshal.PtrToStructure<Timings>(ptr);
                result = new TimingsResult(t.loadMs, t.melMs, t.encodeMs, t.decodeMs);
            }
        });
        return result;
    }

    private List<float> ReadProbabilities(int count)
    {
        var probabilities = new List<float>(count);
        if (count == 0)
        {
            return probabilities;
        }

        var ptr = NativeMethods.VadProbs(handle);
        if (ptr == IntPtr.Zero)
        {
            return probabilities;
        }

        var buffer = new float[count];
        Marshal.Copy(ptr, buffer, 0, count);
        probabilities.AddRange(buffer);
        return probabilities;
    }

    private List<VadSegmentResult> ReadSegments(int count)
    {
        var results = new List<VadSegmentResult>(count);
        if (count == 0)
        {
            return results;
        }

        var size = (int)NativeMethods.AbiStructSize(AbiStruct.AbiVadSegment);
        StackAllocHelper.RunWithBuffer(size, ptr =>
        {
            for (int i = 0; i < count; i++)
            {
                NativeMethods.VadSegmentInit(ptr);
                var status = NativeMethods.VadGetSegment(handle, i, ptr);
                if (status != Status.Ok)
                {
                    throw new TranscribeException(status, nameof(NativeMethods.VadGetSegment));
                }

                var seg = Marshal.PtrToStructure<Interop.VadSegment>(ptr);
                results.Add(new VadSegmentResult(seg.startSample, seg.endSample));
            }
        });

        return results;
    }

    private void RunNative(ReadOnlySpan<float> pcm, VadParamsBuilder runParams)
    {
        var status = NativeMethods.VadRun(handle, pcm, pcm.Length, runParams.Build());
        if (status != Status.Ok)
        {
            throw new TranscribeException(status, nameof(NativeMethods.VadRun));
        }
    }

    private void Check(Status status, string method)
    {
        ThrowIfDisposed();
        if (status != Status.Ok)
        {
            throw new TranscribeException(status, method);
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!disposed)
        {
            NativeMethods.VadSessionFree(handle);
            disposed = true;
        }
    }
}
