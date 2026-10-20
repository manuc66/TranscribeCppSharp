using TranscribeCppSharp.UI.ViewModels;
using Xunit;

namespace TranscribeCppSharp.UI.Tests;

/// <summary>
/// The About section reports the app's version and names the native library's
/// authors. The version is read from the assembly, so this checks that it is a
/// real version rather than a placeholder.
/// </summary>
public class SettingsAboutTests
{
    [Fact]
    public void TheAppVersionIsTheAssemblyVersionNotAPlaceholder()
    {
        var viewModel = new SettingsViewModel();

        Assert.NotEqual("unknown", viewModel.AppVersion);
        Assert.True(Version.TryParse(viewModel.AppVersion, out Version? version), viewModel.AppVersion);
        Assert.NotNull(version);
    }
}
