using TranscribeCppSharp.Models;

namespace TranscribeCppSharp.UI.Tests;

/// <summary>
/// Points <see cref="ModelStore.CacheRootOverride"/> at a temporary directory for
/// the duration of a test, then puts the previous value back.
/// </summary>
/// <remarks>
/// <para>
/// The override is a single process-wide value, so every class that writes it
/// joins the <c>UiGlobalState</c> collection and runs one at a time. That removes
/// the race; restoring is still what keeps the suite order-independent, because a
/// root left behind — this one, or another class's with planted model files in
/// it — is read by the next class that does not set its own.
/// </para>
/// <para>
/// The directory is not created here: the helpers under test create it when they
/// write, and a test that asserts "nothing is cached" needs it to stay absent.
/// </para>
/// </remarks>
internal sealed class CacheRootScope : IDisposable
{
    private readonly string? _previous = ModelStore.CacheRootOverride;

    /// <param name="name">
    /// A directory name under the temp path, shared by the tests that want the
    /// same starting point; null gives the test a unique one.
    /// </param>
    public CacheRootScope(string? name = null)
    {
        Root = Path.Combine(
            Path.GetTempPath(),
            name ?? "tcsharp-ui-tests-" + Guid.NewGuid().ToString("N"));
        ModelStore.CacheRootOverride = Root;
    }

    /// <summary>The directory the cache now points at.</summary>
    public string Root { get; }

    /// <inheritdoc/>
    public void Dispose() => ModelStore.CacheRootOverride = _previous;
}
