namespace TranscribeCppSharp;

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using TranscribeCppSharp.Interop;

/// <summary>
/// Language identification of a clip, for a model that serves
/// <see cref="TranscribeRole.Langid"/> (e.g. VoxLingua107 ECAPA-TDNN).
/// </summary>
/// <remarks>
/// Introduced by upstream transcribe.cpp 0.4. Not thread-safe. The session
/// keeps its parent <see cref="Model"/> alive.
/// </remarks>
public sealed class LangIdSession : IDisposable
{
    // Unread by design: roots the parent Model for the session's lifetime.
    // NOSONAR S4487
    private readonly Model model;
    private readonly IntPtr handle;
    private readonly AbortSource abort;
    private bool disposed;

    private LangIdSession(IntPtr handle, Model model)
    {
        this.handle = handle;
        this.model = model;
        abort = new AbortSource((cb, ud) => NativeMethods.LangidSetAbortCallback(handle, cb, ud));
    }

    /// <summary>Facts about a loaded language-identification model.</summary>
    public record LangIdInfo(int SampleRate, int LabelCount, int MinAudioMs, int MaxAudioMs);

    /// <summary>One ranked language candidate from a language-identification run.</summary>
    public record LangIdCandidate(int Index, string Code, string Name, float Probability, float Logit);

    /// <summary>
    /// The result of a language-identification run: candidates ranked by
    /// probability, plus the share of the distribution that fell inside the
    /// allowed set.
    /// </summary>
    /// <param name="Candidates">Ranked candidates (one per allowed label, or every label when unrestricted).</param>
    /// <param name="AllowedMass">
    /// Share of the softmax over every label that falls in the allowed set; 1 when
    /// unrestricted. A low value means the speech is probably outside the allowed set.
    /// </param>
    public record LangIdResult(IReadOnlyList<LangIdCandidate> Candidates, float AllowedMass);

    internal static LangIdSession Create(ModelHandle modelHandle, Model model, Action<LangIdSessionParamsBuilder>? configure)
    {
        using var sessionParams = new LangIdSessionParamsBuilder();
        configure?.Invoke(sessionParams);

        var outSession = Marshal.AllocHGlobal(IntPtr.Size);
        try
        {
            var status = NativeMethods.LangidSessionInit(modelHandle, sessionParams.Build(), outSession);
            if (status != Status.Ok)
            {
                throw new TranscribeException(status, nameof(NativeMethods.LangidSessionInit));
            }

            return new LangIdSession(Marshal.ReadIntPtr(outSession), model);
        }
        finally
        {
            Marshal.FreeHGlobal(outSession);
        }
    }

    /// <summary>
    /// Identify the spoken language of a PCM buffer (16 kHz mono f32) and return
    /// the ranked candidates. Supports cancellation via <paramref name="ct"/>.
    /// </summary>
    public LangIdResult Run(
        ReadOnlySpan<float> pcm,
        Action<LangIdParamsBuilder>? configure = null,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ct.ThrowIfCancellationRequested();

        using var runParams = new LangIdParamsBuilder();
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

    /// <summary>Read the result of the last run.</summary>
    public LangIdResult ReadResult()
    {
        ThrowIfDisposed();
        var size = (int)NativeMethods.AbiStructSize(AbiStruct.AbiLangidResult);
        var candidates = new List<LangIdCandidate>();
        float allowedMass = 1f;

        StackAllocHelper.RunWithBuffer(size, ptr =>
        {
            NativeMethods.LangidResultInit(ptr);
            var status = NativeMethods.LangidGetResult(handle, ptr);
            if (status != Status.Ok)
            {
                throw new TranscribeException(status, nameof(NativeMethods.LangidGetResult));
            }

            var result = Marshal.PtrToStructure<LangidResult>(ptr);
            allowedMass = result.allowedMass;
            candidates = ReadCandidates(result.nCandidates);
        });

        return new LangIdResult(candidates, allowedMass);
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
            if (NativeMethods.LangidGetTimings(handle, ptr) == Status.Ok)
            {
                var t = Marshal.PtrToStructure<Timings>(ptr);
                result = new TimingsResult(t.loadMs, t.melMs, t.encodeMs, t.decodeMs);
            }
        });
        return result;
    }

    private List<LangIdCandidate> ReadCandidates(int count)
    {
        var results = new List<LangIdCandidate>(count);
        if (count == 0)
        {
            return results;
        }

        var size = (int)NativeMethods.AbiStructSize(AbiStruct.AbiLangidCandidate);
        StackAllocHelper.RunWithBuffer(size, ptr =>
        {
            for (int i = 0; i < count; i++)
            {
                NativeMethods.LangidCandidateInit(ptr);
                var status = NativeMethods.LangidGetCandidate(handle, i, ptr);
                if (status != Status.Ok)
                {
                    throw new TranscribeException(status, nameof(NativeMethods.LangidGetCandidate));
                }

                var c = Marshal.PtrToStructure<LangidCandidate>(ptr);
                results.Add(new LangIdCandidate(
                    Index: c.index,
                    Code: Marshal.PtrToStringUTF8(c.code) ?? string.Empty,
                    Name: Marshal.PtrToStringUTF8(c.name) ?? string.Empty,
                    Probability: c.p,
                    Logit: c.logit));
            }
        });

        return results;
    }

    private void RunNative(ReadOnlySpan<float> pcm, LangIdParamsBuilder runParams)
    {
        var status = NativeMethods.LangidRun(handle, pcm, pcm.Length, runParams.Build());
        if (status != Status.Ok)
        {
            throw new TranscribeException(status, nameof(NativeMethods.LangidRun));
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!disposed)
        {
            NativeMethods.LangidSessionFree(handle);
            disposed = true;
        }
    }
}
