using Trazo.Api.Support;
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

        group.MapGet("/all", AllAsync)
            .WithName("GetAllExercises")
            .WithSummary("Get every active exercise with its complete metadata, including required equipment.");

        group.MapGet("/options", OptionsAsync)
            .WithName("GetExerciseFilterOptions")
            .WithSummary("Vocabulary of the catalog filters (movement patterns, muscles, equipment, levels) with Spanish labels and counts.")
            .Produces<ExerciseFilterOptions>();

        group.MapGet("/{id}/localized", GetLocalizedByIdAsync)
            .WithName("GetLocalizedExerciseById")
            .WithSummary("Get exercise text resolved for the request language, with field-level Spanish fallback.");

        group.MapGet("/{id}", GetByIdAsync)
            .WithName("GetExerciseById")
            .WithSummary("Get the complete details of one exercise.");

        return endpoints;
    }

    private static async Task<IResult> AllAsync(TrazoDbContext db, CancellationToken cancellationToken)
    {
        var exercises = await db.ExerciseCatalog.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.NameEs).ThenBy(x => x.Id)
            .Select(x => new ExerciseCatalogDetail(
                x.Id, x.Source, x.NameEs, x.NameEn, x.DescriptionEs, x.InstructionsEs, x.TipsEs,
                x.ForceType, x.Mechanic, x.Difficulty, x.Equipment, x.RequiredEquipment, x.BodyPart,
                x.Met, x.IsUnilateral, x.IsBodyweight, x.MovementPattern, x.PrimaryMuscles,
                x.SecondaryMuscles, x.MinimumExperience, x.SuitableGoals, x.SelectionPriority))
            .ToListAsync(cancellationToken);

        return Results.Ok(exercises);
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

    private static async Task<IResult> OptionsAsync(TrazoDbContext db, HttpContext http, CancellationToken cancellationToken)
    {
        var rows = await db.ExerciseCatalog.AsNoTracking().Where(x => x.IsActive)
            .Select(x => new { x.MovementPattern, x.PrimaryMuscles, x.SecondaryMuscles, x.RequiredEquipment, x.MinimumExperience, x.Difficulty })
            .ToListAsync(cancellationToken);

        static IReadOnlyList<FilterOption> Build(IEnumerable<string?> values, IReadOnlyDictionary<string, string> labels) =>
            values.Where(v => !string.IsNullOrWhiteSpace(v) && !string.Equals(v.Trim(), "machine", StringComparison.OrdinalIgnoreCase)).Select(v => v!.Trim()).GroupBy(v => v, StringComparer.Ordinal)
                .Select(g => new FilterOption(g.Key, ExerciseVocabulary.Label(labels, g.Key), g.Count()))
                .OrderBy(o => o.Label, StringComparer.CurrentCultureIgnoreCase).ThenBy(o => o.Key, StringComparer.Ordinal).ToList();

        // The labels follow the language of the request (the keys never do).
        var english = http.RequestServices.GetRequiredService<Trazo.Application.Localization.RequestLanguage>().Current == Trazo.Application.Localization.AppLanguage.En;
        var options = new ExerciseFilterOptions(
            Build(rows.Select(r => r.MovementPattern), english ? ExerciseVocabulary.PatternsEn : ExerciseVocabulary.Patterns),
            Build(rows.SelectMany(r => r.PrimaryMuscles.Concat(r.SecondaryMuscles).Distinct()), english ? ExerciseVocabulary.MusclesEn : ExerciseVocabulary.Muscles),
            Build(rows.SelectMany(r => r.RequiredEquipment).Distinct(), english ? ExerciseVocabulary.EquipmentEn : ExerciseVocabulary.Equipment),
            Build(rows.Select(r => r.MinimumExperience), english ? ExerciseVocabulary.LevelsEn : ExerciseVocabulary.Levels),
            Build(rows.Select(r => r.Difficulty), english ? ExerciseVocabulary.LevelsEn : ExerciseVocabulary.Levels),
            new ExerciseFilterParameters("movementPattern", "muscle", "equipment", "experience", "difficulty"));

        // Cacheable: the answer only changes when the catalog does, so clients revalidate with the ETag.
        var etag = "\"" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(options)))[..24] + "\"";
        http.Response.Headers.CacheControl = "public, max-age=300";
        http.Response.Headers.Vary = "Accept-Language";
        http.Response.Headers.ETag = etag;
        if (http.Request.Headers.IfNoneMatch.ToString() == etag) return Results.StatusCode(StatusCodes.Status304NotModified);
        return Results.Ok(options);
    }

    private static async Task<IResult> GetLocalizedByIdAsync(
        string id,
        TrazoDbContext db,
        Trazo.Application.Localization.RequestLanguage requestLanguage,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 100)
            return ApiErrors.BadRequest(new { error = "A valid exercise ID is required." });

        var exercise = await db.ExerciseCatalog.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id && x.IsActive, cancellationToken);
        if (exercise is null) return Results.NotFound();

        var translations = await db.ExerciseCatalogTranslations.AsNoTracking()
            .Where(x => x.ExerciseId == id)
            .ToListAsync(cancellationToken);

        var language = requestLanguage.Current == Trazo.Application.Localization.AppLanguage.En ? "en" : "es";
        var resolved = Trazo.Application.Localization.ExerciseCatalogTranslationResolver.Resolve(
            language, translations, exercise.NameEs, exercise.NameEn, exercise.DescriptionEs,
            exercise.InstructionsEs, exercise.TipsEs);

        return Results.Ok(resolved);
    }

    private static async Task<IResult> GetByIdAsync(
        string id,
        TrazoDbContext db,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 100)
            return ApiErrors.BadRequest(new { error = "A valid exercise ID is required." });

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

public sealed record FilterOption(string Key, string Label, int Count);

/// <summary>Query-string names of GET /api/v1/exercises that each list feeds.</summary>
public sealed record ExerciseFilterParameters(string MovementPattern, string Muscle, string Equipment, string Experience, string Difficulty);

/// <summary>
/// Keys are the values stored in the catalog (stable, accepted as they are by the list filters); labels are Spanish or English, by the language of the request. A value
/// without a known label gets a readable fallback, so a new catalog value never breaks the screen.
/// </summary>
public sealed record ExerciseFilterOptions(
    IReadOnlyList<FilterOption> MovementPatterns, IReadOnlyList<FilterOption> Muscles, IReadOnlyList<FilterOption> Equipment,
    IReadOnlyList<FilterOption> ExperienceLevels, IReadOnlyList<FilterOption> Difficulties, ExerciseFilterParameters Parameters);

public static class ExerciseVocabulary
{
    public static string Label(IReadOnlyDictionary<string, string> labels, string key) =>
        labels.TryGetValue(key, out var label) ? label : Humanize(key);

    private static string Humanize(string key)
    {
        var spaced = System.Text.RegularExpressions.Regex.Replace(key.Replace('-', ' ').Replace('_', ' '), "(?<=[a-z])(?=[A-Z])", " ").Trim().ToLowerInvariant();
        return spaced.Length == 0 ? key : char.ToUpperInvariant(spaced[0]) + spaced[1..];
    }

    private static IReadOnlyDictionary<string, string> Map(params (string, string)[] pairs) =>
        pairs.ToDictionary(p => p.Item1, p => p.Item2, StringComparer.OrdinalIgnoreCase);

    public static readonly IReadOnlyDictionary<string, string> Patterns = Map(
        ("Squat", "Sentadilla"), ("Hinge", "Bisagra de cadera"), ("HorizontalPush", "Empuje horizontal"), ("VerticalPush", "Empuje vertical"),
        ("HorizontalPull", "Tirón horizontal"), ("VerticalPull", "Tirón vertical"), ("SingleLeg", "Unilateral de pierna"),
        ("Core", "Core"), ("Accessory", "Accesorio"));

    public static readonly IReadOnlyDictionary<string, string> Muscles = Map(
        ("Chest", "Pecho"), ("Lats", "Dorsales"), ("UpperBack", "Espalda alta"), ("Deltoids", "Hombros"), ("Biceps", "Bíceps"),
        ("Triceps", "Tríceps"), ("Forearms", "Antebrazos"), ("Abdominals", "Abdominales"), ("Obliques", "Oblicuos"),
        ("ErectorSpinae", "Lumbares"), ("Glutes", "Glúteos"), ("Quadriceps", "Cuádriceps"), ("Hamstrings", "Isquiotibiales"),
        ("Calves", "Gemelos"), ("Adductors", "Aductores"), ("Abductors", "Abductores"), ("HipFlexors", "Flexores de cadera"),
        ("Serratus", "Serrato"), ("TibialisAnterior", "Tibial anterior"), ("RotatorCuff", "Manguito rotador"));

    public static readonly IReadOnlyDictionary<string, string> Equipment = Map(
        ("barbell", "Barra"), ("dumbbell", "Mancuernas"), ("kettlebell", "Pesa rusa"), ("cable", "Polea / estación de cables"),
        ("bench", "Banco de musculación"), ("squat-rack", "Jaula o rack de sentadillas"), ("pull-up-bar", "Barra de dominadas"),
        ("low-row-bar", "Barra baja para remo"), ("bodyweight", "Peso corporal"), ("body-only", "Peso corporal"),
        ("band", "Banda elástica"), ("bands", "Bandas elásticas"), ("resistance-band", "Banda elástica"),
        ("ez-bar", "Barra Z"), ("trap-bar", "Barra hexagonal"), ("smith-machine", "Máquina Smith"),
        ("leg-press-machine", "Prensa de piernas"), ("hack-squat-machine", "Máquina de sentadilla hack"),
        ("leg-curl-machine", "Máquina de curl femoral"), ("leg-extension-machine", "Máquina de extensión de piernas"),
        ("dip-station", "Estación de fondos / paralelas"), ("captains-chair", "Silla romana"),
        ("ab-wheel", "Rueda abdominal"), ("step", "Cajón o plataforma"), ("stability-ball", "Fitball / balón de estabilidad"),
        ("medicine-ball", "Balón medicinal"), ("exercise-ball", "Fitball"), ("rings", "Anillas"),
        ("dip-bar", "Barras paralelas"), ("trx", "TRX"), ("suspension-trainer", "Entrenador de suspensión"),
        ("rower", "Máquina de remo"), ("glute-ham-developer", "Banco GHD (glute-ham developer)"),
        ("landmine", "Anclaje landmine para barra"), ("t-bar-row-handle", "Asa para remo en T"),
        ("preacher-curl-bench", "Banco Scott"), ("other", "Otro"),
        ("assisted-pull-up-machine", "Máquina de dominadas asistidas"), ("chest-press-machine", "Máquina de press de pecho"),
        ("seated-row-machine", "Máquina de remo sentado"), ("hip-abduction-machine", "Máquina de abductores"),
        ("hip-adduction-machine", "Máquina de aductores"), ("back-extension-machine", "Máquina de extensión lumbar"),
        ("bicep-curl-machine", "Máquina de curl de bíceps"), ("standing-calf-raise-machine", "Máquina de gemelos de pie"),
        ("seated-calf-raise-machine", "Máquina de gemelos sentado"), ("chest-fly-machine", "Máquina de aperturas de pecho"),
        ("reverse-fly-machine", "Máquina de aperturas inversas"), ("ab-crunch-machine", "Máquina de abdominales"),
        ("shoulder-press-machine", "Máquina de press de hombros"), ("pec-deck", "Pec deck"),
        ("hip-thrust-machine", "Máquina de hip thrust"),
        ("plate-loaded-lateral-raise-machine", "Máquina de elevaciones laterales"),
        ("lat-pulldown-machine", "Máquina de jalón al pecho"));

    public static readonly IReadOnlyDictionary<string, string> PatternsEn = Map(
        ("Squat", "Squat"), ("Hinge", "Hip hinge"), ("HorizontalPush", "Horizontal push"), ("VerticalPush", "Vertical push"),
        ("HorizontalPull", "Horizontal pull"), ("VerticalPull", "Vertical pull"), ("SingleLeg", "Single-leg"),
        ("Core", "Core"), ("Accessory", "Accessory"));

    public static readonly IReadOnlyDictionary<string, string> MusclesEn = Map(
        ("Chest", "Chest"), ("Lats", "Lats"), ("UpperBack", "Upper back"), ("Deltoids", "Shoulders"), ("Biceps", "Biceps"),
        ("Triceps", "Triceps"), ("Forearms", "Forearms"), ("Abdominals", "Abs"), ("Obliques", "Obliques"),
        ("ErectorSpinae", "Lower back"), ("Glutes", "Glutes"), ("Quadriceps", "Quads"), ("Hamstrings", "Hamstrings"),
        ("Calves", "Calves"), ("Adductors", "Adductors"), ("Abductors", "Abductors"), ("HipFlexors", "Hip flexors"),
        ("Serratus", "Serratus"), ("TibialisAnterior", "Tibialis anterior"), ("RotatorCuff", "Rotator cuff"));

    public static readonly IReadOnlyDictionary<string, string> EquipmentEn = Map(
        ("barbell", "Barbell"), ("dumbbell", "Dumbbells"), ("kettlebell", "Kettlebell"), ("cable", "Cable station"),
        ("bench", "Weight bench"), ("squat-rack", "Squat rack"), ("pull-up-bar", "Pull-up bar"),
        ("low-row-bar", "Low horizontal row bar"), ("bodyweight", "Bodyweight"), ("body-only", "Bodyweight"),
        ("band", "Resistance band"), ("bands", "Resistance bands"), ("resistance-band", "Resistance band"),
        ("ez-bar", "EZ bar"), ("trap-bar", "Trap bar / hex bar"), ("smith-machine", "Smith machine"),
        ("leg-press-machine", "Leg press machine"), ("hack-squat-machine", "Hack squat machine"),
        ("leg-curl-machine", "Leg curl machine"), ("leg-extension-machine", "Leg extension machine"),
        ("dip-station", "Dip station / parallel bars"), ("captains-chair", "Captain's chair"),
        ("ab-wheel", "Ab wheel"), ("step", "Plyometric box / step"), ("stability-ball", "Stability ball"),
        ("medicine-ball", "Medicine ball"), ("exercise-ball", "Exercise ball"), ("rings", "Gymnastics rings"),
        ("dip-bar", "Dip bars"), ("trx", "TRX"), ("suspension-trainer", "Suspension trainer"),
        ("rower", "Rowing machine"), ("glute-ham-developer", "Glute-ham developer (GHD)"),
        ("landmine", "Barbell landmine attachment"), ("t-bar-row-handle", "T-bar row handle"),
        ("preacher-curl-bench", "Preacher curl bench"), ("other", "Other"),
        ("assisted-pull-up-machine", "Assisted pull-up machine"), ("chest-press-machine", "Chest press machine"),
        ("seated-row-machine", "Seated row machine"), ("hip-abduction-machine", "Hip abduction machine"),
        ("hip-adduction-machine", "Hip adduction machine"), ("back-extension-machine", "Back extension machine"),
        ("bicep-curl-machine", "Biceps curl machine"), ("standing-calf-raise-machine", "Standing calf raise machine"),
        ("seated-calf-raise-machine", "Seated calf raise machine"), ("chest-fly-machine", "Chest fly machine"),
        ("reverse-fly-machine", "Reverse fly machine"), ("ab-crunch-machine", "Ab crunch machine"),
        ("shoulder-press-machine", "Shoulder press machine"), ("pec-deck", "Pec deck"),
        ("hip-thrust-machine", "Hip thrust machine"),
        ("plate-loaded-lateral-raise-machine", "Plate-loaded lateral raise machine"),
        ("lat-pulldown-machine", "Lat pulldown machine"));

    public static readonly IReadOnlyDictionary<string, string> LevelsEn = Map(
        ("Beginner", "Beginner"), ("Intermediate", "Intermediate"), ("Advanced", "Advanced"));

    /// <summary>Used for both minimum experience (Beginner…) and difficulty (beginner…); matching ignores case.</summary>
    public static readonly IReadOnlyDictionary<string, string> Levels = Map(
        ("Beginner", "Principiante"), ("Intermediate", "Intermedio"), ("Advanced", "Avanzado"));
}
