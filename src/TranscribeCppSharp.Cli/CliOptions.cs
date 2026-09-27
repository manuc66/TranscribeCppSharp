// Parsing and validation of the command line, kept free of native calls, of the
// network and of model resolution so every rule can be unit tested on any
// machine (CI has no GPU and no model).
//
// The parsed result is the single source of truth for the rest of the run: the
// flags are read once here instead of being re-scanned while transcribing.

using System.Diagnostics.CodeAnalysis;
using TranscribeCppSharp;
using TranscribeCppSharp.Interop;

namespace TranscribeCppSharp.Cli;

/// <summary>Transcript file format selected by --format.</summary>
internal enum TranscriptFormat
{
    /// <summary>Header comments + timestamped lines (the default).</summary>
    Plain,

    /// <summary>WebVTT cues, each with a speaker cue.</summary>
    Vtt,

    /// <summary>Whisper-style JSON: language, full text, segments.</summary>
    Json,
}

/// <summary>One transcript line, with its position on the original timeline.</summary>
internal readonly record struct TranscriptLine(double StartMs, double EndMs, int Speaker, string Text);

/// <summary>Every option of one run, validated. See <see cref="TryParse"/>.</summary>
internal sealed record CliOptions
{
    /// <summary>Default model alias: the diarization model (see --list-models).</summary>
    internal const string DefaultModel = "moss-transcribe-diarize";

    /// <summary>Default decoder language, overridable with --lang.</summary>
    internal const string DefaultLanguage = "en";

    /// <summary>Default window length in seconds, overridable with --chunk.</summary>
    internal const int DefaultChunkSeconds = 300;

    /// <summary>Absolute path of the input audio.</summary>
    internal required string AudioPath { get; init; }

    /// <summary>Model as given: a file path, a curated alias or a HuggingFace spec.</summary>
    internal required string ModelSpec { get; init; }

    /// <summary>--quant, or null for the alias' own quantization.</summary>
    internal string? Quant { get; init; }

    internal string Language { get; init; } = DefaultLanguage;

    internal int ChunkSeconds { get; init; } = DefaultChunkSeconds;

    /// <summary>--no-diarize turns attribution off; otherwise it is requested.</summary>
    internal DiarizeMode Diarize { get; init; } = DiarizeMode.DiarizeModeOn;

    /// <summary>Absolute path of the --out file, or null to print only.</summary>
    internal string? OutPath { get; init; }

    internal TranscriptFormat Format { get; init; } = TranscriptFormat.Plain;

    internal BackendRequest Backend { get; init; } = BackendRequest.BackendAuto;

    /// <summary>--backend as typed, for the error messages.</summary>
    internal string BackendName { get; init; } = "auto";

    /// <summary>--device index, or null to let the backend choose.</summary>
    internal int? DeviceIndex { get; init; }

    internal bool WritesFile => OutPath is not null;

    /// <summary>
    /// Parses the arguments. <paramref name="args"/> must not contain the flags
    /// handled before parsing (--help, --list-models, --model-info,
    /// --list-devices).
    /// </summary>
    internal static bool TryParse(string[] args, [NotNullWhen(true)] out CliOptions? options, out string? error)
    {
        options = null;
        error = null;

        string? model = null, quant = null, language = null, chunk = null, outPath = null,
                format = null, backendName = null, device = null;
        bool noDiarize = false;
        var positional = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--model":
                    if (!TakeValue(args, ref i, "--model", out model, out error)) { return false; }
                    break;
                case "--quant":
                    if (!TakeValue(args, ref i, "--quant", out quant, out error)) { return false; }
                    break;
                case "--lang":
                    if (!TakeValue(args, ref i, "--lang", out language, out error)) { return false; }
                    break;
                case "--chunk":
                    if (!TakeValue(args, ref i, "--chunk", out chunk, out error)) { return false; }
                    break;
                case "--out":
                    if (!TakeValue(args, ref i, "--out", out outPath, out error)) { return false; }
                    break;
                case "--format":
                    if (!TakeValue(args, ref i, "--format", out format, out error)) { return false; }
                    break;
                case "--backend":
                    if (!TakeValue(args, ref i, "--backend", out backendName, out error)) { return false; }
                    break;
                case "--device":
                    if (!TakeValue(args, ref i, "--device", out device, out error)) { return false; }
                    break;
                case "--no-diarize":
                    noDiarize = true;
                    break;
                default:
                    positional.Add(args[i]);
                    break;
            }
        }

        if (positional.Count == 0)
        {
            error = "no audio file given (see 'transcribe --help').";
            return false;
        }

        if (!TryParseFormat(format, out var transcriptFormat, out error))
        {
            return false;
        }

        if (!DeviceSelection.TryParseBackend(backendName, out var backend, out var backendError))
        {
            error = backendError;
            return false;
        }

        if (!TryParseDevice(device, out var deviceIndex, out error))
        {
            return false;
        }

        options = new CliOptions
        {
            AudioPath = Path.GetFullPath(positional[0]),
            ModelSpec = model ?? (positional.Count > 1 ? positional[1] : DefaultModel),
            Quant = quant,
            Language = language ?? DefaultLanguage,
            // An unusable --chunk falls back to the default, as before.
            ChunkSeconds = int.TryParse(chunk, out int chunkSeconds) && chunkSeconds > 0 ? chunkSeconds : DefaultChunkSeconds,
            Diarize = noDiarize ? DiarizeMode.DiarizeModeOff : DiarizeMode.DiarizeModeOn,
            OutPath = outPath is null ? null : Path.GetFullPath(outPath),
            Format = transcriptFormat,
            Backend = backend,
            BackendName = DeviceSelection.Name(backend),
            DeviceIndex = deviceIndex,
        };

        return true;
    }

    /// <summary>
    /// The value that follows a flag, or null when there is none. Used for the
    /// flags answered before parsing (--model-info); the flags handled by
    /// <see cref="TryParse"/> consume their value as they walk the arguments.
    /// </summary>
    internal static string? ValueAfter(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.Ordinal)
                && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    /// <summary>Reads the value that follows a flag, if there is one.</summary>
    private static bool TakeValue(string[] args, ref int i, string name, out string? value, out string? error)
    {
        error = null;
        // A value starting with "--" is the next flag, not a value: a missing
        // value is reported instead of silently ignored.
        if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
        {
            value = null;
            error = $"missing value for {name}.";
            return false;
        }

        value = args[++i];
        return true;
    }

    private static bool TryParseFormat(string? name, out TranscriptFormat format, out string? error)
    {
        format = TranscriptFormat.Plain;
        error = null;

        switch (name)
        {
            case null or "plain":
                return true;
            case "vtt":
                format = TranscriptFormat.Vtt;
                return true;
            case "json":
                format = TranscriptFormat.Json;
                return true;
            default:
                error = $"unknown --format: {name} (expected plain, vtt or json)";
                return false;
        }
    }

    private static bool TryParseDevice(string? name, out int? index, out string? error)
    {
        index = null;
        error = null;

        if (name is null)
        {
            return true;
        }

        if (!int.TryParse(name, out int parsed) || parsed < 0)
        {
            error = $"invalid --device: '{name}' (expected a non-negative index from --list-devices)";
            return false;
        }

        index = parsed;
        return true;
    }
}
