using Trazo.Training;

namespace Trazo.Application.Planning;

/// <summary>A validated manual plan; exercises are still ids that the service resolves against the catalog.</summary>
public sealed record ManualPlan(
    string Name, string Notes, DateOnly StartDate, int TotalWeeks, TrainingGoal Goal, TrainingExperience Experience,
    IReadOnlyList<ManualDay> Days, Trazo.Domain.Planning.PlanSchedulingMode SchedulingMode = Trazo.Domain.Planning.PlanSchedulingMode.Calendar);

public sealed record ManualDay(
    DayOfWeek? Day, string Title, IReadOnlyList<ManualExercise> Exercises, IReadOnlyList<TrainingSupersetPrescription>? Supersets = null);

public sealed record ManualExercise(string ExerciseId, int Sets, int MinRepetitions, int MaxRepetitions, decimal? TargetRir, int RestSeconds, string? SupersetGroup = null,
    TrainingTechniquePrescription? Technique = null);

public sealed record ManualPlanResult(ManualPlan? Plan, IReadOnlyDictionary<string, string[]> Errors)
{
    public bool Succeeded => Plan is not null && Errors.Count == 0;
}

/// <summary>Validates what the user wrote for a manual plan. Free of charge: manual plans never need Premium.</summary>
public static class ManualPlanParser
{
    public const int MinimumTotalWeeks = 1;
    public const int MaximumTotalWeeks = PlanProfileParser.MaximumTotalWeeks;
    public const int MaximumExercisesPerDay = 15;
    public const int MaximumSets = 10;
    public const int DefaultRestSeconds = 90;

    public static ManualPlanResult Parse(ManualPlanInput input, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(input);
        var errors = new Dictionary<string, List<string>>();
        void Fail(string field, string message)
        {
            if (!errors.TryGetValue(field, out var list)) errors[field] = list = [];
            list.Add(message);
        }

        var name = (input.Name ?? string.Empty).Trim();
        if (name.Length is 0 or > 80) Fail("name", "El nombre del plan es obligatorio (máximo 80 caracteres).");
        var notes = (input.Notes ?? string.Empty).Trim();
        if (notes.Length > 500) Fail("notes", "Las notas admiten como máximo 500 caracteres.");

        var weeks = input.TotalWeeks ?? 8;
        if (weeks < MinimumTotalWeeks || weeks > MaximumTotalWeeks)
            Fail("totalWeeks", $"La duración del plan debe estar entre {MinimumTotalWeeks} y {MaximumTotalWeeks} semanas.");

        var start = input.StartDate ?? today;
        if (start < today.AddDays(-PlanProfileParser.StartDateMaxDaysInThePast) || start > today.AddDays(PlanProfileParser.StartDateMaxDaysInTheFuture))
            Fail("startDate", $"La fecha de inicio debe estar entre {PlanProfileParser.StartDateMaxDaysInThePast} días atrás y {PlanProfileParser.StartDateMaxDaysInTheFuture} días adelante.");

        var goal = TrainingGoal.GeneralFitness;
        if (!string.IsNullOrWhiteSpace(input.Goal) && !TryEnum(input.Goal, out goal)) Fail("goal", "El objetivo no es válido.");
        var experience = TrainingExperience.Intermediate;
        if (!string.IsNullOrWhiteSpace(input.Experience) && !TryEnum(input.Experience, out experience)) Fail("experience", "El nivel de experiencia no es válido.");

        var mode = Trazo.Domain.Planning.PlanSchedulingMode.Calendar;
        if (!string.IsNullOrWhiteSpace(input.SchedulingMode) && !TryEnum(input.SchedulingMode, out mode))
            Fail("schedulingMode", $"El modo debe ser uno de: {string.Join(", ", Enum.GetNames<Trazo.Domain.Planning.PlanSchedulingMode>())}.");
        var sequence = mode == Trazo.Domain.Planning.PlanSchedulingMode.Sequence;

        var days = new List<ManualDay>();
        if ((input.Days?.Length ?? 0) is 0 or > 7)
            Fail("days", sequence ? "Indica entre 1 y 7 rutinas." : "Indica entre 1 y 7 días de entrenamiento por semana.");
        var seen = new HashSet<DayOfWeek>();
        var index = 0;
        foreach (var day in (input.Days ?? []).Take(7))
        {
            index++;
            var key = $"days.{index}";
            DayOfWeek weekday = default;
            if (day is null) { Fail(key, "La rutina no es válida."); continue; }
            // In a sequence plan the routines are a rotation in the order given: there are no days of the week.
            if (!sequence)
            {
                if (!TryEnum(day.Day, out weekday)) { Fail(key, "El día no es válido."); continue; }
                if (!seen.Add(weekday)) Fail(key, "No se puede repetir un día de la semana.");
            }
            var title = (day.Title ?? string.Empty).Trim();
            if (title.Length is 0 or > 60) Fail($"{key}.title", "El título de la rutina es obligatorio (máximo 60 caracteres).");
            var exercises = new List<ManualExercise>();
            var items = day.Exercises ?? [];
            if (items.Length is 0 or > MaximumExercisesPerDay)
                Fail($"{key}.exercises", $"Cada rutina necesita entre 1 y {MaximumExercisesPerDay} ejercicios.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var position = 0;
            foreach (var item in items.Take(MaximumExercisesPerDay))
            {
                position++;
                var field = $"{key}.exercises.{position}";
                var id = (item?.ExerciseId ?? string.Empty).Trim().ToLowerInvariant();
                if (id.Length is 0 or > 100) { Fail(field, "El ejercicio es obligatorio."); continue; }
                if (!ids.Add(id)) Fail(field, "El ejercicio está repetido en la rutina.");
                var sets = item!.Sets ?? 3;
                if (sets is < 1 or > MaximumSets) Fail(field, $"Las series deben estar entre 1 y {MaximumSets}.");
                var min = item.MinRepetitions ?? 8;
                var max = item.MaxRepetitions ?? Math.Max(min, 12);
                if (min is < 1 or > 100 || max is < 1 or > 100 || min > max) Fail(field, "Las repeticiones deben estar entre 1 y 100, y el mínimo no puede superar el máximo.");
                if (item.TargetRir is < 0 or > 5) Fail(field, "El RIR objetivo debe estar entre 0 y 5.");
                var rest = item.RestSeconds ?? DefaultRestSeconds;
                if (rest is < 0 or > 600) Fail(field, "El descanso debe estar entre 0 y 600 segundos.");
                var group = string.IsNullOrWhiteSpace(item.SupersetGroup) ? null : item.SupersetGroup.Trim().ToUpperInvariant();
                if (group is not null && (group.Length != 1 || group[0] is < 'A' or > 'F')) Fail(field, "El grupo de superserie debe ser una letra de la A a la F.");
                TrainingTechniquePrescription? technique = null;
                if (item.Technique is { } wanted)
                {
                    technique = Technique(wanted);
                    // Whether this exercise admits it is a matter of the catalog, checked when the plan is stored.
                    var problems = technique is null ? [TechniqueRules.InvalidTechnique]
                        : TechniqueRules.Validate(technique, sets, [technique.Type], group is not null);
                    foreach (var problem in problems) Fail(field, problem);
                }
                exercises.Add(new ManualExercise(id, sets, min, max, item.TargetRir, rest, group, technique));
            }
            // A superset joins two or three consecutive exercises: they are done one after another with a shared rest.
            foreach (var g in exercises.Where(e => e.SupersetGroup is not null).GroupBy(e => e.SupersetGroup))
            {
                var positions = exercises.Select((e, i) => (e, i)).Where(p => p.e.SupersetGroup == g.Key).Select(p => p.i).ToList();
                if (positions.Count is < 2 or > 3 || positions[^1] - positions[0] != positions.Count - 1)
                    Fail($"{key}.exercises", $"La superserie {g.Key} necesita dos o tres ejercicios seguidos.");
            }
            var supersets = new List<TrainingSupersetPrescription>();
            foreach (var wanted in day.Supersets ?? [])
            {
                var letter = (wanted?.Group ?? string.Empty).Trim().ToUpperInvariant();
                var members = exercises.Where(e => e.SupersetGroup == letter).ToList();
                var superset = new TrainingSupersetPrescription(letter,
                    wanted?.Rounds ?? Math.Clamp(members.Select(e => e.Sets).DefaultIfEmpty(1).Max(), 1, TechniqueRules.MaximumSupersetRounds),
                    wanted?.RestBetweenSeconds ?? TechniqueRules.DefaultRestBetweenSeconds,
                    wanted?.RestAfterRoundSeconds ?? TechniqueRules.DefaultRestAfterRoundSeconds);
                if (members.Count == 0 || supersets.Any(s => s.Group == letter) || TechniqueRules.Validate(superset).Count > 0)
                    Fail($"{key}.supersets", TechniqueRules.InvalidSuperset);
                else supersets.Add(superset);
            }
            days.Add(new ManualDay(sequence ? null : weekday, title, exercises, supersets));
        }

        if (errors.Count > 0) return new(null, errors.ToDictionary(p => p.Key, p => p.Value.ToArray()));
        return new(new ManualPlan(name, notes, start, weeks, goal, experience,
                sequence ? days : days.OrderBy(d => ((int)d.Day!.Value + 6) % 7).ToList(), mode),
            new Dictionary<string, string[]>());
    }

    /// <summary>The technique asked for, with the defaults of its type for what was left out; null when a name is unknown.</summary>
    private static TrainingTechniquePrescription? Technique(ManualTechniqueInput input)
    {
        if (!TryEnum(input.Type, out TrainingTechniqueType type)) return null;
        var d = TechniqueRules.Default(type);
        var scope = d.AppliesTo;
        if (!string.IsNullOrWhiteSpace(input.AppliesTo) && !TryEnum(input.AppliesTo, out scope)) return null;
        return new(type, scope, input.MaxSegments ?? d.MaxSegments, input.RestSeconds ?? d.RestSeconds,
            input.LoadDropPercent ?? d.LoadDropPercent, input.SegmentMinRepetitions ?? d.SegmentMinRepetitions,
            input.SegmentMaxRepetitions ?? d.SegmentMaxRepetitions, input.TargetRir ?? d.TargetRir);
    }

    private static bool TryEnum<T>(string? value, out T result) where T : struct, Enum =>
        Enum.TryParse(value?.Trim(), ignoreCase: true, out result) && Enum.IsDefined(result) && !int.TryParse(value, out _);
}
