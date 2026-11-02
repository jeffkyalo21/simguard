namespace SimGuard.Api.Common;

// Builds ILIKE patterns from user input. Use with EscapeCharacter so that
// %, _ and \ typed by the user are matched literally.
public static class SearchPattern
{
    public const string EscapeCharacter = "\\";

    public static string? Contains(string? input)
    {
        var trimmed = input?.Trim();

        return string.IsNullOrEmpty(trimmed)
            ? null
            : $"%{Escape(trimmed)}%";
    }

    public static string? Exact(string? input)
    {
        var trimmed = input?.Trim();

        return string.IsNullOrEmpty(trimmed)
            ? null
            : Escape(trimmed);
    }

    private static string Escape(string value) =>
        value
            .Replace("\\", "\\\\")
            .Replace("%", "\\%")
            .Replace("_", "\\_");
}
