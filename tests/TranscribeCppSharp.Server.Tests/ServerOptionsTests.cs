namespace TranscribeCppSharp.Server.Tests;

using Xunit;

public sealed class ServerOptionsTests
{
    private static readonly string[] DefaultUrls = ["http://127.0.0.1:8080"];
    private static readonly string[] TwoUrls = ["http://0.0.0.0:9000", "http://127.0.0.1:9001"];

    private static ServerOptions Parse(params string[] args)
    {
        Assert.True(
            ServerOptions.TryParse(args, _ => null, out ServerOptions? options, out string? error, out _),
            error);
        return options!;
    }

    [Fact]
    public void The_model_is_required()
    {
        Assert.False(ServerOptions.TryParse([], _ => null, out _, out string? error, out bool help));

        Assert.False(help);
        Assert.Contains("--model is required", error!, StringComparison.Ordinal);
    }

    [Fact]
    public void Defaults_are_the_documented_ones()
    {
        ServerOptions options = Parse("--model", "whisper-tiny");

        Assert.Equal("whisper-tiny", options.Model);
        Assert.Equal("auto", options.Backend);
        Assert.Equal(300, options.ChunkSeconds);
        Assert.Null(options.ApiKey);
        Assert.False(options.AllowDownloads);
        Assert.False(options.Diarize);
        Assert.Equal(1024L * 1024 * 1024, options.MaxUploadBytes);
        Assert.Equal(DefaultUrls, options.Urls);
    }

    [Fact]
    public void Every_documented_flag_is_read()
    {
        ServerOptions options = Parse(
            "--model", "models/x.gguf",
            "--backend", "Vulkan",
            "--chunk", "120",
            "--api-key", "k",
            "--allow-download",
            "--diarize",
            "--max-upload-mb", "64",
            "--urls", "http://0.0.0.0:9000;http://127.0.0.1:9001");

        Assert.Equal("models/x.gguf", options.Model);
        Assert.Equal("vulkan", options.Backend);
        Assert.Equal(120, options.ChunkSeconds);
        Assert.Equal("k", options.ApiKey);
        Assert.True(options.AllowDownloads);
        Assert.True(options.Diarize);
        Assert.Equal(64L * 1024 * 1024, options.MaxUploadBytes);
        Assert.Equal(TwoUrls, options.Urls);
    }

    [Fact]
    public void The_api_key_can_come_from_the_environment()
    {
        ServerOptions options = Parse("--model", "m");
        Assert.Null(options.ApiKey);

        Assert.True(ServerOptions.TryParse(
            ["--model", "m"],
            name => name == ServerOptions.ApiKeyVariable ? "from-env" : null,
            out ServerOptions? fromEnvironment,
            out _,
            out _));

        Assert.Equal("from-env", fromEnvironment!.ApiKey);
    }

    [Fact]
    public void An_explicit_api_key_beats_the_environment()
    {
        Assert.True(ServerOptions.TryParse(
            ["--model", "m", "--api-key", "explicit"],
            _ => "from-env",
            out ServerOptions? options,
            out _,
            out _));

        Assert.Equal("explicit", options!.ApiKey);
    }

    [Theory]
    [InlineData("--backend", "fpga")]
    [InlineData("--chunk", "1")]
    [InlineData("--chunk", "soon")]
    [InlineData("--max-upload-mb", "0")]
    [InlineData("--urls", "")]
    [InlineData("--model", "")]
    public void Bad_values_are_refused(string flag, string value)
    {
        Assert.False(ServerOptions.TryParse(["--model", "m", flag, value], _ => null, out _, out string? error, out _));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void An_unknown_flag_is_refused_instead_of_ignored()
    {
        Assert.False(ServerOptions.TryParse(["--model", "m", "--verbose"], _ => null, out _, out string? error, out _));
        Assert.Contains("unknown argument", error!, StringComparison.Ordinal);
    }

    [Fact]
    public void Help_is_reported_without_requiring_a_model()
    {
        Assert.True(ServerOptions.TryParse(["--help"], _ => null, out _, out _, out bool help));
        Assert.True(help);
    }

    [Theory]
    [InlineData("auto", TranscribeCppSharp.Interop.BackendRequest.BackendAuto)]
    [InlineData("cpu", TranscribeCppSharp.Interop.BackendRequest.BackendCpu)]
    [InlineData("cpu-accel", TranscribeCppSharp.Interop.BackendRequest.BackendCpuAccel)]
    [InlineData("metal", TranscribeCppSharp.Interop.BackendRequest.BackendMetal)]
    [InlineData("vulkan", TranscribeCppSharp.Interop.BackendRequest.BackendVulkan)]
    [InlineData("cuda", TranscribeCppSharp.Interop.BackendRequest.BackendCuda)]
    [InlineData("rocm", TranscribeCppSharp.Interop.BackendRequest.BackendRocm)]
    public void Backend_names_map_to_the_native_request(string name, TranscribeCppSharp.Interop.BackendRequest expected)
    {
        Assert.True(ServerOptions.TryParseBackendName(name, out TranscribeCppSharp.Interop.BackendRequest request));
        Assert.Equal(expected, request);
    }

    [Fact]
    public void An_unknown_backend_name_does_not_map()
    {
        Assert.False(ServerOptions.TryParseBackendName("fpga", out _));
    }
}
