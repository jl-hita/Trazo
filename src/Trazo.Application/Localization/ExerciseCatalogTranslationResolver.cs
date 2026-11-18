using Trazo.Domain.Training;

namespace Trazo.Application.Localization;

public sealed record ResolvedExerciseTranslation(
    string RequestedLanguage,
    string Name,
    string Description,
    string[] Instructions,
    string[] Tips,
    string NameLanguage,
    string DescriptionLanguage,
    string InstructionsLanguage,
    string TipsLanguage,
    bool IsCompleteForRequestedLanguage,
    string[] MissingFields);

/// <summary>
/// Resolves each text field independently. Only published translations are served;
/// missing or draft fields fall back to Spanish, while legacy English names remain usable.
/// </summary>
public static class ExerciseCatalogTranslationResolver
{
    public static ResolvedExerciseTranslation Resolve(
        string requestedLanguage,
        IEnumerable<ExerciseCatalogTranslation> translations,
        string legacyNameEs,
        string legacyNameEn,
        string legacyDescriptionEs,
        string[] legacyInstructionsEs,
        string[] legacyTipsEs)
    {
        var requested = NormalizeLanguage(requestedLanguage);
        var rows = translations.GroupBy(x => NormalizeLanguage(x.LanguageCode), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Status == "published").First(), StringComparer.OrdinalIgnoreCase);
        rows.TryGetValue(requested, out var selected);
        rows.TryGetValue("es", out var spanish);

        var selectedIsPublished = selected?.Status == "published";
        var name = PublishedValue(selectedIsPublished ? selected?.Name : null);
        var description = PublishedValue(selectedIsPublished ? selected?.Description : null);
        var instructions = PublishedArray(selectedIsPublished ? selected?.Instructions : null);
        var tips = PublishedArray(selectedIsPublished ? selected?.Tips : null);

        var nameLanguage = requested;
        var descriptionLanguage = requested;
        var instructionsLanguage = requested;
        var tipsLanguage = requested;
        var missing = new List<string>();

        if (name is null)
        {
            if (requested == "en" && !string.IsNullOrWhiteSpace(legacyNameEn))
            {
                name = legacyNameEn;
                nameLanguage = "en";
            }
            else
            {
                name = PublishedValue(spanish?.Name) ?? legacyNameEs;
                nameLanguage = "es";
            }
            if (selected?.Status != "published" || string.IsNullOrWhiteSpace(selected?.Name)) missing.Add("name");
        }

        if (description is null)
        {
            description = PublishedValue(spanish?.Description) ?? legacyDescriptionEs;
            descriptionLanguage = "es";
            if (selected?.Status != "published" || string.IsNullOrWhiteSpace(selected?.Description)) missing.Add("description");
        }
        if (instructions is null)
        {
            instructions = PublishedArray(spanish?.Instructions) ?? legacyInstructionsEs;
            instructionsLanguage = "es";
            if (selected?.Status != "published" || spanish?.Instructions is null) missing.Add("instructions");
        }
        if (tips is null)
        {
            tips = PublishedArray(spanish?.Tips) ?? legacyTipsEs;
            tipsLanguage = "es";
            if (selected?.Status != "published" || spanish?.Tips is null) missing.Add("tips");
        }

        return new ResolvedExerciseTranslation(
            requested, name, description, instructions, tips,
            nameLanguage, descriptionLanguage, instructionsLanguage, tipsLanguage,
            missing.Count == 0, missing.ToArray());
    }

    private static string NormalizeLanguage(string? language)
    {
        var value = language?.Trim().ToLowerInvariant() ?? "";
        if (value.Length is < 2 or > 10 || !value.All(c => char.IsAsciiLetter(c) || c == '-'))
            return "es";
        return value.Split('-')[0];
    }

    private static string? PublishedValue(string? value) =>
        !string.IsNullOrWhiteSpace(value) ? value : null;

    private static string[]? PublishedArray(string[]? value) =>
        value is { Length: > 0 } && value.All(x => !string.IsNullOrWhiteSpace(x)) ? value : null;
}
