using Kiriha.Core.Domain.Utils.Parsing;

namespace Kiriha.Core.Streaming.Helpers;

public static class AnimeLibTokenHelper
{
    public static string CleanToken(string? rawInput) => BearerTokenHelper.CleanToken(rawInput);

    public static string? TryExtractUsername(string? rawToken) => BearerTokenHelper.TryExtractUsername(rawToken);
}
