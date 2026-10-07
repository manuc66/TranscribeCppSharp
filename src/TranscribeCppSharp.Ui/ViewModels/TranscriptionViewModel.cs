using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TranscribeCppSharp.Audio;
using TranscribeCppSharp.Interop;
using TranscribeCppSharp.Models;
using TranscribeCppSharp.Ui.Models;
using TranscribeCppSharp.Ui.Services;

namespace TranscribeCppSharp.Ui.ViewModels;

public partial class TranscriptionViewModel : ObservableObject
{
    private readonly ITranscriptionService _transcriptionService;

    private readonly SettingsViewModel _settings;

    private readonly Dictionary<string, ModelCapabilitySnapshot> _capabilityCache = new(StringComparer.Ordinal);

    private ModelCapabilitySnapshot? _selectedCapabilities;

    private bool _probing;

    private string? _pendingProbeAlias;

    [ObservableProperty]
    private string _audioPath = string.Empty;

    [ObservableProperty]
    private string _modelAlias = "moss-transcribe-diarize";

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
    private bool _hasResult;

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

    public TranscriptionViewModel(
        ITranscriptionService transcriptionService,
        SettingsViewModel settings)
    {
        _transcriptionService = transcriptionService;
        _settings = settings;

        // Both pickers are seeded first, before LoadModels: a ComboBox bound to an
        // empty list has nothing to match, and the reader sees a blank box until
        // something answers.
        TargetOptions.Add(DefaultTargetOption);
        SelectedTargetOption = DefaultTargetOption;

        // The language list is seeded first, before LoadModels: that call can start
        // the capability probe, and a probe on a cache with nothing in it answers
        // synchronously by clearing this list and refilling it. Seeding afterwards
        // would append a second copy of every code.
        AddFallbackLanguages();

        LoadModels();
        // The default alias is a fixed one (MOSS, ~700 MB), so this bounds the
        // startup probe to a single known model. It runs off the UI thread; a
        // model that is not on disk is not loaded and the options stay unfiltered.
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
    /// </remarks>
    private void LoadModels()
    {
        AvailableModels.Clear();
        foreach (string alias in DownloadedModelCatalog.Aliases())
        {
            AvailableModels.Add(alias);
        }

        if (AvailableModels.Count == 0)
        {
            ModelAlias = string.Empty;
        }
        else if (!AvailableModels.Contains(ModelAlias))
        {
            ModelAlias = AvailableModels[0];
        }

        OnPropertyChanged(nameof(ModelPickerHint));
        OnPropertyChanged(nameof(HasDownloadedModels));
        OnPropertyChanged(nameof(CanTranscribe));
    }

    /// <summary>
    /// Re-reads the cache. Called when the reader comes back from the Models tab,
    /// which is where a download or a delete happens.
    /// </summary>
    public void RefreshModels() => LoadModels();

    partial void OnModelAliasChanged(string value) => RequestCapabilityProbe(value);

    /// <summary>
    /// Reads the capabilities of the selected model, once per alias.
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
        if (_capabilityCache.TryGetValue(alias, out ModelCapabilitySnapshot? cached))
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
            ModelCapabilitySnapshot snapshot = await System.Threading.Tasks.Task.Run(
                () => ModelCapabilityProbe.Probe(path)).ConfigureAwait(true);

            _capabilityCache[alias] = snapshot;
            if (string.Equals(ModelAlias, alias, StringComparison.Ordinal))
            {
                ApplyCapabilities(snapshot);
                CapabilityStatus = string.Empty;
            }
        }
        catch (Exception ex)
        {
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
        UpdateAvailableLanguages(capabilities);
        UpdateTargetOptions(capabilities);
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
                DiarizeMode = DiarizeMode.DiarizeModeDefault,
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
            };

            var progress = new Progress<double>(p => Progress = p);

            Result = await _transcriptionService.TranscribeAsync(
                AudioPath, ModelAlias, options, progress);

            FullText = Result.FullText;
            Segments = new ObservableCollection<MergedSegment>(Result.Segments);
            Words = new ObservableCollection<WordResult>(Result.Words);
            SpeakerSegments = new ObservableCollection<SpeakerSegmentResult>(Result.SpeakerSegments);
            HasResult = true;
            StatusMessage = "Transcription complete.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
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
                new Avalonia.Platform.Storage.FilePickerFileType("JSON") { Patterns = JsonPatterns }
            }
        });

        if (file == null) return;

        var path = Avalonia.Platform.Storage.StorageProviderExtensions.TryGetLocalPath(file);
        if (string.IsNullOrWhiteSpace(path)) return;

        var ext = Path.GetExtension(path).ToLowerInvariant();
        var content = ext switch
        {
            ".vtt" => ExportAsVtt(),
            ".json" => ExportAsJson(),
            _ => FullText
        };

        await File.WriteAllTextAsync(path, content);
        StatusMessage = $"Exported to {path}";
    }

    private string ExportAsVtt()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("WEBVTT").AppendLine();
        sb.AppendLine();
        foreach (var segment in Segments)
        {
            sb.Append(FormatTime(segment.Start)).Append(" --> ").Append(FormatTime(segment.End)).AppendLine();
            sb.Append(segment.Text).AppendLine();
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private string ExportAsJson()
    {
        return System.Text.Json.JsonSerializer.Serialize(Result, JsonOptions);
    }

    private static string FormatTime(TimeSpan time)
    {
        return $"{time.Hours:D2}:{time.Minutes:D2}:{time.Seconds:D2}.{time.Milliseconds:D3}";
    }
}
