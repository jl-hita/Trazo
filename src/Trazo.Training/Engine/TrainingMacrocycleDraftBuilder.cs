namespace Trazo.Training;

/// <summary>
/// Builds an editable macrocycle DRAFT (nothing is persisted or applied). The phase split is an
/// internal product heuristic, not a scheme prescribed by ACSM or another verified guideline.
/// Deload phases are never added unless the professional explicitly asks for them, and then at the
/// interval the professional chooses (4–8 training weeks, a planning reference, not a rule).
/// </summary>
public static class TrainingMacrocycleDraftBuilder
{
    public const int MinimumWeeks = 2;
    public const int MaximumWeeks = 52;
    public const int MinimumDeloadInterval = 4;
    public const int MaximumDeloadInterval = 8;

    public static TrainingMacrocycleDraftResult Build(TrainingMacrocycleDraftRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var errors = new List<string>();
        if (!Enum.IsDefined(request.Goal)) errors.Add("El objetivo no es válido.");
        if (!Enum.IsDefined(request.Experience)) errors.Add("El nivel de experiencia no es válido.");
        if (request.TotalWeeks is < MinimumWeeks or > MaximumWeeks)
            errors.Add($"La duración debe estar entre {MinimumWeeks} y {MaximumWeeks} semanas.");
        if (request.IncludeDeloadPhases &&
            request.DeloadEveryTrainingWeeks is < MinimumDeloadInterval or > MaximumDeloadInterval)
            errors.Add($"El intervalo de descarga debe estar entre {MinimumDeloadInterval} y {MaximumDeloadInterval} semanas de entrenamiento.");
        if (errors.Count > 0) return new(null, errors);

        // Strength plans for non-beginners get an intensification half; every other combination
        // stays in accumulation (beginners adapt to practice itself; general fitness has no peaking).
        var splitIntoIntensification = request.Goal == TrainingGoal.Strength &&
            request.Experience != TrainingExperience.Beginner && request.TotalWeeks >= 6;
        var trainingWeeksTotal = request.IncludeDeloadPhases
            ? Enumerable.Range(1, request.TotalWeeks).Count(week => !IsDeloadWeek(week, request))
            : request.TotalWeeks;
        var weekTypes = new List<string>();
        var trainingSeen = 0;
        for (var week = 1; week <= request.TotalWeeks; week++)
        {
            if (request.IncludeDeloadPhases && IsDeloadWeek(week, request))
            {
                weekTypes.Add("deload");
                continue;
            }
            trainingSeen++;
            weekTypes.Add(splitIntoIntensification && trainingSeen > trainingWeeksTotal / 2
                ? "intensification" : "accumulation");
        }

        var phases = new List<TrainingMacrocycleDraftPhase>();
        var start = 0;
        while (start < weekTypes.Count)
        {
            var end = start;
            while (end + 1 < weekTypes.Count && weekTypes[end + 1] == weekTypes[start]) end++;
            var type = weekTypes[start];
            phases.Add(new(
                $"{PhaseLabel(type)} {phases.Count(p => p.PhaseType == type) + 1}",
                phases.Count + 1,
                request.StartDate.AddDays(start * 7),
                request.StartDate.AddDays((end + 1) * 7 - 1),
                Objective(type, request.Goal), type));
            start = end + 1;
        }

        var notes = "Borrador editable generado con una heurística interna de producto; no es un esquema prescrito por ACSM. " +
            (request.IncludeDeloadPhases
                ? $"Las descargas se incluyen porque el profesional las solicitó (cada {request.DeloadEveryTrainingWeeks} semanas de entrenamiento) y son editables."
                : "No se incluyen descargas: se añaden solo si el profesional las decide.");
        return new(new(
            "Macrociclo propuesto", "custom", request.StartDate,
            request.StartDate.AddDays(request.TotalWeeks * 7 - 1), notes, phases), []);
    }

    private static bool IsDeloadWeek(int week, TrainingMacrocycleDraftRequest request) =>
        week < request.TotalWeeks && week % (request.DeloadEveryTrainingWeeks + 1) == 0;

    private static string PhaseLabel(string type) => type switch
    {
        "deload" => "Descarga",
        "intensification" => "Intensificación",
        _ => "Acumulación"
    };

    private static string Objective(string type, TrainingGoal goal) => type switch
    {
        "deload" => "Reducción temporal y explícita del estrés de entrenamiento (fase decidida por el profesional).",
        "intensification" => "Cargas más altas y repeticiones bajas en los básicos, manteniendo el volumen de accesorios.",
        _ => goal switch
        {
            TrainingGoal.Strength => "Acumular volumen técnico en los básicos con progresión de carga.",
            TrainingGoal.Hypertrophy => "Acumular volumen semanal por grupo muscular con progresión doble.",
            _ => "Construir hábito, técnica y base de fuerza general."
        }
    };
}

public sealed record TrainingMacrocycleDraftRequest(
    DateOnly StartDate,
    int TotalWeeks,
    TrainingGoal Goal,
    TrainingExperience Experience,
    bool IncludeDeloadPhases = false,
    int DeloadEveryTrainingWeeks = 4);

public sealed record TrainingMacrocycleDraftPhase(
    string Name, int SequenceNumber, DateOnly StartDate, DateOnly EndDate, string Objective, string PhaseType);

public sealed record TrainingMacrocycleDraft(
    string Name, string PeriodizationModel, DateOnly StartDate, DateOnly EndDate, string Notes,
    IReadOnlyList<TrainingMacrocycleDraftPhase> Phases);

public sealed record TrainingMacrocycleDraftResult(
    TrainingMacrocycleDraft? Draft, IReadOnlyList<string> Errors)
{
    public bool Succeeded => Draft is not null && Errors.Count == 0;
}
