using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TranscribeCppSharp.Interop;
using TranscribeCppSharp.Models;
using TranscribeCppSharp.SharedSource;
using TranscribeCppSharp.UI.Models;
using TranscribeCppSharp.UI.Services;

namespace TranscribeCppSharp.UI.ViewModels;

public partial class StreamingViewModel : ObservableObject, IDisposable
{
    private readonly ITranscriptionService _transcriptionService;

    private readonly SettingsViewModel _settings;
    private CancellationTokenSource? _cts;
    private bool _disposed;

    private readonly ModelCapabilityCache _capabilityCache;
    private ModelCapabilitySnapshot? _selectedCapabilities;

    /// <summary>The alias <see cref="_selectedCapabilities"/> belongs to, so a stale
    /// snapshot is never read as the current model's answer.</summary>
    private string? _capabilitiesAlias;

    private bool _probing;
    private string? _pendingProbeAlias;

    /// <summary>How many characters of the stream's committed text are hidden
    /// because the reader cleared the text. The native committed prefix is
    /// append-only, so everything before this offset is what the reader asked
    /// not to see again.</summary>
    private int _committedOffset;

    /// <summary>The stream's committed text as last reported. Kept so
    /// <see cref="ClearText"/> can measure how much to hide without reading the
    /// native stream, whose prefix only ever grows.</summary>
    private string _rawCommittedText = string.Empty;

    /// <summary>How many aliases are on disk, streamable or not, for the picker
    /// hint: it says how many of them the picker can offer.</summary>
    private int _onDiskCount;

    [ObservableProperty]
    private string _modelAlias = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStartStreaming))]
    private bool _isStreaming;

    [ObservableProperty]
    private string _committedText = string.Empty;

    [ObservableProperty]
    private string _tentativeText = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>True while the selected model is being loaded to read its capabilities.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStartStreaming))]
    private bool _isCheckingCapabilities;

    /// <summary>What the capability check is doing, or why options are unfiltered.</summary>
    [ObservableProperty]
    private string _capabilityStatus = string.Empty;

    [ObservableProperty]
    private ObservableCollection<string> _availableModels = new();

    [ObservableProperty]
    private StreamCommitPolicy _selectedCommitPolicy = StreamCommitPolicy.StreamCommitAuto;

    [ObservableProperty]
    private int? _stablePrefixAgreement;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAnyStreamingExtension))]
    private int? _moonshineMinDecodeIntervalMs;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAnyStreamingExtension))]
    private int? _parakeetAttContextRight;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAnyStreamingExtension))]
    private int? _parakeetLeftMs;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAnyStreamingExtension))]
    private int? _parakeetChunkMs;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAnyStreamingExtension))]
    private int? _parakeetRightMs;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAnyStreamingExtension))]
    private int? _voxtralNumDelayTokens;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAnyStreamingExtension))]
    private int? _voxtralMinDecodeIntervalMs;

    public ObservableCollection<StreamCommitPolicy> AvailableCommitPolicies { get; } = new()
    {
        StreamCommitPolicy.StreamCommitAuto,
        StreamCommitPolicy.StreamCommitOnFinalize,
        StreamCommitPolicy.StreamCommitStablePrefix,
    };

    /// <summary>Whether the family-extension group applies to the selected model.</summary>
    public bool ShowFamilyExtensions => ModelOptionVisibility.ShowStreamingExtensions(_selectedCapabilities);

    /// <summary>Whether the Moonshine extension applies.</summary>
    public bool ShowMoonshineExtensions => ModelOptionVisibility.ShowMoonshineExtensions(_selectedCapabilities);

    /// <summary>Whether the Parakeet extensions apply.</summary>
    public bool ShowParakeetExtensions => ModelOptionVisibility.ShowParakeetExtensions(_selectedCapabilities);

    /// <summary>Whether the Voxtral realtime extensions apply.</summary>
    public bool ShowVoxtralExtensions => ModelOptionVisibility.ShowVoxtralExtensions(_selectedCapabilities);

    /// <summary>
    /// Whether any family-extension field is set, which is what Reset clears.
    /// </summary>
    /// <remarks>
    /// An empty field is not "off": the extension is left off the run entirely, so
    /// the model's own default applies. That is what the fields start at, and what
    /// Reset returns them to — which is why Reset has something to do.
    /// </remarks>
    public bool HasAnyStreamingExtension
        => MoonshineMinDecodeIntervalMs.HasValue
        || ParakeetAttContextRight.HasValue
        || ParakeetLeftMs.HasValue
        || ParakeetChunkMs.HasValue
        || ParakeetRightMs.HasValue
        || VoxtralNumDelayTokens.HasValue
        || VoxtralMinDecodeIntervalMs.HasValue;

    /// <summary>
    /// Clears every family-extension field, so the run carries no extension and each
    /// model uses its own default.
    /// </summary>
    [RelayCommand]
    private void ResetExtensions()
    {
        MoonshineMinDecodeIntervalMs = null;
        ParakeetAttContextRight = null;
        ParakeetLeftMs = null;
        ParakeetChunkMs = null;
        ParakeetRightMs = null;
        VoxtralNumDelayTokens = null;
        VoxtralMinDecodeIntervalMs = null;
    }

    /// <summary>False while streaming, while a capability check runs, with no
    /// streamable model on disk, or when the selected model cannot stream.</summary>
    public bool CanStartStreaming
        => HasStreamableModels && !IsStreaming && !IsCheckingCapabilities && SelectedModelStreams;

    /// <summary>
    /// Whether the selected model can stream.
    /// </summary>
    /// <remarks>
    /// The loaded model answers when it has been checked on this machine; until
    /// then upstream's pinned catalog entry for the alias does, which is the same
    /// source the Models tab's capability filter reads. Both are real records, and
    /// neither is a guess. When neither has an answer — an alias with no catalog
    /// entry, or a probe that failed — the answer is yes: refusing on unknown
    /// would block a model that works.
    /// <para>
    /// The picker already leaves out the models upstream records as unable to
    /// stream, MOSS among them, so in practice this only overrides upstream when
    /// the machine's own answer disagrees with it.
    /// </para>
    /// </remarks>
    public bool SelectedModelStreams
        => _selectedCapabilities is { } checkedHere
            && string.Equals(_capabilitiesAlias, ModelAlias, StringComparison.Ordinal)
            ? checkedHere.SupportsStreaming
            : DeclaredStreaming(ModelAlias);

    /// <summary>
    /// What sits under the picker when the selected model cannot stream, empty
    /// otherwise, so a disabled Start says why.
    /// </summary>
    public string StreamingSupportHint
        => SelectedModelStreams
            ? string.Empty
            : $"{ModelAlias} does not support streaming. Download a model that does — the Models tab can filter on the 'streaming' capability.";

    /// <summary>Upstream's declaration for an alias; true when it declares nothing.</summary>
    private static bool DeclaredStreaming(string alias)
    {
        Dictionary<string, UpstreamCatalog.Capability>? capabilities =
            UpstreamCatalog.Read(alias)?.Capabilities;

        if (capabilities is null
            || !capabilities.TryGetValue("streaming", out UpstreamCatalog.Capability? streaming))
        {
            return true;
        }

        return streaming.Supported;
    }

    /// <summary>Whether any downloaded model can stream, which is what the picker
    /// offers.</summary>
    public bool HasStreamableModels => AvailableModels.Count > 0;

    /// <summary>
    /// What sits under the model picker. Present even when the list is full: the
    /// route to the other models should not appear only once you have run out.
    /// </summary>
    public string ModelPickerHint
        => DownloadedModelCatalog.StreamingHint(AvailableModels.Count, _onDiskCount);

    /// <summary>Raised when the reader asks to go and fetch more models.</summary>
    public event Action? OpenModelManagerRequested;

    /// <summary>The button under the picker. The window decides which tab that means.</summary>
    [RelayCommand]
    private void OpenModelManager() => OpenModelManagerRequested?.Invoke();

    public StreamingViewModel(
        ITranscriptionService transcriptionService,
        SettingsViewModel settings,
        ModelCapabilityCache capabilityCache)
    {
        _transcriptionService = transcriptionService;
        _settings = settings;
        _capabilityCache = capabilityCache;
        LoadModels();
        // The alias the picker settled on — the streaming model last run, else the
        // first offered — bounds the startup probe to a single model. A model not
        // on disk is not loaded.
        RequestCapabilityProbe(ModelAlias);
    }

    /// <summary>
    /// Rebuilds the picker from what is on disk and repairs the selection.
    /// </summary>
    /// <remarks>
    /// Only downloaded models that may stream are offered. The test is upstream's
    /// pinned declaration — the same source the Models tab's capability filter
    /// reads — because asking each model would load every downloaded one. A model
    /// upstream records as unable to stream is left out; one it says nothing about
    /// is kept, because unknown is not a no.
    /// <para>
    /// The selection has to be repaired rather than left alone: the picker only
    /// offers downloaded models, so an alias that was deleted — or filtered out —
    /// would show as no selection while still starting a stream against it. The
    /// repaired selection is the streaming model this tab last ran when it is still
    /// offered, so reopening the app lands on the model you were using; see
    /// <see cref="DownloadedModelCatalog.Selection"/>.
    /// </para>
    /// </remarks>
    private void LoadModels()
    {
        IReadOnlyList<string> onDisk = DownloadedModelCatalog.Aliases();
        _onDiskCount = onDisk.Count;

        AvailableModels.Clear();
        foreach (string alias in onDisk)
        {
            if (DeclaredStreaming(alias))
            {
                AvailableModels.Add(alias);
            }
        }

        ModelAlias = DownloadedModelCatalog.Selection(
            ModelAlias, AvailableModels, UiSettings.LastModel(UiSettings.StreamingTab));

        OnPropertyChanged(nameof(ModelPickerHint));
        OnPropertyChanged(nameof(HasStreamableModels));
        OnPropertyChanged(nameof(CanStartStreaming));
    }

    /// <summary>
    /// Re-reads the cache. Called when the reader comes back from the Models tab,
    /// which is where a download or a delete happens.
    /// </summary>
    public void RefreshModels() => LoadModels();

    partial void OnModelAliasChanged(string value)
    {
        // The new alias answers for itself until a probe replaces it: the hint and
        // the button must not keep the previous model's verdict.
        OnPropertyChanged(nameof(SelectedModelStreams));
        OnPropertyChanged(nameof(StreamingSupportHint));
        OnPropertyChanged(nameof(CanStartStreaming));
        RequestCapabilityProbe(value);
    }

    /// <summary>
    /// Reads the capabilities of the selected model, once per alias. The answer
    /// is held by the shared <see cref="ModelCapabilityCache"/>, so the
    /// transcription tab and this one load the same model once between them.
    /// </summary>
    /// <remarks>
    /// Nothing is hidden for a model that was not checked: an unloaded model
    /// shows every extension, because a wrong guess would hide one that works.
    /// The architecture gates the family; <c>SupportsStreaming</c> gates the
    /// group, so an offline Parakeet (same architecture as a streaming one) does
    /// not show the buffered-streaming controls.
    /// </remarks>
    private void RequestCapabilityProbe(string alias)
    {
        if (_capabilityCache.TryGet(alias, out ModelCapabilitySnapshot? cached))
        {
            ApplyCapabilities(alias, cached);
            CapabilityStatus = string.Empty;
            return;
        }

        ModelDescriptor? descriptor = ModelStore.Catalog.FirstOrDefault(m => m.Alias == alias);
        if (descriptor is null || !ModelStore.IsCached(descriptor))
        {
            ApplyCapabilities(alias, null);
            CapabilityStatus = descriptor is null
                ? string.Empty
                : $"{alias} is not downloaded, so its capabilities are unknown. Every option is shown until it is checked.";
            return;
        }

        if (_probing)
        {
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
            // load rather than starting a second one. The probe is not
            // cancellable — Model.Load takes no token, and this is not part of
            // the streaming scope that _cts governs.
            ModelCapabilitySnapshot snapshot = await _capabilityCache
                .GetAsync(alias, path).ConfigureAwait(true);

            if (string.Equals(ModelAlias, alias, StringComparison.Ordinal))
            {
                ApplyCapabilities(alias, snapshot);
                CapabilityStatus = string.Empty;
            }
        }
        catch (Exception ex)
        {
            UiLog.Failure($"capability probe for {alias}", ex);
            if (string.Equals(ModelAlias, alias, StringComparison.Ordinal))
            {
                ApplyCapabilities(alias, null);
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

    private void ApplyCapabilities(string alias, ModelCapabilitySnapshot? capabilities)
    {
        _selectedCapabilities = capabilities;
        _capabilitiesAlias = capabilities is null ? null : alias;
        OnPropertyChanged(nameof(ShowFamilyExtensions));
        OnPropertyChanged(nameof(ShowMoonshineExtensions));
        OnPropertyChanged(nameof(ShowParakeetExtensions));
        OnPropertyChanged(nameof(ShowVoxtralExtensions));
        OnPropertyChanged(nameof(SelectedModelStreams));
        OnPropertyChanged(nameof(StreamingSupportHint));
        OnPropertyChanged(nameof(CanStartStreaming));
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task StartStreamingAsync()
    {
        if (IsStreaming)
        {
            return;
        }

        // The button is disabled for this, but the command is the gate: a binding
        // that has not caught up must not reach the native library with a model it
        // will refuse.
        if (!SelectedModelStreams)
        {
            StatusMessage = $"{ModelAlias} does not support streaming. Pick a model that does.";
            return;
        }

        IsStreaming = true;
        StatusMessage = "Streaming...";
        UiSettings.SetLastModel(UiSettings.StreamingTab, ModelAlias);
        CommittedText = string.Empty;
        TentativeText = string.Empty;
        _committedOffset = 0;
        _rawCommittedText = string.Empty;
        _cts = new CancellationTokenSource();

        try
        {
            var options = new StreamOptions
            {
                // The backend and device chosen in Settings. These were
                // displayed but read by nothing, so the pickers did nothing.
                BackendRequest = _settings.SelectedBackend,
                Device = _settings.SelectedDevice,
                CommitPolicy = SelectedCommitPolicy,
                StablePrefixAgreement = (uint?)StablePrefixAgreement,
                MoonshineMinDecodeIntervalMs = MoonshineMinDecodeIntervalMs,
                ParakeetAttContextRight = ParakeetAttContextRight,
                ParakeetLeftMs = ParakeetLeftMs,
                ParakeetChunkMs = ParakeetChunkMs,
                ParakeetRightMs = ParakeetRightMs,
                VoxtralNumDelayTokens = VoxtralNumDelayTokens,
                VoxtralMinDecodeIntervalMs = VoxtralMinDecodeIntervalMs,
            };

            await foreach (var update in _transcriptionService.StreamTranscribeAsync(
                ModelAlias, options, _cts.Token))
            {
                _rawCommittedText = update.CommittedText;
                CommittedText = _rawCommittedText[Math.Min(_committedOffset, _rawCommittedText.Length)..];
                TentativeText = update.TentativeText;
            }

            StatusMessage = "Streaming complete.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Streaming cancelled.";
        }
        catch (Exception ex)
        {
            UiLog.Failure($"streaming start for {ModelAlias}", ex);
            StatusMessage = $"Error: {ex.Message} (logged to {UiLog.FilePath})";
        }
        finally
        {
            IsStreaming = false;
        }
    }

    [RelayCommand]
    private void StopStreaming()
    {
        _cts?.Cancel();
        StatusMessage = "Stopping...";
    }

    /// <summary>
    /// Empties the two text areas. While streaming, only text produced after the
    /// click appears again: the native committed prefix is append-only, so what
    /// it holds when the reader clears is hidden up to that point. The native
    /// stream is not reset — the model keeps the audio context it has — so this
    /// is a display reset, not a new stream.
    /// </summary>
    [RelayCommand]
    private void ClearText()
    {
        _committedOffset = _rawCommittedText.Length;
        CommittedText = string.Empty;
        TentativeText = string.Empty;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cts?.Cancel();
        _cts?.Dispose();
        GC.SuppressFinalize(this);
    }
}
