using Microsoft.EntityFrameworkCore;
using Trazo.Domain.Training;
using Trazo.Infrastructure.Persistence;

namespace Trazo.Api.Endpoints;

public static class ExerciseCatalogEndpoints
{
    public static IEndpointRouteBuilder MapExerciseCatalogEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/exercises")
            .WithTags("Exercise catalog");

        group.MapGet("", SearchAsync)
            .WithName("SearchExercises")
            .WithSummary("Search and filter the exercise catalog.");

        group.MapGet("/{id}", GetByIdAsync)
            .WithName("GetExerciseById")
            .WithSummary("Get the complete details of one exercise.");

        return endpoints;
    }

    private static async Task<IResult> SearchAsync(
        TrazoDbContext db,
        string? q,
        string? movementPattern,
        string? difficulty,
        string? experience,
        string? equipment,
        string? muscle,
        int page = 1,
        int pageSize = 30,
        CancellationToken cancellationToken = default)
    {
        if (page < 1 || pageSize is < 1 or > 100 ||
            (q?.Length > 100) ||
            (movementPattern?.Length > 80) ||
            (difficulty?.Length > 80) ||
            (experience?.Length > 40) ||
            (equipment?.Length > 120) ||
            (muscle?.Length > 80))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["query"] = ["Invalid filter length or pagination. page must be positive and pageSize must be between 1 and 100."]
            });
        }

        IQueryable<ExerciseCatalogItem> query = db.ExerciseCatalog.AsNoTracking().Where(x => x.IsActive);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = $"%{q.Trim()}%";
            query = query.Where(x =>
                EF.Functions.ILike(x.NameEs, pattern) ||
                EF.Functions.ILike(x.NameEn, pattern) ||
                EF.Functions.ILike(x.DescriptionEs, pattern));
        }

        if (!string.IsNullOrWhiteSpace(movementPattern))
            query = query.Where(x => x.MovementPattern == movementPattern.Trim());

        if (!string.IsNullOrWhiteSpace(difficulty))
            query = query.Where(x => x.Difficulty == difficulty.Trim());

        if (!string.IsNullOrWhiteSpace(experience))
            query = query.Where(x => x.MinimumExperience == experience.Trim());

        if (!string.IsNullOrWhiteSpace(equipment))
        {
            var selectedEquipment = equipment.Trim();
            query = query.Where(x =>
                x.Equipment == selectedEquipment ||
                x.RequiredEquipment.Contains(selectedEquipment));
        }

        if (!string.IsNullOrWhiteSpace(muscle))
        {
            var selectedMuscle = muscle.Trim();
            query = query.Where(x =>
                x.PrimaryMuscles.Contains(selectedMuscle) ||
                x.SecondaryMuscles.Contains(selectedMuscle));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(x => x.SelectionPriority)
            .ThenBy(x => x.NameEs)
            .ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new ExerciseCatalogListItem(
                x.Id,
                x.NameEs,
                x.NameEn,
                x.DescriptionEs,
                x.Difficulty,
                x.Equipment,
                x.RequiredEquipment,
                x.MovementPattern,
                x.PrimaryMuscles,
                x.SecondaryMuscles,
                x.MinimumExperience,
                x.IsUnilateral,
                x.IsBodyweight,
                x.SelectionPriority))
            .ToListAsync(cancellationToken);

        return Results.Ok(new ExerciseCatalogPage(
            items,
            page,
            pageSize,
            totalCount,
            (int)Math.Ceiling(totalCount / (double)pageSize)));
    }

    private static async Task<IResult> GetByIdAsync(
        string id,
        TrazoDbContext db,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 100)
            return Results.BadRequest(new { error = "A valid exercise ID is required." });

        var exercise = await db.ExerciseCatalog
            .AsNoTracking()
            .Where(x => x.Id == id && x.IsActive)
            .Select(x => new ExerciseCatalogDetail(
                x.Id,
                x.Source,
                x.NameEs,
                x.NameEn,
                x.DescriptionEs,
                x.InstructionsEs,
                x.TipsEs,
                x.ForceType,
                x.Mechanic,
                x.Difficulty,
                x.Equipment,
                x.RequiredEquipment,
                x.BodyPart,
                x.Met,
                x.IsUnilateral,
                x.IsBodyweight,
                x.MovementPattern,
                x.PrimaryMuscles,
                x.SecondaryMuscles,
                x.MinimumExperience,
                x.SuitableGoals,
                x.SelectionPriority))
            .SingleOrDefaultAsync(cancellationToken);

        return exercise is null ? Results.NotFound() : Results.Ok(exercise);
    }
}

public sealed record ExerciseCatalogPage(
    IReadOnlyList<ExerciseCatalogListItem> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public sealed record ExerciseCatalogListItem(
    string Id,
    string NameEs,
    string NameEn,
    string DescriptionEs,
    string Difficulty,
    string? Equipment,
    string[] RequiredEquipment,
    string MovementPattern,
    string[] PrimaryMuscles,
    string[] SecondaryMuscles,
    string MinimumExperience,
    bool IsUnilateral,
    bool IsBodyweight,
    int SelectionPriority);

public sealed record ExerciseCatalogDetail(
    string Id,
    string Source,
    string NameEs,
    string NameEn,
    string DescriptionEs,
    string[] InstructionsEs,
    string[] TipsEs,
    string ForceType,
    string Mechanic,
    string Difficulty,
    string? Equipment,
    string[] RequiredEquipment,
    string BodyPart,
    double Met,
    bool IsUnilateral,
    bool IsBodyweight,
    string MovementPattern,
    string[] PrimaryMuscles,
    string[] SecondaryMuscles,
    string MinimumExperience,
    string[] SuitableGoals,
    int SelectionPriority);
