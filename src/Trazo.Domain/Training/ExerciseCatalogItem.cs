namespace Trazo.Domain.Training;

/// <summary>
/// Exercise catalog entry. Seeded entries are inserted only when their stable ID is absent,
/// so subsequent application starts never overwrite administrator curation.
/// </summary>
public sealed class ExerciseCatalogItem
{
    public required string Id { get; set; }
    public required string Source { get; set; }
    public required string NameEn { get; set; }
    public required string NameEs { get; set; }
    public required string DescriptionEs { get; set; }
    public string[] InstructionsEs { get; set; } = [];
    public string[] TipsEs { get; set; } = [];
    public required string ForceType { get; set; }
    public required string Mechanic { get; set; }
    public required string Difficulty { get; set; }
    public string? Equipment { get; set; }
    public required string BodyPart { get; set; }
    public double Met { get; set; }
    public bool IsUnilateral { get; set; }
    public bool IsBodyweight { get; set; }
    public required string MovementPattern { get; set; }
    public string[] PrimaryMuscles { get; set; } = [];
    public string[] SecondaryMuscles { get; set; } = [];
    public required string MinimumExperience { get; set; }
    public string[] SuitableGoals { get; set; } = [];
    public string[] RequiredEquipment { get; set; } = [];
    public int SelectionPriority { get; set; }
    public int SeedVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
