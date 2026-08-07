#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using TranscribeCppSharp.Interop;

namespace TranscribeCppSharp;

/// <summary>
/// Batch transcription API. Process multiple audio clips in a single call.
/// </summary>
public static class Batch
{
    /// <summary>
    /// Transcribe multiple PCM buffers in a single batch.
    /// Supports cancellation via <paramref name="ct"/>.
    /// </summary>
    /// <param name="session">The session to use for transcription.</param>
    /// <param name="pcmBuffers">PCM buffers (16 kHz mono f32), one per audio clip.</param>
    /// <param name="configure">Optional configuration for run parameters.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>Array of results, one per input buffer.</returns>
    public static IReadOnlyList<BatchResult> Run(
        Session session,
        IReadOnlyList<float[]> pcmBuffers,
        Action<RunParamsBuilder>? configure = null,
        CancellationToken ct = default)
    {
        return RunInternal(session, pcmBuffers, configure, ct);
    }

    private static List<BatchResult> RunInternal(
        Session session,
        IReadOnlyList<float[]> pcmBuffers,
        Action<RunParamsBuilder>? configure,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(session);

        ArgumentNullException.ThrowIfNull(pcmBuffers);

        ct.ThrowIfCancellationRequested();

        if (pcmBuffers.Count == 0)
        {
            return [];
        }

        var n = pcmBuffers.Count;
        var pcmPtrs = new IntPtr[n];
        var sampleCounts = new int[n];
        var handles = new GCHandle[n];

        try
        {
            PinBuffers(pcmBuffers, handles, pcmPtrs, sampleCounts);

            // Allocate arrays for native call
            var pcmPtrArray = Marshal.AllocHGlobal(n * IntPtr.Size);
            var sampleCountArray = Marshal.AllocHGlobal(n * sizeof(int));

            try
            {
                Marshal.Copy(pcmPtrs, 0, pcmPtrArray, n);
                Marshal.Copy(sampleCounts, 0, sampleCountArray, n);

                using var runParams = new RunParamsBuilder();
                configure?.Invoke(runParams);
                RunBatchWithCancellation(session, pcmPtrArray, sampleCountArray, n, runParams.Build(), ct);

                return ReadResults(session, session.GetBatchResultCount());
            }
            finally
            {
                Marshal.FreeHGlobal(pcmPtrArray);
                Marshal.FreeHGlobal(sampleCountArray);
            }
        }
        finally
        {
            FreeHandles(handles);
        }
    }

    private static void PinBuffers(
        IReadOnlyList<float[]> pcmBuffers,
        GCHandle[] handles,
        IntPtr[] pcmPtrs,
        int[] sampleCounts)
    {
        for (int i = 0; i < pcmBuffers.Count; i++)
        {
            var buffer = pcmBuffers[i];
            if (buffer is null)
            {
                throw new ArgumentNullException(nameof(pcmBuffers), $"Element at index {i} is null.");
            }

            if (buffer.Length == 0)
            {
                throw new ArgumentException($"Element at index {i} is empty (zero samples).", nameof(pcmBuffers));
            }

            handles[i] = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            pcmPtrs[i] = handles[i].AddrOfPinnedObject();
            sampleCounts[i] = buffer.Length;
        }
    }

    private static void RunBatchWithCancellation(
        Session session,
        IntPtr pcmPtrArray,
        IntPtr sampleCountArray,
        int n,
        IntPtr runParams,
        CancellationToken ct)
    {
        Func<bool>? previousCallback = null;
        if (ct.CanBeCanceled)
        {
            previousCallback = session.GetAbortCallback();
            session.SetAbortCallback(() => ct.IsCancellationRequested);
        }

        try
        {
            var status = session.RunBatchInternal(pcmPtrArray, sampleCountArray, n, runParams);
            if (status != Status.Ok)
            {
                throw new TranscribeException(status, nameof(NativeMethods.RunBatch));
            }
        }
        catch (TranscribeException ex) when (ex.StatusCode == Status.ErrAborted)
        {
            throw new OperationCanceledException(ct);
        }
        finally
        {
            if (ct.CanBeCanceled)
            {
                if (previousCallback != null)
                {
                    session.SetAbortCallback(previousCallback);
                }
                else
                {
                    session.ClearAbortCallback();
                }
            }
        }
    }

    private static List<BatchResult> ReadResults(Session session, int resultCount)
    {
        var results = new List<BatchResult>(resultCount);
        for (int i = 0; i < resultCount; i++)
        {
            results.Add(new BatchResult(
                Index: i,
                FullText: session.GetBatchResultFullText(i),
                DetectedLanguage: session.GetBatchResultDetectedLanguage(i),
                Status: session.GetBatchResultStatus(i),
                Segments: session.GetBatchSegments(i),
                Words: session.GetBatchWords(i),
                Tokens: session.GetBatchTokens(i),
                Timing: session.GetBatchTimings(i)));
        }

        return results;
    }

    private static void FreeHandles(GCHandle[] handles)
    {
        for (int i = 0; i < handles.Length; i++)
        {
            if (handles[i].IsAllocated)
            {
                handles[i].Free();
            }
        }
    }
}
