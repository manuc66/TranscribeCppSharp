// Entry point of the `transcribe` tool.
//
// The command itself lives in TranscribeCommand.Run: the entry point generated
// from these top-level statements cannot be called from a test, and the CLI is
// the project's showcase, so its behaviour is verified through Run (with the
// console injected) instead.

using TranscribeCppSharp.Cli;

return TranscribeCommand.Run(args, Console.Out, Console.Error);
