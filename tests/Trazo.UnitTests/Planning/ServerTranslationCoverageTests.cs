using System.Text.Json;
using System.Text.RegularExpressions;
using Trazo.Application.Localization;
using Xunit;

namespace Trazo.UnitTests.Planning;

/// <summary>Every translated language covers every text of the API and every exercise of the catalog.</summary>
public sealed partial class ServerTranslationCoverageTests
{
    [GeneratedRegex(@"\{\d+\}")]
    private static partial Regex Placeholder();

    public static TheoryData<AppLanguage> Translated()
    {
        var data = new TheoryData<AppLanguage>();
        foreach (var language in ServerTranslations.Available) data.Add(language);
        return data;
    }

    [Theory]
    [MemberData(nameof(Translated))]
    public void EveryTextIsTranslatedWithItsValues(AppLanguage language)
    {
        var file = ServerTranslations.For(language);
        var missing = Localizer.SourceTexts().Where(t => !file.TryGetValue(t, out var value) || string.IsNullOrWhiteSpace(value)).ToList();
        Assert.True(missing.Count == 0, $"{language}: {missing.Count} texts missing, first: {missing.FirstOrDefault()}");
        foreach (var source in Localizer.SourceTexts())
            Assert.Equal(Values(source), Values(file[source]));
        foreach (var day in Enum.GetNames<DayOfWeek>()) Assert.True(file.ContainsKey("@short:" + day), $"{language}: short name of {day}");
    }

    [Theory]
    [MemberData(nameof(Translated))]
    public void TheApiAnswersInTheLanguage(AppLanguage language)
    {
        const string spanish = "El objetivo no es válido.";
        var expected = ServerTranslations.For(language)[spanish];
        Assert.Equal(language, LanguageNegotiation.FromAcceptLanguage($"{language.Code()}, en;q=0.8"));
        Assert.Equal(language, LanguageNegotiation.FromChoice(language.Code()));
        Assert.Equal(expected, Localizer.Translate(spanish, language));
        // A text the code writes in English ends in the same language.
        Assert.Equal(ServerTranslations.For(language)["El enlace no es válido."], Localizer.Translate("The link is invalid.", language));
    }

    [Theory]
    [MemberData(nameof(Translated))]
    public void EveryExerciseOfTheCatalogIsTranslated(AppLanguage language)
    {
        var seeds = Path.Combine(AppContext.BaseDirectory, "Seeds");
        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(seeds, "exercise-catalog.json")));
        using var translated = JsonDocument.Parse(File.ReadAllText(Path.Combine(seeds, "translations", language.Code() + ".json")));
        foreach (var exercise in catalog.RootElement.EnumerateArray())
        {
            var id = exercise.GetProperty("id").GetString()!;
            Assert.True(translated.RootElement.TryGetProperty(id, out var text), $"{language}: {id}");
            Assert.False(string.IsNullOrWhiteSpace(text.GetProperty("name").GetString()), id);
            Assert.False(string.IsNullOrWhiteSpace(text.GetProperty("description").GetString()), id);
            Assert.Equal(exercise.GetProperty("instructionsEs").GetArrayLength(), text.GetProperty("instructions").GetArrayLength());
            Assert.Equal(exercise.GetProperty("tipsEs").GetArrayLength(), text.GetProperty("tips").GetArrayLength());
        }
    }

    private static string[] Values(string text) => Placeholder().Matches(text).Select(m => m.Value).Order().ToArray();
}
