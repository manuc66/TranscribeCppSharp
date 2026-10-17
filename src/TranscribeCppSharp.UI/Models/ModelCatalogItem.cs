using TranscribeCppSharp.Models;
using TranscribeCppSharp.SharedSource;
using TranscribeCppSharp.Performance;

namespace TranscribeCppSharp.UI.Models;

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

    /// <summary>
    /// The languages the model card declares, short enough for a column.
    /// </summary>
    /// <remarks>
    /// A column cannot hold 99 codes, so it shows the first four and counts the rest;
    /// the detail pane below the grid carries the whole list. What is shown is what
    /// the card says, not what the model has been asked — the loaded model is the
    /// answer to that, and the manifest only tells a reader something before they
    /// download. A missing list renders as a dash, matching how an unmeasured timing
    /// renders, rather than as an empty cell that reads as a bug.
    /// </remarks>
    public string LanguagesText
    {
        get
        {
            string[]? languages = Descriptor.Languages;
            if (languages is null || languages.Length == 0)
            {
                return "-";
            }

            const int shown = 4;
            return languages.Length <= shown
                ? string.Join(", ", languages)
                : $"{string.Join(", ", languages[..shown])} +{languages.Length - shown}";
        }
    }

    /// <summary>
    /// The full list for the detail pane, named with its provenance.
    /// </summary>
    /// <remarks>
    /// Prefixed with the source because the manifest and the model can disagree, and
    /// a reader of the pane has no other way to tell which one they are looking at.
    /// </remarks>
    public string LanguagesDetail
    {
        get
        {
            string[]? languages = Descriptor.Languages;
            return languages is null || languages.Length == 0
                ? "Languages: not stated in the model card."
                : $"Languages per the model card ({languages.Length}): {string.Join(", ", languages)}";
        }
    }

    private UpstreamCatalog.Record? _upstream;
    private bool _upstreamLoaded;

    /// <summary>
    /// Upstream's record for this model, read once and then held.
    /// </summary>
    /// <remarks>
    /// Lazy because only the selected row's detail pane reads it: building the grid
    /// opens no archives, and scrolling it opens none either. Null means upstream
    /// has no record — the detail lines then show nothing rather than a placeholder
    /// that would read as a missing feature.
    /// </remarks>
    private UpstreamCatalog.Record? Upstream
    {
        get
        {
            if (!_upstreamLoaded)
            {
                _upstream = UpstreamCatalog.Read(Alias);
                _upstreamLoaded = true;
            }

            return _upstream;
        }
    }

    /// <summary>
    /// Parameter count and where the checkpoint came from, as upstream records it.
    /// </summary>
    /// <remarks>
    /// Empty when upstream has no record, rather than a dash: this pane is about
    /// what is known, and an em-dash next to a number implies a measured zero.
    /// </remarks>
    public string UpstreamDetail
    {
        get
        {
            UpstreamCatalog.Record? record = Upstream;
            if (record is null || !record.HasIdentity)
            {
                return string.Empty;
            }

            var parts = new List<string>(3);
            if (record.Params > 0)
            {
                parts.Add($"{record.Params:N0} parameters");
            }

            if (record.UpstreamRepo is not null)
            {
                parts.Add(record.UpstreamCommit is null
                    ? record.UpstreamRepo
                    : $"{record.UpstreamRepo} @ {record.UpstreamCommit}");
            }

            if (record.Family is not null)
            {
                parts.Add($"family {record.Family}");
            }

            return string.Join(" · ", parts);
        }
    }

    /// <summary>
    /// Every quantization upstream publishes, and what each weighs.
    /// </summary>
    /// <remarks>
    /// The manifest pins one quantization per model — the one this project
    /// downloads and verifies. The other published ones are listed for comparison
    /// only: none of them has been fetched, checked against a sha256 or measured
    /// here, so this is a catalogue of what exists upstream and not of what is
    /// available on disk.
    /// </remarks>
    public string QuantizationsDetail
    {
        get
        {
            List<UpstreamCatalog.Download>? downloads = Upstream?.Downloads;
            if (downloads is null || downloads.Count == 0)
            {
                return string.Empty;
            }

            return "Published: " + string.Join(" · ", downloads.Select(d =>
                $"{d.Quant} {ModelSizeFormat.Format(d.SizeBytes)}"));
        }
    }

    /// <summary>
    /// What upstream says can be done with this model, and what was checked.
    /// </summary>
    /// <remarks>
    /// The two flags are kept apart because upstream keeps them apart: a capability
    /// can be supported and unverified, and merging them would turn their caveat
    /// into a claim they do not make. Only what is supported is listed; "unverified"
    /// is stated when none of the listed ones was checked.
    /// </remarks>
    public string CapabilitiesDetail
    {
        get
        {
            Dictionary<string, UpstreamCatalog.Capability>? caps = Upstream?.Capabilities;
            if (caps is null)
            {
                return string.Empty;
            }

            List<string> supported = caps
                .Where(kv => kv.Value.Supported)
                .Select(kv => kv.Key)
                .ToList();
            if (supported.Count == 0)
            {
                return string.Empty;
            }

            bool anyVerified = caps.Any(kv => kv.Value.Supported && kv.Value.Verified);
            return anyVerified
                ? string.Join(", ", supported)
                : string.Join(", ", supported) + " (unverified upstream)";
        }
    }

    /// <summary>
    /// The accuracy row upstream says to quote, preferring the quantization pinned
    /// in this manifest.
    /// </summary>
    /// <remarks>
    /// Preferred for the pinned quant because that is the file a reader would
    /// download — the first version of this printed F32, which nobody does here.
    /// Falls back to any quantization of the same benchmark rather than reporting
    /// nothing, and every place that prints it says which one it got.
    /// </remarks>
    private UpstreamCatalog.Accuracy? HeadlineRow
    {
        get
        {
            UpstreamCatalog.Record? record = Upstream;
            UpstreamCatalog.Headline? headline = record?.HeadlineBenchmark;
            if (headline is null)
            {
                return null;
            }

            static bool Same(UpstreamCatalog.Accuracy a, UpstreamCatalog.Headline h)
                => string.Equals(a.Dataset, h.Dataset, StringComparison.OrdinalIgnoreCase)
                && string.Equals(a.Language, h.Language, StringComparison.OrdinalIgnoreCase)
                && string.Equals(a.Metric, h.Metric, StringComparison.OrdinalIgnoreCase);

            List<UpstreamCatalog.Accuracy> rows = record!.AccuracyBenchmarks ?? new();
            return rows.FirstOrDefault(a => Same(a, headline)
                        && string.Equals(a.Quant, Descriptor.Quant, StringComparison.OrdinalIgnoreCase))
                ?? rows.FirstOrDefault(a => Same(a, headline));
        }
    }

    /// <summary>
    /// The headline figure, short enough for a column.
    /// </summary>
    public string WerText
    {
        get
        {
            UpstreamCatalog.Accuracy? row = HeadlineRow;
            return row?.ErrPct is { } pct ? $"{pct:0.##} %" : "-";
        }
    }

    /// <summary>
    /// What the figure in the WER column is, and whose it is.
    /// </summary>
    /// <remarks>
    /// Named as theirs, on their benchmark, with the provenance they record — and
    /// with the sentence this project would otherwise be implying: a published
    /// word-error rate on someone else's audio is not a measurement of yours, and
    /// this project measures none.
    /// </remarks>
    public string WerDetail
    {
        get
        {
            UpstreamCatalog.Accuracy? row = HeadlineRow;
            UpstreamCatalog.Headline? headline = Upstream?.HeadlineBenchmark;
            if (row is null || row.ErrPct is null)
            {
                return string.Empty;
            }

            string measured = row.MeasurementProvenance ?? "engine build not recorded upstream";
            return $"{row.Metric?.ToUpperInvariant()} {row.ErrPct:0.##} % on {headline!.Dataset} " +
                $"{headline.Split}, {headline.Language}, {row.Quant ?? Descriptor.Quant} — " +
                $"measured by transcribe.cpp ({measured}). Not a measurement of your audio; " +
                "this project publishes no accuracy figure of its own.";
        }
    }

    /// <summary>
    /// The one accuracy result upstream says to quote for this model.
    /// </summary>
    /// <remarks>
    /// Named as theirs, on their benchmark, because that is what it is — this
    /// project measures nothing like it. Where the row carries no provenance the
    /// result is still shown, with the absence stated rather than filled in: many
    /// upstream rows are <c>legacy-published</c> with no engine build recorded, and
    /// saying "measured" without one would overstate what is known.
    /// </remarks>
    public string HeadlineAccuracyDetail
    {
        get
        {
            UpstreamCatalog.Accuracy? row = HeadlineRow;
            UpstreamCatalog.Headline? headline = Upstream?.HeadlineBenchmark;
            if (row is null)
            {
                return string.Empty;
            }

            // The headline is an error rate (WER/CER/DER) for a recognizer and an
            // accuracy for a language model; both are "the one figure upstream says
            // to quote", so both are shown, each with its own metric named.
            string? figure = row.ErrPct is { } err ? $"{err:0.##} %"
                : row.AccPct is { } acc ? $"{acc:0.##} %"
                : null;
            if (figure is null)
            {
                return string.Empty;
            }

            string measured = row.MeasurementProvenance is null
                ? "engine build not recorded upstream"
                : row.MeasurementProvenance;
            return $"{row.Metric?.ToUpperInvariant()} {figure} — upstream, " +
                $"{headline!.Dataset} {headline.Split}, {headline.Language}, " +
                $"{row.Quant ?? Descriptor.Quant} · {measured}";
        }
    }

    /// <summary>
    /// Whether upstream publishes a headline figure for this model — an error rate
    /// (WER/CER/DER) for a recognizer, or an accuracy for a language model.
    /// </summary>
    internal bool HasHeadlineAccuracy
        => HeadlineRow is { ErrPct: not null } or { AccPct: not null };

    /// <summary>
    /// Whether upstream records any capability as supported for this model. A
    /// language-identification model supports none of the ASR-centric flags, so its
    /// capabilities line is legitimately empty.
    /// </summary>
    internal bool HasSupportedCapability
        => Upstream?.Capabilities?.Values.Any(c => c.Supported) == true;

    /// <summary>
    /// Whether upstream records this capability as supported for this model.
    /// </summary>
    /// <param name="capability">The key, e.g. "streaming" or "diarize".</param>
    /// <returns>
    /// False both when the model does not support it and when there is no record to
    /// say — the filter is on what is known, and an unknown model is not a model
    /// that has the feature.
    /// </returns>
    public bool Supports(string capability)
        => Upstream?.Capabilities?.TryGetValue(capability, out UpstreamCatalog.Capability? cap) == true
        && cap.Supported;

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
    /// Whether the weights are here.
    /// </summary>
    /// <remarks>
    /// A yes or a no rather than a size: the Size column beside it already carries
    /// the download size, and the line above the grid carries the total on disk, so
    /// repeating the bytes per row cost the column its width for information the
    /// reader already had twice. What the yes means — verified against the
    /// manifest's sha256 — is spelled out in the detail pane, next to the path.
    /// </remarks>
    public string OnDiskText => IsCached ? "yes" : "no";

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

    /// <summary>True while this row's weights are being fetched.</summary>
    /// <remarks>
    /// Held on the row rather than read from the view model's single
    /// <c>BusyAlias</c>, so the row itself can show it: the button and the progress
    /// bar in the Action column bind to the item they belong to.
    /// </remarks>
    public bool IsDownloading { get; private set; }

    /// <summary>Fraction fetched, 0 to 1, while <see cref="IsDownloading"/>.</summary>
    public double DownloadProgress { get; private set; }

    /// <summary>The Download button's label: it says what the row is doing.</summary>
    public string DownloadButtonText => IsDownloading ? "Downloading…" : "Download";

    /// <summary>Marks the row as downloading, or finished.</summary>
    /// <param name="value">true while the fetch is in flight.</param>
    public void SetDownloading(bool value)
    {
        IsDownloading = value;
        if (!value)
        {
            DownloadProgress = 0;
        }

        Raise(nameof(IsDownloading));
        Raise(nameof(DownloadButtonText));
        Raise(nameof(DownloadProgress));
    }

    /// <summary>Records how much has been fetched.</summary>
    /// <param name="value">Fraction, 0 to 1.</param>
    public void SetDownloadProgress(double value)
    {
        DownloadProgress = value;
        Raise(nameof(DownloadProgress));
    }

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

    /// <summary>
    /// Upstream's diarize flag for this model, or null when it records none.
    /// </summary>
    /// <remarks>
    /// Read straight from the catalog, independent of any local check, so a caller
    /// can ask "what does upstream say?" rather than "what is shown?".
    /// </remarks>
    public bool? UpstreamDeclaresDiarization
    {
        get
        {
            Dictionary<string, UpstreamCatalog.Capability>? caps = Upstream?.Capabilities;
            if (caps is null || !caps.TryGetValue("diarize", out UpstreamCatalog.Capability? cap))
            {
                return null;
            }

            return cap.Supported;
        }
    }

    /// <summary>
    /// Whether loading this model could change the diarization answer.
    /// </summary>
    /// <remarks>
    /// False when upstream already records <c>diarize: false</c>: there is nothing
    /// to confirm, and loading the weights to re-ask a question that is already
    /// answered "no" is the waste the check used to do for 72 of the 76 models.
    /// A model upstream records as supported is worth checking — those are the ones
    /// it marks <c>verified: false</c> — and one it records nothing about is worth
    /// checking too, because then nothing is known.
    /// </remarks>
    public bool DiarizationWorthChecking => UpstreamDeclaresDiarization != false;

    /// <summary>
    /// Whether upstream records diarization for this model, or null when unknown.
    /// </summary>
    /// <remarks>
    /// Read only when nothing has been asked of the model on this machine, and only
    /// ever as the answer to "not checked yet": a local check that succeeded
    /// overrides it, and one that failed does not let a catalog entry stand in for
    /// a result the machine could not produce.
    /// </remarks>
    private bool? UpstreamDiarization
        => Diarization is not DiarizationSupport.Unknown ? null : UpstreamDeclaresDiarization;

    /// <summary>True when upstream records the capability as checked, not merely claimed.</summary>
    private bool UpstreamDiarizationVerified
        => Upstream?.Capabilities?.TryGetValue("diarize", out UpstreamCatalog.Capability? cap) == true
        && cap.Verified;

    public string DiarizationText => Diarization switch
    {
        DiarizationSupport.Checking => "checking…",
        DiarizationSupport.Supported => "yes",
        DiarizationSupport.Unsupported => "no",

        // Asked here and the load failed: no answer. Printing upstream's claim in
        // the cell would turn a machine that could not load the model into a
        // result, which is the one thing this column must not do.
        DiarizationSupport.LoadFailed => "?",

        _ => UpstreamDiarization switch
        {
            true => "yes",
            false => "no",
            _ => "?",
        },
    };

    /// <summary>One-line explanation for the detail pane.</summary>
    public string DiarizationDetail => Diarization switch
    {
        DiarizationSupport.Checking => "checking…",
        DiarizationSupport.Supported => "yes (checked on this machine)",
        DiarizationSupport.Unsupported => "no (checked on this machine)",
        DiarizationSupport.LoadFailed => "unknown, the model could not be loaded",

        _ when UpstreamDiarization == true => UpstreamDiarizationVerified
            ? "yes (per transcribe.cpp's catalog, verified by them; not checked on this machine)"
            : "yes (per transcribe.cpp's catalog, unverified by them; not checked on this machine)",
        _ when UpstreamDiarization == false => "no (per transcribe.cpp's catalog)",
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
    /// The model family as upstream records it, falling back to the repository name.
    /// </summary>
    /// <remarks>
    /// Prefers the manifest, which upstream populates from their own source of
    /// truth, over deriving it from the repository name: deriving gives no family
    /// at all for SenseVoiceSmall-gguf, and is a guess even when it works. The
    /// derived path stays for an entry whose manifest predates the field.
    /// The original reasoning, now historical: the manifest had no family field,
    /// and the native
    /// library reports what a model supports through <c>Model.Supports</c>,
    /// which needs the model loaded — asking all 76 would mean downloading tens
    /// of gigabytes just to fill a column. The repositories are named after
    /// their family ("whisper-tiny-gguf", "parakeet-rnnt-0.6b-gguf"), so the
    /// name is the best signal available, but it is a guess and the column
    /// header says so.
    /// </remarks>
    public string Family => Descriptor.Family ?? DeriveFamily(Descriptor.Repo);

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
