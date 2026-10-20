using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TranscribeCppSharp.Audio;
using TranscribeCppSharp.Interop;
using TranscribeCppSharp.Models;
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

    [ObservableProperty]
    private string _audioPath = string.Empty;

    [ObservableProperty]
    private string _modelAlias = string.Empty;

    [ObservableProperty]
    private string _language = "en";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanTranscribe))]
    private bool _isTranscribing;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>True while the selected model is being loaded to read its capabilities.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanTranscribe))]
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

    [ObservableProperty]
    private bool _hasResult;

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
        && !caps.Roles.HasFlag(TranscribeRole.Diarize);

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

    private static readonly string[] AudioPatterns = ["*.wav", "*.mp3", "*.flac", "*.ogg", "*.m4a"];
    private static readonly string[] TextPatterns = ["*.txt"];
    private static readonly string[] VttPatterns = ["*.vtt"];
    private static readonly string[] JsonPatterns = ["*.json"];
    private static readonly string[] HtmlPatterns = ["*.html"];

    public TranscriptionViewModel(
        ITranscriptionService transcriptionService,
        SettingsViewModel settings,
        ModelCapabilityCache capabilityCache)
    {
        _transcriptionService = transcriptionService;
        _settings = settings;
        _capabilityCache = capabilityCache;

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
            DiarizerOptions.Add(new DiarizerOption(alias, alias));
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

    partial void OnModelAliasChanged(string value) => RequestCapabilityProbe(value);

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
        OnPropertyChanged(nameof(ShowDiarize));
        OnPropertyChanged(nameof(ShowDiarizerPicker));
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

    [RelayCommand]
    private async System.Threading.Tasks.Task TranscribeAsync()
    {
        if (string.IsNullOrWhiteSpace(AudioPath) || !File.Exists(AudioPath))
        {
            StatusMessage = "Please select a valid audio file.";
            return;
        }

        IsTranscribing = true;
        Progress = 0;
        StatusMessage = "Transcribing...";
        HasResult = false;
        UiSettings.SetLastModel(UiSettings.TranscriptionTab, ModelAlias);

        try
        {
            var options = new TranscriptionOptions
            {
                // The backend and device chosen in Settings. These were
                // displayed but read by nothing, so the pickers did nothing.
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
            };

            var progress = new Progress<double>(p => Progress = p);

            Result = await _transcriptionService.TranscribeAsync(
                AudioPath, ModelAlias, options, progress);

            FullText = Result.FullText;
            Segments = new ObservableCollection<MergedSegment>(Result.Segments);
            Words = new ObservableCollection<WordResult>(Result.Words);
            SpeakerSegments = new ObservableCollection<SpeakerSegmentResult>(Result.SpeakerSegments);
            LangIdCandidates = new ObservableCollection<LangIdSession.LangIdCandidate>(Result.LangIdCandidates);
            VadSegments = new ObservableCollection<VadSession.VadSegmentResult>(Result.VadSegments);
            OnPropertyChanged(nameof(ShowLangIdTab));
            OnPropertyChanged(nameof(ShowVadTab));
            HasResult = true;
            StatusMessage = "Transcription complete.";
        }
        catch (Exception ex)
        {
            UiLog.Failure("transcription run", ex);
            StatusMessage = $"Error: {ex.Message} (logged to {UiLog.FilePath})";
        }
        finally
        {
            IsTranscribing = false;
        }
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task ExportAsync()
    {
        if (Result == null) return;

        var window = Avalonia.Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;

        if (window == null) return;

        var file = await window.StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
        {
            Title = "Export transcript",
            DefaultExtension = "txt",
            FileTypeChoices = new[]
            {
                new Avalonia.Platform.Storage.FilePickerFileType("Text") { Patterns = TextPatterns },
                new Avalonia.Platform.Storage.FilePickerFileType("WebVTT") { Patterns = VttPatterns },
                new Avalonia.Platform.Storage.FilePickerFileType("JSON") { Patterns = JsonPatterns },
                new Avalonia.Platform.Storage.FilePickerFileType("HTML") { Patterns = HtmlPatterns }
            }
        });

        if (file == null) return;

        var path = Avalonia.Platform.Storage.StorageProviderExtensions.TryGetLocalPath(file);
        if (string.IsNullOrWhiteSpace(path)) return;

        var ext = Path.GetExtension(path).ToLowerInvariant();
        var content = ext switch
        {
            ".vtt" => BuildVttExport(),
            ".json" => ExportAsJson(),
            ".html" => BuildHtmlExport(),
            _ => FullText
        };

        await File.WriteAllTextAsync(path, content);
        StatusMessage = $"Exported to {path}";
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
        string detected = Result?.DetectedLanguage ?? string.Empty;
        var info = new TranscribeCppSharp.SharedSource.HtmlTranscriptInfo(
            AudioPath,
            Duration: null,
            ModelAlias,
            string.IsNullOrEmpty(detected) ? Language : detected,
            this.DiarizeMode switch
            {
                TranscribeCppSharp.Interop.DiarizeMode.DiarizeModeOff => "off",
                TranscribeCppSharp.Interop.DiarizeMode.DiarizeModeOn => "on",
                _ => "model default",
            },
            WindowSeconds,
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

    private static string FormatTime(TimeSpan time)
    {
        return $"{time.Hours:D2}:{time.Minutes:D2}:{time.Seconds:D2}.{time.Milliseconds:D3}";
    }
}
