using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TranscribeCppSharp.Audio;
using TranscribeCppSharp.Interop;
using Avalonia.Input.Platform;
using TranscribeCppSharp.Models;
using TranscribeCppSharp.SharedSource;
using TranscribeCppSharp.UI.Models;
using TranscribeCppSharp.UI.Services;

namespace TranscribeCppSharp.UI.ViewModels;

public partial class TranscriptionViewModel
{
    /// <summary>Writes the three export options to the settings file, best effort.</summary>
    private void PersistExportOptions()
        => UiSettings.SaveExportSettings(
            new ExportSettings(MarkPauses, PauseThresholdSeconds, TimestampIntervalSeconds));

    /// <summary>
    /// Rebuilds the speaker-name rows when a result arrives, keeping the names
    /// already typed for the speakers that are still there.
    /// </summary>
    /// <remarks>
    /// The rows appear only when there is something to name: a result with one
    /// speaker and no speaker turns would show a single "Speaker 0" field, which
    /// names nothing. A result the model produced without attribution therefore
    /// leaves the editor hidden.
    /// </remarks>
    partial void OnResultChanged(TranscriptionResult? value)
    {
        var ids = new List<int>();
        if (value is not null)
        {
            foreach (MergedSegment segment in value.Segments)
            {
                if (!ids.Contains(segment.SpeakerId))
                {
                    ids.Add(segment.SpeakerId);
                }
            }

            foreach (SpeakerSegmentResult turn in value.SpeakerSegments)
            {
                if (!ids.Contains(turn.SpeakerId))
                {
                    ids.Add(turn.SpeakerId);
                }
            }
        }

        ids.Sort();
        Dictionary<int, string> previous = SpeakerNames.ToDictionary(e => e.SpeakerId, e => e.Name);
        SpeakerNames.Clear();

        if (ids.Count > 1 || value?.SpeakerSegments.Count > 0)
        {
            foreach (int id in ids)
            {
                SpeakerNames.Add(new SpeakerNameEntry(id, previous.GetValueOrDefault(id, string.Empty)));
            }
        }

        OnPropertyChanged(nameof(HasSpeakerNames));
        OnPropertyChanged(nameof(ResultSpeakersNote));
        OnPropertyChanged(nameof(HasResultSpeakersNote));
    }

    /// <summary>The names that were actually typed, by speaker id.</summary>
    private Dictionary<int, string> NamedSpeakers()
        => SpeakerNames
            .Where(e => !string.IsNullOrWhiteSpace(e.Name))
            .ToDictionary(e => e.SpeakerId, e => e.Name.Trim());

    /// <summary>
    /// Reads the capabilities of the selected model, once per alias. The answer
    /// is held by the shared <see cref="ModelCapabilityCache"/>.
    /// </summary>
    /// <remarks>
    /// Only a model that is on disk can be loaded, and loading is the only way
    /// the native library answers — the manifest declares no capabilities. When
    /// there is nothing to check, the options stay unfiltered and
    /// <see cref="CapabilityStatus"/> says why rather than hiding them on a
    /// guess.
    /// </remarks>
    private void RequestCapabilityProbe(string alias)
    {
        if (_capabilityCache.TryGet(alias, out ModelCapabilitySnapshot? cached))
        {
            ApplyCapabilities(cached);
            CapabilityStatus = string.Empty;
            return;
        }

        ModelDescriptor? descriptor = ModelStore.Catalog.FirstOrDefault(m => m.Alias == alias);
        if (descriptor is null || !ModelStore.IsCached(descriptor))
        {
            ApplyCapabilities(null);
            CapabilityStatus = descriptor is null
                ? string.Empty
                : $"{alias} is not downloaded, so its capabilities are unknown. Every option is shown until it is checked.";
            return;
        }

        if (_probing)
        {
            // One load at a time: the setter fires as the user moves through the
            // list, and stacking loads would fight for memory. The last choice
            // wins once the current load returns.
            _pendingProbeAlias = alias;
            return;
        }

        _ = ProbeAsync(alias, descriptor);
    }

    private async System.Threading.Tasks.Task ProbeAsync(string alias, ModelDescriptor descriptor)
    {
        _probing = true;
        IsCheckingCapabilities = true;
        CapabilityStatus = $"Checking what {alias} supports...";
        try
        {
            string path = ModelStore.CachedPath(descriptor);
            // The shared cache: another tab asking for the same alias joins this
            // load rather than starting a second one.
            ModelCapabilitySnapshot snapshot = await _capabilityCache
                .GetAsync(alias, path).ConfigureAwait(true);

            if (string.Equals(ModelAlias, alias, StringComparison.Ordinal))
            {
                ApplyCapabilities(snapshot);
                CapabilityStatus = string.Empty;
            }
        }
        catch (Exception ex)
        {
            UiLog.Failure($"capability probe for {alias}", ex);
            if (string.Equals(ModelAlias, alias, StringComparison.Ordinal))
            {
                ApplyCapabilities(null);
                CapabilityStatus = $"Could not check {alias}: {ex.Message}. Every option is shown.";
            }
        }
        finally
        {
            _probing = false;
            IsCheckingCapabilities = false;

            string? pending = _pendingProbeAlias;
            _pendingProbeAlias = null;
            if (pending is not null && !string.Equals(pending, alias, StringComparison.Ordinal))
            {
                RequestCapabilityProbe(pending);
            }
        }
    }

    /// <summary>
    /// Applies what a probe reported, including rebuilding the language list.
    /// </summary>
    /// <remarks>
    /// Internal rather than private so the tests can hand it a model report without
    /// downloading one: that is the path where the lists change while the view is
    /// already bound, which is the one the constructor cannot reproduce.
    /// </remarks>
    internal void ApplyCapabilities(ModelCapabilitySnapshot? capabilities)
    {
        _selectedCapabilities = capabilities;
        OnPropertyChanged(nameof(ShowWhisperExtensions));
        OnPropertyChanged(nameof(ShowTranslate));
        OnPropertyChanged(nameof(ShowTargetLanguage));
        OnPropertyChanged(nameof(ShowTask));
        OnPropertyChanged(nameof(ShowLanguage));
        OnPropertyChanged(nameof(ShowLanguageColumn));
        OnPropertyChanged(nameof(ModelColumnSpan));
        OnPropertyChanged(nameof(ShowWindow));
        OnPropertyChanged(nameof(ShowTranscriptSection));
        OnPropertyChanged(nameof(ShowAdvanced));
        OnPropertyChanged(nameof(CanEditModel));
        OnPropertyChanged(nameof(CanEditLanguage));
        OnPropertyChanged(nameof(ShowSpeculativeDecoding));
        OnPropertyChanged(nameof(ShowVocabulary));
        OnPropertyChanged(nameof(ShowSpeakerSection));
        OnPropertyChanged(nameof(CanModelDiarize));
        OnPropertyChanged(nameof(HasSegmentTimings));
        OnPropertyChanged(nameof(CanUseDedicatedDiarizer));
        OnPropertyChanged(nameof(CanAskForSpeakerLabels));
        OnPropertyChanged(nameof(CanProbeThisRole));
        OnPropertyChanged(nameof(CanProbeMemory));
        OnPropertyChanged(nameof(ShowDiarizerPicker));
        OnPropertyChanged(nameof(ShowDiarizerUnavailableHint));
        OnPropertyChanged(nameof(DiarizerUnavailableHint));
        OnPropertyChanged(nameof(DiarizerPlan));
        OnPropertyChanged(nameof(NeedsDedicatedDiarizerDownload));
        OnPropertyChanged(nameof(RunSummary));
        OnPropertyChanged(nameof(PipelineSummary));
        OnPropertyChanged(nameof(RunButtonText));
        OnPropertyChanged(nameof(RolesHint));
        OnPropertyChanged(nameof(ShowAllowedLanguages));
        OnPropertyChanged(nameof(ProgressIsIndeterminate));

        // The capabilities can make the current request impossible (a model with no
        // timings cannot be joined) or satisfiable by the model itself (it can
        // diarize). Align the request with that before the reader starts a run.
        SyncSpeakerIntent();

        OnPropertyChanged(nameof(CanTranscribe));
        OnPropertyChanged(nameof(CanProbeMemory));
        OnPropertyChanged(nameof(RunBlockedReason));
        OnPropertyChanged(nameof(HasRunBlockedReason));
        UpdateAvailableLanguages(capabilities);
        UpdateTargetOptions(capabilities);
    }

    /// <summary>Splits the comma-separated allowed-languages field into codes.</summary>
    private static string[]? ParseAllowed(string text)
    {
        string[] parts = text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? null : parts;
    }

    /// <summary>
    /// Splits the vocabulary box into terms: one per line, trimmed, blank lines
    /// dropped. Order is kept because upstream treats it as a priority order, and
    /// duplicates are not removed here — the box says what the reader typed.
    /// </summary>
    internal static string[]? ParseVocabulary(string text)
    {
        string[] parts = text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? null : parts;
    }

    /// <summary>
    /// Everything the selected controls say about one run. Extracted from the run
    /// itself so the mapping can be asserted without a model.
    /// </summary>
    internal TranscriptionOptions BuildTranscriptionOptions()
        => new()
        {
            // The backend and device chosen in Settings. These were displayed but
            // read by nothing, so the pickers did nothing.
            BackendRequest = _settings.SelectedBackend,
            Device = _settings.SelectedDevice,
            Language = Language,
            // Always AUTO. It is the one value the dispatcher never rejects: the
            // family resolves it to its own maximum. Inferring SEGMENT from
            // capabilities.MaxTimestampKind is not safe: granite-speech-4.1-2b-plus
            // reports WORD (which is numerically finer) and still rejects an
            // explicit SEGMENT with ErrUnsupportedTimestamps. AUTO runs and returns
            // the rows the family actually produces.
            TimestampKind = TimestampKind.TimestampsAuto,
            DiarizeMode = DiarizeMode,
            // A dedicated diarizer only runs when the reader asked for speaker
            // labels and the transcriber cannot do it itself. The picker may still
            // hold a remembered alias while the intent is off; the run ignores it.
            DiarizerModel = LabelSpeakers && !CanModelDiarize ? SelectedDiarizer?.Alias : null,
            // The VAD trim is opt-in; the picker may remember an alias while the
            // option is off, and the run must ignore it then.
            VadModel = UseVad && GoalIsAsr ? SelectedVadModel?.Alias : null,
            Threads = Threads,
            KvType = SelectedKvType,
            ContextSize = ContextSize,
            WhisperInitialPrompt = WhisperInitialPrompt,
            WhisperTemperature = (float?)WhisperTemperature,
            Task = SelectedTask,
            // A target only belongs to a Translate run. Keeping the code while the
            // task is Transcribe would hand the decoder a target it is not using.
            TargetLanguage = SelectedTask == TranscriptionTask.Translate ? TargetLanguage : null,
            SpecKDrafts = SpecKDrafts,
            KeepSpecialTags = KeepSpecialTags,
            WindowSeconds = WindowSeconds,
            AutoAdjustWindow = AutoAdjustWindow,
            AllowedLanguages = ParseAllowed(AllowedLanguages),
            Vocabulary = ParseVocabulary(Vocabulary),
        };

    /// <summary>
    /// Rebuilds the language list from what a model reports, or fills the fallback
    /// when there is nothing to report.
    /// </summary>
    /// <remarks>
    /// The invariant is that the list must never be empty while the view is bound:
    /// a picker with no items cannot match <c>Language</c>, so it drops its own
    /// selection, and once it is empty in the view model too the assignment below
    /// cannot repair a control it never re-pushes to. The constructor seeds the
    /// fallback before the window's DataContext is set for exactly that reason; this
    /// method clears and refills it, and the selection it drops writes back an empty
    /// value, which is what makes <see cref="Language"/> fire again.
    /// </remarks>
    /// <summary>
    /// Whether a downloaded model can produce speaker labels for the
    /// "speech to text + speakers" goal: either it attributes speakers itself or
    /// it reports segment timings that a dedicated diarizer can be joined to.
    /// Unknown (no catalog record) is kept, because unknown is not "no".
    /// </summary>
    private static bool ModelServesSpeakers(string alias)
    {
        var record = UpstreamCatalog.Read(alias);
        if (record?.Capabilities is null)
        {
            return true;
        }

        bool diarizes = record.Capabilities.TryGetValue("diarize", out var d) && d.Supported;
        bool timestamps = record.Capabilities.TryGetValue("timestamps", out var t) && t.Supported;
        return diarizes || timestamps;
    }

    /// <summary>
    /// Whether a downloaded model can serve the chosen language, according to the
    /// upstream catalogue. A model that declares no languages is kept, because
    /// unknown is not the same as "no" — hiding it on a guess would hide one that
    /// works. <c>auto</c> is kept only for models that declare language detection.
    /// </summary>
    private static bool ModelServesLanguage(string alias, string language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return true;
        }

        var record = UpstreamCatalog.Read(alias);
        var languages = record?.Languages;
        if (languages is null || languages.Length == 0)
        {
            // Unknown: keep it, the probe validates at run time.
            return true;
        }

        if (string.Equals(language, "auto", StringComparison.Ordinal))
        {
            return record?.Capabilities is not null
                && record.Capabilities.TryGetValue("lang_detect", out var cap)
                && cap.Supported;
        }

        return languages.Contains(language, StringComparer.Ordinal);
    }

}
