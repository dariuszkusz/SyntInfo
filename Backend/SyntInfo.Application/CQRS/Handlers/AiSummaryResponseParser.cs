using System.Text.Json;
using SyntInfo.Application.Models.Llm;

namespace SyntInfo.Application.CQRS.Handlers;

/// <summary>
/// Odpowiada za czyszczenie i parsowanie odpowiedzi JSON z modeli językowych (LLM)
/// oraz walidację jakości wygenerowanych faktów i streszczeń (SRP).
/// </summary>
internal static class AiSummaryResponseParser
{
    public static string CleanJsonResponse(string raw)
    {
        var cleaned = raw.Trim();
        if (cleaned.Contains("```"))
        {
            int firstCodeBlock = cleaned.IndexOf("```", StringComparison.Ordinal);
            int lastCodeBlock = cleaned.LastIndexOf("```", StringComparison.Ordinal);

            if (firstCodeBlock != lastCodeBlock && firstCodeBlock >= 0)
            {
                var sub = cleaned.Substring(firstCodeBlock + 3, lastCodeBlock - firstCodeBlock - 3).Trim();
                if (sub.StartsWith("json", StringComparison.OrdinalIgnoreCase))
                {
                    sub = sub.Substring(4).Trim();
                }
                cleaned = sub;
            }
        }

        if (cleaned.Contains("\" +"))
        {
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\""\s*\+\s*\n?\s*\""", "");
        }

        var firstBrace = cleaned.IndexOf('{');
        var firstBracket = cleaned.IndexOf('[');
        int start = -1;

        if (firstBrace >= 0 && (firstBracket < 0 || firstBrace < firstBracket)) start = firstBrace;
        else if (firstBracket >= 0) start = firstBracket;

        if (start >= 0)
        {
            char startChar = cleaned[start];
            char endChar = startChar == '{' ? '}' : ']';
            int depth = 0;
            int end = -1;
            for (int i = start; i < cleaned.Length; i++)
            {
                if (cleaned[i] == startChar) depth++;
                else if (cleaned[i] == endChar)
                {
                    depth--;
                    if (depth == 0) { end = i; break; }
                }
            }
            if (end > start)
            {
                cleaned = cleaned.Substring(start, end - start + 1);
            }
        }

        return cleaned;
    }

    public static (string Title, string Essence, string CategoryName) ParseEditorResponse(
        string editorJson,
        string editorResponseRaw,
        string fallbackTitle,
        ILogger logger)
    {
        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var structuredContent = JsonSerializer.Deserialize<InfopigulaContent>(editorJson, options);

            if (structuredContent != null &&
                !string.IsNullOrWhiteSpace(structuredContent.Title) &&
                !string.IsNullOrWhiteSpace(structuredContent.Essence))
            {
                return (
                    structuredContent.Title.Trim(),
                    structuredContent.Essence.Trim(),
                    structuredContent.Category?.Trim() ?? "General"
                );
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Błąd parsowania JSON z Redaktora. Raw: {Raw}", editorResponseRaw);
        }

        return (fallbackTitle, string.Empty, "General");
    }

    public static bool HasValidFacts(string? factsJson)
    {
        if (string.IsNullOrWhiteSpace(factsJson))
        {
            return false;
        }

        var trimmed = factsJson.Trim();
        if (trimmed.Length < 10 || trimmed == "[]" || trimmed == "{}")
        {
            return false;
        }

        var lower = trimmed.ToLowerInvariant();
        if (lower.Contains("brak faktów") ||
            lower.Contains("brak danych") ||
            lower.Contains("user safety") ||
            lower.Contains("nie podano"))
        {
            return false;
        }

        return trimmed.StartsWith('[') || trimmed.StartsWith('{');
    }

    public static bool IsValidSummary(string title, string essence)
    {
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(essence))
        {
            return false;
        }

        if (essence.Length < 40)
        {
            return false;
        }

        var lowerTitle = title.ToLowerInvariant();
        if (lowerTitle.Contains("brak faktów") ||
            lowerTitle.Contains("brak danych") ||
            lowerTitle.Contains("nie podano danych") ||
            lowerTitle.Contains("user safety"))
        {
            return false;
        }

        var lower = essence.ToLowerInvariant();
        if (lower.Contains("user safety") ||
            lower.Contains("safety filter") ||
            lower.Contains("as an ai") ||
            lower.Contains("as a language model") ||
            lower.Contains("i cannot fulfill") ||
            lower.Contains("i am sorry, but") ||
            lower.Contains("brak faktów") ||
            lower.Contains("brak danych") ||
            lower.Contains("nie podano danych") ||
            lower.Contains("brak wystarczających danych") ||
            lower.Contains("proszę dostarcz") ||
            lower.Contains("proszę o dostarczenie"))
        {
            return false;
        }

        return true;
    }
}

