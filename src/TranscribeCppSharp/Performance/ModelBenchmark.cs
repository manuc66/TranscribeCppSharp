using System;
using System.Globalization;

namespace TranscribeCppSharp.Performance;

/// <summary>
/// One measured transcription run, and the conditions it was measured under.
/// </summary>
/// <remarks>
/// The conditions are not decoration. A timing is only meaningful for the machine,
/// the compute device, the thread count and the audio it was taken on, so they
/// travel with the number. Sorting models by speed while silently mixing results
/// from two different machines would produce a ranking that is wrong for both.
/// <para>
/// Nothing here says anything about accuracy. RTF measures speed. A model can be
/// the fastest on the machine and the least accurate on your audio, and this
/// project has no accuracy measurement to offer: that needs a reference
/// transcript, which it does not ship.
/// </para>
/// </remarks>
public sealed record ModelBenchmark
{
    /// <summary>The alias this was measured on.</summary>
    public required string Alias { get; init; }

    /// <summary>
    /// Best real-time factor over the timed passes: compute seconds divided by
    /// audio seconds. Below 1.0 keeps up with live audio; for a file
    /// transcription it is simply "how many times faster than real time".
    /// </summary>
    public required double Rtf { get; init; }

    /// <summary>Wall time to load the model, in milliseconds.</summary>
    public required double LoadMs { get; init; }

    /// <summary>Encode time reported by the native layer, when it reports one.</summary>
    public double? EncodeMs { get; init; }

    /// <summary>Decode time reported by the native layer, when it reports one.</summary>
    public double? DecodeMs { get; init; }

    /// <summary>Timed passes behind <see cref="Rtf"/>, after the discarded warm-up.</summary>
    public required int Passes { get; init; }

    /// <summary>Length of the audio excerpt the passes ran over.</summary>
    public required double AudioSeconds { get; init; }

    /// <summary>Name of the file the excerpt came from, for traceability.</summary>
    public required string AudioName { get; init; }

    /// <summary>Compute backend requested for the run.</summary>
    public required string Backend { get; init; }

    /// <summary>Compute device the model actually resolved to.</summary>
    public required string Device { get; init; }

    /// <summary>Session thread count used, or 0 for the library default.</summary>
    public required int Threads { get; init; }

    /// <summary>
    /// Fingerprint of the machine and device this ran on.
    /// </summary>
    /// <remarks>
    /// Used to hide results measured elsewhere rather than to invalidate them: a
    /// row keeps its number, it just is not sorted against numbers from a
    /// different machine. It is a fingerprint, not an identity — two identical
    /// VMs collide, and swapping a GPU for an identical model does not change
    /// it.
    /// </remarks>
    public required string MachineKey { get; init; }

    /// <summary>When the measurement was taken.</summary>
    public required DateTimeOffset MeasuredAt { get; init; }

    /// <summary>RTF formatted for the grid.</summary>
    /// <remarks>
    /// Invariant culture on purpose: this sits in a numeric column next to other
    /// rows, where a comma decimal separator reads as a thousands separator.
    /// <para>
    /// Three decimals below 0.1 and two above. Two decimals everywhere would
    /// print a fast model as "0.00" and round 0.085 to "0.09", which in a
    /// column of closely ranked numbers is a difference you cannot see.
    /// </para>
    /// </remarks>
    public string RtfText => Rtf <= 0
        ? "-"
        : Rtf < 0.1
            ? Rtf.ToString("0.000", CultureInfo.InvariantCulture)
            : Rtf.ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>Load time formatted for the grid.</summary>
    public string LoadText => LoadMs < 1000
        ? $"{LoadMs.ToString("0", CultureInfo.InvariantCulture)} ms"
        : LoadMs < 60_000
            ? $"{(LoadMs / 1000).ToString("0.0", CultureInfo.InvariantCulture)} s"
            : $"{(LoadMs / 60_000).ToString("0.0", CultureInfo.InvariantCulture)} min";

    /// <summary>
    /// Encoder and decoder times the native layer reported, when it did.
    /// </summary>
    /// <remarks>
    /// Rounded to a tenth of a millisecond. The native struct carries these as
    /// <c>float</c>, so the raw value prints as something like
    /// 1832.3199462890625 — that trailing noise is float precision, not signal.
    /// </remarks>
    public string PhasesText
    {
        get
        {
            if (EncodeMs is null && DecodeMs is null)
            {
                return "no phase timings reported";
            }

            string enc = EncodeMs is { } encode && encode > 0
                ? encode.ToString("0.0", CultureInfo.InvariantCulture) + " ms encode"
                : "encode not reported";
            string dec = DecodeMs is { } decode && decode > 0
                ? decode.ToString("0.0", CultureInfo.InvariantCulture) + " ms decode"
                : "decode not reported";
            return $"{enc}, {dec}";
        }
    }

    /// <summary>
    /// The conditions, as one line, for a tooltip and the detail pane.
    /// </summary>
    public string ConditionsText
        => $"{RtfText}x realtime ({PhasesText}), load {LoadText}, {Passes} passes over "
            + $"{AudioSeconds.ToString("0", CultureInfo.InvariantCulture)}s of {AudioName}; "
            + $"{Backend} on {Device}, {Threads} threads, {MeasuredAt:yyyy-MM-dd HH:mm}";

    /// <summary>
    /// A short description of what this number is, for the grid header tooltip.
    /// </summary>
    public const string AboutSpeed
        = "Measured on this machine, on your audio, by loading each model and transcribing a "
            + "short excerpt of it. It is a speed figure, not an accuracy figure: nothing here "
            + "knows which model transcribes your speech correctly. A model can be fastest and "
            + "worst at the same time. Each number is valid only for the machine, device, thread "
            + "count and audio shown in the row.";

    /// <summary>
    /// A short description of the fingerprint, for the detail pane.
    /// </summary>
    public const string AboutMachineKey
        = "Results are only compared against others from the same machine, device and backend "
            + "version. The key is a fingerprint built from those, so two identical machines "
            + "cannot be told apart, and it does not notice a part swapped for an identical one.";
}
