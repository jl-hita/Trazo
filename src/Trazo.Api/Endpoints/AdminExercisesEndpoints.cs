using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Trazo.Domain.Training;
using Trazo.Infrastructure.Persistence;
using Trazo.Infrastructure.Seeding;

namespace Trazo.Api.Endpoints;

/// <summary>
/// Exercise library management for the administration console (superadmin only).
/// Editing an imported exercise marks it as locally curated, so later DietoExpress imports keep the edit.
/// </summary>
public static partial class AdminExercisesEndpoints
{
    private const int MaxListItems = 30;
    private const int MaxItemLength = 100;

    public static IEndpointRouteBuilder MapAdminExercisesEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/admin/exercises")
            .WithTags("Admin exercises")
            .RequireAuthorization(SetupEndpoints.SuperadminPolicy);

        group.MapGet("", ListAsync);
        group.MapGet("/options", OptionsAsync);
        group.MapGet("/{id}", GetAsync);
        group.MapPost("", CreateAsync);
        group.MapPut("/{id}", UpdateAsync);
        group.MapPost("/{id}/activate", (string id, ClaimsPrincipal p, TrazoDbContext db, ILoggerFactory l, CancellationToken ct) => SetActiveAsync(id, true, p, db, l, ct));
        group.MapPost("/{id}/deactivate", (string id, ClaimsPrincipal p, TrazoDbContext db, ILoggerFactory l, CancellationToken ct) => SetActiveAsync(id, false, p, db, l, ct));
        group.MapPost("/{id}/release-curation", ReleaseCurationAsync);
        group.MapPut("/{id}/images/{slot:int}", UploadImageAsync);
        group.MapDelete("/{id}/images/{slot:int}", DeleteImageAsync);
        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        TrazoDbContext db,
        string? q, string? movementPattern, string? difficulty, string? experience, string? equipment,
        string? muscle, string? source, string? status, string? sort,
        int page = 1, int pageSize = 25, CancellationToken cancellationToken = default)
    {
        var errors = new Dictionary<string, string[]>();
        if (page < 1 || pageSize is < 1 or > 100) errors["paging"] = ["page must be positive and pageSize between 1 and 100."];
        if (q?.Length > 100 || movementPattern?.Length > 80 || difficulty?.Length > 80 || experience?.Length > 40 ||
            equipment?.Length > 120 || muscle?.Length > 80 || source?.Length > 40)
            errors["filters"] = ["A filter value is too long."];
        if (!string.IsNullOrWhiteSpace(status) && status is not ("active" or "inactive"))
            errors["status"] = ["Use active or inactive."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        IQueryable<ExerciseCatalogItem> query = db.ExerciseCatalog.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var lower = q.Trim().ToLowerInvariant();
            query = query.Where(x => x.NameEs.ToLower().Contains(lower) || x.NameEn.ToLower().Contains(lower) || x.Id.Contains(lower));
        }
        if (!string.IsNullOrWhiteSpace(movementPattern)) query = query.Where(x => x.MovementPattern == movementPattern);
        if (!string.IsNullOrWhiteSpace(difficulty)) query = query.Where(x => x.Difficulty == difficulty);
        if (!string.IsNullOrWhiteSpace(experience)) query = query.Where(x => x.MinimumExperience == experience);
        if (!string.IsNullOrWhiteSpace(source)) query = query.Where(x => x.Source == source);
        if (!string.IsNullOrWhiteSpace(equipment))
            query = query.Where(x => x.Equipment == equipment || x.RequiredEquipment.Contains(equipment));
        if (!string.IsNullOrWhiteSpace(muscle))
            query = query.Where(x => x.PrimaryMuscles.Contains(muscle) || x.SecondaryMuscles.Contains(muscle));
        query = status switch
        {
            "active" => query.Where(x => x.IsActive),
            "inactive" => query.Where(x => !x.IsActive),
            _ => query
        };

        var totalCount = await query.CountAsync(cancellationToken);
        query = sort switch
        {
            "priority" => query.OrderByDescending(x => x.SelectionPriority).ThenBy(x => x.NameEs).ThenBy(x => x.Id),
            "updated" => query.OrderByDescending(x => x.UpdatedAt).ThenBy(x => x.Id),
            _ => query.OrderBy(x => x.NameEs).ThenBy(x => x.Id)
        };
        var rows = await query.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new
            {
                x.Id, x.Source, x.NameEs, x.NameEn, x.Difficulty, x.Equipment, x.MovementPattern, x.MinimumExperience,
                x.PrimaryMuscles, x.SecondaryMuscles, x.SelectionPriority, x.IsActive
            })
            .ToListAsync(cancellationToken);

        var curated = (await db.Database
            .SqlQuery<string>($"SELECT exercise_id AS \"Value\" FROM exercise_catalog_import_metadata WHERE is_locally_curated = {true}")
            .ToListAsync(cancellationToken)).ToHashSet();
        var imageCounts = (await db.Database
            .SqlQuery<ImageCountRow>($"SELECT exercise_id AS \"ExerciseId\", CAST(COUNT(*) AS INTEGER) AS \"Count\" FROM exercise_catalog_images GROUP BY exercise_id")
            .ToListAsync(cancellationToken)).ToDictionary(r => r.ExerciseId, r => r.Count);

        var items = rows.Select(r => new AdminExerciseSummary(
            r.Id, r.Source, r.NameEs, r.NameEn, r.Difficulty, r.Equipment, r.MovementPattern, r.MinimumExperience,
            r.PrimaryMuscles, r.SecondaryMuscles, r.SelectionPriority, r.IsActive,
            curated.Contains(r.Id), imageCounts.GetValueOrDefault(r.Id))).ToList();
        return Results.Ok(new AdminExercisePage(items, page, pageSize, totalCount, (int)Math.Ceiling(totalCount / (double)pageSize)));
    }

    /// <summary>Values already used in the catalog, offered as suggestions in the editor.</summary>
    private static async Task<IResult> OptionsAsync(TrazoDbContext db, CancellationToken cancellationToken)
    {
        var rows = await db.ExerciseCatalog.AsNoTracking().Select(x => new
        {
            x.MovementPattern, x.Difficulty, x.MinimumExperience, x.Equipment, x.ForceType, x.Mechanic, x.BodyPart, x.Source,
            x.PrimaryMuscles, x.SecondaryMuscles, x.SuitableGoals, x.RequiredEquipment
        }).ToListAsync(cancellationToken);

        static string[] Distinct(IEnumerable<string?> values) =>
            values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!).Distinct().Order().ToArray();

        return Results.Ok(new AdminExerciseOptions(
            Distinct(rows.Select(r => r.MovementPattern)),
            Distinct(rows.Select(r => r.Difficulty)),
            Distinct(rows.Select(r => r.MinimumExperience)),
            Distinct(rows.Select(r => r.Equipment).Concat(rows.SelectMany(r => r.RequiredEquipment))),
            Distinct(rows.Select(r => r.ForceType)),
            Distinct(rows.Select(r => r.Mechanic)),
            Distinct(rows.Select(r => r.BodyPart)),
            Distinct(rows.Select(r => r.Source)),
            Distinct(rows.SelectMany(r => r.PrimaryMuscles).Concat(rows.SelectMany(r => r.SecondaryMuscles))),
            Distinct(rows.SelectMany(r => r.SuitableGoals))));
    }

    private static async Task<IResult> GetAsync(string id, TrazoDbContext db, CancellationToken cancellationToken)
    {
        var detail = await BuildDetailAsync(db, id, cancellationToken);
        return detail is null ? Results.NotFound() : Results.Ok(detail);
    }

    private static async Task<IResult> CreateAsync(
        AdminExerciseInput input, ClaimsPrincipal principal, TrazoDbContext db, ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        var (clean, errors) = Validate(input);
        var id = string.IsNullOrWhiteSpace(input.Id) ? Slugify(clean.NameEn) : input.Id.Trim().ToLowerInvariant();
        if (!IdPattern().IsMatch(id)) errors["id"] = ["Use 2 to 100 lowercase letters, digits, hyphens or underscores."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);
        if (await db.ExerciseCatalog.AnyAsync(x => x.Id == id, cancellationToken))
            return Results.Conflict(new { error = $"An exercise with the id '{id}' already exists." });

        var now = DateTimeOffset.UtcNow;
        var item = new ExerciseCatalogItem
        {
            Id = id, Source = "trazo", NameEn = clean.NameEn, NameEs = clean.NameEs, DescriptionEs = clean.DescriptionEs,
            ForceType = clean.ForceType, Mechanic = clean.Mechanic, Difficulty = clean.Difficulty, BodyPart = clean.BodyPart,
            MovementPattern = clean.MovementPattern, MinimumExperience = clean.MinimumExperience, SeedVersion = 0,
            CreatedAt = now, UpdatedAt = now, IsActive = true
        };
        Apply(item, clean);
        db.ExerciseCatalog.Add(item);
        await db.SaveChangesAsync(cancellationToken);
        Log(loggerFactory, principal, "created", id);
        return Results.Created($"/api/v1/admin/exercises/{id}", await BuildDetailAsync(db, id, cancellationToken));
    }

    private static async Task<IResult> UpdateAsync(
        string id, AdminExerciseInput input, ClaimsPrincipal principal, TrazoDbContext db, ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var item = await db.ExerciseCatalog.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (item is null) return Results.NotFound();
        var (clean, errors) = Validate(input);
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        item.NameEn = clean.NameEn; item.NameEs = clean.NameEs; item.DescriptionEs = clean.DescriptionEs;
        item.ForceType = clean.ForceType; item.Mechanic = clean.Mechanic; item.Difficulty = clean.Difficulty;
        item.BodyPart = clean.BodyPart; item.MovementPattern = clean.MovementPattern; item.MinimumExperience = clean.MinimumExperience;
        Apply(item, clean);
        item.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        // Keep the edit through future DietoExpress imports.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE exercise_catalog_import_metadata SET is_locally_curated = {true} WHERE exercise_id = {id}", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        Log(loggerFactory, principal, "edited", id);
        return Results.Ok(await BuildDetailAsync(db, id, cancellationToken));
    }

    private static async Task<IResult> SetActiveAsync(
        string id, bool active, ClaimsPrincipal principal, TrazoDbContext db, ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        var item = await db.ExerciseCatalog.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (item is null) return Results.NotFound();
        item.IsActive = active;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        Log(loggerFactory, principal, active ? "activated" : "deactivated", id);
        return Results.Ok(await BuildDetailAsync(db, id, cancellationToken));
    }

    /// <summary>Lets the next DietoExpress import overwrite this exercise again.</summary>
    private static async Task<IResult> ReleaseCurationAsync(
        string id, ClaimsPrincipal principal, TrazoDbContext db, ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        if (!await db.ExerciseCatalog.AnyAsync(x => x.Id == id, cancellationToken)) return Results.NotFound();
        var changed = await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE exercise_catalog_import_metadata SET is_locally_curated = {false} WHERE exercise_id = {id}", cancellationToken);
        if (changed == 0) return Results.Conflict(new { error = "This exercise was not imported, so there is nothing to release." });
        Log(loggerFactory, principal, "released the local curation of", id);
        return Results.Ok(await BuildDetailAsync(db, id, cancellationToken));
    }

    // ---- images -------------------------------------------------------------------------------

    /// <summary>Stores the request body as the image of a slot. An upload replaces an imported image and protects it from imports.</summary>
    private static async Task<IResult> UploadImageAsync(
        string id, int slot, HttpRequest request, ClaimsPrincipal principal, TrazoDbContext db, ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (slot is < 0 or > ExerciseImageRules.MaximumSlot)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["slot"] = [$"Use a slot from 0 to {ExerciseImageRules.MaximumSlot}."] });
        if (!await db.ExerciseCatalog.AnyAsync(x => x.Id == id, cancellationToken)) return Results.NotFound();
        if (request.ContentLength > ExerciseImageRules.MaximumBytes) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await request.Body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > ExerciseImageRules.MaximumBytes) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            buffer.Write(chunk, 0, read);
        }
        var data = buffer.ToArray();
        if (!ExerciseImageRules.TryDetectContentType(data, out var contentType))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["image"] = ["Upload a WebP, PNG or JPEG image."] });

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO exercise_catalog_images (exercise_id, slot, content_type, data, sha256, source, origin_system, imported_at)
            VALUES ({id}, {slot}, {contentType}, {data}, {ExerciseImageRules.Sha256(data)}, {"trazo-upload"}, {ExerciseImageSeeder.OriginSystem}, {DateTimeOffset.UtcNow})
            ON CONFLICT (exercise_id, slot) DO UPDATE SET content_type = excluded.content_type, data = excluded.data,
                sha256 = excluded.sha256, source = excluded.source, origin_system = excluded.origin_system, imported_at = excluded.imported_at
            """, cancellationToken);
        Log(loggerFactory, principal, $"uploaded image {slot} of", id);
        return Results.Ok(await BuildDetailAsync(db, id, cancellationToken));
    }

    private static async Task<IResult> DeleteImageAsync(
        string id, int slot, ClaimsPrincipal principal, TrazoDbContext db, ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        if (!await db.ExerciseCatalog.AnyAsync(x => x.Id == id, cancellationToken)) return Results.NotFound();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM exercise_catalog_images WHERE exercise_id = {id} AND slot = {slot}", cancellationToken);
        Log(loggerFactory, principal, $"removed image {slot} of", id);
        return Results.Ok(await BuildDetailAsync(db, id, cancellationToken));
    }

    // ---- validation ---------------------------------------------------------------------------

    private sealed record Clean(
        string NameEs, string NameEn, string DescriptionEs, string[] InstructionsEs, string[] TipsEs, string ForceType,
        string Mechanic, string Difficulty, string? Equipment, string BodyPart, double Met, bool IsUnilateral, bool IsBodyweight,
        string MovementPattern, string[] PrimaryMuscles, string[] SecondaryMuscles, string MinimumExperience,
        string[] SuitableGoals, string[] RequiredEquipment, int SelectionPriority);

    private static (Clean Clean, Dictionary<string, string[]> Errors) Validate(AdminExerciseInput i)
    {
        var errors = new Dictionary<string, string[]>();
        string Required(string field, string? value, int max)
        {
            var v = value?.Trim() ?? "";
            if (v.Length == 0) errors[field] = ["Required."];
            else if (v.Length > max) errors[field] = [$"At most {max} characters."];
            return v;
        }
        string[] List(string field, string[]? values, bool required)
        {
            var list = (values ?? []).Select(v => v?.Trim() ?? "").Where(v => v.Length > 0).Distinct().ToArray();
            if (required && list.Length == 0) errors[field] = ["Add at least one value."];
            else if (list.Length > MaxListItems) errors[field] = [$"At most {MaxListItems} values."];
            else if (list.Any(v => v.Length > (field is "instructionsEs" or "tipsEs" ? 1000 : MaxItemLength)))
                errors[field] = ["A value is too long."];
            return list;
        }
        string Optional(string field, string? value, int max, string fallback)
        {
            var v = value?.Trim();
            if (string.IsNullOrEmpty(v)) return fallback;
            if (v.Length > max) errors[field] = [$"At most {max} characters."];
            return v;
        }

        var nameEs = Required("nameEs", i.NameEs, 200);
        var nameEn = Required("nameEn", i.NameEn, 200);
        var description = Required("descriptionEs", i.DescriptionEs, 5000);
        var pattern = Required("movementPattern", i.MovementPattern, 80);
        var difficulty = Required("difficulty", i.Difficulty, 80);
        var experience = Required("minimumExperience", i.MinimumExperience, 40);
        var instructions = List("instructionsEs", i.InstructionsEs, required: true);
        var tips = List("tipsEs", i.TipsEs, required: false);
        var primary = List("primaryMuscles", i.PrimaryMuscles, required: true);
        var secondary = List("secondaryMuscles", i.SecondaryMuscles, required: false);
        var goals = List("suitableGoals", i.SuitableGoals, required: true);
        var equipmentList = List("requiredEquipment", i.RequiredEquipment, required: false);
        var forceType = Optional("forceType", i.ForceType, 80, "unknown");
        var mechanic = Optional("mechanic", i.Mechanic, 80, "unknown");
        var bodyPart = Optional("bodyPart", i.BodyPart, 100, "unknown");
        var equipment = Optional("equipment", i.Equipment, 120, "");
        var met = i.Met ?? 0;
        if (met is < 0 or > 30) errors["met"] = ["Use a value between 0 and 30."];
        var priority = i.SelectionPriority ?? 50;
        if (priority is < 0 or > 100) errors["selectionPriority"] = ["Use a value between 0 and 100."];

        return (new Clean(nameEs, nameEn, description, instructions, tips, forceType, mechanic, difficulty,
            equipment.Length == 0 ? null : equipment, bodyPart, met, i.IsUnilateral ?? false, i.IsBodyweight ?? false,
            pattern, primary, secondary, experience, goals, equipmentList, priority), errors);
    }

    private static void Apply(ExerciseCatalogItem item, Clean c)
    {
        item.InstructionsEs = c.InstructionsEs; item.TipsEs = c.TipsEs; item.Equipment = c.Equipment; item.Met = c.Met;
        item.IsUnilateral = c.IsUnilateral; item.IsBodyweight = c.IsBodyweight; item.PrimaryMuscles = c.PrimaryMuscles;
        item.SecondaryMuscles = c.SecondaryMuscles; item.SuitableGoals = c.SuitableGoals;
        item.RequiredEquipment = c.RequiredEquipment; item.SelectionPriority = c.SelectionPriority;
    }

    private static string Slugify(string name)
    {
        var decomposed = name.Trim().ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        var plain = new System.Text.StringBuilder();
        foreach (var ch in decomposed)
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark)
                plain.Append(ch);
        var slug = SlugCleanup().Replace(plain.ToString(), "-").Trim('-');
        return slug.Length > 100 ? slug[..100].Trim('-') : slug;
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9_-]{1,99}$")]
    private static partial Regex IdPattern();

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex SlugCleanup();

    // ---- detail -------------------------------------------------------------------------------

    private static async Task<AdminExerciseDetail?> BuildDetailAsync(TrazoDbContext db, string id, CancellationToken cancellationToken)
    {
        var x = await db.ExerciseCatalog.AsNoTracking().SingleOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (x is null) return null;
        var meta = (await db.Database
            .SqlQuery<MetadataRow>($"SELECT source_system AS \"SourceSystem\", is_locally_curated AS \"IsLocallyCurated\" FROM exercise_catalog_import_metadata WHERE exercise_id = {id}")
            .ToListAsync(cancellationToken)).FirstOrDefault();
        var slots = await db.Database
            .SqlQuery<int>($"SELECT CAST(slot AS INTEGER) AS \"Value\" FROM exercise_catalog_images WHERE exercise_id = {id} ORDER BY slot")
            .ToListAsync(cancellationToken);
        return new AdminExerciseDetail(
            x.Id, x.Source, x.NameEs, x.NameEn, x.DescriptionEs, x.InstructionsEs, x.TipsEs, x.ForceType, x.Mechanic,
            x.Difficulty, x.Equipment, x.BodyPart, x.Met, x.IsUnilateral, x.IsBodyweight, x.MovementPattern,
            x.PrimaryMuscles, x.SecondaryMuscles, x.MinimumExperience, x.SuitableGoals, x.RequiredEquipment,
            x.SelectionPriority, x.IsActive, meta?.IsLocallyCurated ?? false, meta?.SourceSystem, slots, x.CreatedAt, x.UpdatedAt);
    }

    private static void Log(ILoggerFactory loggerFactory, ClaimsPrincipal principal, string action, string exerciseId) =>
        loggerFactory.CreateLogger("Trazo.Admin").LogInformation("Superadmin {Actor} {Action} exercise {Exercise}",
            principal.FindFirstValue("sub") ?? principal.FindFirstValue(ClaimTypes.NameIdentifier), action, exerciseId);

    private sealed record ImageCountRow(string ExerciseId, int Count);
    private sealed record MetadataRow(string SourceSystem, bool IsLocallyCurated);
}

public sealed record AdminExerciseSummary(
    string Id, string Source, string NameEs, string NameEn, string Difficulty, string? Equipment, string MovementPattern,
    string MinimumExperience, string[] PrimaryMuscles, string[] SecondaryMuscles, int SelectionPriority, bool IsActive,
    bool LocallyCurated, int ImageCount);

public sealed record AdminExercisePage(IReadOnlyList<AdminExerciseSummary> Items, int Page, int PageSize, int TotalCount, int TotalPages);

public sealed record AdminExerciseDetail(
    string Id, string Source, string NameEs, string NameEn, string DescriptionEs, string[] InstructionsEs, string[] TipsEs,
    string ForceType, string Mechanic, string Difficulty, string? Equipment, string BodyPart, double Met, bool IsUnilateral,
    bool IsBodyweight, string MovementPattern, string[] PrimaryMuscles, string[] SecondaryMuscles, string MinimumExperience,
    string[] SuitableGoals, string[] RequiredEquipment, int SelectionPriority, bool IsActive, bool LocallyCurated,
    string? ImportedFrom, IReadOnlyList<int> ImageSlots, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record AdminExerciseInput(
    string? Id, string? NameEs, string? NameEn, string? DescriptionEs, string[]? InstructionsEs, string[]? TipsEs,
    string? ForceType, string? Mechanic, string? Difficulty, string? Equipment, string? BodyPart, double? Met,
    bool? IsUnilateral, bool? IsBodyweight, string? MovementPattern, string[]? PrimaryMuscles, string[]? SecondaryMuscles,
    string? MinimumExperience, string[]? SuitableGoals, string[]? RequiredEquipment, int? SelectionPriority);

public sealed record AdminExerciseOptions(
    string[] MovementPatterns, string[] Difficulties, string[] Experiences, string[] Equipment, string[] ForceTypes,
    string[] Mechanics, string[] BodyParts, string[] Sources, string[] Muscles, string[] Goals);
