namespace TranscribeCppSharp.Server;

/// <summary>
/// Wire shapes. Property names are serialized with
/// <see cref="JsonNamingPolicy.SnakeCaseLower"/>, so <c>OwnedBy</c> is
/// <c>owned_by</c> without an attribute on every member.
/// </summary>
/// <remarks>
/// Only fields this library can actually fill are present. OpenAI's
/// <c>avg_logprob</c>, <c>compression_ratio</c>, <c>no_speech_prob</c> and token
/// arrays are absent because transcribe.cpp does not return them; inventing
/// zeros would be worse than the omission, which the docs state.
/// </remarks>
internal sealed record ErrorEnvelope(ErrorBody Error);

/// <summary>OpenAI's error object.</summary>
internal sealed record ErrorBody(string Message, string Type, string? Param, string? Code);

/// <summary><c>GET /v1/models</c>.</summary>
internal sealed record ModelList(string Object, IReadOnlyList<ModelCard> Data);

/// <summary>One entry of the model list.</summary>
internal sealed record ModelCard(string Id, string Object, string OwnedBy);

/// <summary><c>response_format=json</c>.</summary>
internal sealed record TextResponse(string Text);

/// <summary><c>response_format=verbose_json</c>, plus the <c>speaker</c> extension.</summary>
internal sealed record VerboseResponse(
    string Task,
    string Language,
    double Duration,
    string Text,
    IReadOnlyList<VerboseSegment> Segments,
    IReadOnlyList<VerboseWord> Words);

/// <summary>One verbose segment.</summary>
internal sealed record VerboseSegment(int Id, double Start, double End, string Text, int? Speaker);

/// <summary>One word timestamp.</summary>
internal sealed record VerboseWord(string Word, double Start, double End);

/// <summary><c>GET /health</c>.</summary>
internal sealed record HealthResponse(string Status, string Model, bool Loaded);
