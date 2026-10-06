using System;
using System.Collections.Generic;
using TranscribeCppSharp;
using TranscribeCppSharp.Interop;

namespace TranscribeCppSharp.Ui.Services;

/// <summary>
/// What a loaded model reports about itself, gathered in a single load.
/// </summary>
/// <remarks>
/// The manifest declares no capabilities, so this can only be filled by loading
/// the weights. It is the authoritative answer, preferred over guessing a family
/// from the alias — which is wrong for a Whisper-derived model whose name does
/// not say "whisper".
/// </remarks>
public sealed record ModelCapabilitySnapshot(
    string Architecture,
    string Variant,
    bool SupportsTranslate,
    bool SupportsLanguageDetect,
    bool SupportsStreaming,
    bool SupportsSpecDecode,
    IReadOnlySet<Feature> Features,
    IReadOnlyList<string> Languages,
    IReadOnlyList<string> TranslateTargetLanguages)
{
    /// <summary>Whether the model reports a given feature.</summary>
    public bool Supports(Feature feature) => Features.Contains(feature);
}

/// <summary>Loads a model once and reads everything the UI asks about it.</summary>
public static class ModelCapabilityProbe
{
    /// <summary>
    /// Loads <paramref name="modelPath"/> on the CPU and returns its
    /// capabilities. The model is disposed before returning, so the caller pays
    /// the load and not the memory. The CPU is used so the probe does not take
    /// the GPU from a run that is already using it.
    /// </summary>
    public static ModelCapabilitySnapshot Probe(string modelPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);

        using var model = Model.Load(modelPath, p => p.WithBackend(BackendRequest.BackendCpu));
        var capabilities = model.GetCapabilities();

        var features = new HashSet<Feature>();
        foreach (Feature feature in Enum.GetValues<Feature>())
        {
            if (model.Supports(feature))
            {
                features.Add(feature);
            }
        }

        return new ModelCapabilitySnapshot(
            Architecture: model.Architecture,
            Variant: model.Variant,
            SupportsTranslate: capabilities.SupportsTranslate,
            SupportsLanguageDetect: capabilities.SupportsLanguageDetect,
            SupportsStreaming: capabilities.SupportsStreaming,
            SupportsSpecDecode: capabilities.SupportsSpecDecode,
            Features: features,
            Languages: capabilities.Languages,
            TranslateTargetLanguages: capabilities.TranslateTargetLanguages);
    }
}

/// <summary>
/// Which option groups apply to a model, decided from its own capabilities.
/// </summary>
/// <remarks>
/// Null means "not checked yet", and then everything is shown: an option is
/// hidden only once the model itself said it does not have the feature. Hiding
/// on an unloaded model would be a guess, and a wrong guess hides an option that
/// would have worked.
/// </remarks>
public static class ModelOptionVisibility
{
    /// <summary>Initial prompt and temperature fallback: Whisper's features.</summary>
    public static bool ShowWhisperExtensions(ModelCapabilitySnapshot? capabilities)
        => capabilities is null
        || capabilities.Supports(Feature.FeatureInitialPrompt)
        || capabilities.Supports(Feature.FeatureTemperatureFallback);

    /// <summary>Translation and target language, only for models that translate.</summary>
    public static bool ShowTranslate(ModelCapabilitySnapshot? capabilities)
        => capabilities is null || capabilities.SupportsTranslate;

    /// <summary>Speculative decoding drafts, only for models that do it.</summary>
    public static bool ShowSpeculativeDecoding(ModelCapabilitySnapshot? capabilities)
        => capabilities is null || capabilities.SupportsSpecDecode;

    /// <summary>
    /// Whether the whole family-extensions group applies: a streaming model
    /// whose architecture has one of the extension builders.
    /// </summary>
    public static bool ShowStreamingExtensions(ModelCapabilitySnapshot? capabilities)
        => capabilities is null
        || (capabilities.SupportsStreaming && IsKnownStreamingArchitecture(capabilities.Architecture));

    /// <summary>Moonshine's decode-interval extension.</summary>
    public static bool ShowMoonshineExtensions(ModelCapabilitySnapshot? capabilities)
        => IsStreamingFamily(capabilities, "moonshine_streaming");

    /// <summary>Parakeet's streaming extensions.</summary>
    public static bool ShowParakeetExtensions(ModelCapabilitySnapshot? capabilities)
        => IsStreamingFamily(capabilities, "parakeet");

    /// <summary>Sortformer's preset extension.</summary>
    public static bool ShowSortformerExtensions(ModelCapabilitySnapshot? capabilities)
        => IsStreamingFamily(capabilities, "sortformer");

    /// <summary>Voxtral's realtime extensions.</summary>
    public static bool ShowVoxtralExtensions(ModelCapabilitySnapshot? capabilities)
        => IsStreamingFamily(capabilities, "voxtral_realtime");

    /// <summary>
    /// A family extension applies only to a model that both streams and has
    /// that architecture. The architecture alone is not enough: an offline
    /// Parakeet and a streaming Parakeet share <c>parakeet</c>, but only the
    /// streaming one has the buffered-streaming extension.
    /// </summary>
    private static bool IsStreamingFamily(ModelCapabilitySnapshot? capabilities, string architecture)
        => capabilities is null
        || (capabilities.SupportsStreaming
            && string.Equals(capabilities.Architecture, architecture, StringComparison.Ordinal));

    /// <summary>
    /// Architecture strings read from the <c>general.architecture</c> GGUF
    /// metadata of the published models (not guessed): the four that have a
    /// stream extension builder in the wrapper.
    /// </summary>
    private static bool IsKnownStreamingArchitecture(string architecture)
        => architecture is "moonshine_streaming" or "parakeet" or "sortformer" or "voxtral_realtime";
}
