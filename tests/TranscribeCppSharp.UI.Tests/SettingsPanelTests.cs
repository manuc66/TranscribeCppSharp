using TranscribeCppSharp.Interop;
using TranscribeCppSharp.UI.ViewModels;
using Xunit;

namespace TranscribeCppSharp.UI.Tests;

/// <summary>
/// The Settings tab's compute panel is optional: Auto is the default and resolves
/// a backend by itself. The tab has to say that, because a list of seven backends
/// otherwise reads as a choice that must be made.
/// </summary>
public class SettingsPanelTests
{
    [Fact]
    public void AutoIsSelectedAndThePanelSaysItIsOptional()
    {
        var viewModel = new SettingsViewModel();

        Assert.Equal(BackendRequest.BackendAuto, viewModel.SelectedBackend);
        Assert.Null(viewModel.SelectedDevice);
        Assert.Contains("Auto", viewModel.BackendAvailabilityHint);
        Assert.Contains("default", viewModel.BackendAvailabilityHint, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("force", viewModel.BackendAvailabilityHint, StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrWhiteSpace(viewModel.BackendTargetHint));
    }
}
