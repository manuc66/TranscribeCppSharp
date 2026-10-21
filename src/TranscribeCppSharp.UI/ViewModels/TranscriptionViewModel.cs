using System.Collections.ObjectModel;
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

public partial class TranscriptionViewModel : ObservableObject
{
    private readonly ITranscriptionService _transcriptionService;

    private readonly SettingsViewModel _settings;

    private readonly ModelCapabilityCache _capabilityCache;

    private ModelCapabilitySnapshot? _selectedCapabilities;

    private bool _probing;

    private string? _pendingProbeAlias;

    /// <summary>The run's cancellation, while one is in flight.</summary>
    private Action? _cancelTranscription;

    /// <summary>
    /// What produced the result on screen. Kept apart from the pickers so exporting
    /// a restored run names the model and settings that actually ran, not whatever
    /// the tab happens to have selected now.
    /// </summary>
    private ResultProvenance? _provenance;

    [ObservableProperty]
    private string _audioPath = string.Empty;

    [ObservableProperty]
    private string _modelAlias = string.Empty;

    [ObservableProperty]
    private string _language = "en";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanTranscribe))]
    [NotifyPropertyChangedFor(nameof(CanCancelTranscription))]
    [NotifyPropertyChangedFor(nameof(CanProbeMemory))]
    [NotifyPropertyChangedFor(nameof(ShowLiveText))]
    private bool _isTranscribing;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>True while the selected model is being loaded to read its capabilities.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanTranscribe))]
    [NotifyPropertyChangedFor(nameof(CanProbeMemory))]
    private bool _isCheckingCapabilities;

    /// <summary>What the capability check is doing, or why options are unfiltered.</summary>
    [ObservableProperty]
    private string _capabilityStatus = string.Empty;

    [ObservableProperty]
    private TranscriptionResult? _result;

    [ObservableProperty]
    private string _fullText = string.Empty;

    [ObservableProperty]
    private ObservableCollection<MergedSegment> _segments = new();

    [ObservableProperty]
    private ObservableCollection<WordResult> _words = new();

    [ObservableProperty]
    private ObservableCollection<SpeakerSegmentResult> _speakerSegments = new();

    [ObservableProperty]
    private ObservableCollection<LangIdSession.LangIdCandidate> _langIdCandidates = new();

    [ObservableProperty]
    private ObservableCollection<VadSession.VadSegmentResult> _vadSegments = new();

    /// <summary>Comma-separated language codes for a LANGID model; empty ranks all.</summary>
    [ObservableProperty]
    private string _allowedLanguages = string.Empty;

    /// <summary>
    /// Custom terms for a model that supports a vocabulary, one per line, in
    /// priority order. Passed as the run's vocabulary list; a model that does not
    /// support it ignores the list with a native warning.
    /// </summary>
    [ObservableProperty]
    private string _vocabulary = string.Empty;

    [ObservableProperty]
    private bool _hasResult;

    /// <summary>
    /// The text of the windows finished so far, while a run is in flight. One
    /// update per window, not word by word: the offline run offers no finer
    /// callback, so the Window setting decides how often this changes.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLiveText))]
    private string _liveText = string.Empty;

    /// <summary>Whether the live preview has something to show.</summary>
    public bool ShowLiveText => IsTranscribing && LiveText.Length > 0;

    /// <summary>
    /// The last failure in full, ready to copy: type, message, native status,
    /// failed method, log path and stack trace. Null after a successful run.
    /// </summary>
    [ObservableProperty]
    private string? _lastError;

    /// <summary>Whether there is an error report to copy.</summary>
    public bool CanCopyError => !string.IsNullOrEmpty(LastError);

    partial void OnLastErrorChanged(string? value) => OnPropertyChanged(nameof(CanCopyError));

    /// <summary>
    /// Insert pause markers into the HTML export. Off, the export has none; the
    /// transcript itself never carries them.
    /// </summary>
    [ObservableProperty]
    private bool _markPauses;

    /// <summary>Shortest silence that counts as a pause, in seconds.</summary>
    [ObservableProperty]
    private int _pauseThresholdSeconds = 1;

    /// <summary>
    /// Longest gap between two inline timestamps in the HTML export. Zero leaves
    /// only the first segment and each speaker change stamped.
    /// </summary>
    [ObservableProperty]
    private int _timestampIntervalSeconds = 60;

    /// <summary>
    /// The speakers in the current result, with the name the reader may give
    /// each. Empty unless the result has more than one speaker or speaker turns.
    /// </summary>
    public ObservableCollection<SpeakerNameEntry> SpeakerNames { get; } = new();

    /// <summary>Whether the speaker-name editor has anything to show.</summary>
    public bool HasSpeakerNames => SpeakerNames.Count > 0;

    public ObservableCollection<string> AvailableModels { get; } = new();
    public ObservableCollection<string> AvailableLanguages { get; } = new();

    [ObservableProperty]
    private int? _threads;

    [ObservableProperty]
    private KvType? _selectedKvType = KvType.KvTypeAuto;

    [ObservableProperty]
    private int? _contextSize;

    [ObservableProperty]
    private string? _whisperInitialPrompt;

    [ObservableProperty]
    private double? _whisperTemperature;

    public ObservableCollection<KvType> KvTypes { get; } = new()
    {
        KvType.KvTypeAuto,
        KvType.KvTypeF32,
        KvType.KvTypeF16,
    };

    [ObservableProperty]
    private TranscriptionTask? _selectedTask;

    [ObservableProperty]
    private string? _targetLanguage;

    /// <summary>
    /// The one choice with no code: the model decides for itself.
    /// </summary>
    /// <remarks>
    /// Cached rather than rebuilt so the picker can be re-seated on the same
    /// instance after the list is rebuilt, without relying on the ComboBox
    /// recognising an equal-but-different record.
    /// </remarks>
    private static readonly TargetOption DefaultTargetOption = new("(model default)", null);

    /// <summary>
    /// Everything the loaded model says it can translate into, plus
    /// <see cref="DefaultTargetOption"/>.
    /// </summary>
    public ObservableCollection<TargetOption> TargetOptions { get; } = new();

    /// <summary>
    /// The row the picker shows. Null until it is seeded.
    /// </summary>
    [ObservableProperty]
    private TargetOption? _selectedTargetOption;

    partial void OnSelectedTargetOptionChanged(TargetOption? value)
        => TargetLanguage = value?.Code;

    [ObservableProperty]
    private int? _specKDrafts;

    [ObservableProperty]
    private bool? _keepSpecialTags;

    [ObservableProperty]
    private int _windowSeconds = 300;

    public ObservableCollection<TranscriptionTask> AvailableTasks { get; } = new()
    {
        TranscriptionTask.Transcribe,
        TranscriptionTask.Translate,
    };

    /// <summary>Whether the Whisper-only controls apply to the selected model.</summary>
    public bool ShowWhisperExtensions => ModelOptionVisibility.ShowWhisperExtensions(_selectedCapabilities);

    /// <summary>Whether the translation controls apply to the selected model.</summary>
    public bool ShowTranslate => ModelOptionVisibility.ShowTranslate(_selectedCapabilities);

    /// <summary>Whether speculative decoding applies to the selected model.</summary>
    public bool ShowSpeculativeDecoding => ModelOptionVisibility.ShowSpeculativeDecoding(_selectedCapabilities);

    /// <summary>Whether the vocabulary field applies to the selected model.</summary>
    public bool ShowVocabulary => ModelOptionVisibility.ShowVocabulary(_selectedCapabilities);

    /// <summary>
    /// Whether the diarization picker applies: a model that transcribes and can also
    /// attribute speakers. A model that only diarizes runs the DIARIZE role, where
    /// this setting has nothing to ask.
    /// </summary>
    public bool ShowDiarize => ModelOptionVisibility.ShowDiarize(_selectedCapabilities);

    /// <summary>Whether the run asks the model to attribute speakers.</summary>
    /// <remarks>
    /// Passed as <c>WithDiarize</c> on an ASR run. It was hardcoded to the model
    /// default, so an ASR model that can diarize — MOSS, granite-speech-4.1-2b-plus —
    /// could not be asked to from the app at all.
    /// </remarks>
    [ObservableProperty]
    private DiarizeMode _diarizeMode = DiarizeMode.DiarizeModeDefault;

    /// <summary>The diarization choices, with the label each shows.</summary>
    public IReadOnlyList<DiarizeOption> DiarizeOptions { get; } = new[]
    {
        new DiarizeOption(DiarizeMode.DiarizeModeDefault, "model default"),
        new DiarizeOption(DiarizeMode.DiarizeModeOff, "off"),
        new DiarizeOption(DiarizeMode.DiarizeModeOn, "on"),
    };

    /// <summary>
    /// The run button's label, named for what the selected model will actually do.
    /// </summary>
    /// <remarks>
    /// A model that only diarizes is not "transcribing", and the button saying
    /// "Transcribe" over a run that returns speaker turns is what made the feature
    /// look unsupported.
    /// </remarks>
    public string RunButtonText
    {
        get
        {
            TranscribeRole roles = _selectedCapabilities?.Roles ?? TranscribeRole.None;
            if (roles.HasFlag(TranscribeRole.Asr))
            {
                return "Transcribe";
            }

            if (roles.HasFlag(TranscribeRole.Diarize))
            {
                return "Diarize";
            }

            if (roles.HasFlag(TranscribeRole.Langid))
            {
                return "Identify language";
            }

            if (roles.HasFlag(TranscribeRole.Vad))
            {
                return "Detect speech";
            }

            return "Transcribe";
        }
    }

    /// <summary>The "no separate diarizer" entry, kept so the picker can reseat it.</summary>
    private static readonly DiarizerOption NoDiarizer = new(null, "(none)");

    /// <summary>
    /// The diarizers that can be paired with the selected model: downloaded models
    /// that serve only the DIARIZE role, plus "none".
    /// </summary>
    public ObservableCollection<DiarizerOption> DiarizerOptions { get; } = new();

    /// <summary>
    /// The diarizer to run after the transcription, or the "none" entry.
    /// </summary>
    [ObservableProperty]
    private DiarizerOption? _selectedDiarizer;

    /// <summary>
    /// Whether the diarizer picker applies: a model that transcribes and cannot
    /// attribute speakers itself.
    /// </summary>
    /// <remarks>
    /// A model that already diarizes has no need of a second one, and a model that
    /// only diarizes has nothing to pair with. Unknown shows nothing here, unlike
    /// the other option groups: the picker is an addition to a run, and offering it
    /// before the model has answered would add a control that may not apply.
    /// </remarks>
    public bool ShowDiarizerPicker
        => _selectedCapabilities is { } caps
        && caps.Roles.HasFlag(TranscribeRole.Asr)
        && !caps.Supports(Feature.FeatureDiarization)
        && !caps.Roles.HasFlag(TranscribeRole.Diarize)
        && caps.MaxTimestampKind >= TimestampKind.TimestampsSegment;

    /// <summary>
    /// Whether the tab has to explain why no diarizer can be chained: the model
    /// transcribes, cannot attribute speakers, and reports no segment timings.
    /// </summary>
    /// <remarks>
    /// Without timings there is nothing to overlap a speaker turn with, so the join
    /// would silently leave every segment as Speaker 0. Hiding the picker silently
    /// would be just as misleading, hence the note in its place.
    /// </remarks>
    public bool ShowDiarizerUnavailableHint
        => _selectedCapabilities is { } caps
        && caps.Roles.HasFlag(TranscribeRole.Asr)
        && !caps.Supports(Feature.FeatureDiarization)
        && !caps.Roles.HasFlag(TranscribeRole.Diarize)
        && caps.MaxTimestampKind < TimestampKind.TimestampsSegment;

    /// <summary>Why the diarizer picker is absent, in the reader's terms.</summary>
    public string DiarizerUnavailableHint
    {
        get
        {
            string model = string.IsNullOrEmpty(ModelAlias) ? "This model" : ModelAlias;
            return $"{model} reports no segment timings, so a separate diarizer cannot be "
                + "joined to it: there is nothing to overlap a speaker turn with. Pick a model "
                + "that emits timestamps, or one that attributes speakers itself.";
        }
    }

    /// <summary>
    /// What the run will actually do about speaker attribution, said before it
    /// starts rather than inferred from two controls with similar names.
    /// </summary>
    public string DiarizerPlan
    {
        get
        {
            if (!ShowDiarizerPicker)
            {
                return string.Empty;
            }

            if (SelectedDiarizer?.Alias is not { } alias)
            {
                return "No separate diarizer: every segment stays Speaker 0.";
            }

            string model = string.IsNullOrEmpty(ModelAlias) ? "the selected model" : ModelAlias;
            return $"Two passes: {model}, then {alias} over the same audio. "
                + "The second pass adds speaker turns, not text.";
        }
    }

    /// <summary>
    /// A warning when the diarizer's catalog record does not declare the chosen
    /// language. Empty while no diarizer is chosen, the model declares nothing, or
    /// the language is left to detection.
    /// </summary>
    public string DiarizerLanguageWarning
    {
        get
        {
            if (SelectedDiarizer is not { Alias: not null, Languages: { Count: > 0 } languages } option)
            {
                return string.Empty;
            }

            if (Language is "auto" or "")
            {
                return string.Empty;
            }

            return languages.Contains(Language, StringComparer.OrdinalIgnoreCase)
                ? string.Empty
                : $"{option.Label} declares {string.Join(", ", languages)} only; "
                    + $"the selected language is {Language}.";
        }
    }

    /// <summary>Whether <see cref="DiarizerLanguageWarning"/> has something to say.</summary>
    public bool HasDiarizerLanguageWarning => DiarizerLanguageWarning.Length > 0;

    /// <summary>
    /// The work the selected model serves (transcribe.cpp 0.4 roles), shown under
    /// the picker so the tab says what it will run. Empty until the model answers.
    /// </summary>
    public string RolesHint
    {
        get
        {
            TranscribeRole roles = _selectedCapabilities?.Roles ?? TranscribeRole.None;
            if (roles == TranscribeRole.None)
            {
                return string.Empty;
            }

            var names = new List<string>();
            if (roles.HasFlag(TranscribeRole.Asr))
            {
                names.Add("transcription");
            }

            if (roles.HasFlag(TranscribeRole.Diarize))
            {
                names.Add("diarization");
            }

            if (roles.HasFlag(TranscribeRole.Langid))
            {
                names.Add("language identification");
            }

            if (roles.HasFlag(TranscribeRole.Vad))
            {
                names.Add("voice activity");
            }

            return "runs: " + string.Join(", ", names);
        }
    }

    /// <summary>Whether the selected model's roles include language identification.</summary>
    public bool ShowLangIdTab => Result?.Roles.HasFlag(TranscribeRole.Langid) == true;

    /// <summary>Whether the selected model's roles include voice activity detection.</summary>
    public bool ShowVadTab => Result?.Roles.HasFlag(TranscribeRole.Vad) == true;

    /// <summary>Whether the allowed-languages field applies (the model serves LANGID).</summary>
    public bool ShowAllowedLanguages => _selectedCapabilities?.Roles.HasFlag(TranscribeRole.Langid) == true;

    /// <summary>False while a transcription or a capability check is running.</summary>
    public bool CanTranscribe
        => HasDownloadedModels && !IsTranscribing && !IsCheckingCapabilities;

    /// <summary>True while a run is in flight and can still be stopped.</summary>
    public bool CanCancelTranscription => IsTranscribing;

    /// <summary>
    /// What can be said about memory before a run: the weights do not fit, or a
    /// previous run of the same configuration went lower than the current free
    /// figure. Empty when there is nothing factual to say.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMemoryWarning))]
    private string _memoryWarning = string.Empty;

    /// <summary>Whether <see cref="MemoryWarning"/> has something to say.</summary>
    public bool HasMemoryWarning => MemoryWarning.Length > 0;

    /// <summary>True while the one-window memory probe is running.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanProbeMemory))]
    private bool _isProbingMemory;

    /// <summary>The probe's outcome, and what it does and does not measure.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMemoryProbeStatus))]
    private string _memoryProbeStatus = string.Empty;

    /// <summary>Whether <see cref="MemoryProbeStatus"/> has something to say.</summary>
    public bool HasMemoryProbeStatus => MemoryProbeStatus.Length > 0;

    /// <summary>Whether the memory probe can run: the same conditions as a run, and not already probing.</summary>
    public bool CanProbeMemory => CanTranscribe && !IsProbingMemory;

    /// <summary>Whether any model is on disk, which is what the picker offers.</summary>
    public bool HasDownloadedModels => AvailableModels.Count > 0;

    /// <summary>
    /// What sits under the model picker.
    /// </summary>
    /// <remarks>
    /// Present even when the list is full: a picker offering one model does not tell
    /// you the other 71 exist, so the route to them is always on screen rather than
    /// only when the list is empty.
    /// </remarks>
    public string ModelPickerHint
        => DownloadedModelCatalog.Hint(AvailableModels.Count, ModelStore.Catalog.Count);

    /// <summary>Raised when the reader asks to go and fetch more models.</summary>
    public event Action? OpenModelManagerRequested;

    /// <summary>The button under the picker. The window decides which tab that means.</summary>
    [RelayCommand]
    private void OpenModelManager() => OpenModelManagerRequested?.Invoke();

    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    /// <summary>
    /// What the Browse dialog offers, and what a drop accepts.
    /// </summary>
    /// <remarks>
    /// Video containers are here because the decoder is ffmpeg and it takes the
    /// audio track out of one with the same command it uses for an audio file: the
    /// output format is raw PCM, so no video stream is selected. A file with no
    /// audio track still fails, in the loader, with ffmpeg's own error — the
    /// extensions are what the picker filters on, not a claim about the file.
    /// Every extension in this list was decoded with that command (ffmpeg 9.0.1)
    /// rather than assumed from the container name; a WAV is read directly.
    /// </remarks>
    private static readonly string[] AudioPatterns =
    [
        "*.wav", "*.mp3", "*.flac", "*.ogg", "*.m4a", "*.aac", "*.opus", "*.wma",
        "*.mp4", "*.m4v", "*.mkv", "*.mov", "*.avi", "*.webm", "*.3gp",
    ];
    private static readonly string[] TextPatterns = ["*.txt"];
    private static readonly string[] VttPatterns = ["*.vtt"];
    private static readonly string[] JsonPatterns = ["*.json"];
    private static readonly string[] HtmlPatterns = ["*.html"];
    private static readonly string[] MarkdownPatterns = ["*.md"];

    public TranscriptionViewModel(
        ITranscriptionService transcriptionService,
        SettingsViewModel settings,
        ModelCapabilityCache capabilityCache)
    {
        _transcriptionService = transcriptionService;
        _settings = settings;
        _capabilityCache = capabilityCache;

        // The memory warning depends on the backend and the pinned device, which
        // live on the Settings tab. The event is the only way this tab learns that
        // they changed.
        _settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(SettingsViewModel.SelectedBackend)
                or nameof(SettingsViewModel.SelectedDevice))
            {
                UpdateMemoryWarning();
            }
        };

        // Restore the export options before anything can change them: the partial
        // change handlers save on every edit, and assigning through the generated
        // properties here would write the file once per field on every launch.
        ExportSettings export = UiSettings.LoadExportSettings();
        _markPauses = export.MarkPauses;
        _pauseThresholdSeconds = export.PauseThresholdSeconds;
        _timestampIntervalSeconds = export.TimestampIntervalSeconds;

        // Both pickers are seeded first, before LoadModels: a ComboBox bound to an
        // empty list has nothing to match, and the reader sees a blank box until
        // something answers.
        TargetOptions.Add(DefaultTargetOption);
        SelectedTargetOption = DefaultTargetOption;

        // Seeded the same way: the picker must have an entry to match before
        // LoadModels rebuilds the list from what is on disk.
        DiarizerOptions.Add(NoDiarizer);
        SelectedDiarizer = NoDiarizer;

        // The language list is seeded first, before LoadModels: that call can start
        // the capability probe, and a probe on a cache with nothing in it answers
        // synchronously by clearing this list and refilling it. Seeding afterwards
        // would append a second copy of every code.
        AddFallbackLanguages();

        LoadModels();
        // The alias the picker settled on (the one last run, else the first),
        // bounds the startup probe to a single known model. It runs off the UI
        // thread; a model that is not on disk is not loaded and the options stay
        // unfiltered.
        RequestCapabilityProbe(ModelAlias);
        UpdateMemoryWarning();
    }

    /// <summary>
    /// Rebuilds the picker from what is on disk and repairs the selection.
    /// </summary>
    /// <remarks>
    /// The selection has to be repaired rather than left alone: the picker only
    /// offers downloaded models, so an alias that was deleted (or never fetched)
    /// would show as no selection while still letting a run start against a file
    /// that is not there. With nothing on disk the alias is emptied, which is what
    /// disables the button.
    /// <para>
    /// The repaired selection is the model this tab last ran when it is still
    /// offered, so reopening the app lands on the model you were using; see
    /// <see cref="DownloadedModelCatalog.Selection"/>.
    /// </para>
    /// </remarks>
    private void LoadModels()
    {
        AvailableModels.Clear();
        foreach (string alias in DownloadedModelCatalog.Aliases())
        {
            AvailableModels.Add(alias);
        }

        ModelAlias = DownloadedModelCatalog.Selection(
            ModelAlias, AvailableModels, UiSettings.LastModel(UiSettings.TranscriptionTab));

        LoadDiarizers();

        OnPropertyChanged(nameof(ModelPickerHint));
        OnPropertyChanged(nameof(HasDownloadedModels));
        OnPropertyChanged(nameof(CanTranscribe));
        OnPropertyChanged(nameof(CanProbeMemory));
    }

    /// <summary>
    /// Rebuilds the diarizer picker from what is on disk, keeping the choice when it
    /// is still offered.
    /// </summary>
    private void LoadDiarizers()
    {
        DiarizerOption? keep = SelectedDiarizer;

        DiarizerOptions.Clear();
        DiarizerOptions.Add(NoDiarizer);
        foreach (string alias in DownloadedModelCatalog.DiarizerAliases())
        {
            IReadOnlyList<string> languages = UpstreamCatalog.Read(alias)?.Languages ?? [];
            DiarizerOptions.Add(new DiarizerOption(alias, alias, languages));
        }

        // A remembered alias that is no longer offered — deleted — falls back to
        // "none" rather than leaving the picker on something that is not there.
        SelectedDiarizer = keep is not null
            ? DiarizerOptions.FirstOrDefault(o => o.Alias == keep.Alias) ?? NoDiarizer
            : NoDiarizer;
    }

    /// <summary>
    /// Re-reads the cache. Called when the reader comes back from the Models tab,
    /// which is where a download or a delete happens.
    /// </summary>
    public void RefreshModels() => LoadModels();

    partial void OnModelAliasChanged(string value)
    {
        RequestCapabilityProbe(value);
        OnPropertyChanged(nameof(DiarizerPlan));
        UpdateMemoryWarning();
    }

    partial void OnWindowSecondsChanged(int value) => UpdateMemoryWarning();

    partial void OnSelectedDiarizerChanged(DiarizerOption? value)
    {
        OnPropertyChanged(nameof(DiarizerPlan));
        OnPropertyChanged(nameof(DiarizerLanguageWarning));
        OnPropertyChanged(nameof(HasDiarizerLanguageWarning));
    }

    partial void OnLanguageChanged(string value)
    {
        OnPropertyChanged(nameof(DiarizerLanguageWarning));
        OnPropertyChanged(nameof(HasDiarizerLanguageWarning));
    }

    partial void OnMarkPausesChanged(bool value) => PersistExportOptions();

    partial void OnPauseThresholdSecondsChanged(int value) => PersistExportOptions();

    partial void OnTimestampIntervalSecondsChanged(int value) => PersistExportOptions();

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
        OnPropertyChanged(nameof(ShowSpeculativeDecoding));
        OnPropertyChanged(nameof(ShowVocabulary));
        OnPropertyChanged(nameof(ShowDiarize));
        OnPropertyChanged(nameof(ShowDiarizerPicker));
        OnPropertyChanged(nameof(ShowDiarizerUnavailableHint));
        OnPropertyChanged(nameof(DiarizerUnavailableHint));
        OnPropertyChanged(nameof(DiarizerPlan));
        OnPropertyChanged(nameof(RunButtonText));
        OnPropertyChanged(nameof(RolesHint));
        OnPropertyChanged(nameof(ShowAllowedLanguages));
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
            TimestampKind = TimestampKind.TimestampsSegment,
            DiarizeMode = DiarizeMode,
            DiarizerModel = SelectedDiarizer?.Alias,
            Threads = Threads,
            KvType = SelectedKvType,
            ContextSize = ContextSize,
            WhisperInitialPrompt = WhisperInitialPrompt,
            WhisperTemperature = (float?)WhisperTemperature,
            Task = SelectedTask,
            TargetLanguage = TargetLanguage,
            SpecKDrafts = SpecKDrafts,
            KeepSpecialTags = KeepSpecialTags,
            WindowSeconds = WindowSeconds,
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
    private void UpdateAvailableLanguages(ModelCapabilitySnapshot? capabilities)
    {
        AvailableLanguages.Clear();
        if (capabilities == null)
        {
            AddFallbackLanguages();
            return;
        }

        if (capabilities.SupportsLanguageDetect)
        {
            AvailableLanguages.Add("auto");
        }

        foreach (var lang in capabilities.Languages.Where(l => !string.IsNullOrEmpty(l) && !AvailableLanguages.Contains(l)))
        {
            AvailableLanguages.Add(lang);
        }

        if (AvailableLanguages.Count == 0)
        {
            AvailableLanguages.Add("en");
        }

        if (!AvailableLanguages.Contains(Language))
        {
            Language = AvailableLanguages.First();
        }
    }

    /// <summary>
    /// Rebuilds the target-language choices from what a model says it translates
    /// into, or leaves the single default when nothing has answered.
    /// </summary>
    /// <remarks>
    /// The selection is re-seated explicitly rather than left alone. The list is
    /// rebuilt with new objects, so the ComboBox's selection is dropped, and
    /// reassigning an equal-but-different record would not fire the setter that
    /// restores it. Going through null first guarantees the transition; the value
    /// that matters is the one assigned last, so a transient unset is never seen by
    /// a run — nothing runs while a model is still being probed.
    /// <para>
    /// A choice the model does not declare is dropped rather than kept: the native
    /// library rejects a target outside the model's list, so keeping it would hand
    /// the reader a value that cannot work.
    /// </para>
    /// </remarks>
    private void UpdateTargetOptions(ModelCapabilitySnapshot? capabilities)
    {
        string? current = TargetLanguage;

        TargetOptions.Clear();
        TargetOptions.Add(DefaultTargetOption);
        if (capabilities is not null)
        {
            foreach (string code in capabilities.TranslateTargetLanguages)
            {
                if (!string.IsNullOrEmpty(code))
                {
                    TargetOptions.Add(new TargetOption(code, code));
                }
            }
        }

        SelectedTargetOption = null;
        SelectedTargetOption = TargetOptions.FirstOrDefault(o => o.Code == current)
            ?? DefaultTargetOption;
    }

    /// <summary>
    /// The languages offered before a model has answered.
    /// </summary>
    /// <remarks>
    /// Called twice: once in the constructor, so the picker has items before the
    /// window's DataContext is set, and again when a model cannot be probed — not
    /// downloaded, or the probe failed. It is a fixed list because nothing has said
    /// otherwise yet; once a model reports, its own languages replace it.
    /// </remarks>
    private void AddFallbackLanguages()
    {
        AvailableLanguages.Add("en");
        AvailableLanguages.Add("fr");
        AvailableLanguages.Add("de");
        AvailableLanguages.Add("es");
        AvailableLanguages.Add("it");
        AvailableLanguages.Add("pt");
        AvailableLanguages.Add("ru");
        AvailableLanguages.Add("zh");
        AvailableLanguages.Add("ja");
        AvailableLanguages.Add("ko");
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task BrowseAudioAsync()
    {
        var window = Avalonia.Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;

        if (window == null) return;

        var files = await window.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "Select audio file",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new Avalonia.Platform.Storage.FilePickerFileType("Audio files")
                {
                    Patterns = AudioPatterns
                }
            }
        });

        if (files.Count > 0)
        {
            AudioPath = Avalonia.Platform.Storage.StorageProviderExtensions.TryGetLocalPath(files[0]) ?? string.Empty;
        }
    }

    /// <summary>
    /// Takes the first dropped path this tab can run, and returns whether it did.
    /// </summary>
    /// <remarks>
    /// The extension filter is the picker's own list, so a drop and the Browse
    /// dialog accept the same files. The file must exist: a drop of a path that
    /// has been moved or deleted would otherwise become an audio path that fails
    /// only when the run starts. Returning false lets the caller leave the drop
    /// unhandled instead of silently swallowing something it cannot use.
    /// </remarks>
    public bool TryAcceptDroppedFile(string? path)
    {
        if (!CanAcceptDroppedFile(path))
        {
            return false;
        }

        AudioPath = path!;
        return true;
    }

    /// <summary>
    /// Whether a drop would be accepted, without touching the current audio path.
    /// The view uses it to show a copy cursor over a file it can take.
    /// </summary>
    public static bool CanAcceptDroppedFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        string extension = Path.GetExtension(path);
        return AudioPatterns.Any(
            pattern => string.Equals(pattern, "*" + extension, StringComparison.OrdinalIgnoreCase));
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task TranscribeAsync()
    {
        if (string.IsNullOrWhiteSpace(AudioPath) || !File.Exists(AudioPath))
        {
            StatusMessage = "Please select a valid audio file.";
            return;
        }

        // The CTS is owned by this method and cancelled through the action; the
        // field is null outside a run, so the view model itself owns nothing.
        using var cancellation = new CancellationTokenSource();
        _cancelTranscription = cancellation.Cancel;
        IsTranscribing = true;
        Progress = 0;
        LiveText = string.Empty;
        LastError = null;
        StatusMessage = "Transcribing...";
        HasResult = false;
        UiSettings.SetLastModel(UiSettings.TranscriptionTab, ModelAlias);

        try
        {
            var options = BuildTranscriptionOptions();

            // Progress<T> posts to the UI context. The service reports from the
            // worker thread that runs the windows, and from a continuation that
            // used ConfigureAwait(false), so updating the bindings inline would be
            // a cross-thread mutation.
            var progress = new Progress<double>(p => Progress = p);
            var liveText = new Progress<string>(text => LiveText = text);
            var phase = new Progress<TranscriptionPhase>(p => StatusMessage = p switch
            {
                TranscriptionPhase.Diarizing =>
                    "Diarizing… the transcript is complete; a separate diarizer is attributing speakers.",
                _ => "Transcribing...",
            });

            Result = await _transcriptionService.TranscribeAsync(
                AudioPath, ModelAlias, options, progress, liveText, phase, cancellation.Token);

            ShowResult(
                Result,
                new ResultProvenance(ModelAlias, EffectiveLanguage(), DiarizeLabel(), WindowSeconds));
            StatusMessage = "Transcription complete.";
            TrySaveHistory();
            if (Result.Memory is { } observation)
            {
                SaveMemoryObservation(observation);
            }

            UpdateMemoryWarning();
        }
        catch (OperationCanceledException)
        {
            // The wrapper turns the native abort into cancellation before it reads
            // the partial result, so there is nothing to show. Say that rather than
            // leaving a half-filled tab or logging a failure the reader caused.
            StatusMessage = "Transcription cancelled. The partial result was discarded.";
        }
        catch (TranscribeException ex)
        {
            // A native status is more useful than a sentence, and an out-of-memory
            // run has a next step worth naming. The details stay copyable.
            LastError = BuildErrorReport(ex, "transcription run");
            UiLog.Failure("transcription run", ex);
            string model = string.IsNullOrEmpty(ModelAlias) ? "the selected model" : ModelAlias;
            StatusMessage = ex.StatusCode == Status.ErrOom
                ? $"Out of memory (ErrOom) while running {model}: lower Window (now {WindowSeconds} s), "
                    + "use a smaller quantization or context size, or force the CPU backend in Settings. "
                    + $"(logged to {UiLog.FilePath})"
                : $"Error: {ex.Message} (logged to {UiLog.FilePath})";
        }
        catch (Exception ex)
        {
            LastError = BuildErrorReport(ex, "transcription run");
            UiLog.Failure("transcription run", ex);
            StatusMessage = $"Error: {ex.Message} (logged to {UiLog.FilePath})";
        }
        finally
        {
            _cancelTranscription = null;
            LiveText = string.Empty;
            IsTranscribing = false;
        }
    }

    /// <summary>Stops the run in flight, if any.</summary>
    [RelayCommand]
    private void CancelTranscription() => _cancelTranscription?.Invoke();

    /// <summary>
    /// Copies the last failure to the clipboard. The status line is a sentence; the
    /// report is what a bug report or a developer needs, including the native
    /// status and the log path.
    /// </summary>
    [RelayCommand]
    private async System.Threading.Tasks.Task CopyErrorAsync()
    {
        if (string.IsNullOrEmpty(LastError))
        {
            return;
        }

        var window = Avalonia.Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;

        if (window?.Clipboard is null)
        {
            StatusMessage = "Could not copy: the clipboard is unavailable.";
            return;
        }

        try
        {
            await window.Clipboard.SetTextAsync(LastError);
            StatusMessage = "Error details copied to the clipboard.";
        }
        catch (Exception ex)
        {
            // A desktop clipboard can refuse the write (no clipboard owner, a
            // session without a display); that must not take the window down.
            UiLog.Failure("clipboard copy", ex);
            StatusMessage = $"Could not copy: {ex.Message} (logged to {UiLog.FilePath})";
        }
    }

    /// <summary>
    /// Runs one window of the selected file with the selected settings, without
    /// producing a transcript, and reports whether it held.
    /// </summary>
    /// <remarks>
    /// This is the closest thing to a prediction the app can offer for the compute
    /// buffer: it measures the real allocation instead of estimating it. It costs
    /// one window of compute, and it does not test later windows or the diarizer.
    /// </remarks>
    [RelayCommand]
    private async System.Threading.Tasks.Task ProbeMemoryAsync()
    {
        if (string.IsNullOrWhiteSpace(AudioPath) || !File.Exists(AudioPath))
        {
            StatusMessage = "Please select a valid audio file.";
            return;
        }

        if (!CanProbeMemory)
        {
            return;
        }

        IsProbingMemory = true;
        MemoryProbeStatus = string.Empty;
        StatusMessage = $"Testing one window (up to {WindowSeconds} s)…";

        try
        {
            MemoryProbeResult result = await _transcriptionService.ProbeMemoryAsync(
                AudioPath, ModelAlias, BuildTranscriptionOptions());

            if (result.Succeeded)
            {
                if (result.Observation is { } observation)
                {
                    SaveMemoryObservation(observation);
                }

                MemoryProbeStatus = result.Observation is { } measured
                    ? $"Holds: {measured.DeviceName} went down to "
                        + $"{MemoryPreflight.FormatBytes(measured.MinimumFreeBytes)} free. "
                        + "That is a measurement of one window, not a guarantee for the whole file."
                    : "Holds: the window completed, but this device reports no free-memory figure.";
                StatusMessage = "Memory probe finished.";
            }
            else if (result.OutOfMemory)
            {
                MemoryProbeStatus = $"Did not hold (ErrOom): lower Window (now {WindowSeconds} s), "
                    + "use a smaller quantization or context size, or force the CPU backend in Settings.";
                StatusMessage = "Memory probe: out of memory.";
            }
            else
            {
                MemoryProbeStatus = $"The probe failed: {result.Message}";
                StatusMessage = "Memory probe failed.";
            }

            UpdateMemoryWarning();
        }
        catch (Exception ex)
        {
            UiLog.Failure("memory probe", ex);
            MemoryProbeStatus = $"The probe failed: {ex.Message}";
            StatusMessage = "Memory probe failed.";
        }
        finally
        {
            IsProbingMemory = false;
        }
    }

    /// <summary>The full report, with what the status line has no room for.</summary>
    private string BuildErrorReport(Exception exception, string what)
    {
        var report = new System.Text.StringBuilder();
        report.AppendLine(System.Globalization.CultureInfo.InvariantCulture,
            $"{what} failed at {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        report.AppendLine(System.Globalization.CultureInfo.InvariantCulture,
            $"exception: {exception.GetType().FullName}");
        report.AppendLine(System.Globalization.CultureInfo.InvariantCulture,
            $"message: {exception.Message}");
        report.AppendLine("run:");
        report.Append(DescribeRun());

        if (exception is TranscribeException native)
        {
            report.AppendLine(System.Globalization.CultureInfo.InvariantCulture,
                $"native status: {native.StatusCode} ({(int)native.StatusCode})");
            report.AppendLine(System.Globalization.CultureInfo.InvariantCulture,
                $"failed method: {native.FailedMethod ?? "unknown"}");
        }

        if (exception.Data["memory"] is MemoryObservation observed)
        {
            report.AppendLine(System.Globalization.CultureInfo.InvariantCulture,
                $"device at failure: {observed.DeviceName} — "
                + $"{MemoryPreflight.FormatBytes(observed.MinimumFreeBytes)} free of "
                + $"{MemoryPreflight.FormatBytes(observed.TotalBytes)}");
        }

        if (exception.Data["session limits"] is Session.SessionLimitsInfo limits)
        {
            report.AppendLine(System.Globalization.CultureInfo.InvariantCulture,
                $"session limits: n_ctx {limits.EffectiveNCtx}, "
                + $"KV {MemoryPreflight.FormatBytes((ulong)Math.Max(0, limits.MaxKvBytes))}, "
                + $"max audio {limits.EffectiveMaxAudioMs / 1000} s");
        }

        if (exception.Data["window"] is string window)
        {
            report.AppendLine(System.Globalization.CultureInfo.InvariantCulture,
                $"failed at window: {window}");
        }

        report.AppendLine(System.Globalization.CultureInfo.InvariantCulture,
            $"log: {UiLog.FilePath}");
        report.AppendLine();
        report.Append(exception);
        return report.ToString();
    }

    /// <summary>
    /// The settings that were used for the failed run. Without them a native status
    /// is a dead end: the model, window and backend are what decide whether an
    /// allocation fits.
    /// </summary>
    private string DescribeRun()
    {
        var run = new System.Text.StringBuilder();
        run.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"  model: {ModelAlias}");
        run.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"  window: {WindowSeconds} s");
        run.AppendLine(System.Globalization.CultureInfo.InvariantCulture,
            $"  kv type: {SelectedKvType?.ToString() ?? "auto"}");
        run.AppendLine(System.Globalization.CultureInfo.InvariantCulture,
            $"  context size: {(ContextSize?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "model default")}");
        run.AppendLine(System.Globalization.CultureInfo.InvariantCulture,
            $"  backend: {_settings.SelectedBackend}");
        run.AppendLine(System.Globalization.CultureInfo.InvariantCulture,
            $"  device: {_settings.SelectedDevice?.Name ?? "(the backend chooses)"}");

        BackendDevice? target;
        string when;
        try
        {
            // The report is written after the failure: re-read the devices so it
            // says what the machine looked like then, not what a stale refresh said.
            IReadOnlyList<BackendDevice> live = Backends.EnumerateDevices();
            BackendDevice? pinned = _settings.SelectedDevice;
            if (pinned is not null)
            {
                pinned = live.FirstOrDefault(d => d.Handle == pinned.Handle && d.Handle != IntPtr.Zero)
                    ?? pinned;
            }

            target = MemoryPreflight.Target(live, _settings.SelectedBackend, pinned);
            when = "live at report time";
        }
        catch (Exception ex) when (ex is TranscribeException or DllNotFoundException or EntryPointNotFoundException)
        {
            target = MemoryPreflight.Target(
                _settings.Devices, _settings.SelectedBackend, _settings.SelectedDevice);
            when = "as of the last device refresh";
        }

        run.AppendLine(System.Globalization.CultureInfo.InvariantCulture,
            $"  target device: {MemoryPreflight.Describe(target)} ({when})");
        run.AppendLine(System.Globalization.CultureInfo.InvariantCulture,
            $"  diarizer: {SelectedDiarizer?.Alias ?? "(none)"}");
        return run.ToString();
    }

    /// <summary>Puts a result into the views and grids the tab shows.</summary>
    private void ShowResult(TranscriptionResult result, ResultProvenance provenance)
    {
        Result = result;
        _provenance = provenance;
        FullText = result.FullText;
        Segments = new ObservableCollection<MergedSegment>(result.Segments);
        Words = new ObservableCollection<WordResult>(result.Words);
        SpeakerSegments = new ObservableCollection<SpeakerSegmentResult>(result.SpeakerSegments);
        LangIdCandidates = new ObservableCollection<LangIdSession.LangIdCandidate>(result.LangIdCandidates);
        VadSegments = new ObservableCollection<VadSession.VadSegmentResult>(result.VadSegments);
        OnPropertyChanged(nameof(ShowLangIdTab));
        OnPropertyChanged(nameof(ShowVadTab));
        HasResult = true;
    }

    /// <summary>
    /// Reopens a saved run as if it had just finished. Nothing is transcribed and
    /// nothing is saved again; the entry it came from already holds it.
    /// </summary>
    public void Restore(TranscriptionResult result, HistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(entry);

        AudioPath = entry.AudioPath;
        ShowResult(
            result,
            new ResultProvenance(entry.ModelAlias, entry.Language, entry.Diarize, entry.WindowSeconds));
        string name = string.IsNullOrEmpty(entry.AudioPath)
            ? "the saved result"
            : Path.GetFileName(entry.AudioPath);
        StatusMessage = $"Restored {name} from history.";
    }

    /// <summary>
    /// Writes the finished run to the local history. A disk problem is reported
    /// but does not turn a successful transcription into a failed one.
    /// </summary>
    private void TrySaveHistory()
    {
        if (Result is null)
        {
            return;
        }

        ResultProvenance provenance = Provenance();
        try
        {
            TranscriptionHistory.Save(
                AudioPath, provenance.ModelAlias, provenance.Language, provenance.Diarize,
                provenance.WindowSeconds, Result);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            UiLog.Failure("history save", ex);
            StatusMessage = $"Transcription complete, but the history could not be saved: {ex.Message}";
        }
    }

    /// <summary>
    /// Recomputes the warning shown before a run. It states only two facts: whether
    /// the weight file fits in the target device's free memory, and whether a
    /// previous run of this exact configuration went lower than the free memory now.
    /// </summary>
    /// <remarks>
    /// The compute buffer's size is not exposed by the native API, so nothing here
    /// estimates it. A run whose weights fit can still run out of memory; that is
    /// why the second sentence is a measurement from an earlier run, not a promise.
    /// </remarks>
    private void UpdateMemoryWarning()
    {
        BackendDevice? target = MemoryPreflight.Target(
            _settings.Devices, _settings.SelectedBackend, _settings.SelectedDevice);
        if (target is not { MemoryFree: > 0 })
        {
            MemoryWarning = string.Empty;
            return;
        }

        long modelBytes = ModelSize(ModelAlias);
        if (modelBytes > 0 && !MemoryPreflight.WeightsFit(modelBytes, target))
        {
            MemoryWarning = $"The model file is {MemoryPreflight.FormatBytes((ulong)modelBytes)}; "
                + $"{target.Name} has {MemoryPreflight.FormatBytes(target.MemoryFree)} free. "
                + "The weights alone do not fit — pin another device in Settings, or choose a smaller quantization.";
            return;
        }

        string key = MemoryPreflight.RunKey(
            ModelAlias, WindowSeconds, _settings.SelectedBackend, target.Name);
        MemoryObservation? measured = UiSettings.LoadMemoryObservation(key);
        MemoryWarning = measured is not null && target.MemoryFree < measured.MinimumFreeBytes
            ? $"A previous run of this configuration went down to "
                + $"{MemoryPreflight.FormatBytes(measured.MinimumFreeBytes)} free on {target.Name}; "
                + $"{MemoryPreflight.FormatBytes(target.MemoryFree)} is free now. Lower Window or pin another device."
            : string.Empty;
    }

    /// <summary>The pinned file size from the manifest, or -1 when the alias is unknown.</summary>
    private static long ModelSize(string alias)
        => string.IsNullOrEmpty(alias)
            ? -1
            : ModelStore.Catalog.FirstOrDefault(model => model.Alias == alias)?.Size ?? -1;

    /// <summary>Records what a run or a probe observed about its device, for the next warning.</summary>
    private void SaveMemoryObservation(MemoryObservation observation)
    {
        UiSettings.SaveMemoryObservation(
            MemoryPreflight.RunKey(
                ModelAlias, WindowSeconds, _settings.SelectedBackend, observation.DeviceName),
            observation);
    }

    /// <summary>
    /// The run the shown result came from: the recorded one when it exists, else
    /// what the pickers say. Used by the exports so a restored run is described
    /// truthfully.
    /// </summary>
    private ResultProvenance Provenance()
    {
        if (_provenance is not { } provenance)
        {
            return new ResultProvenance(ModelAlias, EffectiveLanguage(), DiarizeLabel(), WindowSeconds);
        }

        return provenance with
        {
            Language = string.IsNullOrEmpty(provenance.Language)
                ? EffectiveLanguage()
                : provenance.Language,
        };
    }

    /// <summary>What produced the result currently shown.</summary>
    private readonly record struct ResultProvenance(
        string ModelAlias,
        string Language,
        string Diarize,
        int WindowSeconds);

    [RelayCommand]
    private async System.Threading.Tasks.Task ExportAsync()
    {
        if (Result == null) return;

        var window = Avalonia.Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;

        if (window == null) return;

        try
        {
            var file = await window.StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
            {
                Title = "Export transcript",
                DefaultExtension = "txt",
                FileTypeChoices = new[]
                {
                    new Avalonia.Platform.Storage.FilePickerFileType("Text") { Patterns = TextPatterns },
                    new Avalonia.Platform.Storage.FilePickerFileType("WebVTT") { Patterns = VttPatterns },
                    new Avalonia.Platform.Storage.FilePickerFileType("JSON") { Patterns = JsonPatterns },
                    new Avalonia.Platform.Storage.FilePickerFileType("HTML") { Patterns = HtmlPatterns },
                    new Avalonia.Platform.Storage.FilePickerFileType("Markdown") { Patterns = MarkdownPatterns }
                }
            });

            if (file == null) return;

            var path = Avalonia.Platform.Storage.StorageProviderExtensions.TryGetLocalPath(file);
            if (string.IsNullOrWhiteSpace(path))
            {
                StatusMessage = "Could not export: the chosen location has no local path.";
                return;
            }

            await ExportToAsync(path);
        }
        catch (Exception ex)
        {
            // A failed write is the reader's disk (full, read-only, gone), not a
            // reason for the whole window to come down. Say what happened, keep
            // the transcript on screen, and leave the details in the log.
            UiLog.Failure("export", ex);
            StatusMessage = $"Could not export: {ex.Message} (logged to {UiLog.FilePath})";
        }
    }

    /// <summary>
    /// Writes the result to <paramref name="path"/>, choosing the format from its
    /// extension. Internal so the failure path is reachable without a file picker.
    /// A disk error is reported, not thrown: the transcript remains on screen.
    /// </summary>
    internal async System.Threading.Tasks.Task ExportToAsync(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        try
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            var content = ext switch
            {
                ".vtt" => BuildVttExport(),
                ".json" => ExportAsJson(),
                ".html" => BuildHtmlExport(),
                ".md" => BuildMarkdownExport(),
                _ => FullText
            };

            await File.WriteAllTextAsync(path, content);
            StatusMessage = $"Exported to {path}";
        }
        catch (Exception ex)
        {
            UiLog.Failure("export", ex);
            StatusMessage = $"Could not export: {ex.Message} (logged to {UiLog.FilePath})";
        }
    }

    /// <summary>
    /// The WebVTT export. Speaker names are used when the reader typed one;
    /// otherwise the cue says "Speaker N", as the CLI's does.
    /// </summary>
    internal string BuildVttExport()
    {
        Dictionary<int, string> names = NamedSpeakers();
        var sb = new System.Text.StringBuilder();
        sb.Append("WEBVTT\n\n");
        foreach (var segment in Segments)
        {
            string speaker = names.TryGetValue(segment.SpeakerId, out string? name)
                ? name
                : $"Speaker {segment.SpeakerId}";
            sb.Append(FormatTime(segment.Start)).Append(" --> ").Append(FormatTime(segment.End)).Append('\n');
            sb.Append("<v ").Append(speaker).Append('>').Append(segment.Text).Append("</v>\n\n");
        }

        return sb.ToString();
    }

    private string ExportAsJson()
    {
        return System.Text.Json.JsonSerializer.Serialize(Result, JsonOptions);
    }

    /// <summary>
    /// The same document the CLI's --format html writes, through the shared
    /// renderer. The duration is left unknown: the app does not keep the audio
    /// file's length, and the last segment's end is not it.
    /// </summary>
    /// <remarks>
    /// The three export options are read here, not stored in the result: the same
    /// transcript exported twice can differ only in the document that describes
    /// it. The transcript itself is never rewritten.
    /// </remarks>
    internal string BuildHtmlExport()
    {
        ResultProvenance provenance = Provenance();
        var info = new TranscribeCppSharp.SharedSource.HtmlTranscriptInfo(
            AudioPath,
            Duration: null,
            provenance.ModelAlias,
            provenance.Language,
            provenance.Diarize,
            provenance.WindowSeconds,
            DateTime.Now);

        var segments = Segments
            .Select(s => new TranscribeCppSharp.SharedSource.HtmlTranscriptSegment(
                s.Start.TotalMilliseconds, s.End.TotalMilliseconds, s.SpeakerId, s.Text))
            .ToArray();

        Dictionary<int, string> names = NamedSpeakers();
        using var writer = new StringWriter();
        TranscribeCppSharp.SharedSource.HtmlTranscriptWriter.Write(
            writer,
            info,
            segments,
            timestampIntervalMs: Math.Max(0, TimestampIntervalSeconds) * 1000,
            pauseThresholdMs: MarkPauses ? Math.Max(1, PauseThresholdSeconds) * 1000 : 0,
            speakerNames: names.Count > 0 ? names : null);
        return writer.ToString();
    }

    /// <summary>
    /// A Markdown note for the same result: a header, then one paragraph per
    /// segment with the speaker in bold and the timestamp in code, plus the pause
    /// markers when they are on.
    /// </summary>
    /// <remarks>
    /// The text is escaped so a transcript containing <c>*</c>, <c>_</c> or angle
    /// brackets renders as what was said rather than as markup. This is a note,
    /// not another transcript format: there is no claim that a Markdown reader
    /// aligns the times, and the timestamps follow the same setting as the HTML
    /// export.
    /// </remarks>
    internal string BuildMarkdownExport()
    {
        Dictionary<int, string> names = NamedSpeakers();
        string title = string.IsNullOrEmpty(AudioPath) ? "Transcript" : Path.GetFileName(AudioPath);
        ResultProvenance provenance = Provenance();

        var sb = new System.Text.StringBuilder();
        sb.Append("# ").Append(EscapeMarkdown(title)).Append("\n\n");
        sb.Append("- **Source:** `").Append(AudioPath).Append("`\n");
        sb.Append("- **Model:** `").Append(provenance.ModelAlias).Append("`\n");
        sb.Append("- **Language:** `").Append(provenance.Language).Append("`\n");
        sb.Append("- **Diarization:** ").Append(provenance.Diarize).Append('\n');
        sb.Append("- **Window:** ").Append(provenance.WindowSeconds).Append(" s\n");
        sb.Append("- **Generated:** ")
            .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture))
            .Append("\n\n");

        double lastStampMs = double.NegativeInfinity;
        int lastSpeaker = int.MinValue;
        for (int i = 0; i < Segments.Count; i++)
        {
            MergedSegment segment = Segments[i];

            if (MarkPauses && i > 0)
            {
                double gapMs = (segment.Start - Segments[i - 1].End).TotalMilliseconds;
                if (gapMs >= Math.Max(1, PauseThresholdSeconds) * 1000)
                {
                    int dots = Math.Clamp((int)Math.Floor(gapMs / 1000.0), 1, 10);
                    sb.Append("*[")
                      .Append(new string('.', dots))
                      .Append("]*\n\n");
                }
            }

            double startMs = segment.Start.TotalMilliseconds;
            bool timestamp = i == 0
                || segment.SpeakerId != lastSpeaker
                || (TimestampIntervalSeconds > 0 && startMs - lastStampMs >= TimestampIntervalSeconds * 1000);
            if (timestamp)
            {
                lastStampMs = startMs;
            }

            lastSpeaker = segment.SpeakerId;

            string speaker = names.TryGetValue(segment.SpeakerId, out string? name)
                ? name
                : $"Speaker {segment.SpeakerId}";
            sb.Append("**").Append(EscapeMarkdown(speaker)).Append("**");
            if (timestamp)
            {
                sb.Append(" `[").Append(FormatClock(segment.Start)).Append("]`");
            }

            sb.Append(" — ").Append(EscapeMarkdown(segment.Text)).Append("\n\n");
        }

        return sb.ToString();
    }

    /// <summary>The language the result reports, or the requested one when it reports none.</summary>
    private string EffectiveLanguage()
        => string.IsNullOrEmpty(Result?.DetectedLanguage) ? Language : Result!.DetectedLanguage;

    /// <summary>How the run's diarization request is described in the exports.</summary>
    private string DiarizeLabel()
        => this.DiarizeMode switch
        {
            TranscribeCppSharp.Interop.DiarizeMode.DiarizeModeOff => "off",
            TranscribeCppSharp.Interop.DiarizeMode.DiarizeModeOn => "on",
            _ => "model default",
        };

    /// <summary>HH:MM:SS, always with hours, as the HTML renderer writes it.</summary>
    private static string FormatClock(TimeSpan time)
        => $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}";

    /// <summary>
    /// Escapes the characters that would turn a sentence into markup, and turns
    /// a line break into a space so one segment stays one paragraph.
    /// </summary>
    private static string EscapeMarkdown(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (c is '\\' or '`' or '*' or '_' or '[' or ']' or '<' or '>')
            {
                sb.Append('\\');
            }

            sb.Append(c is '\n' or '\r' ? ' ' : c);
        }

        return sb.ToString();
    }

    private static string FormatTime(TimeSpan time)
    {
        return $"{time.Hours:D2}:{time.Minutes:D2}:{time.Seconds:D2}.{time.Milliseconds:D3}";
    }
}
