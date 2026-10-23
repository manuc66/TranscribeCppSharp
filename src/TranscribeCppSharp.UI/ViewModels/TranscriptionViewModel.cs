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

public partial class TranscriptionViewModel : ObservableObject
{
    private readonly ITranscriptionService _transcriptionService;

    private readonly ISegmentPlayer _segmentPlayer;

    private readonly SettingsViewModel _settings;

    private readonly ModelCapabilityCache _capabilityCache;

    private ModelCapabilitySnapshot? _selectedCapabilities;

    private bool _probing;

    private string? _pendingProbeAlias;

    /// <summary>The run's cancellation, while one is in flight.</summary>
    private Action? _cancelTranscription;

    /// <summary>
    /// The options and model the run started with. The report and the result's
    /// provenance describe that snapshot, not whatever the pickers say when the
    /// failure is caught — the two can differ if the reader edits during a run.
    /// </summary>
    private TranscriptionOptions? _runOptions;

    private string _runModelAlias = string.Empty;

    /// <summary>
    /// Why the run is using a smaller window than the one asked for, if it is.
    /// Kept for the whole run so the progress note does not lose it after the
    /// first window starts filling the bar.
    /// </summary>
    private string _windowAdjustmentNote = string.Empty;

    /// <summary>
    /// True while the goal is setting the speaker intent. The checkbox and the
    /// goal are two views of the same choice; the flag stops one from re-entering
    /// the other and stops a checkbox toggle from rebuilding the model list.
    /// </summary>
    private bool _syncingGoal;

    /// <summary>
    /// True while the constructor is restoring saved choices. The change handlers
    /// save on every edit; without this guard, opening the app would write the file
    /// once for each restored value.
    /// </summary>
    private bool _restoringSettings;

    /// <summary>
    /// True while the language picker is being repaired from the global list, so
    /// the change does not rebuild the model list a second time.
    /// </summary>
    private bool _syncingLanguage;

    /// <summary>
    /// How many catalog models are on disk, independent of the goal. The goal can
    /// filter the picker to none while the cache holds other roles.
    /// </summary>
    private int _downloadedModelCount;

    /// <summary>
    /// What produced the result on screen. Kept apart from the pickers so exporting
    /// a restored run names the model and settings that actually ran, not whatever
    /// the tab happens to have selected now.
    /// </summary>
    private ResultProvenance? _provenance;

    /// <summary>When the run started, for the elapsed line under the bar.</summary>
    private DateTime _runStartedAt;

    /// <summary>
    /// When the first window finished. The estimate per window is measured from
    /// here, not from the run's start, so loading the model and decoding the audio
    /// do not inflate it.
    /// </summary>
    private DateTime? _firstWindowFinishedAt;

    /// <summary>Windows finished and planned, as last reported by the service.</summary>
    private int _windowsCompleted;

    private int _windowsTotal;

    /// <summary>The phase last reported, so the bar knows whether it can fill.</summary>
    private TranscriptionPhase _phase = TranscriptionPhase.Preparing;

    /// <summary>Refreshes the elapsed line once a second while a run is in flight.</summary>
    private readonly DispatcherTimer _elapsedTimer;

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
    [NotifyPropertyChangedFor(nameof(CanEditSettings))]
    [NotifyPropertyChangedFor(nameof(CanEditModel))]
    [NotifyPropertyChangedFor(nameof(CanEditLanguage))]
    [NotifyPropertyChangedFor(nameof(ShowLiveText))]
    [NotifyPropertyChangedFor(nameof(ShowProgressPercent))]
    [NotifyPropertyChangedFor(nameof(ShowStatus))]
    [NotifyPropertyChangedFor(nameof(RunBlockedReason))]
    [NotifyPropertyChangedFor(nameof(HasRunBlockedReason))]
    private bool _isTranscribing;

    [ObservableProperty]
    private double _progress;

    /// <summary>
    /// The run's countable step, e.g. "Transcribing — window 3 of 12".
    /// </summary>
    /// <remarks>
    /// The percentage is the same work; this names it. It is set before the
    /// first window has finished and cleared by the view when the run ends.
    /// </remarks>
    [ObservableProperty]
    private string _progressStage = string.Empty;

    /// <summary>
    /// True when the current phase reports no fraction. The bar then animates
    /// instead of claiming a number nobody measured — a model load, an ffmpeg
    /// decode, and a whole-buffer diarizer are all one native call.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowProgressPercent))]
    private bool _progressIsIndeterminate;

    /// <summary>Why the bar has no fraction, in the reader's terms.</summary>
    [ObservableProperty]
    private string _progressNote = string.Empty;

    /// <summary>How long the run has been going. Measured, not estimated.</summary>
    [ObservableProperty]
    private string _elapsedText = string.Empty;

    /// <summary>
    /// A rough time left, derived from the windows finished so far. Labelled as an
    /// estimate because window times vary; empty until one full window has been
    /// measured.
    /// </summary>
    [ObservableProperty]
    private string _remainingText = string.Empty;

    /// <summary>Whether a measured fraction is on screen.</summary>
    public bool ShowProgressPercent => IsTranscribing && !ProgressIsIndeterminate;

    /// <summary>
    /// Whether the status line is shown. The progress block already says what is
    /// happening during a run; the status is for the completion or the failure,
    /// and an empty one is not laid out at all.
    /// </summary>
    public bool ShowStatus => !IsTranscribing && StatusMessage.Length > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowStatus))]
    private string _statusMessage = string.Empty;

    /// <summary>True while the selected model is being loaded to read its capabilities.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanTranscribe))]
    [NotifyPropertyChangedFor(nameof(CanProbeMemory))]
    [NotifyPropertyChangedFor(nameof(RunBlockedReason))]
    [NotifyPropertyChangedFor(nameof(HasRunBlockedReason))]
    private bool _isCheckingCapabilities;

    /// <summary>What the capability check is doing, or why options are unfiltered.</summary>
    [ObservableProperty]
    private string _capabilityStatus = string.Empty;

    [ObservableProperty]
    private TranscriptionResult? _result;

    [ObservableProperty]
    private string _fullText = string.Empty;

    /// <summary>The text searched for in the transcript.</summary>
    [ObservableProperty]
    private string _searchQuery = string.Empty;

    /// <summary>Start of the current match in <see cref="FullText"/>, for the text box's selection.</summary>
    [ObservableProperty]
    private int _searchSelectionStart;

    /// <summary>End of the current match in <see cref="FullText"/>.</summary>
    [ObservableProperty]
    private int _searchSelectionEnd;

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
    [NotifyPropertyChangedFor(nameof(ResultSpeakersNote))]
    [NotifyPropertyChangedFor(nameof(HasResultSpeakersNote))]
    private bool _hasResult;

    /// <summary>
    /// A note when the result on screen already has speaker turns while the
    /// checkbox is clear. The speaker controls describe the next run; a restored
    /// diarized history entry would otherwise look as if its speakers were gone.
    /// </summary>
    public string ResultSpeakersNote
        => HasResult && !LabelSpeakers && Result?.SpeakerSegments.Count > 0
            ? "The result on screen already has its speaker turns; choose Speech to text + speakers "
                + "to include them in the next run."
            : string.Empty;

    /// <summary>Whether <see cref="ResultSpeakersNote"/> has something to say.</summary>
    public bool HasResultSpeakersNote => ResultSpeakersNote.Length > 0;

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

    /// <summary>
    /// What the reader wants out of the run. The model picker follows it, and so
    /// does the speaker step: a text goal can be extended with the checkbox, and
    /// choosing the speaker goal ticks it.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedGoal))]
    [NotifyPropertyChangedFor(nameof(GoalDescription))]
    [NotifyPropertyChangedFor(nameof(GoalRoleLabel))]
    [NotifyPropertyChangedFor(nameof(GoalIsAsr))]
    [NotifyPropertyChangedFor(nameof(GoalRole))]
    [NotifyPropertyChangedFor(nameof(GoalModelHint))]
    [NotifyPropertyChangedFor(nameof(HasGoalModelHint))]
    [NotifyPropertyChangedFor(nameof(PipelineSummary))]
    [NotifyPropertyChangedFor(nameof(RunButtonText))]
    [NotifyPropertyChangedFor(nameof(RunSummary))]
    [NotifyPropertyChangedFor(nameof(ShowSpeakerSection))]
    [NotifyPropertyChangedFor(nameof(ShowLanguage))]
    [NotifyPropertyChangedFor(nameof(ShowLanguageColumn))]
    [NotifyPropertyChangedFor(nameof(ModelColumnSpan))]
    [NotifyPropertyChangedFor(nameof(ModelGridColumn))]
    [NotifyPropertyChangedFor(nameof(ShowWindow))]
    [NotifyPropertyChangedFor(nameof(ShowTranscriptSection))]
    [NotifyPropertyChangedFor(nameof(ShowTask))]
    [NotifyPropertyChangedFor(nameof(ShowTargetLanguage))]
    [NotifyPropertyChangedFor(nameof(ShowAdvanced))]
    [NotifyPropertyChangedFor(nameof(ShowAllowedLanguages))]
    [NotifyPropertyChangedFor(nameof(CanProbeThisRole))]
    [NotifyPropertyChangedFor(nameof(VadSatisfied))]
    [NotifyPropertyChangedFor(nameof(NeedsVadModelDownload))]
    [NotifyPropertyChangedFor(nameof(CanTranscribe))]
    [NotifyPropertyChangedFor(nameof(CanProbeMemory))]
    [NotifyPropertyChangedFor(nameof(RunBlockedReason))]
    [NotifyPropertyChangedFor(nameof(HasRunBlockedReason))]
    private TranscriptionGoal _goal = TranscriptionGoal.Transcribe;

    /// <summary>The goals the picker offers, with the words each shows.</summary>
    public IReadOnlyList<TranscriptionGoalOption> Goals { get; } = new[]
    {
        new TranscriptionGoalOption(
            TranscriptionGoal.Transcribe,
            "Speech to text",
            "Text only. The selected model transcribes the audio; no speaker labels.",
            ModelRole.Asr,
            "speech-to-text"),
        new TranscriptionGoalOption(
            TranscriptionGoal.TranscribeWithSpeakers,
            "Speech to text + speakers",
            "Text and who spoke. The model attributes speakers itself, or a dedicated diarizer runs a second pass.",
            ModelRole.Asr,
            "speech-to-text"),
        new TranscriptionGoalOption(
            TranscriptionGoal.SpeakersOnly,
            "Speakers only",
            "Speaker turns, no text. Needs a model whose role is diarization (Sortformer).",
            ModelRole.Diarize,
            "diarization"),
        new TranscriptionGoalOption(
            TranscriptionGoal.IdentifyLanguage,
            "Identify language",
            "Ranked language candidates, no text. Needs the language-identification model.",
            ModelRole.Langid,
            "language identification"),
        new TranscriptionGoalOption(
            TranscriptionGoal.DetectSpeech,
            "Detect speech",
            "Speech segments, no text. Needs the voice-activity model.",
            ModelRole.Vad,
            "speech detection"),
    };

    /// <summary>The goal row the picker displays.</summary>
    public TranscriptionGoalOption SelectedGoal => Goals.First(o => o.Value == Goal);

    /// <summary>What the chosen goal produces, in one line.</summary>
    public string GoalDescription => SelectedGoal.Description;

    /// <summary>The role in the words the empty-state hints use.</summary>
    public string GoalRoleLabel => SelectedGoal.RoleLabel;

    /// <summary>Whether the goal is one of the two ASR outcomes.</summary>
    public bool GoalIsAsr
        => Goal is TranscriptionGoal.Transcribe or TranscriptionGoal.TranscribeWithSpeakers;

    /// <summary>The native role a model must serve for the chosen goal.</summary>
    public TranscribeRole GoalRole => Goal switch
    {
        TranscriptionGoal.SpeakersOnly => TranscribeRole.Diarize,
        TranscriptionGoal.IdentifyLanguage => TranscribeRole.Langid,
        TranscriptionGoal.DetectSpeech => TranscribeRole.Vad,
        _ => TranscribeRole.Asr,
    };

    /// <summary>
    /// The actual pipeline, in the order the run executes it. It says the two
    /// passes when a dedicated diarizer is chained, and one pass when the model
    /// attributes speakers itself.
    /// </summary>
    public string PipelineSummary
    {
        get
        {
            string window = $"windows of {WindowSeconds} s";
            string steps = Goal switch
            {
                TranscriptionGoal.Transcribe =>
                    $"speech to text ({window}) → text.",
                TranscriptionGoal.TranscribeWithSpeakers => CanModelDiarize
                    ? "speech to text with speaker labels (same pass) → text + speakers."
                    : SelectedDiarizer?.Alias is { } diarizer
                        ? $"speech to text ({window}) → speaker labels with {diarizer} (second pass) → text + speakers."
                        : "speech to text → speaker labels need a diarizer on disk.",
                TranscriptionGoal.SpeakersOnly =>
                    "speaker diarization (whole file) → speaker turns.",
                TranscriptionGoal.IdentifyLanguage =>
                    "language identification → ranked languages.",
                TranscriptionGoal.DetectSpeech =>
                    $"speech detection ({window}) → speech segments.",
                _ => string.Empty,
            };

            // The VAD trim is an ASR pre-pass, not a goal; say it first when on.
            string prefix = UseVad && GoalIsAsr
                ? "Audio → detect speech (VAD) → "
                : "Audio → ";
            return prefix + steps;
        }
    }

    /// <summary>Whether any catalog model is on disk, whatever its role.</summary>
    public bool HasAnyModelOnDisk => _downloadedModelCount > 0;

    /// <summary>
    /// What to say when the goal's role has no downloaded model while the cache
    /// may hold other roles. Empty when the picker has something to offer.
    /// </summary>
    public string GoalModelHint
    {
        get
        {
            if (AvailableModels.Count > 0)
            {
                return string.Empty;
            }

            return HasAnyModelOnDisk
                ? $"No {GoalRoleLabel} model on disk for this goal. The Models tab can filter on that role."
                : "Nothing on disk yet. The Models tab downloads one.";
        }
    }

    /// <summary>Whether <see cref="GoalModelHint"/> has something to say.</summary>
    public bool HasGoalModelHint => GoalModelHint.Length > 0;

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

    partial void OnTargetLanguageChanged(string? value) => OnPropertyChanged(nameof(RunSummary));

    [ObservableProperty]
    private int? _specKDrafts;

    [ObservableProperty]
    private bool? _keepSpecialTags;

    /// <summary>
    /// The longest audio passed to the model in one native call.
    /// </summary>
    /// <remarks>
    /// Deliberately small. The native API reports device free memory but not the
    /// size of the compute graph a window builds, and that graph grows with the
    /// window. In the reported Parakeet failure the whole file was sent in one
    /// call (the setting was ignored by an earlier seconds/milliseconds mix-up)
    /// and asked for 10.1 GB on Vulkan and 113.6 GB on the CPU; 30 s windows
    /// completed on both. A caller who knows its device can raise this;
    /// <see cref="ProbeMemoryAsync"/> measures the real allocation instead of
    /// promising one.
    /// </remarks>
    [ObservableProperty]
    private int _windowSeconds = 30;

    /// <summary>
    /// Whether the service may lower <see cref="WindowSeconds"/> and retry when
    /// the first window does not fit in memory.
    /// </summary>
    /// <remarks>
    /// Only lowering is automatic: the native API does not say how large a window
    /// a device can take, so raising would be a guess. The reader raises the field
    /// and the run lowers it again if it has to.
    /// </remarks>
    [ObservableProperty]
    private bool _autoAdjustWindow = true;

    public ObservableCollection<TranscriptionTask> AvailableTasks { get; } = new()
    {
        TranscriptionTask.Transcribe,
        TranscriptionTask.Translate,
    };

    /// <summary>Whether the Whisper-only controls apply to the selected model.</summary>
    public bool ShowWhisperExtensions => ModelOptionVisibility.ShowWhisperExtensions(_selectedCapabilities);

    /// <summary>Whether the translation controls apply to the selected model.</summary>
    public bool ShowTranslate => ModelOptionVisibility.ShowTranslate(_selectedCapabilities);

    /// <summary>
    /// Whether the target-language picker applies. A target only means something
    /// while the task is Translate: showing it beside a Transcribe run offered two
    /// controls that contradicted each other.
    /// </summary>
    public bool ShowTargetLanguage
        => ShowTask && SelectedTask == TranscriptionTask.Translate;

    /// <summary>Whether the task picker applies at all.</summary>
    public bool ShowTask => GoalIsAsr && ShowTranslate;

    /// <summary>
    /// Whether the language picker applies. Only an ASR session takes a language
    /// code; a diarizer and a voice-activity model ignore it, and a language
    /// identifier produces one rather than consuming it.
    /// </summary>
    public bool ShowLanguage
        => GoalIsAsr
        && (_selectedCapabilities is null
            || _selectedCapabilities.Roles.HasFlag(TranscribeRole.Asr));

    /// <summary>
    /// Whether the language column has anything in it. A LANGID model still shows
    /// its allowed-languages field there, even though it has no input language.
    /// </summary>
    public bool ShowLanguageColumn => ShowLanguage || ShowAllowedLanguages;

    /// <summary>How many of the two model columns the model stack takes.</summary>
    public int ModelColumnSpan => ShowLanguageColumn ? 1 : 2;

    /// <summary>Which grid column the model stack starts in, now that language comes first.</summary>
    public int ModelGridColumn => ShowLanguageColumn ? 1 : 0;

    /// <summary>
    /// Whether the window setting applies. The ASR and VAD paths are chunked; a
    /// diarizer reads the whole buffer and a language identifier uses its own
    /// maximum clip.
    /// </summary>
    public bool ShowWindow
        => (Goal is TranscriptionGoal.Transcribe
                or TranscriptionGoal.TranscribeWithSpeakers
                or TranscriptionGoal.DetectSpeech)
            && (_selectedCapabilities is null
                || _selectedCapabilities.Roles.HasFlag(TranscribeRole.Asr)
                || _selectedCapabilities.Roles.HasFlag(TranscribeRole.Vad));

    /// <summary>
    /// Whether the run-options card has a control in it at all. With no model on
    /// disk it would only show defaults for a run that cannot start.
    /// </summary>
    public bool ShowTranscriptSection
        => (Goal is TranscriptionGoal.Transcribe
                or TranscriptionGoal.TranscribeWithSpeakers
                or TranscriptionGoal.DetectSpeech)
            && (_selectedCapabilities is not null
                ? ShowTask || ShowWindow
                : HasDownloadedModels);

    /// <summary>
    /// Whether the decoder tuning applies. Every field behind the disclosure is a
    /// session or ASR extension knob, and the non-ASR sessions take none of them.
    /// </summary>
    public bool ShowAdvanced
        => GoalIsAsr
        && (_selectedCapabilities is not null
            ? _selectedCapabilities.Roles.HasFlag(TranscribeRole.Asr)
            : HasDownloadedModels);

    /// <summary>Whether the model picker may be changed now.</summary>
    public bool CanEditModel => CanEditSettings && HasDownloadedModels;

    /// <summary>Whether the language controls may be changed now.</summary>
    public bool CanEditLanguage
        => CanEditSettings && (_selectedCapabilities is not null || HasDownloadedModels);

    /// <summary>Whether speculative decoding applies to the selected model.</summary>
    public bool ShowSpeculativeDecoding => ModelOptionVisibility.ShowSpeculativeDecoding(_selectedCapabilities);

    /// <summary>Whether the vocabulary field applies to the selected model.</summary>
    public bool ShowVocabulary => ModelOptionVisibility.ShowVocabulary(_selectedCapabilities);

    /// <summary>
    /// Whether the tab shows the speaker group at all: a model that transcribes,
    /// or one whose capabilities are not known yet. A model that only diarizes
    /// returns speaker turns as its whole job, and the other roles have no text to
    /// attach a speaker to.
    /// </summary>
    public bool ShowSpeakerSection
        => Goal == TranscriptionGoal.TranscribeWithSpeakers
        && (_selectedCapabilities is not null
            ? _selectedCapabilities.Roles.HasFlag(TranscribeRole.Asr)
            // Not checked yet: show the group only when there is a model whose
            // capabilities could still arrive. With an empty cache it would be a
            // control about nothing.
            : HasDownloadedModels);

    /// <summary>
    /// Whether the selected model attributes speakers itself. Unknown is not a yes:
    /// until the model has answered, the tab treats it as a transcriber that needs
    /// a dedicated diarizer rather than promising an attribution it cannot confirm.
    /// </summary>
    public bool CanModelDiarize
        => _selectedCapabilities is { } caps
        && (caps.Supports(Feature.FeatureDiarization)
            || caps.Roles.HasFlag(TranscribeRole.Diarize));

    /// <summary>
    /// Whether the model reports segment timings, which a separate diarizer needs
    /// to be joined to its segments.
    /// </summary>
    public bool HasSegmentTimings
        => _selectedCapabilities is null
        || _selectedCapabilities.MaxTimestampKind >= TimestampKind.TimestampsSegment;

    /// <summary>
    /// Whether a dedicated diarizer can be offered: a transcriber with segment
    /// timings that does not attribute speakers itself.
    /// </summary>
    public bool CanUseDedicatedDiarizer
        => ShowSpeakerSection && !CanModelDiarize && HasSegmentTimings;

    /// <summary>Whether the model can produce speaker labels at all.</summary>
    public bool CanAskForSpeakerLabels
        => ShowSpeakerSection && (CanModelDiarize || HasSegmentTimings);

    /// <summary>
    /// The reader's explicit intent: tick the box to attribute speakers, leave it
    /// clear to leave every segment as Speaker 0.
    /// </summary>
    /// <remarks>
    /// The previous UI offered a "Diarization: model default / off / on" picker in
    /// one row and a "Diarizer" model picker in another. Two controls with similar
    /// names, neither saying what the result would be, and "model default" was a
    /// third state that read like a choice but meant "let the library decide" —
    /// which is off for every family. There is now one intent, and the plan line
    /// under it states the outcome in one sentence.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanTranscribe))]
    [NotifyPropertyChangedFor(nameof(CanProbeMemory))]
    [NotifyPropertyChangedFor(nameof(ShowDiarizerPicker))]
    [NotifyPropertyChangedFor(nameof(DiarizerPlan))]
    [NotifyPropertyChangedFor(nameof(DiarizerLanguageWarning))]
    [NotifyPropertyChangedFor(nameof(HasDiarizerLanguageWarning))]
    [NotifyPropertyChangedFor(nameof(RunBlockedReason))]
    [NotifyPropertyChangedFor(nameof(HasRunBlockedReason))]
    [NotifyPropertyChangedFor(nameof(ResultSpeakersNote))]
    [NotifyPropertyChangedFor(nameof(HasResultSpeakersNote))]
    private bool _labelSpeakers;

    /// <summary>
    /// What the run passes as <c>WithDiarize</c>. It follows the intent, and is
    /// never "on" for a model that cannot do it: that would hand the library a
    /// request it can only answer with a warning and Speaker 0.
    /// </summary>
    [ObservableProperty]
    private DiarizeMode _diarizeMode = DiarizeMode.DiarizeModeOff;

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
            // The goal names the action before the probe answers; the loaded model
            // remains the authority once it has.
            TranscribeRole roles = _selectedCapabilities?.Roles ?? GoalRole;
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

    /// <summary>
    /// What the status says when a run succeeds, named for the role it ran. A
    /// diarizer or a language identifier is not a transcription, and saying it was
    /// one would make the result look wrong.
    /// </summary>
    internal string CompletionMessage
    {
        get
        {
            TranscribeRole roles = _selectedCapabilities?.Roles ?? GoalRole;
            // Same order as the run button: a model that transcribes runs ASR,
            // even when it can also diarize or identify a language.
            if (roles.HasFlag(TranscribeRole.Asr))
            {
                return "Transcription complete.";
            }

            if (roles.HasFlag(TranscribeRole.Diarize))
            {
                return "Diarization complete.";
            }

            if (roles.HasFlag(TranscribeRole.Langid))
            {
                return "Language identification complete.";
            }

            if (roles.HasFlag(TranscribeRole.Vad))
            {
                return "Speech detection complete.";
            }

            return "Transcription complete.";
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
    /// What the dedicated-diarizer picker offers: the downloaded diarizers only.
    /// </summary>
    /// <remarks>
    /// "(none)" is deliberately absent. The checkbox is the on/off control, so an
    /// entry meaning "no diarizer" inside a picker that only exists while speaker
    /// labels are on would contradict the box above it.
    /// </remarks>
    public ObservableCollection<DiarizerOption> DiarizerChoices { get; } = new();

    /// <summary>
    /// The diarizer to run after the transcription, or the "none" entry.
    /// </summary>
    [ObservableProperty]
    private DiarizerOption? _selectedDiarizer;

    /// <summary>
    /// Downloaded models that serve the VAD role, for the opt-in silence trim.
    /// </summary>
    public ObservableCollection<VadOption> VadOptions { get; } = new();

    /// <summary>
    /// The VAD model used before transcription, when <see cref="UseVad"/> is on.
    /// </summary>
    [ObservableProperty]
    private VadOption? _selectedVadModel;

    /// <summary>
    /// Whether the run skips non-speech before transcription. Off by default: the
    /// pass changes what the model sees and its effect is unmeasured.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VadSatisfied))]
    [NotifyPropertyChangedFor(nameof(NeedsVadModelDownload))]
    [NotifyPropertyChangedFor(nameof(PipelineSummary))]
    [NotifyPropertyChangedFor(nameof(CanTranscribe))]
    [NotifyPropertyChangedFor(nameof(CanProbeMemory))]
    [NotifyPropertyChangedFor(nameof(RunBlockedReason))]
    [NotifyPropertyChangedFor(nameof(HasRunBlockedReason))]
    private bool _useVad;

    /// <summary>Whether any VAD model is on disk.</summary>
    public bool HasVadModels => VadOptions.Count > 0;

    /// <summary>Whether the chosen VAD option can run for this goal.</summary>
    public bool VadSatisfied
        => !UseVad || !GoalIsAsr || SelectedVadModel?.Alias is not null;

    /// <summary>
    /// Whether the silence trim is on but no VAD model is available. The tab then
    /// offers the route to Models filtered to the VAD role.
    /// </summary>
    public bool NeedsVadModelDownload
        => UseVad && GoalIsAsr && SelectedVadModel?.Alias is null;

    /// <summary>
    /// Whether the dedicated-diarizer picker is shown: speaker labels are on and
    /// the transcriber cannot do it itself.
    /// </summary>
    /// <remarks>
    /// A model that already diarizes has no need of a second one, and a model that
    /// only diarizes has nothing to pair with. The picker follows the intent above
    /// rather than sitting in the form all the time: an option that only matters
    /// when the box is ticked is not shown when it is clear.
    /// </remarks>
    public bool ShowDiarizerPicker
        => LabelSpeakers && CanUseDedicatedDiarizer && HasDedicatedDiarizer;

    /// <summary>
    /// Whether the tab has to explain why speaker labels are not on offer: the
    /// model transcribes, cannot attribute speakers, and reports no segment
    /// timings.
    /// </summary>
    /// <remarks>
    /// Without timings there is nothing to overlap a speaker turn with, so the join
    /// would silently leave every segment as Speaker 0. Hiding the control silently
    /// would be just as misleading, hence the note in its place.
    /// </remarks>
    public bool ShowDiarizerUnavailableHint
        => ShowSpeakerSection && !CanModelDiarize && !HasSegmentTimings;

    /// <summary>Why speaker labels are not available, in the reader's terms.</summary>
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
    /// Whether a dedicated diarizer model is on disk. False means speaker labels
    /// for this transcriber need a download first.
    /// </summary>
    public bool HasDedicatedDiarizer => DiarizerOptions.Any(o => o.Alias is not null);

    /// <summary>
    /// Whether the reader asked for speaker labels and the only way to get them is
    /// a diarizer that is not downloaded. The tab then offers the route to Models
    /// instead of letting the run start and silently produce Speaker 0.
    /// </summary>
    public bool NeedsDedicatedDiarizerDownload
        => LabelSpeakers && CanUseDedicatedDiarizer && !HasDedicatedDiarizer;

    /// <summary>
    /// What the settings add up to, in one sentence. It is a restatement of the
    /// controls, not a prediction: the language, task and speaker choice named here
    /// are the ones the run will pass.
    /// </summary>
    public string RunSummary
    {
        get
        {
            if (!HasDownloadedModels)
            {
                return HasAnyModelOnDisk
                    ? $"No {GoalRoleLabel} model on disk for this goal."
                    : "Nothing to run yet: no model is on disk.";
            }

            // The goal decides what the tab produces. Saying it in plain words is
            // what keeps a diarizer or a language identifier from looking like a
            // transcription that came back empty; the probe can only confirm it.
            switch (Goal)
            {
                case TranscriptionGoal.SpeakersOnly:
                    return "This goal returns speaker turns; it produces no text transcript.";
                case TranscriptionGoal.IdentifyLanguage:
                    return "This goal identifies the language; it produces no text transcript.";
                case TranscriptionGoal.DetectSpeech:
                    return $"This goal detects speech segments, scored in windows of {WindowSeconds} s.";
            }

            string language = Language is "auto" or ""
                ? "language detected by the model"
                : $"language {Language}";
            string task = SelectedTask == TranscriptionTask.Translate
                ? $"translate to {TargetLanguage ?? "the model default"}"
                : "transcribe";
            string speakers = LabelSpeakers
                ? CanModelDiarize
                    ? ", speakers labelled by the model"
                    : SelectedDiarizer?.Alias is { } diarizer
                        ? $", speakers labelled by {diarizer} in a second pass"
                        : ", speaker labels not yet available"
                : string.Empty;

            return $"Will {task}, {language}{speakers}, windows of {WindowSeconds} s.";
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
            if (!ShowSpeakerSection || (!CanModelDiarize && !HasSegmentTimings))
            {
                return string.Empty;
            }

            if (_selectedCapabilities is null)
            {
                return "Speaker labels depend on the selected model; its capabilities are still being checked.";
            }

            string model = string.IsNullOrEmpty(ModelAlias) ? "the selected model" : ModelAlias;
            if (!LabelSpeakers)
            {
                if (CanModelDiarize)
                {
                    return "No speaker labels: every segment is Speaker 0. "
                        + $"{model} can attribute speakers itself — tick the box.";
                }

                if (HasDedicatedDiarizer)
                {
                    return "No speaker labels: every segment is Speaker 0. "
                        + "A second pass with a diarizer can add them — tick the box.";
                }

                return "No speaker labels: every segment is Speaker 0. "
                    + "This model cannot attribute speakers, and no diarizer model is downloaded.";
            }

            if (CanModelDiarize)
            {
                return $"{model} attributes speakers itself, in one pass.";
            }

            if (SelectedDiarizer?.Alias is not { } alias)
            {
                return $"Speaker labels are on, but no diarizer is selected. "
                    + "Download a diarizer model in Models, or turn speaker labels off.";
            }

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
            if (!ShowDiarizerPicker)
            {
                return string.Empty;
            }

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

            // One role is already named by the run summary and the button. Listing
            // it again is noise; two or more is the fact worth stating, because the
            // tab runs whichever role the model serves.
            return names.Count > 1
                ? "This model serves: " + string.Join(", ", names) + "."
                : string.Empty;
        }
    }

    /// <summary>Whether the selected model's roles include language identification.</summary>
    public bool ShowLangIdTab => Result?.Roles.HasFlag(TranscribeRole.Langid) == true;

    /// <summary>Whether the selected model's roles include voice activity detection.</summary>
    public bool ShowVadTab => Result?.Roles.HasFlag(TranscribeRole.Vad) == true;

    /// <summary>
    /// Whether the allowed-languages field applies: only when the run will actually
    /// take the LANGID path. A model that transcribes *and* identifies languages
    /// runs ASR here, where the field is ignored.
    /// </summary>
    public bool ShowAllowedLanguages
        => Goal == TranscriptionGoal.IdentifyLanguage
        && (_selectedCapabilities is null
            || (!_selectedCapabilities.Roles.HasFlag(TranscribeRole.Asr)
                && _selectedCapabilities.Roles.HasFlag(TranscribeRole.Langid)));

    /// <summary>
    /// Whether a file is selected and still on disk. The run command checks this
    /// again; the button is disabled rather than left to fail after a click.
    /// </summary>
    public bool HasAudioFile
        => !string.IsNullOrWhiteSpace(AudioPath) && File.Exists(AudioPath);

    /// <summary>A sentence about the selected path, or empty when it is usable.</summary>
    public string AudioProblem
        => AudioPath.Length > 0 && !HasAudioFile
            ? "That file no longer exists. Pick another one."
            : string.Empty;

    /// <summary>Whether <see cref="AudioProblem"/> has something to say.</summary>
    public bool HasAudioProblem => AudioProblem.Length > 0;

    /// <summary>
    /// Whether the chosen intent can actually be honoured: speaker labels need
    /// either a model that attributes speakers itself or a dedicated diarizer.
    /// </summary>
    public bool SpeakerLabelsSatisfied
        => !LabelSpeakers
        || (CanAskForSpeakerLabels
            && (CanModelDiarize || SelectedDiarizer?.Alias is not null));

    /// <summary>False while a transcription or a capability check is running.</summary>
    public bool CanTranscribe => CanStartRun;

    /// <summary>
    /// The conditions a run needs, before the reader clicks. A probe counts as a
    /// run in flight: it loads the same model and would compete for the device.
    /// </summary>
    private bool CanStartRun
        => HasDownloadedModels
        && HasAudioFile
        && !IsTranscribing
        && !IsCheckingCapabilities
        && !IsProbingMemory
        && SpeakerLabelsSatisfied
        && VadSatisfied;

    /// <summary>
    /// Why the run button is disabled, in one actionable sentence. Empty when the
    /// run can start.
    /// </summary>
    /// <remarks>
    /// This is the "no surprise" rule applied to the primary button: a disabled
    /// control with no reason is the same as a button that fails after a click.
    /// The sentence names the missing thing and the place that provides it.
    /// </remarks>
    public string RunBlockedReason
    {
        get
        {
            if (IsTranscribing)
            {
                return string.Empty;
            }

            if (!HasDownloadedModels)
            {
                return HasAnyModelOnDisk
                    ? $"No {GoalRoleLabel} model on disk for this goal. "
                        + "Open the Models tab; it is filtered to that role."
                    : "No model on disk. Open the Models tab to download one.";
            }

            if (string.IsNullOrWhiteSpace(AudioPath))
            {
                return "Choose an audio or video file, or drop one on this tab.";
            }

            if (!HasAudioFile)
            {
                return "The selected file no longer exists. Pick another one.";
            }

            if (IsCheckingCapabilities)
            {
                return "Checking what the selected model supports…";
            }

            if (IsProbingMemory)
            {
                return "A memory test is running; the run starts when it finishes.";
            }

            if (!VadSatisfied)
            {
                return NeedsVadModelDownload
                    ? "Skip silence needs a VAD model, and none is downloaded. "
                        + "Open the Models tab and get one, or turn Skip silence off."
                    : "Skip silence needs a VAD model selected. Pick one, or turn Skip silence off.";
            }

            if (LabelSpeakers && !CanAskForSpeakerLabels)
            {
                return "The selected model reports no segment timings, so speaker labels "
                    + "cannot be joined to its segments. Pick a model with timestamps, or "
                    + "one that attributes speakers itself.";
            }

            if (!SpeakerLabelsSatisfied)
            {
                return "Speaker labels need a diarizer model, and none is downloaded. "
                    + "Open the Models tab and get one, or turn speaker labels off.";
            }

            return string.Empty;
        }
    }

    /// <summary>Whether <see cref="RunBlockedReason"/> has something to say.</summary>
    public bool HasRunBlockedReason => RunBlockedReason.Length > 0;

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
    [NotifyPropertyChangedFor(nameof(CanTranscribe))]
    [NotifyPropertyChangedFor(nameof(CanProbeMemory))]
    [NotifyPropertyChangedFor(nameof(CanEditSettings))]
    [NotifyPropertyChangedFor(nameof(CanEditModel))]
    [NotifyPropertyChangedFor(nameof(CanEditLanguage))]
    [NotifyPropertyChangedFor(nameof(RunBlockedReason))]
    [NotifyPropertyChangedFor(nameof(HasRunBlockedReason))]
    private bool _isProbingMemory;

    /// <summary>The probe's outcome, and what it does and does not measure.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMemoryProbeStatus))]
    private string _memoryProbeStatus = string.Empty;

    /// <summary>Whether <see cref="MemoryProbeStatus"/> has something to say.</summary>
    public bool HasMemoryProbeStatus => MemoryProbeStatus.Length > 0;

    /// <summary>
    /// Whether the memory probe applies. It runs one ASR window through
    /// <c>CreateSession</c>, which a DIARIZE, LANGID or VAD model does not serve:
    /// the button would fail with an unsupported-role error, so it is not offered
    /// for those roles.
    /// </summary>
    public bool CanProbeThisRole
        => GoalIsAsr
        && (_selectedCapabilities is null
            || _selectedCapabilities.Roles.HasFlag(TranscribeRole.Asr));

    /// <summary>Whether the memory probe can run: the same conditions as a run, and not already probing.</summary>
    public bool CanProbeMemory => CanStartRun && CanProbeThisRole;

    /// <summary>
    /// Whether the run's settings may be edited. A run works from a snapshot, so an
    /// edit made while it runs would change nothing it does but would change what
    /// the reader believes it did — and the report would describe the wrong run.
    /// </summary>
    public bool CanEditSettings => !IsTranscribing && !IsProbingMemory;

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
    {
        get
        {
            if (_downloadedModelCount == 0 || AvailableModels.Count == _downloadedModelCount)
            {
                return DownloadedModelCatalog.Hint(_downloadedModelCount, ModelStore.Catalog.Count);
            }

            // The cache holds other roles too. Saying only the cache count under a
            // filtered picker would imply those models are selectable here.
            return $"{_downloadedModelCount} of {ModelStore.Catalog.Count} on disk, "
                + $"{AvailableModels.Count} for this goal — the Models tab downloads more.";
        }
    }

    /// <summary>Raised when the reader asks to go and fetch more models.</summary>
    public event Action? OpenModelManagerRequested;

    /// <summary>
    /// Raised when the reader needs a model for a specific role. The window opens
    /// the Models tab with the role filter already set, so the answer is one click
    /// away instead of a search through 70-odd rows.
    /// </summary>
    public event Action<string>? OpenModelsForRoleRequested;

    /// <summary>The button under the picker. The window decides which tab that means.</summary>
    [RelayCommand]
    private void OpenModelManager() => OpenModelManagerRequested?.Invoke();

    /// <summary>Open the catalogue filtered to the role this goal needs.</summary>
    [RelayCommand]
    private void OpenModelsForGoal() => OpenModelsForRoleRequested?.Invoke(SelectedGoal.RoleKey);

    /// <summary>The button shown when speaker labels need a diarizer on disk.</summary>
    [RelayCommand]
    private void OpenDiarizerModels() => OpenModelsForRoleRequested?.Invoke(ModelRole.Diarize);

    /// <summary>The button shown when Skip silence needs a VAD model on disk.</summary>
    [RelayCommand]
    private void OpenVadModels() => OpenModelsForRoleRequested?.Invoke(ModelRole.Vad);

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
    private static readonly string[] SrtPatterns = ["*.srt"];
    private static readonly string[] AssPatterns = ["*.ass"];

    public TranscriptionViewModel(
        ITranscriptionService transcriptionService,
        SettingsViewModel settings,
        ModelCapabilityCache capabilityCache,
        ISegmentPlayer? segmentPlayer = null)
    {
        _transcriptionService = transcriptionService;
        _segmentPlayer = segmentPlayer ?? new FfplaySegmentPlayer();
        _settings = settings;
        _capabilityCache = capabilityCache;

        // Elapsed time is the one number the bar cannot get from the service: the
        // run can be 20 minutes long with one update per window. One tick a second
        // recomputes it from the start time, which is a measurement.
        _elapsedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _elapsedTimer.Tick += (_, _) => UpdateProgressTiming();

        // Transcribe is the named default rather than null: the task picker then
        // shows a real choice instead of an empty box, and the target-language
        // picker is hidden until Translate is picked.
        _selectedTask = TranscriptionTask.Transcribe;

        // The memory warning depends on the backend and the pinned device, which
        // live on the Settings tab. The event is the only way this tab learns that
        // they changed.
        _settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(SettingsViewModel.SelectedBackend)
                or nameof(SettingsViewModel.SelectedDevice))
            {
                ApplyRememberedWindow();
                UpdateMemoryWarning();
            }
        };

        // Restored through the backing field: the generated setter would save it
        // again on every launch.
        _autoAdjustWindow = UiSettings.LoadAutoAdjustWindow();

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

        // The global language list is built before LoadModels, which filters the
        // models by the chosen language.
        RebuildGlobalLanguages();

        // Restore the goal before LoadModels: it decides which roles the picker
        // offers. The VAD choice is restored through the backing fields for the
        // same reason the export options are, and the guard keeps the change
        // handlers from writing the file on every launch.
        _restoringSettings = true;
        _goal = UiSettings.LoadTranscriptionGoal();
        _useVad = UiSettings.LoadTranscriptionUseVad();

        LoadModels();

        string? rememberedVad = UiSettings.LoadTranscriptionVadModel();
        if (rememberedVad is not null)
        {
            SelectedVadModel = VadOptions.FirstOrDefault(option => option.Alias == rememberedVad)
                ?? SelectedVadModel;
        }

        _restoringSettings = false;

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
        // Capture before clearing: the ComboBox bound to AvailableModels is
        // TwoWay on SelectedItem, so clearing the collection can null ModelAlias
        // through the binding before we get to rebuild it. Keeping the value
        // here is what lets the selection survive a refresh (and a tab switch).
        string currentModelAlias = ModelAlias;

        AvailableModels.Clear();
        IReadOnlyList<string> downloaded = DownloadedModelCatalog.Aliases();
        _downloadedModelCount = downloaded.Count;

        // The language is chosen first (task-oriented): a model is offered only when
        // its catalogue declares that it serves the chosen language — or declares
        // nothing, which means "unknown" rather than "no". The probe remains the
        // authority at run time and re-validates below.
        foreach (string alias in downloaded)
        {
            if (!string.Equals(ModelRole.KeyOf(alias), SelectedGoal.RoleKey, StringComparison.Ordinal))
            {
                continue;
            }
            // The language only filters models for ASR goals: a diarizer or a VAD
            // model takes no input language, so applying it would empty their list.
            if (GoalIsAsr && !ModelServesLanguage(alias, Language))
            {
                continue;
            }

            // The "speech to text + speakers" goal needs a transcript that can be
            // joined to speaker turns: either a model that attributes speakers
            // itself or one that reports segment timings. A model that does neither
            // (e.g. canary) is offered for the text-only goal instead, so the
            // picker never advertises an outcome it cannot produce.
            if (Goal == TranscriptionGoal.TranscribeWithSpeakers
                && !ModelServesSpeakers(alias))
            {
                continue;
            }
            AvailableModels.Add(alias);
        }

        // The global language list is rebuilt on goal change and on refresh, not
        // on every language change: rebuilding it here would clear the ComboBox
        // and drop its selection.
        ModelAlias = DownloadedModelCatalog.Selection(
            currentModelAlias, AvailableModels, UiSettings.LastModel(UiSettings.TranscriptionTab));

        LoadDiarizers();
        LoadVadModels();

        OnPropertyChanged(nameof(ModelPickerHint));
        OnPropertyChanged(nameof(HasDownloadedModels));
        OnPropertyChanged(nameof(HasAnyModelOnDisk));
        OnPropertyChanged(nameof(GoalModelHint));
        OnPropertyChanged(nameof(HasGoalModelHint));
        OnPropertyChanged(nameof(RunSummary));
        OnPropertyChanged(nameof(PipelineSummary));
        OnPropertyChanged(nameof(ShowTranscriptSection));
        OnPropertyChanged(nameof(ShowAdvanced));
        OnPropertyChanged(nameof(ShowLanguage));
        OnPropertyChanged(nameof(ShowLanguageColumn));
        OnPropertyChanged(nameof(ModelColumnSpan));
        OnPropertyChanged(nameof(ShowWindow));
        OnPropertyChanged(nameof(ShowAllowedLanguages));
        OnPropertyChanged(nameof(CanEditModel));
        OnPropertyChanged(nameof(CanEditLanguage));
        OnPropertyChanged(nameof(CanProbeThisRole));
        OnPropertyChanged(nameof(ShowSpeakerSection));
        OnPropertyChanged(nameof(CanAskForSpeakerLabels));
        OnPropertyChanged(nameof(ShowDiarizerPicker));
        OnPropertyChanged(nameof(DiarizerPlan));
        OnPropertyChanged(nameof(CanTranscribe));
        OnPropertyChanged(nameof(CanProbeMemory));
        OnPropertyChanged(nameof(RunBlockedReason));
        OnPropertyChanged(nameof(HasRunBlockedReason));
    }

    /// <summary>
    /// Rebuilds the diarizer picker from what is on disk, keeping the choice when it
    /// is still offered.
    /// </summary>
    private void LoadDiarizers()
    {
        DiarizerOption? keep = SelectedDiarizer;

        DiarizerOptions.Clear();
        DiarizerChoices.Clear();
        DiarizerOptions.Add(NoDiarizer);
        foreach (string alias in DownloadedModelCatalog.DiarizerAliases())
        {
            IReadOnlyList<string> languages = UpstreamCatalog.Read(alias)?.Languages ?? [];
            var option = new DiarizerOption(alias, alias, languages);
            DiarizerOptions.Add(option);
            DiarizerChoices.Add(option);
        }

        // A remembered alias that is no longer offered — deleted — falls back to
        // "none" rather than leaving the picker on something that is not there.
        SelectedDiarizer = keep is not null
            ? DiarizerOptions.FirstOrDefault(o => o.Alias == keep.Alias) ?? NoDiarizer
            : NoDiarizer;

        OnPropertyChanged(nameof(HasDedicatedDiarizer));
        OnPropertyChanged(nameof(NeedsDedicatedDiarizerDownload));
        OnPropertyChanged(nameof(DiarizerPlan));
        OnPropertyChanged(nameof(RunSummary));
        OnPropertyChanged(nameof(CanTranscribe));
        OnPropertyChanged(nameof(CanProbeMemory));
        OnPropertyChanged(nameof(RunBlockedReason));
        OnPropertyChanged(nameof(HasRunBlockedReason));
        SyncSpeakerIntent();
    }

    /// <summary>
    /// Rebuilds the VAD-model picker from what is on disk, keeping the choice when
    /// it is still offered. The option itself stays off; it is opt-in.
    /// </summary>
    private void LoadVadModels()
    {
        VadOption? keep = SelectedVadModel;

        VadOptions.Clear();
        foreach (string alias in DownloadedModelCatalog.Aliases())
        {
            if (string.Equals(ModelRole.KeyOf(alias), ModelRole.Vad, StringComparison.Ordinal))
            {
                VadOptions.Add(new VadOption(alias, alias));
            }
        }

        SelectedVadModel = keep is not null
            ? VadOptions.FirstOrDefault(o => o.Alias == keep.Alias)
            : VadOptions.FirstOrDefault();

        OnPropertyChanged(nameof(HasVadModels));
        OnPropertyChanged(nameof(NeedsVadModelDownload));
        OnPropertyChanged(nameof(VadSatisfied));
        OnPropertyChanged(nameof(PipelineSummary));
        OnPropertyChanged(nameof(CanTranscribe));
        OnPropertyChanged(nameof(CanProbeMemory));
        OnPropertyChanged(nameof(RunBlockedReason));
        OnPropertyChanged(nameof(HasRunBlockedReason));
    }

    /// <summary>
    /// Re-reads the cache. Called when the reader comes back from the Models tab,
    /// which is where a download or a delete happens.
    /// </summary>
    public void RefreshModels()
    {
        RebuildGlobalLanguages();
        LoadModels();
    }

    partial void OnAudioPathChanged(string value)
    {
        OnPropertyChanged(nameof(HasAudioFile));
        OnPropertyChanged(nameof(AudioProblem));
        OnPropertyChanged(nameof(HasAudioProblem));
        OnPropertyChanged(nameof(CanTranscribe));
        OnPropertyChanged(nameof(CanProbeMemory));
        OnPropertyChanged(nameof(RunBlockedReason));
        OnPropertyChanged(nameof(HasRunBlockedReason));
    }

    partial void OnGoalChanged(TranscriptionGoal value)
    {
        if (!_restoringSettings)
        {
            UiSettings.SaveTranscriptionGoal(value);
        }

        // A checkbox toggle sets the goal with this flag: update the picker's
        // words but do not rebuild the model list, which is a role decision.
        if (_syncingGoal)
        {
            return;
        }

        SyncGoalAndSpeakers();
        LoadModels();
        OnPropertyChanged(nameof(GoalModelHint));
        OnPropertyChanged(nameof(HasGoalModelHint));
        OnPropertyChanged(nameof(PipelineSummary));
        OnPropertyChanged(nameof(VadSatisfied));
        OnPropertyChanged(nameof(NeedsVadModelDownload));
        OnPropertyChanged(nameof(RunSummary));
        OnPropertyChanged(nameof(RunBlockedReason));
        OnPropertyChanged(nameof(HasRunBlockedReason));
    }

    /// <summary>Lets the goal set the speaker checkbox without re-entering itself.</summary>
    private void SyncGoalAndSpeakers()
    {
        _syncingGoal = true;
        try
        {
            LabelSpeakers = Goal == TranscriptionGoal.TranscribeWithSpeakers;
        }
        finally
        {
            _syncingGoal = false;
        }
    }

    partial void OnModelAliasChanged(string value)
    {
        RequestCapabilityProbe(value);
        ApplyRememberedWindow();
        OnPropertyChanged(nameof(DiarizerPlan));
        OnPropertyChanged(nameof(CanTranscribe));
        OnPropertyChanged(nameof(CanProbeMemory));
        OnPropertyChanged(nameof(RunBlockedReason));
        OnPropertyChanged(nameof(HasRunBlockedReason));
        OnPropertyChanged(nameof(RunSummary));
        UpdateMemoryWarning();
    }

    partial void OnSelectedTaskChanged(TranscriptionTask? value)
    {
        OnPropertyChanged(nameof(ShowTargetLanguage));
        OnPropertyChanged(nameof(RunSummary));
    }

    partial void OnWindowSecondsChanged(int value)
    {
        OnPropertyChanged(nameof(RunSummary));
        OnPropertyChanged(nameof(PipelineSummary));
        UpdateMemoryWarning();
    }

    partial void OnAutoAdjustWindowChanged(bool value)
        => UiSettings.SaveAutoAdjustWindow(value);

    partial void OnUseVadChanged(bool value)
    {
        // Turning the option on with a downloaded VAD model selects it, so the
        // common case is one click. With none, the run is refused and the tab
        // offers the route to Models; the option stays on so the intent is visible.
        if (value && SelectedVadModel is null)
        {
            SelectedVadModel = VadOptions.FirstOrDefault();
        }

        if (!_restoringSettings)
        {
            UiSettings.SaveTranscriptionUseVad(value);
            if (value)
            {
                UiSettings.SaveTranscriptionVadModel(SelectedVadModel?.Alias);
            }
        }

        OnPropertyChanged(nameof(NeedsVadModelDownload));
        OnPropertyChanged(nameof(VadSatisfied));
        OnPropertyChanged(nameof(PipelineSummary));
    }

    partial void OnSelectedVadModelChanged(VadOption? value)
    {
        if (!_restoringSettings && UseVad && GoalIsAsr)
        {
            UiSettings.SaveTranscriptionVadModel(value?.Alias);
        }

        OnPropertyChanged(nameof(NeedsVadModelDownload));
        OnPropertyChanged(nameof(VadSatisfied));
        OnPropertyChanged(nameof(PipelineSummary));
        OnPropertyChanged(nameof(CanTranscribe));
        OnPropertyChanged(nameof(CanProbeMemory));
        OnPropertyChanged(nameof(RunBlockedReason));
        OnPropertyChanged(nameof(HasRunBlockedReason));
    }

    partial void OnSelectedDiarizerChanged(DiarizerOption? value)
    {
        OnPropertyChanged(nameof(DiarizerPlan));
        OnPropertyChanged(nameof(DiarizerLanguageWarning));
        OnPropertyChanged(nameof(HasDiarizerLanguageWarning));
        OnPropertyChanged(nameof(CanTranscribe));
        OnPropertyChanged(nameof(CanProbeMemory));
        OnPropertyChanged(nameof(RunBlockedReason));
        OnPropertyChanged(nameof(HasRunBlockedReason));
        OnPropertyChanged(nameof(NeedsDedicatedDiarizerDownload));
        OnPropertyChanged(nameof(RunSummary));
        OnPropertyChanged(nameof(PipelineSummary));
    }

    /// <summary>
    /// Keeps the run's request aligned with the reader's intent: the model's own
    /// diarization when it has one, a downloaded dedicated diarizer otherwise, and
    /// never "on" for a model that cannot answer it.
    /// </summary>
    private void SyncSpeakerIntent()
    {
        // A model that can do neither cannot be asked for labels. A text-only goal
        // clears the box; an explicit speaker goal keeps the intent so the tab can
        // say why it cannot be honoured instead of silently dropping it.
        bool canHonour = CanAskForSpeakerLabels;
        bool asrModel = _selectedCapabilities is null
            || _selectedCapabilities.Roles.HasFlag(TranscribeRole.Asr);
        if (!canHonour && LabelSpeakers
            && !(Goal == TranscriptionGoal.TranscribeWithSpeakers && asrModel))
        {
            LabelSpeakers = false;
            return;
        }

        DiarizeMode = LabelSpeakers && CanModelDiarize
            ? DiarizeMode.DiarizeModeOn
            : DiarizeMode.DiarizeModeOff;

        if (LabelSpeakers && canHonour && !CanModelDiarize && SelectedDiarizer?.Alias is null)
        {
            // Picking the only (or first) downloaded diarizer saves a step and makes
            // the plan line name the model that will actually run. The picker stays
            // visible, so the choice can still be changed.
            SelectedDiarizer = DiarizerOptions.FirstOrDefault(o => o.Alias is not null) ?? NoDiarizer;
        }

        OnPropertyChanged(nameof(ShowDiarizerPicker));
        OnPropertyChanged(nameof(ShowDiarizerUnavailableHint));
        OnPropertyChanged(nameof(DiarizerPlan));
        OnPropertyChanged(nameof(DiarizerLanguageWarning));
        OnPropertyChanged(nameof(HasDiarizerLanguageWarning));
        OnPropertyChanged(nameof(NeedsDedicatedDiarizerDownload));
        OnPropertyChanged(nameof(SpeakerLabelsSatisfied));
        OnPropertyChanged(nameof(PipelineSummary));
        OnPropertyChanged(nameof(RunSummary));
        OnPropertyChanged(nameof(RunBlockedReason));
        OnPropertyChanged(nameof(HasRunBlockedReason));
    }

    partial void OnLabelSpeakersChanged(bool value)
    {
        // The checkbox is the optional speaker step; the goal is the same choice
        // at a higher level. Keep them in step without rebuilding the model list
        // from a checkbox click.
        if (!_syncingGoal && Goal is TranscriptionGoal.Transcribe or TranscriptionGoal.TranscribeWithSpeakers)
        {
            _syncingGoal = true;
            try
            {
                Goal = value
                    ? TranscriptionGoal.TranscribeWithSpeakers
                    : TranscriptionGoal.Transcribe;
            }
            finally
            {
                _syncingGoal = false;
            }
        }

        SyncSpeakerIntent();
    }

    partial void OnLanguageChanged(string value)
    {
        OnPropertyChanged(nameof(DiarizerLanguageWarning));
        OnPropertyChanged(nameof(HasDiarizerLanguageWarning));
        OnPropertyChanged(nameof(RunSummary));

        // The language is chosen first: changing it re-filters the model list.
        if (!_syncingLanguage && !_syncingGoal)
        {
            LoadModels();
        }

        // Re-push the selection after the list rebuild above, so the ComboBox
        // re-matches instead of staying on the first item.
        OnPropertyChanged(nameof(Language));
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

    /// <summary>
    /// Rebuilds the global language list from the union of what the downloaded
    /// models declare, plus the ten fallback codes. The list is no longer derived
    /// from the selected model: the language is chosen first and filters the
    /// models, so it must stand on its own.
    /// </summary>
    private void RebuildGlobalLanguages()
    {
        var codes = new HashSet<string>(StringComparer.Ordinal);
        foreach (string alias in DownloadedModelCatalog.Aliases())
        {
            var langs = UpstreamCatalog.Read(alias)?.Languages;
            if (langs is not null)
            {
                foreach (string l in langs)
                {
                    if (!string.IsNullOrWhiteSpace(l))
                    {
                        codes.Add(l);
                    }
                }
            }
        }

        bool anyLangDetect = DownloadedModelCatalog.Aliases().Any(a =>
        {
            var rec = UpstreamCatalog.Read(a);
            return rec?.Capabilities is not null
                && rec.Capabilities.TryGetValue("lang_detect", out var cap)
                && cap.Supported;
        });

        foreach (string f in new[] { "en", "fr", "de", "es", "it", "pt", "ru", "zh", "ja", "ko" })
        {
            codes.Add(f);
        }

        _syncingLanguage = true;
        try
        {
            AvailableLanguages.Clear();
            if (anyLangDetect)
            {
                AvailableLanguages.Add("auto");
            }
            // Keep the familiar fallback order first (en, fr, ...), so the default
            // language stays the first entry and existing selections survive.
            foreach (string f in new[] { "en", "fr", "de", "es", "it", "pt", "ru", "zh", "ja", "ko" })
            {
                if (codes.Contains(f))
                {
                    AvailableLanguages.Add(f);
                }
            }
            foreach (string c in codes)
            {
                if (!string.Equals(c, "auto", StringComparison.Ordinal)
                    && !new[] { "en", "fr", "de", "es", "it", "pt", "ru", "zh", "ja", "ko" }.Contains(c))
                {
                    AvailableLanguages.Add(c);
                }
            }
            if (AvailableLanguages.Count == 0)
            {
                AvailableLanguages.Add("en");
            }
            // "auto" is allowed to stay even when no downloaded model declares
            // language detection: the warning logic and the run treat it as a
            // distinct choice, and resetting it here would silently change the
            // reader's pick. Other values are repaired to the first available.
            if (!string.Equals(Language, "auto", StringComparison.Ordinal)
                && !AvailableLanguages.Contains(Language))
            {
                Language = AvailableLanguages.First();
            }

            // Re-push the selection: rebuilding the list drops the ComboBox's
            // match, and a value that did not change would otherwise never be
            // re-pushed. See LanguagePickerTests.
            OnPropertyChanged(nameof(Language));
        }
        finally
        {
            _syncingLanguage = false;
        }
    }

    private void UpdateAvailableLanguages(ModelCapabilitySnapshot? capabilities)
    {
        // The language list is now global (chosen first). The probe only
        // validates the current choice against what the model actually accepts,
        // and grows the global list with anything the catalogue did not declare.
        if (capabilities is null)
        {
            return;
        }

        foreach (string lang in capabilities.Languages)
        {
            if (!string.IsNullOrWhiteSpace(lang) && !AvailableLanguages.Contains(lang))
            {
                AvailableLanguages.Add(lang);
            }
        }
        if (capabilities.SupportsLanguageDetect && !AvailableLanguages.Contains("auto"))
        {
            AvailableLanguages.Insert(0, "auto");
        }

        if (AvailableLanguages.Count == 0)
        {
            if (!AvailableLanguages.Contains("en"))
            {
                AvailableLanguages.Add("en");
            }
        }

        // If the model does not accept the chosen language, fall back to the
        // first one it does accept (or "en" when it declares nothing).
        if (capabilities.Languages.Count > 0
            && !capabilities.Languages.Contains(Language)
            && !string.Equals(Language, "auto", StringComparison.Ordinal))
        {
            Language = capabilities.Languages[0];
        }
        else if (string.Equals(Language, "auto", StringComparison.Ordinal)
                 && !capabilities.SupportsLanguageDetect)
        {
            Language = capabilities.Languages.Count > 0
                ? capabilities.Languages[0]
                : (AvailableLanguages.Contains("en") ? "en" : AvailableLanguages[0]);
        }

        OnPropertyChanged(nameof(Language));
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
            SuggestedStartLocation = await PickerFolder.StartAsync(window.StorageProvider),
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
            string? path = Avalonia.Platform.Storage.StorageProviderExtensions.TryGetLocalPath(files[0]);
            AudioPath = path ?? string.Empty;

            // Only a path the picker actually resolved is a folder the user
            // used; a selection that has none leaves the previous note alone.
            PickerFolder.Remember(path);
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

        // The button is disabled in this state, but a command can also be invoked
        // from a key binding: never start a run whose speaker request cannot be
        // honoured, because it would come back with every segment as Speaker 0.
        if (!SpeakerLabelsSatisfied)
        {
            StatusMessage = RunBlockedReason;
            return;
        }

        // The CTS is owned by this method and cancelled through the action; the
        // field is null outside a run, so the view model itself owns nothing.
        using var cancellation = new CancellationTokenSource();
        _cancelTranscription = cancellation.Cancel;
        IsTranscribing = true;
        Progress = 0;
        _runStartedAt = DateTime.UtcNow;
        _firstWindowFinishedAt = null;
        _windowsCompleted = 0;
        _windowsTotal = 0;
        _windowAdjustmentNote = string.Empty;
        _phase = TranscriptionPhase.Preparing;
        ProgressIsIndeterminate = true;
        ProgressNote = "Starting. The model load and the audio read come first; neither reports a fraction.";
        // The first phase replaces this within milliseconds; it exists so the bar
        // has an honest label even if the service's first callback is delayed.
        ProgressStage = "Starting…";
        ElapsedText = string.Empty;
        RemainingText = string.Empty;
        LiveText = string.Empty;
        LastError = null;
        StatusMessage = "Starting…";
        HasResult = false;
        UiSettings.SetLastModel(UiSettings.TranscriptionTab, ModelAlias);
        _elapsedTimer.Start();

        try
        {
            var options = BuildTranscriptionOptions();
            _runOptions = options;
            _runModelAlias = ModelAlias;

            // Progress<T> posts to the UI context. The service reports from the
            // worker thread that runs the windows, and from a continuation that
            // used ConfigureAwait(false), so updating the bindings inline would be
            // a cross-thread mutation.
            var progress = new Progress<double>(p =>
            {
                Progress = p;

                // A role whose capabilities could not be read starts indeterminate;
                // the first measured fraction is proof that this run can fill the
                // bar, so it stops pretending it cannot.
                if (p > 0 && _phase == TranscriptionPhase.Transcribing)
                {
                    ProgressIsIndeterminate = false;
                    ProgressNote = _windowAdjustmentNote;
                }
            });
            var liveText = new Progress<string>(text => LiveText = text);
            var phase = new Progress<TranscriptionPhase>(ApplyPhase);

            // The same windows the bar is filled from, named. Completed is 0 on
            // the announcement made before the first window runs, so the text
            // says "window 1" then rather than "window 0".
            var stepProgress = new Progress<TranscriptionStep>(s =>
            {
                _phase = s.Phase;
                if (s.Phase == TranscriptionPhase.Diarizing)
                {
                    ProgressIsIndeterminate = true;
                    ProgressNote = "The diarizer runs the whole file in one native call and reports no progress.";
                    ProgressStage = "Diarizing — attributing speakers";
                }
                else
                {
                    // The service lowers the window only after the first attempt
                    // failed to fit; say so while the retry runs, not only at the end.
                    if (s.WindowSeconds is int usedWindow
                        && _runOptions is { } requested
                        && usedWindow < requested.WindowSeconds)
                    {
                        _windowAdjustmentNote =
                            $"The first attempt did not fit in memory; the window was lowered to {usedWindow} s.";
                    }

                    _windowsTotal = Math.Max(1, s.Total);
                    _windowsCompleted = Math.Clamp(s.Completed, 0, _windowsTotal);
                    if (s.Completed > 0 && _firstWindowFinishedAt is null)
                    {
                        _firstWindowFinishedAt = DateTime.UtcNow;
                    }

                    // A file that fits in one window has no fraction to show until
                    // the native call returns: a determinate bar at 0% would look
                    // frozen for the whole transcription. Say that instead.
                    bool singleWindow = _windowsTotal == 1 && _windowsCompleted == 0;
                    ProgressIsIndeterminate = singleWindow;
                    ProgressNote = _windowAdjustmentNote.Length > 0
                        ? _windowAdjustmentNote
                            + (singleWindow
                                ? " This file fits in one window; the native run reports no progress inside a window."
                                : string.Empty)
                        : singleWindow
                            ? "This file fits in one window; the native run reports no progress inside a window."
                            : string.Empty;
                    ProgressStage = $"Transcribing — window "
                        + $"{Math.Clamp(s.Completed == 0 ? 1 : s.Completed, 1, _windowsTotal)} of {_windowsTotal}";
                }

                UpdateProgressTiming();
            });

            Result = await _transcriptionService.TranscribeAsync(
                AudioPath, ModelAlias, options, progress, liveText, phase, stepProgress, cancellation.Token);

            // The window the run actually used. A service that had to lower it
            // after an out-of-memory failure reports the smaller one, and that is
            // what the result, the history and the field must name.
            int usedWindow = Result.EffectiveWindowSeconds ?? options.WindowSeconds;
            bool adjusted = usedWindow != options.WindowSeconds;

            ShowResult(
                Result,
                new ResultProvenance(
                    _runModelAlias, EffectiveLanguage(), DiarizeLabel(options.DiarizeMode), usedWindow));

            if (adjusted)
            {
                WindowSeconds = usedWindow;
            }

            // The field follows the combination, not the process: the next run of
            // this model on this device starts at the value that worked. A role
            // that does not use the window does not record one.
            if (Result.Roles.HasFlag(TranscribeRole.Asr) || Result.Roles.HasFlag(TranscribeRole.Vad))
            {
                RememberWindow(options, usedWindow, Result.Memory?.DeviceName);
            }

            // A result-bearing stop is not a failure: the partial transcript is
            // valid and already in the result. Say why it ends early instead of
            // calling it complete, which would overstate what the model produced.
            string outcome = Result.StopStatus switch
            {
                Status.ErrOutputRepetition =>
                    "Transcription stopped when the decoder's output began repeating; "
                    + "the transcript may be incomplete.",
                Status.ErrOutputTruncated =>
                    "Transcription stopped at the decoder's output limit; "
                    + "the transcript may be incomplete.",
                _ => CompletionMessage,
            };

            StatusMessage = adjusted
                ? $"{outcome} The window was lowered from {options.WindowSeconds} s "
                    + $"to {usedWindow} s to fit memory; that value is remembered for this model and device."
                : outcome;
            if (options.VadModel is { } vad)
            {
                StatusMessage += $" Silence was skipped with {vad}.";
            }

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
            string model = string.IsNullOrEmpty(_runModelAlias) ? "the selected model" : _runModelAlias;
            int window = _runOptions?.WindowSeconds ?? WindowSeconds;
            string attempts = ex.Data["window attempts"] is string tried
                ? $" Automatic adjustment tried {tried} and none fit."
                : string.Empty;
            StatusMessage = ex.StatusCode == Status.ErrOom
                ? $"Out of memory (ErrOom) while running {model}: lower Window (now {window} s), "
                    + "or use a smaller quantization or a lower context size. "
                    + "The CPU backend needs its own memory and is not a guaranteed fallback."
                    + attempts
                    + $" (logged to {UiLog.FilePath})"
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
            _elapsedTimer.Stop();
            _cancelTranscription = null;
            _runOptions = null;
            _runModelAlias = string.Empty;
            LiveText = string.Empty;
            ProgressNote = string.Empty;
            ProgressIsIndeterminate = false;
            ElapsedText = string.Empty;
            RemainingText = string.Empty;
            IsTranscribing = false;
        }
    }

    /// <summary>
    /// Puts a phase on screen: its label, whether the bar can fill, and the
    /// sentence that says why when it cannot.
    /// </summary>
    private void ApplyPhase(TranscriptionPhase phase)
    {
        _phase = phase;
        switch (phase)
        {
            case TranscriptionPhase.Preparing:
                ProgressIsIndeterminate = true;
                ProgressNote = "Loading the model. The native library reports no progress for a load.";
                ProgressStage = $"Loading {ModelAlias}…";
                StatusMessage = "Loading the model…";
                break;

            case TranscriptionPhase.ReadingAudio:
                ProgressIsIndeterminate = true;
                ProgressNote = "Reading and decoding the audio. The decoder reports no progress.";
                ProgressStage = "Reading the audio…";
                StatusMessage = "Reading the audio…";
                break;

            case TranscriptionPhase.DetectingSpeech:
                ProgressIsIndeterminate = true;
                ProgressNote = "The VAD model is finding the speech intervals. It reports no fraction; "
                    + "the transcriber then runs only over the speech it found.";
                ProgressStage = "Detecting speech…";
                StatusMessage = "Detecting speech…";
                break;

            case TranscriptionPhase.Diarizing:
                ProgressIsIndeterminate = true;
                // A chained diarizer follows a finished transcript; a model whose
                // whole role is diarization has no transcript to speak of.
                bool chained = LabelSpeakers && !CanModelDiarize;
                ProgressNote = chained
                    ? "The transcript is complete. The diarizer runs the whole file in one native call "
                        + "and reports no progress; this pass adds speaker turns."
                    : "Diarizing runs the whole file in one native call and reports no progress.";
                ProgressStage = "Diarizing — attributing speakers";
                StatusMessage = chained
                    ? "Diarizing… the transcript is complete; a separate diarizer is attributing speakers."
                    : "Diarizing…";
                break;

            default:
                ProgressIsIndeterminate = !PhaseReportsFraction(phase);
                ProgressNote = !ProgressIsIndeterminate
                    ? string.Empty
                    : _selectedCapabilities is null
                        ? "No fraction has been reported yet."
                        : _selectedCapabilities.Roles.HasFlag(TranscribeRole.Vad)
                            ? "Voice activity is scored one window at a time; the bar fills after the first window returns."
                            : "This role runs the whole file in one native call and reports no progress.";
                if (ProgressIsIndeterminate)
                {
                    ProgressStage = $"{RunButtonText}…";
                }

                StatusMessage = "Transcribing...";
                break;
        }

        UpdateProgressTiming();
    }

    /// <summary>
    /// Whether a phase reports a measured fraction. Windows fill the ASR and VAD
    /// paths; the other roles answer in one call.
    /// </summary>
    private bool PhaseReportsFraction(TranscriptionPhase phase)
    {
        if (phase != TranscriptionPhase.Transcribing)
        {
            return false;
        }

        // Not probed yet: nothing is known, so the bar does not claim a fraction.
        // The first window step or the first progress report replaces that as soon
        // as one arrives, so an ASR or VAD run corrects itself immediately. VAD
        // reports a fraction per window but no step, so it starts indeterminate and
        // turns determinate on its first progress report.
        return _selectedCapabilities is { } caps
            && caps.Roles.HasFlag(TranscribeRole.Asr);
    }

    /// <summary>
    /// Recomputes the elapsed line and the estimate from the windows finished so
    /// far. Called once a second and on every step, so a long run never looks
    /// frozen between windows.
    /// </summary>
    private void UpdateProgressTiming()
    {
        if (!IsTranscribing)
        {
            ElapsedText = string.Empty;
            RemainingText = string.Empty;
            return;
        }

        ElapsedText = FormatDuration(DateTime.UtcNow - _runStartedAt);

        // The estimate starts after a second window: the first one's time includes
        // whatever warm-up the model does, and using it would overstate the rest.
        if (_phase == TranscriptionPhase.Transcribing
            && _firstWindowFinishedAt is { } first
            && _windowsCompleted > 1
            && _windowsTotal > _windowsCompleted)
        {
            double secondsPerWindow =
                (DateTime.UtcNow - first).TotalSeconds / (_windowsCompleted - 1);
            TimeSpan remaining = TimeSpan.FromSeconds(
                secondsPerWindow * (_windowsTotal - _windowsCompleted));
            RemainingText = "about " + FormatDuration(remaining) + " left (estimate)";
        }
        else
        {
            RemainingText = string.Empty;
        }
    }

    /// <summary>mm:ss, or h:mm:ss past an hour. Never a rounded-up claim.</summary>
    private static string FormatDuration(TimeSpan value)
    {
        if (value < TimeSpan.Zero)
        {
            value = TimeSpan.Zero;
        }

        return value.TotalHours >= 1
            ? $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}"
            : $"{value.Minutes:00}:{value.Seconds:00}";
    }

    /// <summary>Stops the run in flight, if any.</summary>
    [RelayCommand]
    private void CancelTranscription() => _cancelTranscription?.Invoke();

    /// <summary>
    /// Selects the next occurrence of <see cref="SearchQuery"/> in the transcript,
    /// wrapping at the end. Case-insensitive; an empty query does nothing.
    /// </summary>
    public void FindNext()
    {
        if (string.IsNullOrEmpty(SearchQuery) || string.IsNullOrEmpty(FullText))
        {
            return;
        }

        int from = Math.Clamp(SearchSelectionEnd, 0, FullText.Length);
        int index = FullText.IndexOf(SearchQuery, from, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            index = FullText.IndexOf(SearchQuery, 0, StringComparison.OrdinalIgnoreCase);
        }

        if (index < 0)
        {
            SearchSelectionStart = 0;
            SearchSelectionEnd = 0;
            StatusMessage = $"No match for \"{SearchQuery}\".";
            return;
        }

        SearchSelectionStart = index;
        SearchSelectionEnd = index + SearchQuery.Length;
    }

    /// <summary>
    /// The transcript as one timestamped line per segment, with the speaker names the
    /// reader gave. This is the shape a notes tool can take; the HTML export has the
    /// same information in a document.
    /// </summary>
    internal string BuildTimestampedTranscript()
    {
        Dictionary<int, string> names = NamedSpeakers();
        var text = new System.Text.StringBuilder();
        foreach (MergedSegment segment in Segments)
        {
            string speaker = names.TryGetValue(segment.SpeakerId, out string? name)
                ? name
                : $"Speaker {segment.SpeakerId}";
            text.Append('[').Append(FormatClock(segment.Start)).Append("] ")
                .Append(speaker).Append(": ").Append(segment.Text).Append('\n');
        }

        return text.ToString();
    }

    /// <summary>
    /// Plays the audio behind one segment. This is the review step the tab can offer
    /// without an editor: the player is external (<c>ffplay</c>), and its absence is
    /// reported in the status rather than silently doing nothing.
    /// </summary>
    public void PlaySegment(MergedSegment segment)
    {
        ArgumentNullException.ThrowIfNull(segment);

        if (string.IsNullOrEmpty(AudioPath) || !File.Exists(AudioPath))
        {
            StatusMessage = "Cannot play the segment: the audio file is no longer there.";
            return;
        }

        StatusMessage = _segmentPlayer.TryPlay(AudioPath, segment.Start, segment.End, out string? error)
            ? $"Playing {FormatClock(segment.Start)}–{FormatClock(segment.End)}."
            : $"Cannot play the segment: {error}";
    }

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
                    + "or use a smaller quantization or a lower context size. "
                    + "The CPU backend needs its own memory and is not a guaranteed fallback.";
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

        if (exception.Data["window attempts"] is string attempts)
        {
            report.AppendLine(System.Globalization.CultureInfo.InvariantCulture,
                $"window attempts: {attempts}");
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
        // The snapshot the run started with, not the pickers at report time. A
        // reader can edit the tab while a run is in flight even though the run does
        // not see it; the report must describe what actually ran.
        TranscriptionOptions? snapshot = _runOptions;
        string model = snapshot is null ? ModelAlias : _runModelAlias;
        int window = snapshot is null ? WindowSeconds : snapshot.WindowSeconds;
        KvType? kv = snapshot is null ? SelectedKvType : snapshot.KvType;
        int? context = snapshot is null ? ContextSize : snapshot.ContextSize;
        BackendRequest backend = snapshot?.BackendRequest ?? _settings.SelectedBackend;
        BackendDevice? device = snapshot is null ? _settings.SelectedDevice : snapshot.Device;
        string diarizer = snapshot is null
            ? SelectedDiarizer?.Alias ?? "(none)"
            : snapshot.DiarizerModel ?? "(none)";

        var run = new System.Text.StringBuilder();
        run.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"  model: {model}");
        run.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"  window: {window} s");
        run.AppendLine(System.Globalization.CultureInfo.InvariantCulture,
            $"  timestamps: {snapshot?.TimestampKind.ToString() ?? "auto"}");
        run.AppendLine(System.Globalization.CultureInfo.InvariantCulture,
            $"  kv type: {kv?.ToString() ?? "auto"}");
        run.AppendLine(System.Globalization.CultureInfo.InvariantCulture,
            $"  context size: {(context?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "model default")}");
        run.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"  backend: {backend}");
        run.AppendLine(System.Globalization.CultureInfo.InvariantCulture,
            $"  device: {device?.Name ?? "(the backend chooses)"}");

        BackendDevice? target;
        string when;
        try
        {
            // The report is written after the failure: re-read the devices so it
            // says what the machine looked like then, not what a stale refresh said.
            IReadOnlyList<BackendDevice> live = Backends.EnumerateDevices();
            BackendDevice? pinned = device;
            if (pinned is not null)
            {
                pinned = live.FirstOrDefault(d => d.Handle == pinned.Handle && d.Handle != IntPtr.Zero)
                    ?? pinned;
            }

            target = MemoryPreflight.Target(live, backend, pinned);
            when = "live at report time";
        }
        catch (Exception ex) when (ex is TranscribeException or DllNotFoundException or EntryPointNotFoundException)
        {
            target = MemoryPreflight.Target(_settings.Devices, backend, device);
            when = "as of the last device refresh";
        }

        run.AppendLine(System.Globalization.CultureInfo.InvariantCulture,
            $"  target device: {MemoryPreflight.Describe(target)} ({when})");
        run.AppendLine(System.Globalization.CultureInfo.InvariantCulture,
            $"  diarizer: {diarizer}");
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
    /// Applies the window remembered for the selected model, backend and device.
    /// </summary>
    /// <remarks>
    /// The same model on two devices can take two different windows, and a window
    /// that a previous run had to lower should not be tried again from the top on
    /// the next launch. Only a finished run records a value; moving a picker never
    /// does. With no remembered value the field keeps what it has, so a deliberate
    /// choice is not thrown away by selecting another model.
    /// </remarks>
    private void ApplyRememberedWindow()
    {
        if (string.IsNullOrEmpty(ModelAlias))
        {
            return;
        }

        if (UiSettings.LoadWindow(WindowKey()) is int remembered)
        {
            WindowSeconds = remembered;
        }
    }

    /// <summary>The key the current pickers would look a remembered window up under.</summary>
    private string WindowKey()
        => MemoryPreflight.CombinationKey(
            ModelAlias,
            _settings.SelectedBackend,
            ResolvedDeviceName(_settings.SelectedBackend, _settings.SelectedDevice));

    /// <summary>
    /// Records the window a finished run used for its model, backend and device.
    /// </summary>
    /// <remarks>
    /// The actual device name wins over the request when the run reported one: an
    /// Auto run may have resolved to a device the pickers do not name. When it
    /// reported none, the target the request resolves to is the best the tab knows,
    /// and the pinned name is the fallback.
    /// </remarks>
    private void RememberWindow(TranscriptionOptions options, int seconds, string? actualDevice)
    {
        // The snapshot may leave the backend unset when it was Auto; the run then
        // resolved to whatever the settings said, which is the same value.
        BackendRequest backend = options.BackendRequest ?? _settings.SelectedBackend;

        string device = !string.IsNullOrEmpty(actualDevice)
            ? actualDevice
            : ResolvedDeviceName(backend, options.Device) is { Length: > 0 } target
                ? target
                : options.Device?.Name ?? string.Empty;

        UiSettings.SaveWindow(
            MemoryPreflight.CombinationKey(_runModelAlias, backend, device),
            seconds);
    }

    /// <summary>The name of the device a request resolves to, or the pinned name.</summary>
    private string ResolvedDeviceName(BackendRequest backend, BackendDevice? pinned)
        => MemoryPreflight.Target(_settings.Devices, backend, pinned)?.Name
            ?? pinned?.Name
            ?? string.Empty;

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
                SuggestedStartLocation = await PickerFolder.StartAsync(window.StorageProvider),
                DefaultExtension = "txt",
                FileTypeChoices = new[]
                {
                    new Avalonia.Platform.Storage.FilePickerFileType("Text") { Patterns = TextPatterns },
                    new Avalonia.Platform.Storage.FilePickerFileType("WebVTT") { Patterns = VttPatterns },
                    new Avalonia.Platform.Storage.FilePickerFileType("JSON") { Patterns = JsonPatterns },
                    new Avalonia.Platform.Storage.FilePickerFileType("HTML") { Patterns = HtmlPatterns },
                    new Avalonia.Platform.Storage.FilePickerFileType("Markdown") { Patterns = MarkdownPatterns },
                    new Avalonia.Platform.Storage.FilePickerFileType("SubRip subtitles") { Patterns = SrtPatterns },
                    new Avalonia.Platform.Storage.FilePickerFileType("SubStation Alpha subtitles") { Patterns = AssPatterns }
                }
            });

            if (file == null) return;

            var path = Avalonia.Platform.Storage.StorageProviderExtensions.TryGetLocalPath(file);
            if (string.IsNullOrWhiteSpace(path))
            {
                StatusMessage = "Could not export: the chosen location has no local path.";
                return;
            }

            PickerFolder.Remember(path);
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
                ".srt" => BuildSubtitleExport(SubtitleFormat.Srt),
                ".ass" => BuildSubtitleExport(SubtitleFormat.Ass),
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

}
