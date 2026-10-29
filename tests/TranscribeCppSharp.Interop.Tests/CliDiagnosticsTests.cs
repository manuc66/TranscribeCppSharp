using System.Text.Json;
using TranscribeCppSharp.Cli;
using TranscribeCppSharp.Models;
using Xunit;

namespace TranscribeCppSharp.Interop.Tests;

/// <summary>
/// The cache commands and <c>--doctor</c>: what a user runs when something is
/// wrong, so they must work without a model, a network or a GPU, and must not
/// change anything they were not asked to change.
/// </summary>
/// <remarks>
/// In-process, against a temporary cache root: the assertions depend on files
/// this test creates, not on what the machine happens to have downloaded.
/// </remarks>
[Collection(nameof(ModelStoreDownloadTests))]
public class CliDiagnosticsTests
{
    private static (int Code, string Out, string Error) Run(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int code = TranscribeCommand.Run(args, stdout, stderr);
        return (code, stdout.ToString(), stderr.ToString());
    }

    /// <summary>A temp cache root plus the directory restored afterwards.</summary>
    private static Restore UseCache(string root)
    {
        string? previous = ModelStore.CacheRootOverride;
        ModelStore.CacheRootOverride = root;
        return new Restore(() => ModelStore.CacheRootOverride = previous);
    }

    /// <summary>Writes a file at the path an alias would be cached at.</summary>
    private static void FakeCachedModel(string alias, string content)
    {
        ModelDescriptor descriptor = ModelStore.Find(alias)
            ?? throw new InvalidOperationException($"{alias} is not in the manifest");
        string path = ModelStore.CachedPath(descriptor);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    [Fact]
    public void CacheSizeCountsOnlyTheModelsTheManifestKnows()
    {
        using var temp = new TempWorkspace();
        using (UseCache(temp.Path))
        {
            FakeCachedModel("whisper-tiny", new string('x', 2048));

            (int code, string output, string error) = Run("--cache-size");

            Assert.Equal(0, code);
            Assert.Empty(error);
            Assert.Contains("1 model(s) cached", output, StringComparison.Ordinal);
            Assert.Contains(temp.Path, output, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void RemoveModelDeletesTheFileAndSaysWhatItFreed()
    {
        using var temp = new TempWorkspace();
        using (UseCache(temp.Path))
        {
            FakeCachedModel("whisper-tiny", new string('x', 4096));
            ModelDescriptor descriptor = ModelStore.Find("whisper-tiny")!;
            string path = ModelStore.CachedPath(descriptor);

            (int code, string output, string error) = Run("--remove-model", "whisper-tiny");

            Assert.Equal(0, code);
            Assert.Empty(error);
            Assert.False(File.Exists(path));
            Assert.Contains("Removed whisper-tiny", output, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void RemoveModelIsIdempotentAndRefusesAnUnknownAlias()
    {
        using var temp = new TempWorkspace();
        using (UseCache(temp.Path))
        {
            (int notCached, string output, _) = Run("--remove-model", "whisper-tiny");
            Assert.Equal(0, notCached);
            Assert.Contains("not cached", output, StringComparison.Ordinal);

            (int unknown, _, string error) = Run("--remove-model", "not-a-model");
            Assert.Equal(1, unknown);
            Assert.Contains("--list-models", error, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void DoctorReportsTheNativeLibraryAndExitsZero()
    {
        (int code, string output, _) = Run("--doctor");

        Assert.Equal(0, code);
        Assert.Contains("native", output, StringComparison.Ordinal);
        Assert.Contains("transcribe.cpp", output, StringComparison.Ordinal);
        Assert.Contains("abi", output, StringComparison.Ordinal);
    }

    [Fact]
    public void DoctorJsonIsOneParsableObject()
    {
        (int code, string output, _) = Run("--doctor", "--json");

        Assert.Equal(0, code);

        // Parsed whole: a native log line leaking onto stdout would break this,
        // which is the point of routing native logging to stderr in --doctor.
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement root = document.RootElement;

        Assert.True(root.GetProperty("ok").GetBoolean());
        JsonElement checks = root.GetProperty("checks");
        Assert.True(checks.GetArrayLength() >= 5);
        Assert.Contains(
            checks.EnumerateArray(),
            check => check.GetProperty("name").GetString() == "native");
        Assert.Contains(
            checks.EnumerateArray(),
            check => check.GetProperty("name").GetString() == "cache");
    }

    [Fact]
    public void DoctorVerifyCacheFailsOnAFileThatDoesNotMatchItsHash()
    {
        using var temp = new TempWorkspace();
        using (UseCache(temp.Path))
        {
            FakeCachedModel("whisper-tiny", "these are not the bytes the manifest pins");

            (int code, string output, string error) = Run("--doctor", "--json", "--verify-cache");

            Assert.Equal(1, code);
            Assert.Empty(error);

            using JsonDocument document = JsonDocument.Parse(output);
            Assert.False(document.RootElement.GetProperty("ok").GetBoolean());

            JsonElement integrity = document.RootElement.GetProperty("checks")
                .EnumerateArray()
                .Single(c => c.GetProperty("name").GetString() == "cache-integrity");

            Assert.Equal("fail", integrity.GetProperty("status").GetString());
            Assert.Contains("whisper-tiny", integrity.GetProperty("detail").GetString()!, StringComparison.Ordinal);
        }
    }

    private sealed class Restore(Action action) : IDisposable
    {
        public void Dispose() => action();
    }
}
