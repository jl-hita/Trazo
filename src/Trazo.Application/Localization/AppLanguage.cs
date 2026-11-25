namespace Trazo.Application.Localization;

/// <summary>
/// The languages of the app. Spanish is the default and the language the texts are written in, English is the fallback; any other one is
/// answered in its own language once it has a translation file (<c>Localization/Languages/{code}.json</c>, see <see cref="ServerTranslations"/>)
/// and in English until then.
/// </summary>
public enum AppLanguage
{
    Es,
    En,
    Ca, Gl, Eu, Pt, Fr, It, De, Nl, Da, Nb, Sv, Fi, Pl, Cs, Sk, Sl, Hr, Sr, Bs, Mk, Ru, El, Tr, Ar, Zh, Ja, Ko
}

public static class AppLanguageCodes
{
    /// <summary>The two-letter code: <c>de</c> for German.</summary>
    public static string Code(this AppLanguage language) => language.ToString().ToLowerInvariant();
}

/// <summary>The language of the current request (set from <c>Accept-Language</c>); one instance per request.</summary>
public sealed class RequestLanguage
{
    public AppLanguage Current { get; set; } = AppLanguage.Es;
}

public static class LanguageNegotiation
{
    /// <summary>Every language the app can be shown in, which is what a user may choose.</summary>
    public static readonly IReadOnlyList<string> AppChoices = ["es", "en", "ca", "gl", "eu", "pt", "fr", "it", "de", "nl", "nl-be", "da", "nb", "sv", "fi", "pl", "cs", "sk", "sl", "hr", "sr", "bs", "mk", "ru", "el", "tr", "ar", "zh", "ja", "ko"];

    /// <summary>The languages the API answers in with their own texts.</summary>
    public static IReadOnlyList<AppLanguage> Supported { get; } =
        [AppLanguage.Es, AppLanguage.En, .. ServerTranslations.Available.OrderBy(l => l)];

    /// <summary>The supported language of a tag (<c>de-AT</c>, <c>nl-be</c>, <c>no</c>), or null.</summary>
    public static AppLanguage? FromTag(string? tag)
    {
        var primary = tag?.Trim().Split('-', '_')[0].ToLowerInvariant();
        if (primary == "no") primary = "nb";
        foreach (var language in Supported)
            if (language.Code() == primary) return language;
        return null;
    }

    /// <summary>The language the API answers in for a stored choice; null for automatic or anything unknown. A language of the app without a translation yet is English.</summary>
    public static AppLanguage? FromChoice(string? choice)
    {
        var value = choice?.Trim().ToLowerInvariant();
        if (value is null || !AppChoices.Contains(value)) return null;
        return FromTag(value) ?? AppLanguage.En;
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
            if (FromTag(tag) is { } language) return language;
        }
        return AppLanguage.Es;
    }
}
