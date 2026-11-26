using System.Text.RegularExpressions;

namespace Trazo.Application.Catalog;

/// <summary>Everything that describes an exercise, whatever the way it enters the catalog.</summary>
public sealed record ExerciseMetadata(
    string Id, string NameEs, string NameEn, string DescriptionEs, string[] InstructionsEs, string[] TipsEs, string ForceType,
    string Mechanic, string Difficulty, string? Equipment, string BodyPart, double Met, bool IsUnilateral, bool IsBodyweight,
    string MovementPattern, string[] PrimaryMuscles, string[] SecondaryMuscles, string MinimumExperience,
    string[] SuitableGoals, string[] RequiredEquipment, int SelectionPriority);

/// <summary>The texts of an exercise in one language.</summary>
public sealed record ExerciseText(string Name, string Description, string[] Instructions, string[] Tips);

/// <summary>
/// The metadata with canonical spelling (trimmed, vocabulary casing, no repeated list values) and what is wrong with it,
/// keyed by field name in camelCase.
/// </summary>
public sealed record ExerciseValidation(ExerciseMetadata Metadata, Dictionary<string, string[]> Errors)
{
    public bool IsValid => Errors.Count == 0;

    public string Describe() => string.Join(" ", Errors.OrderBy(e => e.Key, StringComparer.Ordinal)
        .Select(e => $"{e.Key}: {string.Join(" ", e.Value)}"));
}

/// <summary>
/// The single definition of a valid exercise. The seed, the administration console and the library import all go through it,
/// so no route can add incomplete or unknown metadata.
/// </summary>
public static partial class ExerciseMetadataValidator
{
    public const int MinimumInstructions = 3;
    public const int MaximumListItems = 30;

    public static ExerciseValidation Validate(ExerciseMetadata input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        void Fail(string field, string message)
        {
            if (!errors.TryGetValue(field, out var list)) errors[field] = list = [];
            list.Add(message);
        }

        string Text(string field, string? value, int max)
        {
            var v = value?.Trim() ?? "";
            if (v.Length == 0) Fail(field, "Required.");
            else if (v.Length > max) Fail(field, $"At most {max} characters.");
            return v;
        }

        string[] List(string field, string[]? values, int maxLength, int minimum, bool distinct = true)
        {
            var trimmed = (values ?? []).Select(v => v?.Trim() ?? "").Where(v => v.Length > 0);
            var list = (distinct ? trimmed.Distinct(StringComparer.OrdinalIgnoreCase) : trimmed).ToArray();
            if (list.Length < minimum) Fail(field, minimum == 1 ? "Add at least one value." : $"Add at least {minimum} values.");
            else if (list.Length > MaximumListItems) Fail(field, $"At most {MaximumListItems} values.");
            else if (list.Any(v => v.Length > maxLength)) Fail(field, "A value is too long.");
            return list;
        }

        string One(string field, string? value, IReadOnlyList<string> vocabulary)
        {
            var v = value?.Trim() ?? "";
            if (v.Length == 0) { Fail(field, "Required."); return v; }
            var known = vocabulary.FirstOrDefault(item => item.Equals(v, StringComparison.OrdinalIgnoreCase));
            if (known is null) Fail(field, $"Unknown value '{v}'. Use one of: {string.Join(", ", vocabulary)}.");
            return known ?? v;
        }

        string[] Many(string field, string[]? values, IReadOnlyList<string> vocabulary, int minimum)
        {
            var list = List(field, values, 100, minimum);
            return list.Select(value => One(field, value, vocabulary)).ToArray();
        }

        var id = input.Id?.Trim() ?? "";
        if (!IdPattern().IsMatch(id)) Fail("id", "Use 2 to 100 lowercase letters, digits, hyphens or underscores.");

        var instructions = List("instructionsEs", input.InstructionsEs, 1000, MinimumInstructions, distinct: false);
        var tips = List("tipsEs", input.TipsEs, 1000, 0, distinct: false);
        var pattern = One("movementPattern", input.MovementPattern, ExerciseCatalogVocabulary.MovementPatterns);
        var primary = Many("primaryMuscles", input.PrimaryMuscles, ExerciseCatalogVocabulary.Muscles, 1);
        var secondary = Many("secondaryMuscles", input.SecondaryMuscles, ExerciseCatalogVocabulary.Muscles, 0);
        if (secondary.Intersect(primary, StringComparer.OrdinalIgnoreCase).Any())
            Fail("secondaryMuscles", "A muscle cannot be both primary and secondary.");
        var equipment = Many("requiredEquipment", input.RequiredEquipment, ExerciseCatalogVocabulary.Equipment, 0);
        if (equipment.Length == 0 && !input.IsBodyweight)
            Fail("requiredEquipment", "List the equipment, or mark the exercise as bodyweight.");
        if (pattern == "SingleLeg" && !input.IsUnilateral)
            Fail("isUnilateral", "A single-leg exercise is unilateral.");

        var displayEquipment = input.Equipment?.Trim();
        if (string.IsNullOrEmpty(displayEquipment)) displayEquipment = null;
        else if (!DisplayEquipmentPattern().IsMatch(displayEquipment))
            Fail("equipment", "Use lowercase letters, digits, hyphens or underscores.");

        if (input.Met is < 0 or > 30 || double.IsNaN(input.Met)) Fail("met", "Use a value between 0 and 30.");
        if (input.SelectionPriority is < 0 or > 100) Fail("selectionPriority", "Use a value between 0 and 100.");

        var clean = new ExerciseMetadata(
            id,
            Text("nameEs", input.NameEs, 200),
            Text("nameEn", input.NameEn, 200),
            Text("descriptionEs", input.DescriptionEs, 5000),
            instructions, tips,
            One("forceType", input.ForceType, ExerciseCatalogVocabulary.ForceTypes),
            One("mechanic", input.Mechanic, ExerciseCatalogVocabulary.Mechanics),
            One("difficulty", input.Difficulty, ExerciseCatalogVocabulary.Difficulties),
            displayEquipment,
            One("bodyPart", input.BodyPart, ExerciseCatalogVocabulary.BodyParts),
            input.Met, input.IsUnilateral, input.IsBodyweight, pattern, primary, secondary,
            One("minimumExperience", input.MinimumExperience, ExerciseCatalogVocabulary.Experiences),
            Many("suitableGoals", input.SuitableGoals, ExerciseCatalogVocabulary.Goals, 1),
            equipment, input.SelectionPriority);

        return new ExerciseValidation(clean, errors.ToDictionary(e => e.Key, e => e.Value.ToArray(), StringComparer.Ordinal));
    }

    /// <summary>What a translation lacks to be complete; empty when it can be published.</summary>
    public static IReadOnlyList<string> ValidateText(ExerciseText text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(text.Name) || text.Name.Length > 200) problems.Add("name");
        if (string.IsNullOrWhiteSpace(text.Description) || text.Description.Length > 5000) problems.Add("description");
        if (text.Instructions is not { Length: >= MinimumInstructions } || text.Instructions.Any(Invalid)) problems.Add("instructions");
        if (text.Tips is null || text.Tips.Any(Invalid)) problems.Add("tips");
        return problems;

        static bool Invalid(string value) => string.IsNullOrWhiteSpace(value) || value.Length > 1000;
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9_-]{1,99}$")]
    private static partial Regex IdPattern();

    [GeneratedRegex("^[a-z0-9_-]{1,120}$")]
    private static partial Regex DisplayEquipmentPattern();
}
