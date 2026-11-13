using Microsoft.EntityFrameworkCore;
using Trazo.Application.Planning;
using Trazo.Infrastructure.Persistence;
using Trazo.Training;

namespace Trazo.Api.Endpoints;

/// <summary>
/// Lets administrators run the planning engine against the real catalog and inspect the result.
/// Nothing is saved: it is a preview used to evaluate the engine and the catalog.
/// </summary>
public static class AdminPlanningEndpoints
{
    public static IEndpointRouteBuilder MapAdminPlanningEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/admin/planning")
            .WithTags("Admin planning")
            .RequireAuthorization(SetupEndpoints.SuperadminPolicy);
        group.MapPost("/preview", PreviewAsync);
        group.MapGet("/catalog-readiness", ReadinessAsync);
        return endpoints;
    }

    private static async Task<IResult> PreviewAsync(
        PlanPreviewRequest request, TrazoDbContext db, CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        if (!Enum.TryParse<TrainingGoal>(request.Goal, true, out var goal) || !Enum.IsDefined(goal)) errors.Add("El objetivo no es válido.");
        if (!Enum.TryParse<TrainingExperience>(request.Experience, true, out var experience) || !Enum.IsDefined(experience)) errors.Add("El nivel de experiencia no es válido.");
        var split = TrainingProgramSplit.Automatic;
        if (!string.IsNullOrWhiteSpace(request.Split) && (!Enum.TryParse(request.Split, true, out split) || !Enum.IsDefined(split)))
            errors.Add("La distribución no es válida.");
        var days = new List<DayOfWeek>();
        foreach (var name in request.TrainingDays ?? [])
        {
            if (Enum.TryParse<DayOfWeek>(name, true, out var day) && Enum.IsDefined(day)) days.Add(day);
            else errors.Add($"El día '{name}' no es válido.");
        }
        if (errors.Count > 0) return Results.BadRequest(new { errors });

        var items = await db.ExerciseCatalog.AsNoTracking().ToListAsync(cancellationToken);
        var mapping = ExerciseCandidateMapper.Map(items);
        var start = request.StartDate ?? DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var weeks = request.DurationWeeks ?? 4;

        var generation = TrainingProgramGenerator.Generate(new TrainingProgramGenerationRequest(
            goal, experience, start, weeks, days.Distinct().ToList(), request.MaxSessionMinutes ?? 60,
            request.AvailableEquipment ?? [], request.ExcludedExerciseIds ?? [],
            Split: split, IsDeload: request.IsDeload ?? false), mapping.Candidates);
        if (!generation.Succeeded) return Results.BadRequest(new { errors = generation.Errors });

        TrainingMacrocycleDraft? macrocycle = null;
        if (request.MacrocycleWeeks is { } total)
        {
            var draft = TrainingMacrocycleDraftBuilder.Build(new TrainingMacrocycleDraftRequest(
                start, total, goal, experience, request.IncludeDeloadPhases ?? false, request.DeloadEveryTrainingWeeks ?? 4));
            if (draft.Draft is null) return Results.BadRequest(new { errors = draft.Errors });
            macrocycle = draft.Draft;
        }

        return Results.Ok(new PlanPreviewResponse(
            generation.Prescription!, macrocycle, mapping.Candidates.Count, mapping.Skipped));
    }

    /// <summary>How many catalog exercises the engine can use, by pattern, level and goal.</summary>
    private static async Task<IResult> ReadinessAsync(TrazoDbContext db, CancellationToken cancellationToken)
    {
        var mapping = ExerciseCandidateMapper.Map(await db.ExerciseCatalog.AsNoTracking().ToListAsync(cancellationToken));
        return Results.Ok(new CatalogReadiness(
            mapping.Candidates.Count,
            mapping.Skipped,
            mapping.Candidates.GroupBy(c => c.MovementPattern).OrderBy(g => g.Key)
                .Select(g => new ReadinessGroup(g.Key.ToString(), g.Count())).ToList(),
            mapping.Candidates.GroupBy(c => c.MinimumExperience).OrderBy(g => g.Key)
                .Select(g => new ReadinessGroup(g.Key.ToString(), g.Count())).ToList(),
            Enum.GetValues<TrainingGoal>()
                .Select(goal => new ReadinessGroup(goal.ToString(), mapping.Candidates.Count(c => c.SuitableGoals.Contains(goal)))).ToList()));
    }
}

public sealed record PlanPreviewRequest(
    string? Goal, string? Experience, DateOnly? StartDate, int? DurationWeeks, string[]? TrainingDays,
    int? MaxSessionMinutes, string[]? AvailableEquipment, string[]? ExcludedExerciseIds, string? Split, bool? IsDeload,
    int? MacrocycleWeeks, bool? IncludeDeloadPhases, int? DeloadEveryTrainingWeeks);

public sealed record PlanPreviewResponse(
    TrainingProgramPrescription Program, TrainingMacrocycleDraft? Macrocycle, int UsableExercises,
    IReadOnlyList<SkippedExercise> Skipped);

public sealed record ReadinessGroup(string Name, int Count);

public sealed record CatalogReadiness(
    int UsableExercises, IReadOnlyList<SkippedExercise> Skipped, IReadOnlyList<ReadinessGroup> ByPattern,
    IReadOnlyList<ReadinessGroup> ByExperience, IReadOnlyList<ReadinessGroup> ByGoal);
