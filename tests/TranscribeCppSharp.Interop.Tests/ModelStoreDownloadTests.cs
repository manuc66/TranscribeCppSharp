using System.Net;
using System.Security.Cryptography;
using System.Text;
using TranscribeCppSharp.Cli;
using TranscribeCppSharp.Models;
using Xunit;
using Xunit.Abstractions;

// The test namespace is TranscribeCppSharp.Interop.Tests, so the unqualified name
// Task binds to the generated enum transcribe_task (TranscribeCppSharp.Interop.Task)
// rather than to System.Threading.Tasks.Task.
using ThreadingTask = System.Threading.Tasks.Task;

namespace TranscribeCppSharp.Interop.Tests;

/// <summary>
/// Tests for the download half of the model store: the HuggingFace lookup, the
/// sha256 verification, the cache and the failures. The README promises the
/// weights are "verified by sha256 and cached", so that path is asserted here
/// with a stubbed HTTP handler — no network, no 600 MB download.
///
/// The two seams used (HttpClientFactory, CacheRootOverride) exist only for
/// these tests; nothing in the command sets them.
/// </summary>
[Collection(nameof(ModelStoreDownloadTests))]
public class ModelStoreDownloadTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly TempWorkspace _temp = new();
    private readonly Func<HttpClient> _originalFactory;
    private readonly string? _originalCacheRoot;
    private readonly StubHandler _handler = new();

    public ModelStoreDownloadTests(ITestOutputHelper output)
    {
        _output = output;
        _originalFactory = ModelStore.HttpClientFactory;
        _originalCacheRoot = ModelStore.CacheRootOverride;

        ModelStore.HttpClientFactory = () => new HttpClient(_handler) { Timeout = TimeSpan.FromSeconds(5) };
        ModelStore.CacheRootOverride = Path.Combine(_temp.Path, "cache");
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        ModelStore.HttpClientFactory = _originalFactory;
        ModelStore.CacheRootOverride = _originalCacheRoot;
        _temp.Dispose();
    }

    // ---- the HuggingFace endpoints, stubbed -----------------------------------

    /// <summary>Serves a repo tree listing one GGUF with the given LFS sha256.</summary>
    private void ServeTree(string repo, string revision, string file, string? sha256, long size)
    {
        string lfs = sha256 is null
            ? string.Empty
            : $",\"lfs\":{{\"oid\":\"{sha256}\",\"size\":{size}}}";

        _handler.On($"api/models/{repo}/tree/{revision}", $"[{{\"path\":\"{file}\",\"type\":\"file\"{lfs}}}]");
    }

    private void ServeFile(string repo, string revision, string file, byte[] content)
        => _handler.On($"huggingface.co/{repo}/resolve/{revision}/{file}", content);

    private static byte[] Payload(string seed) => Encoding.UTF8.GetBytes($"GGUF-ish payload for {seed}");

    private static string Sha256Of(byte[] content) => Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    // ---- a curated alias ----------------------------------------------------

    [Fact]
    public void ACuratedAliasIsDownloadedVerifiedAndCached()
    {
        (string output, _) = Capture(o => ModelStore.Info("whisper-tiny", o, TextWriter.Null));
        string revision = output.Split('\n').First(l => l.Contains("revision")).Split(':')[1].Trim();
        const string repo = "handy-computer/whisper-tiny-gguf";
        const string file = "whisper-tiny-Q5_K_M.gguf";

        // The manifest pins both the revision and the sha256, so this path
        // downloads that exact file and no metadata lookup is needed. Wrong
        // bytes are rejected here; AChecksumMismatchIsFatalAndLeavesNothingBehind
        // asserts that case in detail.
        ServeTree(repo, revision, file, new string('1', 64), 5);
        ServeFile(repo, revision, file, Payload("alias"));

        var error = new StringWriter();
        Assert.Throws<InvalidDataException>(() => ModelStore.Resolve("whisper-tiny", null, error));

        // The pinned revision is fetched, not "main": that is what makes the
        // weights identical from one machine to the next.
        Assert.Equal(
            $"https://huggingface.co/{repo}/resolve/{revision}/{file}",
            Assert.Single(_handler.Requests));
    }

    [Fact]
    public void AChecksumMismatchIsFatalAndLeavesNothingBehind()
    {
        const string repo = "handy-computer/whisper-tiny-gguf";
        const string revision = "2678cc66038359b97c8e6fd6454c56fc9006d571";
        string file = "whisper-tiny-Q5_K_M.gguf";

        // The manifest says one sha256; the download will not match it.
        ServeTree(repo, revision, file, sha256: new string('1', 64), size: 32);
        ServeFile(repo, revision, file, Payload("wrong"));

        var error = new StringWriter();
        InvalidDataException ex = Assert.Throws<InvalidDataException>(
            () => ModelStore.Resolve("whisper-tiny", null, error));

        Assert.Contains("Checksum mismatch", ex.Message);
        Assert.Contains("Aborting", ex.Message);
        _output.WriteLine(ex.Message);

        // No half-written file left behind, and the cache is empty so the next
        // run tries again instead of loading a corrupt model.
        string cacheRoot = ModelStore.CacheRootOverride!;
        if (Directory.Exists(cacheRoot))
        {
            Assert.Empty(Directory.EnumerateFiles(cacheRoot, "*", SearchOption.AllDirectories));
        }
    }

    [Fact]
    public void AQuantizationOverrideIsLookedUpBySuffix()
    {
        (string output, _) = Capture(o => ModelStore.Info("whisper-tiny", o, TextWriter.Null));
        string revision = output.Split('\n').First(l => l.Contains("revision")).Split(':')[1].Trim();
        const string repo = "handy-computer/whisper-tiny-gguf";

        // Only the Q8_0 file is in the tree, and its hash matches what we serve.
        byte[] content = Payload("q8_0");
        ServeTree(repo, revision, "whisper-tiny-Q8_0.gguf", Sha256Of(content), content.Length);
        ServeFile(repo, revision, "whisper-tiny-Q8_0.gguf", content);

        string resolved = ModelStore.Resolve("whisper-tiny", "Q8_0", TextWriter.Null);

        Assert.True(File.Exists(resolved));
        Assert.Equal("whisper-tiny-Q8_0.gguf", Path.GetFileName(resolved));
        Assert.Equal(content, File.ReadAllBytes(resolved));

        // A quantization the manifest does not carry has no sha256 of its own, so
        // this path does ask HuggingFace which file and which hash.
        Assert.Contains(_handler.Requests, r => r.Contains($"/api/models/{repo}/tree/{revision}"));
    }

    [Fact]
    public void AQuantizationThatTheRepoDoesNotHaveIsReported()
    {
        (string output, _) = Capture(o => ModelStore.Info("whisper-tiny", o, TextWriter.Null));
        string revision = output.Split('\n').First(l => l.Contains("revision")).Split(':')[1].Trim();
        const string repo = "handy-computer/whisper-tiny-gguf";
        ServeTree(repo, revision, "whisper-tiny-Q5_K_M.gguf", new string('0', 64), 1);

        FileNotFoundException ex = Assert.Throws<FileNotFoundException>(
            () => ModelStore.Resolve("whisper-tiny", "Q2_K", TextWriter.Null));

        Assert.Contains("No 'Q2_K' quantization found", ex.Message);
    }

    // ---- a generic <owner>/<repo>/<file>[@<revision>] spec ------------------

    [Fact]
    public void AGenericSpecIsDownloadedVerifiedAndCached()
    {
        const string repo = "someone/custom-model";
        const string revision = "abc123";
        const string file = "model-Q4_K_M.gguf";
        byte[] content = Payload("spec");
        ServeTree(repo, revision, file, Sha256Of(content), content.Length);
        ServeFile(repo, revision, file, content);

        var error = new StringWriter();
        string resolved = ModelStore.Resolve($"{repo}/{file}@{revision}", null, error);

        Assert.Equal(content, File.ReadAllBytes(resolved));
        // The license is unknown for a spec, and says so instead of inventing one.
        Assert.Contains("see the model card", error.ToString());
        Assert.Contains($"https://huggingface.co/{repo}", error.ToString());
    }

    [Theory]
    [InlineData("../../../../tmp/escaped")]
    [InlineData("/tmp/escaped")]
    [InlineData("..")]
    [InlineData("main/../..")]
    public void ASpecRevisionThatIsNotOneSegmentIsRefusedBeforeAnyRequest(string revision)
    {
        var error = new StringWriter();

        ArgumentException ex = Assert.Throws<ArgumentException>(
            () => ModelStore.Resolve($"someone/custom-model/model.gguf@{revision}", null, error));

        Assert.Contains("revision", ex.Message, StringComparison.Ordinal);
        // Refused while parsing the spec, before any lookup ...
        Assert.Empty(_handler.Requests);
        // ... so nothing was created, least of all outside the cache.
        Assert.False(Directory.Exists(ModelStore.CacheRootOverride!));
    }

    [Fact]
    public void AFileNameWithASeparatorIsFlattenedInsideTheRevisionDirectory()
    {
        var descriptor = new ModelDescriptor(
            "x",
            "someone/custom-model",
            "rev1",
            "MIT",
            string.Empty,
            "Q5",
            "..\\..\\escaped.gguf",
            new string('0', 64),
            0);

        string path = ModelStore.CachedPath(descriptor);
        string cacheRoot = Path.GetFullPath(ModelStore.CacheRootOverride!) + Path.DirectorySeparatorChar;

        Assert.StartsWith(cacheRoot, Path.GetFullPath(path), StringComparison.Ordinal);
        Assert.Equal(".._.._escaped.gguf", Path.GetFileName(path));
    }

    [Fact]
    public void ARevisionThatWalksUpIsRefusedWhenLocatingACachedFile()
    {
        var descriptor = new ModelDescriptor(
            "x",
            "someone/custom-model",
            "..",
            "MIT",
            string.Empty,
            "Q5",
            "model.gguf",
            new string('0', 64),
            0);

        Assert.Throws<InvalidOperationException>(() => ModelStore.CachedPath(descriptor));
    }

    [Fact]
    public void AServerThatSendsMoreThanItAnnouncedIsStopped()
    {
        // The sha256 is only checked once the whole file is on disk, so without a
        // byte cap a server that keeps sending fills the disk before the mismatch
        // is noticed.
        const string repo = "someone/custom-model";
        const string revision = "abc123";
        const string file = "model.gguf";
        byte[] content = Payload("more than announced");
        ServeTree(repo, revision, file, Sha256Of(content), size: 4);
        ServeFile(repo, revision, file, content);

        var error = new StringWriter();
        InvalidDataException ex = Assert.Throws<InvalidDataException>(
            () => ModelStore.Resolve($"{repo}/{file}@{revision}", null, error));

        Assert.Contains("announced as 4 bytes", ex.Message, StringComparison.Ordinal);
        string cacheRoot = ModelStore.CacheRootOverride!;
        if (Directory.Exists(cacheRoot))
        {
            Assert.Empty(Directory.EnumerateFiles(cacheRoot, "*", SearchOption.AllDirectories));
        }
    }

    [Fact]
    public void ASymlinkedRevisionDirectoryThatLeavesTheCacheIsRefused()
    {
        // Contain() compares strings; a link is not a string difference. Creating
        // one needs no privilege on Unix, and nothing on Windows without developer
        // mode, so that case is skipped rather than pretended to pass.
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string cacheRoot = ModelStore.CacheRootOverride!;
        string outside = Path.Combine(_temp.Path, "outside");
        Directory.CreateDirectory(Path.Combine(cacheRoot, "someone_custom-model"));
        Directory.CreateDirectory(outside);
        Directory.CreateSymbolicLink(
            Path.Combine(cacheRoot, "someone_custom-model", "abc123"),
            outside);

        var descriptor = new ModelDescriptor(
            "x", "someone/custom-model", "abc123", "MIT", string.Empty, "Q5",
            "model.gguf", new string('0', 64), 0);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => ModelStore.CachedPath(descriptor));

        Assert.Contains("resolves outside the model cache", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ASymlinkedCacheRootIsStillInsideItself()
    {
        // Keeping the weights on another disk is a legitimate setup: the root is a
        // link, and everything under it resolves under its target.
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string real = Path.Combine(_temp.Path, "real-cache");
        Directory.CreateDirectory(Path.Combine(real, "someone_custom-model", "abc123"));
        string linked = Path.Combine(_temp.Path, "linked-cache");
        Directory.CreateSymbolicLink(linked, real);
        string? previous = ModelStore.CacheRootOverride;
        ModelStore.CacheRootOverride = linked;
        try
        {
            var descriptor = new ModelDescriptor(
                "x", "someone/custom-model", "abc123", "MIT", string.Empty, "Q5",
                "model.gguf", new string('0', 64), 0);

            string path = ModelStore.CachedPath(descriptor);

            Assert.Equal(
                Path.Combine(linked, "someone_custom-model", "abc123", "model.gguf"),
                path);
        }
        finally
        {
            ModelStore.CacheRootOverride = previous;
        }
    }

    [Fact]
    public void ASecondResolveReusesTheCachedWeights()
    {
        const string repo = "someone/custom-model";
        const string revision = "abc123";
        const string file = "model-Q4_K_M.gguf";
        byte[] content = Payload("cached");
        ServeTree(repo, revision, file, Sha256Of(content), content.Length);
        ServeFile(repo, revision, file, content);

        string first = ModelStore.Resolve($"{repo}/{file}@{revision}", null, TextWriter.Null);
        int downloadsAfterFirst = _handler.Requests.Count(r => r.Contains("/resolve/", StringComparison.Ordinal));
        string second = ModelStore.Resolve($"{repo}/{file}@{revision}", null, TextWriter.Null);

        Assert.Equal(first, second);
        // The weights are downloaded once. The metadata lookup still happens on
        // every run: a spec carries no sha256 of its own, so the hash is read
        // from HuggingFace each time (which is why aliases, which pin the hash
        // in the manifest, are the offline form).
        Assert.Equal(1, downloadsAfterFirst);
        Assert.Equal(1, _handler.Requests.Count(r => r.Contains("/resolve/", StringComparison.Ordinal)));
    }

    [Fact]
    public void ACachedFileOfTheWrongSizeIsDownloadedAgain()
    {
        const string repo = "someone/custom-model";
        const string revision = "abc123";
        const string file = "model-Q4_K_M.gguf";
        byte[] content = Payload("truncated cache");
        ServeTree(repo, revision, file, Sha256Of(content), content.Length);
        ServeFile(repo, revision, file, content);

        string resolved = ModelStore.Resolve($"{repo}/{file}@{revision}", null, TextWriter.Null);
        File.WriteAllText(resolved, "truncated");

        var error = new StringWriter();
        string resolvedAgain = ModelStore.Resolve($"{repo}/{file}@{revision}", null, error);

        Assert.Equal(resolved, resolvedAgain);
        Assert.Equal(content, File.ReadAllBytes(resolvedAgain));
        Assert.Contains("downloading it again", error.ToString());
        Assert.Equal(2, _handler.Requests.Count(r => r.Contains("/resolve/", StringComparison.Ordinal)));
    }

    [Fact]
    public void AFileThatIsNotStoredAsAnLfsObjectIsRefused()
    {
        const string repo = "someone/custom-model";
        const string revision = "abc123";
        // No lfs block: there is no sha256 to verify the download against.
        ServeTree(repo, revision, "model.gguf", sha256: null, size: 0);

        InvalidDataException ex = Assert.Throws<InvalidDataException>(
            () => ModelStore.Resolve($"{repo}/model.gguf@{revision}", null, TextWriter.Null));

        Assert.Contains("not stored as an LFS object", ex.Message);
        Assert.Contains("cannot be verified", ex.Message);
    }

    [Fact]
    public void AFileThatIsNotInTheRepoIsReported()
    {
        const string repo = "someone/custom-model";
        const string revision = "abc123";
        ServeTree(repo, revision, "other.gguf", new string('0', 64), 1);

        FileNotFoundException ex = Assert.Throws<FileNotFoundException>(
            () => ModelStore.Resolve($"{repo}/missing.gguf@{revision}", null, TextWriter.Null));

        Assert.Contains("not found in", ex.Message);
    }

    [Fact]
    public void AFileInASubdirectoryIsFound()
    {
        const string repo = "someone/custom-model";
        const string revision = "abc123";
        byte[] content = Payload("subdir");
        _handler.On($"api/models/{repo}/tree/{revision}", $"[{{\"path\":\"q4/model-Q4_K_M.gguf\",\"type\":\"file\",\"lfs\":{{\"oid\":\"{Sha256Of(content)}\",\"size\":{content.Length}}}}}]");
        ServeFile(repo, revision, "q4/model-Q4_K_M.gguf", content);

        string resolved = ModelStore.Resolve($"{repo}/q4/model-Q4_K_M.gguf@{revision}", null, TextWriter.Null);

        // The '/' is not a path separator on the file system.
        Assert.Equal(content, File.ReadAllBytes(resolved));
        Assert.DoesNotContain(Path.DirectorySeparatorChar + "q4" + Path.DirectorySeparatorChar, Path.GetFileName(resolved));
    }

    [Fact]
    public void WithoutARevisionTheCurrentOneIsLookedUpFirst()
    {
        const string repo = "someone/custom-model";
        const string revision = "resolved-sha";
        const string file = "model.gguf";
        byte[] content = Payload("unpinned");

        // The more specific route is registered first: the stub answers the
        // first route that matches, and the tree URL contains the repo URL.
        ServeTree(repo, revision, file, Sha256Of(content), content.Length);
        _handler.On($"api/models/{repo}", $"{{\"sha\":\"{revision}\"}}");
        ServeFile(repo, revision, file, content);

        string resolved = ModelStore.Resolve($"{repo}/{file}", null, TextWriter.Null);

        Assert.Equal(content, File.ReadAllBytes(resolved));
        // An unpinned spec asks HuggingFace which revision is current.
        Assert.Contains(_handler.Requests, r => r == $"https://huggingface.co/api/models/{repo}");
    }

    [Fact]
    public void AFailedLookupIsReportedAsOneLineByTheCommand()
    {
        // A 404 from the tree endpoint, as a mistyped repo would give.
        _handler.On($"api/models/nobody/nothing/tree/main", HttpStatusCode.NotFound, "not found");

        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int exitCode = TranscribeCommand.Run(["audio.wav", "nobody/nothing/model.gguf@main"], stdout, stderr);

        Assert.Equal(1, exitCode);
        Assert.False(string.IsNullOrWhiteSpace(stderr.ToString()));
        Assert.DoesNotContain("   at ", stderr.ToString());
    }

    [Fact]
    public void ACancelledDownloadStopsAndLeavesNothingBehind()
    {
        const string repo = "nobody/cancel-download";
        const string revision = "main";
        const string file = "model.gguf";

        // The sha256 is never compared: the transfer is cancelled from inside the
        // body stream, after the first chunk, so the next read or write observes
        // the token instead of waiting for the whole model.
        byte[] content = Payload("cancel");
        ServeTree(repo, revision, file, sha256: new string('0', 64), size: content.Length);

        using var cancellation = new CancellationTokenSource();
        _handler.OnStream(
            $"huggingface.co/{repo}/resolve/{revision}/{file}",
            () => new CancellingStream(content, cancellation));

        Assert.ThrowsAny<OperationCanceledException>(() => ModelStore.Resolve(
            $"{repo}/{file}@{revision}", null, TextWriter.Null, null, cancellation.Token));

        Assert.True(cancellation.IsCancellationRequested);

        // Nothing usable and nothing partial: the .part file is deleted in the
        // store's finally, so the next attempt starts from zero rather than
        // trusting a truncated model.
        string cacheRoot = ModelStore.CacheRootOverride!;
        if (Directory.Exists(cacheRoot))
        {
            Assert.Empty(Directory.EnumerateFiles(cacheRoot, "*", SearchOption.AllDirectories));
        }
    }

    private static (string Out, string Error) Capture(Action<TextWriter> body)
    {
        var stdout = new StringWriter();
        body(stdout);
        return (stdout.ToString(), string.Empty);
    }

    /// <summary>Answers the URLs it is given and records what was requested.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly List<(string Match, Func<HttpResponseMessage> Respond)> _routes = new();

        public List<string> Requests { get; } = new();

        public void On(string pathContains, string json)
            => _routes.Add((pathContains, () => Json(json)));

        public void On(string pathContains, byte[] content)
            => _routes.Add((pathContains, () => Binary(content)));

        /// <summary>Serves a body from a stream the route builds per request.</summary>
        public void OnStream(string pathContains, Func<Stream> content)
            => _routes.Add((pathContains, () => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(content()),
            }));

        public void On(string pathContains, HttpStatusCode status, string body)
            => _routes.Add((pathContains, () => new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            }));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.ToString();
            Requests.Add(url);

            foreach ((string match, Func<HttpResponseMessage> respond) in _routes)
            {
                if (url.Contains(match, StringComparison.Ordinal))
                {
                    return ThreadingTask.FromResult(respond());
                }
            }

            return ThreadingTask.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent($"stub: no route for {url}"),
            });
        }

        private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

        private static HttpResponseMessage Binary(byte[] content) => new(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(content),
        };
    }

    /// <summary>
    /// A response body that cancels the download after its first chunk. Without
    /// it the only way to cancel mid-transfer would be a race with a timer; with
    /// it the token is guaranteed to be set while the store is still reading.
    /// </summary>
    private sealed class CancellingStream : Stream
    {
        private const int Chunk = 16;

        private readonly byte[] _content;
        private readonly CancellationTokenSource _cancellation;
        private int _position;

        public CancellingStream(byte[] content, CancellationTokenSource cancellation)
        {
            _content = content;
            _cancellation = cancellation;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => _content.Length;

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            _cancellation.Token.ThrowIfCancellationRequested();

            int length = Math.Min(Math.Min(count, Chunk), _content.Length - _position);
            if (length <= 0)
            {
                return 0;
            }

            _content.AsSpan(_position, length).CopyTo(buffer.AsSpan(offset, length));
            _position += length;

            // Cancel once the first chunk is in hand, so the store observes the
            // token on its next read or write rather than after the whole body.
            _cancellation.Cancel();
            return length;
        }

        public override void Flush()
        {
            // Read-only body: nothing to flush.
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

/// <summary>
/// The store keeps its seams in static state, so its download tests must not run
/// at the same time as each other.
/// </summary>
[CollectionDefinition(nameof(ModelStoreDownloadTests), DisableParallelization = true)]
public class ModelStoreDownloadTestGroup
{
}
