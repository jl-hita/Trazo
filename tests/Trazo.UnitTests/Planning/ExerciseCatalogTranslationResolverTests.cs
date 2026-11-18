using Trazo.Application.Localization;
using Trazo.Domain.Training;
using Xunit;

namespace Trazo.UnitTests.Planning;

public sealed class ExerciseCatalogTranslationResolverTests
{
    private static readonly string[] EsInstructions = ["Coloca los pies.", "Controla la bajada.", "Empuja con fuerza."];
    private static readonly string[] EsTips = ["Mantén el control.", "No rebotes."];

    [Fact]
    public void SpanishUsesThePublishedSourceTranslation()
    {
        var rows = new[]
        {
            new ExerciseCatalogTranslation
            {
                ExerciseId = "bench", LanguageCode = "es", Name = "Press de banca",
                Description = "Ejercicio de pecho.", Instructions = EsInstructions, Tips = EsTips, Status = "published"
            }
        };

        var result = ExerciseCatalogTranslationResolver.Resolve("es-ES", rows,
            "Press de banca", "Bench press", "Ejercicio de pecho.", EsInstructions, EsTips);

        Assert.Equal("Press de banca", result.Name);
        Assert.Equal("es", result.NameLanguage);
        Assert.True(result.IsCompleteForRequestedLanguage);
        Assert.Empty(result.MissingFields);
    }

    [Fact]
    public void DraftEnglishContentFallsBackFieldByFieldWithoutLosingLegacyEnglishName()
    {
        var rows = new[]
        {
            new ExerciseCatalogTranslation
            {
                ExerciseId = "bench", LanguageCode = "es", Name = "Press de banca",
                Description = "Ejercicio de pecho.", Instructions = EsInstructions, Tips = EsTips, Status = "published"
            },
            new ExerciseCatalogTranslation
            {
                ExerciseId = "bench", LanguageCode = "en", Name = "Bench press", Status = "draft"
            }
        };

        var result = ExerciseCatalogTranslationResolver.Resolve("en-GB", rows,
            "Press de banca", "Bench press", "Ejercicio de pecho.", EsInstructions, EsTips);

        Assert.Equal("Bench press", result.Name);
        Assert.Equal("en", result.NameLanguage);
        Assert.Equal("Ejercicio de pecho.", result.Description);
        Assert.Equal("es", result.DescriptionLanguage);
        Assert.Equal(EsInstructions, result.Instructions);
        Assert.Equal(EsTips, result.Tips);
        Assert.False(result.IsCompleteForRequestedLanguage);
        Assert.Equal(["description", "instructions", "tips"], result.MissingFields);
    }

    [Fact]
    public void UnsupportedLanguageFallsBackToSpanishAndIsMarkedIncompleteForRequestedLanguage()
    {
        var result = ExerciseCatalogTranslationResolver.Resolve("fr", [],
            "Press de banca", "Bench press", "Descripción.", EsInstructions, EsTips);

        Assert.Equal("fr", result.RequestedLanguage);
        Assert.Equal("Descripción.", result.Description);
        Assert.Equal("es", result.DescriptionLanguage);
        Assert.False(result.IsCompleteForRequestedLanguage);
        Assert.Equal(["name", "description", "instructions", "tips"], result.MissingFields);
    }

    [Fact]
    public void PublishedEnglishTranslationIsUsedInsteadOfSpanishFallback()
    {
        var rows = new[]
        {
            new ExerciseCatalogTranslation
            {
                ExerciseId = "bench", LanguageCode = "es", Name = "Press de banca",
                Description = "Ejercicio de pecho.", Instructions = EsInstructions, Tips = EsTips, Status = "published"
            },
            new ExerciseCatalogTranslation
            {
                ExerciseId = "bench", LanguageCode = "en", Name = "Bench press",
                Description = "A chest exercise.", Instructions = ["Set your feet.", "Lower with control.", "Press up."],
                Tips = ["Stay controlled.", "Do not bounce."], Status = "published"
            }
        };

        var result = ExerciseCatalogTranslationResolver.Resolve("en", rows,
            "Press de banca", "Bench press", "Ejercicio de pecho.", EsInstructions, EsTips);

        Assert.Equal("A chest exercise.", result.Description);
        Assert.Equal("en", result.DescriptionLanguage);
        Assert.True(result.IsCompleteForRequestedLanguage);
        Assert.Empty(result.MissingFields);
    }
}
