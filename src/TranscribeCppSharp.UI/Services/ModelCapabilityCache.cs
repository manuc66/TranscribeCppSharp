using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace TranscribeCppSharp.UI.Services;

/// <summary>
/// What each cached model reports about itself, loaded at most once per alias
/// however many callers ask for it at the same time.
/// </summary>
/// <remarks>
/// The transcription tab and the streaming tab both read the capabilities of the
/// default model when they are built. With a cache of its own in each view model,
/// the two loaded the weights on separate threads at once, so startup held two
/// copies of the same file in memory — the probe's own docs say the caller pays
/// the load and not the memory, and the second copy is exactly that memory.
/// Registered as a singleton, this is what makes the second caller wait for the
/// first instead of loading again. It caches too, so re-selecting an alias costs
/// nothing.
/// </remarks>
public sealed class ModelCapabilityCache
{
    /// <summary>
    /// The load in progress, or the one that already finished, per alias. A
    /// <see cref="Lazy{T}"/> so that two threads racing into
    /// <see cref="ConcurrentDictionary{TKey,TValue}.GetOrAdd(TKey,Func{TKey,TValue})"/>
    /// still run the probe once: <c>GetOrAdd</c> may invoke its factory twice, the
    /// <see cref="Lazy{T}"/> may not.
    /// </summary>
    private readonly ConcurrentDictionary<string, Lazy<Task<ModelCapabilitySnapshot>>> _loads = new(StringComparer.Ordinal);

    /// <summary>
    /// How the weights are read. The real probe in production; a seam for tests,
    /// so that the sharing can be shown without loading a model.
    /// </summary>
    public Func<string, ModelCapabilitySnapshot> Probe { get; set; } = ModelCapabilityProbe.Probe;

    /// <summary>
    /// A snapshot already in hand for <paramref name="alias"/>, without loading.
    /// </summary>
    /// <param name="alias">The catalogue alias to look up.</param>
    /// <param name="snapshot">The known snapshot, when there is one.</param>
    /// <returns>true when a load has already completed for the alias.</returns>
    public bool TryGet(string alias, [NotNullWhen(true)] out ModelCapabilitySnapshot? snapshot)
    {
        if (_loads.TryGetValue(alias, out Lazy<Task<ModelCapabilitySnapshot>>? load)
            && load.IsValueCreated
            && load.Value.IsCompletedSuccessfully)
        {
            snapshot = load.Value.Result;
            return true;
        }

        snapshot = null;
        return false;
    }

    /// <summary>
    /// Loads <paramref name="modelPath"/> unless an answer is already in hand.
    /// Concurrent calls for the same alias share the one load.
    /// </summary>
    /// <param name="alias">The catalogue alias, the key the answer is kept under.</param>
    /// <param name="modelPath">The weights file to load.</param>
    /// <returns>What the model reported about itself.</returns>
    public Task<ModelCapabilitySnapshot> GetAsync(string alias, string modelPath)
        => _loads.GetOrAdd(
            alias,
            _ => new Lazy<Task<ModelCapabilitySnapshot>>(
                () => Load(alias, modelPath),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    private async Task<ModelCapabilitySnapshot> Load(string alias, string modelPath)
    {
        try
        {
            // Off the UI thread: the model is loaded and disposed by the probe,
            // which is the part that costs time and address space.
            return await Task.Run(() => Probe(modelPath)).ConfigureAwait(false);
        }
        catch
        {
            // A failure is not an answer: drop it so the next request tries again
            // rather than handing every later caller the same exception.
            _loads.TryRemove(alias, out _);
            throw;
        }
    }
}
