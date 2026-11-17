using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace Trazo.Application.Localization;

/// <summary>
/// Translates the Spanish texts of the plan API. The Spanish text is the key (as in gettext), so the engine shared with DietoExpress
/// is not touched: its messages come out in Spanish and are translated at the boundary.
/// <list type="number">
/// <item>The whole text, exactly.</item>
/// <item>A template with values (<c>La duración debe estar entre {0} y {1} semanas.</c>); the values are translated too when they are
/// texts of the catalog (for example a nested explanation).</item>
/// <item>Sentence by sentence: engine messages are several sentences, often built from several literals. Sentences with no entry stay Spanish.</item>
/// <item><c>identifier: text</c> (a note prefixed with an exercise id).</item>
/// </list>
/// Spanish is returned unchanged. The result of a text is cached.
/// </summary>
public static partial class Localizer
{
    private sealed record Template(Regex Pattern, string English, int Literals);

    private static readonly Dictionary<string, string> Exact = new(StringComparer.Ordinal);
    private static readonly List<Template> Templates = [];
    private static readonly ConcurrentDictionary<string, string> Cache = new(StringComparer.Ordinal);

    static Localizer()
    {
        foreach (var (es, en) in EnglishCatalog.Entries)
        {
            if (!Placeholder().IsMatch(es)) { Exact[es] = en; continue; }
            var pattern = Placeholder().Replace(Regex.Escape(es).Replace(@"\{", "{"), match => $"(?<g{match.Groups[1].Value}>.+?)");
            Templates.Add(new Template(new Regex("^" + pattern + "$", RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.Compiled), en,
                Placeholder().Replace(es, string.Empty).Length));
        }
        // The longest literal text first, so the most specific template wins.
        Templates.Sort((a, b) => b.Literals.CompareTo(a.Literals));
    }

    [GeneratedRegex(@"\{(\d+)\}")]
    private static partial Regex Placeholder();

    [GeneratedRegex(@"(?<=[.!?])\s+(?=[A-ZÁÉÍÓÚÑ¿¡«])")]
    private static partial Regex SentenceBreak();

    [GeneratedRegex(@"^(?<prefix>[^\s:]+): (?<rest>.+)$", RegexOptions.Singleline)]
    private static partial Regex Prefixed();

    public static string? Translate(string? text, AppLanguage language)
    {
        if (language == AppLanguage.Es || string.IsNullOrWhiteSpace(text)) return text;
        return Cache.GetOrAdd(text, Compute);
    }

    /// <summary>The same for every text of a list (validation messages).</summary>
    public static string[] Translate(IEnumerable<string> texts, AppLanguage language) =>
        texts.Select(t => Translate(t, language) ?? t).ToArray();

    private static string Compute(string text)
    {
        var whole = Whole(text);
        if (whole is not null) return whole;

        // "bench-press: <explanation>": a note prefixed with an identifier.
        var prefixed = Prefixed().Match(text);
        if (prefixed.Success)
        {
            var rest = Whole(prefixed.Groups["rest"].Value) ?? Sentences(prefixed.Groups["rest"].Value);
            if (rest is not null) return prefixed.Groups["prefix"].Value + ": " + rest;
        }
        return Sentences(text) ?? text;
    }

    private static string? Sentences(string text)
    {
        var parts = SentenceBreak().Split(text.Trim());
        var translated = parts.Select(Whole).ToArray();
        return translated.All(t => t is null) ? null : string.Join(' ', parts.Select((s, i) => translated[i] ?? s));
    }

    private static string? Whole(string text)
    {
        text = text.Trim();
        if (Exact.TryGetValue(text, out var exact)) return exact;
        foreach (var template in Templates)
        {
            var match = template.Pattern.Match(text);
            if (!match.Success) continue;
            return Placeholder().Replace(template.English, m =>
            {
                var value = match.Groups["g" + m.Groups[1].Value].Value;
                return Cache.GetOrAdd(value, Compute);
            });
        }
        return null;
    }
}
