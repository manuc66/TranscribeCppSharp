#nullable enable

using TranscribeCppSharp.Models;
using Xunit;

namespace TranscribeCppSharp.Interop.Tests;

/// <summary>
/// Tests for the catalogue and cache half of ModelStore: the part a GUI uses
/// to list models, show what is already on disk, and delete a download.
/// <see cref="ModelStoreTests"/> covers resolution and download instead.
///
/// The cache root is redirected at the machine level and restored afterwards,
/// because these tests create and delete real files: the assertions are about
/// the filesystem, and mocking File would only test the mock.
/// </summary>
[Collection("ModelStoreCache")]
public class ModelCatalogTests : IDisposable
{
    private readonly TempWorkspace _temp = new();
    private readonly string? _previousCacheRoot = ModelStore.CacheRootOverride;

    public ModelCatalogTests() => ModelStore.CacheRootOverride = _temp.Combine("cache");

    public void Dispose()
    {
        ModelStore.CacheRootOverride = _previousCacheRoot;
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Catalog_HoldsEveryAliasTheListCommandPrints()
    {
        var catalog = ModelStore.Catalog;
        var stdout = new StringWriter();
        ModelStore.List(stdout);
        string listing = stdout.ToString();

        Assert.NotEmpty(catalog);
        Assert.All(catalog, m => Assert.Contains(m.Alias, listing, StringComparison.Ordinal));
    }

    [Fact]
    public void Catalog_EntriesAreOrderedAndUnique()
    {
        var aliases = ModelStore.Catalog.Select(m => m.Alias).ToList();

        Assert.Equal(aliases.OrderBy(a => a, StringComparer.Ordinal), aliases);
        Assert.Equal(aliases.Distinct(StringComparer.Ordinal).Count(), aliases.Count);
    }

    [Fact]
    public void Catalog_AgreesWithFindOnEveryEntry()
    {
        foreach (ModelDescriptor entry in ModelStore.Catalog)
        {
            ModelDescriptor? found = ModelStore.Find(entry.Alias);

            Assert.NotNull(found);
            Assert.Equal(entry, found);
        }
    }

    [Fact]
    public void Find_AnUnknownAlias_IsNull()
    {
        Assert.Null(ModelStore.Find("not-a-real-alias"));
    }

    [Fact]
    public void EveryEntryHasTheFieldsTheResolverNeeds()
    {
        // A missing repo or revision would turn a download into a 404 or an
        // unpinned fetch, and a missing sha256 would skip verification.
        foreach (ModelDescriptor m in ModelStore.Catalog)
        {
            Assert.False(string.IsNullOrWhiteSpace(m.Alias));
            Assert.Contains('/', m.Repo);
            Assert.False(string.IsNullOrWhiteSpace(m.Revision));
            Assert.EndsWith(".gguf", m.File, StringComparison.OrdinalIgnoreCase);
            Assert.Matches("^[0-9a-f]{64}$", m.Sha256);
            Assert.True(m.Size > 0, $"{m.Alias} has no size in the manifest");
            Assert.False(string.IsNullOrWhiteSpace(m.License));
        }
    }

    [Fact]
    public void DownloadUrl_IsPinnedToTheManifestRevision()
    {
        ModelDescriptor whisperTiny = NotNull(ModelStore.Find("whisper-tiny"));

        Assert.Equal(
            $"https://huggingface.co/{whisperTiny.Repo}/resolve/{whisperTiny.Revision}/{whisperTiny.File}",
            whisperTiny.DownloadUrl);
        Assert.Equal($"https://huggingface.co/{whisperTiny.Repo}", whisperTiny.RepoUrl);
    }

    [Fact]
    public void CacheRoot_IsRedirectableAndUsedByTheCachedPath()
    {
        ModelDescriptor model = NotNull(ModelStore.Find("whisper-tiny"));

        Assert.Equal(_temp.Combine("cache"), ModelStore.CacheRoot());
        Assert.StartsWith(ModelStore.CacheRoot(), ModelStore.CachedPath(model), StringComparison.Ordinal);
        Assert.Equal(model.File, Path.GetFileName(ModelStore.CachedPath(model)));
    }

    [Fact]
    public void CachedPath_SurvivesAFileNameContainingASlash()
    {
        // An HF path may be "subdir/model.gguf"; the cache flattens the slash
        // so the download and the lookup cannot disagree about the file name.
        ModelDescriptor model = ModelStore.Find("whisper-tiny")!
            with { File = "q4/model-Q4_K_M.gguf" };

        string path = ModelStore.CachedPath(model);

        Assert.Equal("q4_model-Q4_K_M.gguf", Path.GetFileName(path));
    }

    [Fact]
    public void IsCachedAndCachedSize_ReportAMissingDownload()
    {
        ModelDescriptor model = NotNull(ModelStore.Find("whisper-tiny"));

        Assert.False(ModelStore.IsCached(model));
        Assert.Equal(0, ModelStore.CachedSize(model));
    }

    [Fact]
    public void IsCachedAndCachedSize_ReportAPresentDownload()
    {
        ModelDescriptor model = NotNull(ModelStore.Find("whisper-tiny"));
        WriteCached(model, [0x47, 0x47, 0x55, 0x46]);

        Assert.True(ModelStore.IsCached(model));
        Assert.Equal(4, ModelStore.CachedSize(model));
    }

    [Fact]
    public void Delete_RemovesTheCachedFileAndReportsIt()
    {
        ModelDescriptor model = NotNull(ModelStore.Find("whisper-tiny"));
        WriteCached(model, [0x47, 0x47, 0x55, 0x46]);

        Assert.True(ModelStore.Delete(model));
        Assert.False(ModelStore.IsCached(model));
        Assert.False(File.Exists(ModelStore.CachedPath(model)));
    }

    [Fact]
    public void Delete_AnAbsentModel_ReportsFalseInsteadOfThrowing()
    {
        // A GUI delete button should disable itself when there is nothing to
        // delete; throwing for the common no-op case would be hostile.
        Assert.False(ModelStore.Delete(NotNull(ModelStore.Find("whisper-tiny"))));
    }

    [Fact]
    public void Delete_LeavesASiblingModelThatSharesTheRepository()
    {
        // Two quantizations of one repo/revision would share a directory, so
        // deleting one must not take the other's weights with it. No such pair
        // exists in the manifest today (every alias has its own repository), so
        // the case is built here rather than found: the guarantee Delete makes
        // is about the cache layout, not about today's catalogue.
        ModelDescriptor first = NotNull(ModelStore.Find("whisper-tiny"));
        ModelDescriptor sibling = first with { Alias = "whisper-tiny-q4", File = "whisper-tiny-Q4_K_M.gguf" };
        Assert.Equal(Path.GetDirectoryName(ModelStore.CachedPath(first)), Path.GetDirectoryName(ModelStore.CachedPath(sibling)));

        WriteCached(first, [0x47, 0x47, 0x55, 0x46]);
        WriteCached(sibling, [0x47, 0x47, 0x55, 0x46]);

        Assert.True(ModelStore.Delete(first));

        Assert.False(ModelStore.IsCached(first));
        Assert.True(ModelStore.IsCached(sibling));
    }

    [Fact]
    public void Delete_PrunesTheRevisionDirectoryItEmptied()
    {
        ModelDescriptor model = NotNull(ModelStore.Find("whisper-tiny"));
        WriteCached(model, [0x47, 0x47, 0x55, 0x46]);
        string directory = Path.GetDirectoryName(ModelStore.CachedPath(model))!;
        Assert.True(Directory.Exists(directory));

        Assert.True(ModelStore.Delete(model));

        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public void NonCommercialLicensesAreFlaggedFromTheManifestText()
    {
        // The manifest carries SPDX ids, so this is a text test on the id and
        // not a licence lookup: it can only be as good as the manifest is.
        ModelDescriptor[] flagged = ModelStore.Catalog.Where(m => m.IsNonCommercialLicense).ToArray();
        Assert.NotEmpty(flagged);

        foreach (ModelDescriptor m in flagged)
        {
            Assert.True(
                m.License.Contains("-nc", StringComparison.OrdinalIgnoreCase)
                || m.License.Contains("noncommercial", StringComparison.OrdinalIgnoreCase)
                || m.License.Contains("non-commercial", StringComparison.OrdinalIgnoreCase),
                $"'{m.License}' was flagged non-commercial but matches none of the markers.");
        }
    }

    private static T NotNull<T>(T? value)
        where T : class
        => value ?? throw new InvalidOperationException("Expected the manifest to contain the alias.");

    private static void WriteCached(ModelDescriptor descriptor, byte[] content)
    {
        string path = ModelStore.CachedPath(descriptor);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
    }
}

/// <summary>
/// ModelStore.CacheRootOverride is process-wide, so the tests that redirect it
/// must not run at the same time as each other.
/// </summary>
[CollectionDefinition("ModelStoreCache", DisableParallelization = true)]
public sealed class ModelStoreCache
{
}