// transcribe-server: an OpenAI-compatible endpoint over transcribe.cpp.
// The flow is ServerOptions (arguments) -> ServerApp (HTTP) ->
// NativeTranscriptionBackend (model, windows, native calls).

using TranscribeCppSharp.Server;

return await ServerHost.RunAsync(args);
