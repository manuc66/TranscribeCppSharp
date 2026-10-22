namespace TranscribeCppSharp.Server;

/// <summary>Entry point: parse, build, run, and say plainly what is enabled.</summary>
internal static class ServerHost
{
    /// <summary>Runs the server until shutdown. Returns a process exit code.</summary>
    internal static async Task<int> RunAsync(string[] args)
    {
        if (!ServerOptions.TryParse(args, environment: null, out ServerOptions? options, out string? error, out bool help))
        {
            Console.Error.WriteLine(error);
            Console.Error.WriteLine("Run 'transcribe-server --help' for the accepted arguments.");
            return 2;
        }

        if (help)
        {
            Console.Out.WriteLine(ServerOptions.HelpText);
            return 0;
        }

        await using var backend = new NativeTranscriptionBackend(options!);
        WebApplication app = ServerApp.Build(options!, backend);

        PrintBanner(options!);
        await app.RunAsync().ConfigureAwait(false);
        return 0;
    }

    private static void PrintBanner(ServerOptions options)
    {
        Console.Out.WriteLine($"transcribe-server listening on {string.Join(", ", options.Urls)}");
        Console.Out.WriteLine($"  model    : {options.Model} (loads on the first request)");
        Console.Out.WriteLine($"  backend  : {options.Backend}");
        Console.Out.WriteLine($"  window   : {options.ChunkSeconds} s, split and deduplicated above that");
        Console.Out.WriteLine($"  diarize  : {(options.Diarize ? "on by default" : "off by default")}");
        Console.Out.WriteLine($"  auth     : {(options.ApiKey is null ? "DISABLED — anyone who can reach the port can transcribe" : "Bearer token required (health stays open)")}");
        Console.Out.WriteLine($"  download : {(options.AllowDownloads ? "enabled on first request" : "disabled")}");
        Console.Out.WriteLine("  requests : serialized, one native run per model at a time");
    }
}
