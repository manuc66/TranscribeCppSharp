using TranscribeCppSharp.UI.Services;
using Xunit;

namespace TranscribeCppSharp.UI.Tests;

/// <summary>
/// One collection for the view-model tests that redirect the app's global state.
/// </summary>
/// <remarks>
/// <see cref="TranscribeCppSharp.Models.ModelStore.CacheRootOverride"/> and
/// <see cref="UiSettings.FilePathOverride"/> are single static values, and a class
/// that sets one expects it to still be in place a few lines later. Left to
/// xUnit's default parallelism, another class can move the same value in between —
/// which is what made a test read a settings file a parallel test had written, and
/// fail on an alias it never set. Classes that touch either value join this
/// collection and run one at a time with respect to each other;
/// <see cref="CacheRootScope"/> is what puts the cache root back when a test ends.
/// </remarks>
[CollectionDefinition("UiGlobalState", DisableParallelization = true)]
public static class UiGlobalStateTestGroup
{
}
