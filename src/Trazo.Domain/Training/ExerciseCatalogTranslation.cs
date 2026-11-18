namespace Trazo.Domain.Training;

/// <summary>
/// User-facing text for one exercise and one BCP-47 language tag.
/// Biomechanical and planning metadata deliberately remains on ExerciseCatalogItem.
/// </summary>
public sealed class ExerciseCatalogTranslation
{
    public required string ExerciseId { get; set; }
    public required string LanguageCode { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string[]? Instructions { get; set; }
    public string[]? Tips { get; set; }
    /// <summary>draft, review, or published. Only published translations are eligible for normal client delivery.</summary>
    public string Status { get; set; } = "draft";
    public DateTimeOffset UpdatedAt { get; set; }
}
