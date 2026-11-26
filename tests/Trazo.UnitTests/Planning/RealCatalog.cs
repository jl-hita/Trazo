using System.Text.Json;
using Trazo.Application.Planning;
using Trazo.Domain.Training;
using Trazo.Training;

namespace Trazo.UnitTests.Planning;

/// <summary>The complete reviewed exercise library (the production seed), for tests that judge plans against the real catalog.</summary>
internal static class RealCatalog
{
    private sealed class Seed
    {
        public string Id { get; init; } = ""; public string NameEs { get; init; } = ""; public string NameEn { get; init; } = "";
        public string MovementPattern { get; init; } = ""; public string[] PrimaryMuscles { get; init; } = []; public string[] SecondaryMuscles { get; init; } = [];
        public string MinimumExperience { get; init; } = ""; public string[] SuitableGoals { get; init; } = []; public string[] RequiredEquipment { get; init; } = [];
        public int SelectionPriority { get; init; } = 50; public bool IsActive { get; init; } = true;
        public string[] EffectiveSecondaryMuscles { get; init; } = []; public string[] Emphasis { get; init; } = []; public string[] LimitationAreas { get; init; } = [];
        public bool MetadataReviewed { get; init; }
        public string[]? AllowedTechniques { get; init; }
    }

    private static IReadOnlyCollection<TrainingExerciseCandidate>? cached;

    public static IReadOnlyCollection<TrainingExerciseCandidate> Candidates()
    {
        if (cached is not null) return cached;
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Trazo.Infrastructure", "Seeds", "exercise-catalog.json"))) dir = dir.Parent;
        var json = File.ReadAllText(Path.Combine(dir!.FullName, "src", "Trazo.Infrastructure", "Seeds", "exercise-catalog.json"));
        var seeds = JsonSerializer.Deserialize<List<Seed>>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var items = seeds.Select(s => new ExerciseCatalogItem
        {
            Id = s.Id, Source = "trazo-curated", NameEn = s.NameEn, NameEs = s.NameEs, DescriptionEs = "x", ForceType = "x", Mechanic = "x", Difficulty = "x",
            BodyPart = "x", MovementPattern = s.MovementPattern, MinimumExperience = s.MinimumExperience, SuitableGoals = s.SuitableGoals,
            PrimaryMuscles = s.PrimaryMuscles, SecondaryMuscles = s.SecondaryMuscles, RequiredEquipment = s.RequiredEquipment,
            SelectionPriority = s.SelectionPriority, IsActive = s.IsActive, SeedVersion = 1, EffectiveSecondaryMuscles = s.EffectiveSecondaryMuscles,
            Emphasis = s.Emphasis, LimitationAreas = s.LimitationAreas, MetadataReviewed = s.MetadataReviewed, AllowedTechniques = s.AllowedTechniques ?? []
        }).Where(i => i.IsActive).ToList();
        return cached = ExerciseCandidateMapper.Map(items).Candidates;
    }
}
