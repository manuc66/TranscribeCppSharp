using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using TranscribeCppSharp.Ui.ViewModels;
using TranscribeCppSharp.Ui.Views;
using Xunit;

namespace TranscribeCppSharp.Ui.Tests;

/// <summary>
/// Reads AutomationProperties back from the controls. A screenshot cannot show
/// an automation id (it changes no pixel), so these tests are the only place
/// the ids are actually observed.
/// </summary>
public class AutomationIdTests
{
    private static readonly string[] TranscriptionIds =
    [
        "transcription-audio-path",
        "transcription-browse-audio",
        "transcription-model",
        "transcription-model-hint",
        "transcription-get-models",
        "transcription-capability-status",
        "transcription-language",
        "transcription-threads",
        "transcription-kv-type",
        "transcription-context-size",
        "transcription-whisper-prompt",
        "transcription-whisper-temperature",
        "transcription-task",
        "transcription-target-language",
        "transcription-window-seconds",
        "transcription-spec-k-drafts",
        "transcription-keep-special-tags",
        "transcription-run",
        "transcription-export",
        "transcription-progress",
        "transcription-status",
        "transcription-results",
    ];

    private static readonly string[] StreamingIds =
    [
        "streaming-model",
        "streaming-model-hint",
        "streaming-get-models",
        "streaming-capability-status",
        "streaming-commit-policy",
        "streaming-stable-prefix-agreement",
        "streaming-family-extensions",
        "streaming-moonshine-interval",
        "streaming-parakeet-att-right",
        "streaming-parakeet-left",
        "streaming-parakeet-chunk",
        "streaming-parakeet-right",
        "streaming-sortformer-preset",
        "streaming-voxtral-delay-tokens",
        "streaming-voxtral-interval",
        "streaming-start",
        "streaming-stop",
        "streaming-status",
        "streaming-committed-text",
        "streaming-tentative-text",
    ];

    private static readonly string[] BatchIds =
    [
        "batch-model",
        "batch-model-hint",
        "batch-get-models",
        "batch-language",
        "batch-add-files",
        "batch-clear",
        "batch-run",
        "batch-export",
        "batch-progress",
        "batch-status",
        "batch-results",
    ];

    private static readonly string[] SettingsIds =
    [
        "settings-backend",
        "settings-device",
        "settings-cache-directory",
        "settings-backend-version",
        "settings-backend-error",
        "settings-devices",
    ];

    private static readonly string[] ModelManagerStaticIds =
    [
        "models-filter",
        "models-downloaded-only",
        "models-commercial-only",
        "models-size-filter",
        "models-license-filter",
        "models-language-filter",
        "models-clear-filters",
        "models-sort-name",
        "models-sort-size",
        "models-sort-speed",
        "models-benchmark-audio",
        "models-benchmark-seconds",
        "models-benchmark-backend",
        "models-benchmark-all",
        "models-grid",
        "models-detail",
        "models-headline-accuracy",
        "models-refresh",
        "models-delete-all",
        "models-check-diarization",
    ];

    [AvaloniaFact]
    public void TranscriptionViewExposesItsIds()
        => AssertIds(new TranscriptionView(), TranscriptionIds);

    [AvaloniaFact]
    public void StreamingViewExposesItsIds()
        => AssertIds(new StreamingView(), StreamingIds);

    [AvaloniaFact]
    public void BatchViewExposesItsIds()
        => AssertIds(new BatchView(), BatchIds);

    [AvaloniaFact]
    public void SettingsViewExposesItsIds()
        => AssertIds(new SettingsView(), SettingsIds);

    [AvaloniaFact]
    public void ModelManagerViewExposesItsStaticIds()
        => AssertIds(new ModelManagerView(), ModelManagerStaticIds);

    /// <summary>
    /// The per-row buttons get their id from the row's alias through
    /// RowIdConverter, not from XAML. Realizing the rows needs a DataContext and
    /// a shown window, so this is the one test that exercises the binding and
    /// not just the converter.
    /// </summary>
    [AvaloniaFact]
    public void ModelManagerRowsExposeOneIdPerAliasAndAction()
    {
        var view = new ModelManagerView { DataContext = new ModelManagerViewModel() };
        var window = new Window { Content = view, Width = 1100, Height = 720 };
        window.Show();

        var ids = window.GetVisualDescendants()
            .OfType<Control>()
            .Select(AutomationProperties.GetAutomationId)
            .Where(id => !string.IsNullOrEmpty(id))
            .Cast<string>()
            .ToList();

        var downloadIds = ids.Where(id => id.StartsWith("models-download-", StringComparison.Ordinal)).ToList();
        var measureIds = ids.Where(id => id.StartsWith("models-measure-", StringComparison.Ordinal)).ToList();

        Assert.NotEmpty(downloadIds);
        Assert.NotEmpty(measureIds);
        Assert.Equal(downloadIds.Count, downloadIds.Distinct().Count());
        Assert.Equal(measureIds.Count, measureIds.Distinct().Count());
    }

    private static void AssertIds(Control view, IReadOnlyCollection<string> expected)
    {
        var actual = view.GetLogicalDescendants()
            .OfType<Control>()
            .Select(AutomationProperties.GetAutomationId)
            .Where(id => !string.IsNullOrEmpty(id))
            .Cast<string>()
            .ToHashSet();

        string[] missing = expected.Where(id => !actual.Contains(id)).ToArray();
        string[] extra = actual.Where(id => !expected.Contains(id)).ToArray();

        Assert.True(missing.Length == 0 && extra.Length == 0,
            $"missing=[{string.Join(", ", missing)}] extra=[{string.Join(", ", extra)}]");
    }
}
