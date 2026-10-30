namespace SyntInfo.Domain.Entities;

public static class TagFormattingHelper
{
    public static string SanitizeTag(string? text, string fallback = "")
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return fallback;
        }

        var clean = new string(text.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        return string.IsNullOrWhiteSpace(clean) ? fallback : clean;
    }
}

