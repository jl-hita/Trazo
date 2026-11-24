namespace Trazo.Application.Localization;

/// <summary>Languages the API can answer in. Spanish is the default and the language the texts are written in.</summary>
public enum AppLanguage
{
    Es,
    En
}

/// <summary>The language of the current request (set from <c>Accept-Language</c>); one instance per request.</summary>
public sealed class RequestLanguage
{
    public AppLanguage Current { get; set; } = AppLanguage.Es;
}

public static class LanguageNegotiation
{
    /// <summary>
    /// Every language the app can be shown in, which is what a user may choose. The API itself answers in Spanish and English only:
    /// any other language of the app is answered in English until its texts are translated.
    /// </summary>
    public static readonly IReadOnlyList<string> AppChoices = ["es", "en", "ca", "gl", "eu", "pt", "fr", "it", "de", "nl", "nl-be", "da", "nb", "sv", "fi", "pl", "cs", "sk", "sl", "hr", "sr", "bs", "mk", "ru", "el", "tr", "ar", "zh", "ja", "ko"];

    /// <summary>The language the API answers in for a stored choice; null for automatic or anything unknown. Spanish is Spanish, every other language of the app is English for now.</summary>
    public static AppLanguage? FromChoice(string? choice)
    {
        var value = choice?.Trim().ToLowerInvariant();
        if (value == "es") return AppLanguage.Es;
        return value is not null && AppChoices.Contains(value) ? AppLanguage.En : null;
    }

    /// <summary>The value to store for a choice: a language of the app, or null (automatic) for an empty or "auto" choice; false when it is none of them.</summary>
    public static bool TryNormalizeChoice(string? choice, out string? stored)
    {
        stored = null;
        var value = choice?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(value) || value == "auto") return true;
        if (!AppChoices.Contains(value)) return false;
        stored = value;
        return true;
    }

    /// <summary>
    /// The first supported language of an <c>Accept-Language</c> value, by quality: <c>en-GB,en;q=0.9,es;q=0.8</c> is English.
    /// Anything missing, malformed or unsupported is Spanish, the default.
    /// </summary>
    public static AppLanguage FromAcceptLanguage(string? header)
    {
        if (string.IsNullOrWhiteSpace(header)) return AppLanguage.Es;
        var ranked = new List<(string Tag, double Quality, int Order)>();
        var order = 0;
        foreach (var part in header.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pieces = part.Split(';', StringSplitOptions.TrimEntries);
            var quality = 1.0;
            foreach (var parameter in pieces.Skip(1))
                if (parameter.StartsWith("q=", StringComparison.OrdinalIgnoreCase)
                    && double.TryParse(parameter[2..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var q))
                    quality = q;
            ranked.Add((pieces[0], quality, order++));
        }
        foreach (var (tag, quality, _) in ranked.OrderByDescending(r => r.Quality).ThenBy(r => r.Order))
        {
            if (quality <= 0) continue;
            if (tag.Equals("es", StringComparison.OrdinalIgnoreCase) || tag.StartsWith("es-", StringComparison.OrdinalIgnoreCase)) return AppLanguage.Es;
            if (tag.Equals("en", StringComparison.OrdinalIgnoreCase) || tag.StartsWith("en-", StringComparison.OrdinalIgnoreCase)) return AppLanguage.En;
        }
        return AppLanguage.Es;
    }
}
