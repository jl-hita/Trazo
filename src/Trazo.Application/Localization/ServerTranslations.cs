using System.Reflection;
using System.Text.Json;

namespace Trazo.Application.Localization;

/// <summary>
/// The translation files embedded in this assembly, one per language: <c>Localization/Languages/{code}.json</c>, an object whose keys are the
/// Spanish texts of the catalogs and whose values are the translation. Keys starting with <c>@</c> are not sentences (<c>@short:Monday</c>,
/// the one-letter weekday). Adding a language to the API is adding its file.
/// </summary>
public static class ServerTranslations
{
    private const string Prefix = "Trazo.Application.Localization.Languages.";

    private static readonly IReadOnlyDictionary<AppLanguage, Lazy<IReadOnlyDictionary<string, string>>> Files = Find();

    public static IEnumerable<AppLanguage> Available => Files.Keys;

    public static IReadOnlyDictionary<string, string> For(AppLanguage language) =>
        Files.TryGetValue(language, out var file) ? file.Value : new Dictionary<string, string>();

    private static Dictionary<AppLanguage, Lazy<IReadOnlyDictionary<string, string>>> Find()
    {
        var assembly = typeof(ServerTranslations).Assembly;
        var result = new Dictionary<AppLanguage, Lazy<IReadOnlyDictionary<string, string>>>();
        foreach (var name in assembly.GetManifestResourceNames())
        {
            if (!name.StartsWith(Prefix, StringComparison.Ordinal) || !name.EndsWith(".json", StringComparison.Ordinal)) continue;
            if (!Enum.TryParse<AppLanguage>(name[Prefix.Length..^5], ignoreCase: true, out var language)) continue;
            result[language] = new Lazy<IReadOnlyDictionary<string, string>>(() => Read(assembly, name));
        }
        return result;
    }

    private static IReadOnlyDictionary<string, string> Read(Assembly assembly, string name)
    {
        using var stream = assembly.GetManifestResourceStream(name)!;
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
    }
}
