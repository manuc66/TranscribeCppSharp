namespace TranscribeCppSharp;

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using TranscribeCppSharp.Interop;

/// <summary>
/// Offline speaker diarization over a whole buffer, for a model that serves
/// <see cref="TranscribeRole.Diarize"/> (e.g. Sortformer).
/// </summary>
/// <remarks>
/// Introduced by upstream transcribe.cpp 0.4: diarization is a role of its own,
/// and models whose product is speaker turns no longer run through the ASR
/// session (<see cref="Model.CreateSession"/> returns
/// <see cref="Status.ErrUnsupportedRole"/> on them). The upstream API has no
/// streaming diarization entry point — run the whole buffer.
///
/// Not thread-safe. The session keeps its parent <see cref="Model"/> alive.
/// </remarks>
public sealed class DiarizeSession : IDisposable
{
    // Unread by design: roots the parent Model for the session's lifetime.
    // NOSONAR S4487
    private readonly Model model;
    private readonly IntPtr handle;
    private readonly AbortSource abort;
    private bool disposed;

    private DiarizeSession(IntPtr handle, Model model)
    {
        this.handle = handle;
        this.model = model;
        abort = new AbortSource((cb, ud) => NativeMethods.DiarizeSetAbortCallback(handle, cb, ud));
    }

    /// <summary>Facts about a loaded diarization model.</summary>
    public record DiarizeInfo(int SampleRate, int MaxSpeakers);

    internal static DiarizeSession Create(ModelHandle modelHandle, Model model, Action<DiarizeSessionParamsBuilder>? configure)
    {
        using var sessionParams = new DiarizeSessionParamsBuilder();
        configure?.Invoke(sessionParams);

        var outSession = Marshal.AllocHGlobal(IntPtr.Size);
        try
        {
            var status = NativeMethods.DiarizeSessionInit(modelHandle, sessionParams.Build(), outSession);
            if (status != Status.Ok)
            {
                throw new TranscribeException(status, nameof(NativeMethods.DiarizeSessionInit));
            }

            return new DiarizeSession(Marshal.ReadIntPtr(outSession), model);
        }
        finally
        {
            Marshal.FreeHGlobal(outSession);
        }
    }

    /// <summary>
    /// Diarize a PCM buffer (16 kHz mono f32) and return the speaker segments.
    /// Supports cancellation via <paramref name="ct"/>.
    /// </summary>
    public IReadOnlyList<SpeakerSegmentResult> Run(
        ReadOnlySpan<float> pcm,
        Action<DiarizeParamsBuilder>? configure = null,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ct.ThrowIfCancellationRequested();

        using var runParams = new DiarizeParamsBuilder();
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

        return ReadSegments();
    }

    /// <summary>Number of speaker segments in the last result.</summary>
    public int SegmentCount
    {
        get
        {
            ThrowIfDisposed();
            var count = NativeMethods.DiarizeNSegments(handle);
            GC.KeepAlive(this);
            return count;
        }
    }

    /// <summary>Read the speaker segments of the last run.</summary>
    public IReadOnlyList<SpeakerSegmentResult> ReadSegments()
    {
        ThrowIfDisposed();
        var count = NativeMethods.DiarizeNSegments(handle);
        var results = new List<SpeakerSegmentResult>(count);
        if (count == 0)
        {
            return results;
        }

        var size = (int)NativeMethods.AbiStructSize(AbiStruct.AbiSpeakerSegment);
        StackAllocHelper.RunWithBuffer(size, ptr =>
        {
            for (int i = 0; i < count; i++)
            {
                NativeMethods.SpeakerSegmentInit(ptr);
                var status = NativeMethods.DiarizeGetSegment(handle, i, ptr);
                if (status != Status.Ok)
                {
                    throw new TranscribeException(status, nameof(NativeMethods.DiarizeGetSegment));
                }

                var seg = Marshal.PtrToStructure<SpeakerSegment>(ptr);
                results.Add(new SpeakerSegmentResult(
                    Start: TimeSpan.FromMilliseconds(seg.t0Ms),
                    End: TimeSpan.FromMilliseconds(seg.t1Ms),
                    SpeakerId: seg.speakerId,
                    Probability: seg.p));
            }
        });

        return results;
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
            if (NativeMethods.DiarizeGetTimings(handle, ptr) == Status.Ok)
            {
                var t = Marshal.PtrToStructure<Timings>(ptr);
                result = new TimingsResult(t.loadMs, t.melMs, t.encodeMs, t.decodeMs);
            }
        });
        return result;
    }

    private void RunNative(ReadOnlySpan<float> pcm, DiarizeParamsBuilder runParams)
    {
        var status = NativeMethods.DiarizeRun(handle, pcm, pcm.Length, runParams.Build());
        if (status != Status.Ok)
        {
            throw new TranscribeException(status, nameof(NativeMethods.DiarizeRun));
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!disposed)
        {
            NativeMethods.DiarizeSessionFree(handle);
            disposed = true;
        }
    }
}
