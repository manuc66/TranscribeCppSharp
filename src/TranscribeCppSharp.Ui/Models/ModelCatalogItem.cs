using TranscribeCppSharp.Models;
using TranscribeCppSharp.Performance;

namespace TranscribeCppSharp.Ui.Models;

/// <summary>
/// One row of the model manager: a curated alias plus what is true of it right
/// now on this machine.
/// </summary>
/// <remarks>
/// A plain wrapper over <see cref="ModelDescriptor"/> rather than a copy of it.
/// The manifest fields are the same for every user; only the cache state and the
/// display formatting change, and those are the ones a view binds to.
/// </remarks>
public class ModelCatalogItem : System.ComponentModel.INotifyPropertyChanged
{
    public ModelCatalogItem(ModelDescriptor descriptor)
    {
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        Refresh();
    }

    /// <summary>The manifest entry this row shows.</summary>
    public ModelDescriptor Descriptor { get; }

    public string Alias => Descriptor.Alias;

    public string Repo => Descriptor.Repo;

    public string Revision => Descriptor.Revision;

    public string Quant => Descriptor.Quant;

    public string License => Descriptor.License;

    public string LicenseUrl => Descriptor.LicenseUrl;

    public string Sha256 => Descriptor.Sha256;

    public string DownloadUrl => Descriptor.DownloadUrl;

    /// <summary>True when the weights are on disk and verified.</summary>
    public bool IsCached { get; private set; }

    /// <summary>Path the weights occupy, or would occupy once downloaded.</summary>
    public string LocalPath => ModelStore.CachedPath(Descriptor);

    /// <summary>Size on disk in bytes; 0 when not downloaded.</summary>
    public long CachedBytes { get; private set; }

    /// <summary>
    /// True when the license forbids commercial use.
    /// </summary>
    /// <remarks>
    /// A text test on the SPDX identifier, not a licence database. It is only as
    /// reliable as the manifest entry, so the row still shows the licence text
    /// and its URL rather than hiding the terms behind this flag.
    /// </remarks>
    public bool IsNonCommercialLicense => Descriptor.IsNonCommercialLicense;

    /// <summary>Size stated in the manifest, formatted for display.</summary>
    public string SizeText => ModelSizeFormat.Format(Descriptor.Size).ToString();

    /// <summary>
    /// Size on disk when downloaded, or the manifest size with a marker when
    /// not, so the two are never confused for one another.
    /// </summary>
    public string OnDiskText => IsCached ? $"{ModelSizeFormat.Format(CachedBytes)} on disk" : "not downloaded";

    /// <summary>
    /// The timing measured on this machine, when there is one.
    /// </summary>
    /// <remarks>
    /// Set by the view model after a measurement and cleared when the list is
    /// reloaded. Null means "not measured here", which the grid shows as a dash:
    /// it is not the same as zero and must not look like a number.
    /// </remarks>
    public ModelBenchmark? Benchmark { get; internal set; }

    /// <summary>The measured real-time factor, or a dash when unmeasured.</summary>
    public string MeasuredText => Benchmark?.RtfText ?? "-";

    /// <summary>Whether this row has a timing from this machine.</summary>
    public bool IsMeasured => Benchmark is not null;

    /// <summary>
    /// What is known about speaker diarization for this model.
    /// </summary>
    /// <remarks>
    /// The manifest declares no capabilities, and the native library only answers
    /// <c>Model.Supports(Feature.FeatureDiarization)</c> once a model is loaded.
    /// So this stays <see cref="DiarizationSupport.Unknown"/> until the weights
    /// are on disk and the row has been checked; it is never guessed from the
    /// alias. See <see cref="DiarizationDetail"/> for what one cell cannot say.
    /// </remarks>
    public DiarizationSupport Diarization { get; private set; } = DiarizationSupport.Unknown;

    /// <summary>Result of asking a loaded model whether it attributes speakers.</summary>
    public enum DiarizationSupport
    {
        /// <summary>Never checked, or the weights are not on disk.</summary>
        Unknown,

        /// <summary>A check is in flight.</summary>
        Checking,

        /// <summary><c>Supports(FeatureDiarization)</c> returned true.</summary>
        Supported,

        /// <summary><c>Supports(FeatureDiarization)</c> returned false.</summary>
        Unsupported,

        /// <summary>The model could not be loaded, so nothing was learned.</summary>
        LoadFailed,
    }

    /// <summary>Short cell text for the diarization column.</summary>
    public string DiarizationText => Diarization switch
    {
        DiarizationSupport.Checking => "checking…",
        DiarizationSupport.Supported => "yes",
        DiarizationSupport.Unsupported => "no",
        _ => "?",
    };

    /// <summary>One-line explanation for the detail pane.</summary>
    public string DiarizationDetail => Diarization switch
    {
        DiarizationSupport.Checking => "checking…",
        DiarizationSupport.Supported => "yes (checked on this machine)",
        DiarizationSupport.Unsupported => "no (checked on this machine)",
        DiarizationSupport.LoadFailed => "unknown, the model could not be loaded",
        _ => IsCached ? "unknown, not checked yet" : "unknown, not downloaded",
    };

    /// <summary>Records a check result and tells the grid about it.</summary>
    public void SetDiarization(DiarizationSupport value)
    {
        Diarization = value;
        Raise(nameof(Diarization));
        Raise(nameof(DiarizationText));
        Raise(nameof(DiarizationDetail));
    }

    /// <summary>
    /// The model family, read off the repository name.
    /// </summary>
    /// <remarks>
    /// Derived, not declared: the manifest has no family field, and the native
    /// library reports what a model supports through <c>Model.Supports</c>,
    /// which needs the model loaded — asking all 72 would mean downloading tens
    /// of gigabytes just to fill a column. The repositories are named after
    /// their family ("whisper-tiny-gguf", "parakeet-rnnt-0.6b-gguf"), so the
    /// name is the best signal available, but it is a guess and the column
    /// header says so.
    /// </remarks>
    public string Family => DeriveFamily(Descriptor.Repo);

    /// <summary>
    /// Whether this is the alias the CLI defaults to.
    /// </summary>
    /// <remarks>
    /// Marked on the grid because the Models tab banner sends the reader to the
    /// model marked 'default'. The marking says which alias a command with no
    /// <c>--model</c> ends up using; it is not a claim about quality or speed,
    /// because this project publishes no comparison between the models.
    /// </remarks>
    public bool IsDefaultModel => string.Equals(Alias, DefaultAlias, StringComparison.Ordinal);

    /// <summary>The alias the CLI uses when none is given.</summary>
    public const string DefaultAlias = "moss-transcribe-diarize";

    /// <summary>
    /// Pulls the family out of a repository name.
    /// </summary>
    /// <param name="repo">Repository as "owner/name", usually "owner/name-gguf".</param>
    /// <returns>The name without a trailing "-gguf".</returns>
    /// <remarks>
    /// A display convenience, not a classification. "whisper-large-v3-turbo"
    /// and "whisper-tiny" both yield "whisper", which is what a reader wants to
    /// see; "granite-speech-5.0-470m-turboctc" yields the whole string, which
    /// is unhelpful but not wrong. Nothing cleverer is attempted without a field
    /// in the manifest to be clever from.
    /// </remarks>
    private static string DeriveFamily(string repo)
    {
        string name = repo.Contains('/') ? repo[(repo.LastIndexOf('/') + 1)..] : repo;
        return name.EndsWith("-gguf", StringComparison.OrdinalIgnoreCase)
            ? name[..^"-gguf".Length]
            : name;
    }

    /// <summary>Re-reads the cache state. Called after a download or a delete.</summary>
    public void Refresh()
    {
        IsCached = ModelStore.IsCached(Descriptor);
        CachedBytes = IsCached ? ModelStore.CachedSize(Descriptor) : 0;
        Raise(nameof(IsCached));
        Raise(nameof(CachedBytes));
        Raise(nameof(OnDiskText));
        Raise(nameof(LocalPath));
        Raise(nameof(MeasuredText));
        Raise(nameof(IsMeasured));
        Raise(nameof(DiarizationText));
        Raise(nameof(DiarizationDetail));
    }

    private void Raise(string propertyName)
        => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));

    /// <summary>Raised when the cache state changes, so the grid can re-read the row.</summary>
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}
