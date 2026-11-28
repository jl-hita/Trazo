using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace Trazo.Application.Localization;

/// <summary>
/// Translates the texts of the API. The text in its source language is the key (as in gettext), so the engine shared with DietoExpress
/// is not touched: its Spanish messages are translated at the boundary. Most texts are written in Spanish; a few (the authentication
/// ones) were written in English, so both directions exist. A text is tried:
/// <list type="number">
/// <item>whole, exactly;</item>
/// <item>as a template with values (<c>La duración debe estar entre {0} y {1} semanas.</c>); the values are translated too when they
/// are texts of the catalog (a nested explanation, a phrase such as "desde la web");</item>
/// <item><c>identifier: text</c> (a note prefixed with an exercise id);</item>
/// <item>sentence by sentence: engine messages are several sentences, often built from several literals. A sentence with no entry stays as it is.</item>
/// </list>
/// A text already in the requested language comes out unchanged, and so does one with no entry (never an error). Results are cached.
/// </summary>
public static partial class Localizer
{
    private sealed record Template(Regex Pattern, string Target, int Literals);

    private sealed class Direction
    {
        private readonly Dictionary<string, string> _exact = new(StringComparer.Ordinal);
        private readonly List<Template> _templates = [];
        private readonly ConcurrentDictionary<string, string> _cache = new(StringComparer.Ordinal);

        public Direction(IEnumerable<(string Source, string Target)> entries)
        {
            foreach (var (source, target) in entries)
            {
                if (!Placeholder().IsMatch(source)) { _exact[source] = target; continue; }
                var pattern = Placeholder().Replace(Regex.Escape(source).Replace(@"\{", "{"), match => $"(?<g{match.Groups[1].Value}>.+?)");
                _templates.Add(new Template(
                    new Regex("^" + pattern + "$", RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.Compiled), target,
                    Placeholder().Replace(source, string.Empty).Length));
            }
            // The longest literal text first, so the most specific template wins.
            _templates.Sort((a, b) => b.Literals.CompareTo(a.Literals));
        }

        public string Translate(string text) => _cache.GetOrAdd(text, Compute);

        private string Compute(string text)
        {
            // When every sentence is a text of its own, that reading wins: a template ending in a value would otherwise
            // take the following sentences into the value and leave them untranslated.
            var each = SentenceBreak().Split(text.Trim());
            if (each.Length > 1 && !_exact.ContainsKey(text.Trim()))
            {
                var all = each.Select(Whole).ToArray();
                if (all.All(t => t is not null)) return string.Join(' ', all);
            }
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

        private string? Sentences(string text)
        {
            var parts = SentenceBreak().Split(text.Trim());
            var translated = parts.Select(Whole).ToArray();
            return translated.All(t => t is null) ? null : string.Join(' ', parts.Select((s, i) => translated[i] ?? s));
        }

        /// <summary>A value of a template: a text of the catalog, or a list of them ("Glúteos, Cuádriceps").</summary>
        private string Value(string value)
        {
            if (!value.Contains(", ", StringComparison.Ordinal) || _exact.ContainsKey(value)) return Translate(value);
            var parts = value.Split(", ");
            return parts.All(_exact.ContainsKey) ? string.Join(", ", parts.Select(p => _exact[p])) : Translate(value);
        }

        private string? Whole(string text)
        {
            text = text.Trim();
            if (_exact.TryGetValue(text, out var exact)) return exact;
            foreach (var template in _templates)
            {
                var match = template.Pattern.Match(text);
                if (!match.Success) continue;
                return Placeholder().Replace(template.Target, m => Value(match.Groups["g" + m.Groups[1].Value].Value));
            }
            return null;
        }
    }

    private static readonly Direction ToEnglish = new(Pairs().Concat(EnglishCatalog.DayLists));
    private static readonly Direction ToSpanish = new(AccountCatalog.EnglishToSpanish);
    private static readonly ConcurrentDictionary<AppLanguage, Lazy<Direction>> ToOthers = new();

    /// <summary>Every pair of the catalogs, (Spanish, English). A method, so no static field depends on another one's initialisation order.</summary>
    private static IEnumerable<(string Es, string En)> Pairs() =>
        EnglishCatalog.Entries.Concat(AccountCatalog.Pairs).Concat(PageCatalog.Pairs).Concat(VocabularyCatalog.Pairs);

    /// <summary>The Spanish texts a translation file has to cover (the weekday lists are built from the seven names).</summary>
    public static IReadOnlyList<string> SourceTexts() =>
        Pairs().Select(p => p.Es).Concat(EnglishCatalog.DayNames.Select(d => d.Es)).Distinct(StringComparer.Ordinal).ToArray();

    /// <summary>
    /// The direction to a translated language: the Spanish text and the English one both lead to the translation, and a text the file
    /// does not have yet comes out in English.
    /// </summary>
    private static Direction To(AppLanguage language)
    {
        var file = ServerTranslations.For(language);
        var entries = new List<(string, string)>();
        foreach (var (es, en) in Pairs())
        {
            var target = file.GetValueOrDefault(es) ?? en;
            entries.Add((en, target));
            entries.Add((es, target));
        }
        entries.AddRange(EnglishCatalog.Lists(EnglishCatalog.DayNames.Select(d => (d.Es, file.GetValueOrDefault(d.Es) ?? d.En)).ToArray()));
        return new Direction(entries);
    }

    [GeneratedRegex(@"\{(\d+)\}")]
    private static partial Regex Placeholder();

    [GeneratedRegex(@"(?<=[.!?])\s+(?=[A-ZÁÉÍÓÚÑ¿¡«])")]
    private static partial Regex SentenceBreak();

    [GeneratedRegex(@"^(?<prefix>[^\s:]+): (?<rest>.+)$", RegexOptions.Singleline)]
    private static partial Regex Prefixed();

    public static string? Translate(string? text, AppLanguage language)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;
        var direction = language switch
        {
            AppLanguage.Es => ToSpanish,
            AppLanguage.En => ToEnglish,
            _ => ToOthers.GetOrAdd(language, l => new Lazy<Direction>(() => To(l))).Value
        };
        return direction.Translate(text);
    }

    /// <summary>A text with values, given by its Spanish template (<c>{0} días</c>): the template is translated and the values go in as they are.</summary>
    public static string Format(AppLanguage language, string template, params object[] values)
    {
        var translated = template;
        if (language != AppLanguage.Es)
        {
            var english = Pairs().FirstOrDefault(p => p.Es == template).En ?? template;
            translated = language == AppLanguage.En ? english : ServerTranslations.For(language).GetValueOrDefault(template) ?? english;
        }
        return string.Format(System.Globalization.CultureInfo.InvariantCulture, translated, values);
    }

    /// <summary>The same for every text of a list (validation messages).</summary>
    public static string[] Translate(IEnumerable<string> texts, AppLanguage language) =>
        texts.Select(t => Translate(t, language) ?? t).ToArray();
}
