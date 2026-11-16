using Trazo.Training;

namespace Trazo.Application.Planning;

/// <summary>A validated manual plan; exercises are still ids that the service resolves against the catalog.</summary>
public sealed record ManualPlan(
    string Name, string Notes, DateOnly StartDate, int TotalWeeks, TrainingGoal Goal, TrainingExperience Experience,
    IReadOnlyList<ManualDay> Days);

public sealed record ManualDay(DayOfWeek Day, string Title, IReadOnlyList<ManualExercise> Exercises);

public sealed record ManualExercise(string ExerciseId, int Sets, int MinRepetitions, int MaxRepetitions, decimal? TargetRir, int RestSeconds, string? SupersetGroup = null);

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

        var days = new List<ManualDay>();
        if ((input.Days?.Length ?? 0) is 0 or > 7) Fail("days", "Indica entre 1 y 7 días de entrenamiento por semana.");
        var seen = new HashSet<DayOfWeek>();
        var index = 0;
        foreach (var day in (input.Days ?? []).Take(7))
        {
            index++;
            var key = $"days.{index}";
            if (day is null || !TryEnum<DayOfWeek>(day.Day, out var weekday)) { Fail(key, "El día no es válido."); continue; }
            if (!seen.Add(weekday)) Fail(key, "No se puede repetir un día de la semana.");
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
                exercises.Add(new ManualExercise(id, sets, min, max, item.TargetRir, rest, group));
            }
            // A superset joins two or three consecutive exercises: they are done one after another with a shared rest.
            foreach (var g in exercises.Where(e => e.SupersetGroup is not null).GroupBy(e => e.SupersetGroup))
            {
                var positions = exercises.Select((e, i) => (e, i)).Where(p => p.e.SupersetGroup == g.Key).Select(p => p.i).ToList();
                if (positions.Count is < 2 or > 3 || positions[^1] - positions[0] != positions.Count - 1)
                    Fail($"{key}.exercises", $"La superserie {g.Key} necesita dos o tres ejercicios seguidos.");
            }
            days.Add(new ManualDay(weekday, title, exercises));
        }

        if (errors.Count > 0) return new(null, errors.ToDictionary(p => p.Key, p => p.Value.ToArray()));
        return new(new ManualPlan(name, notes, start, weeks, goal, experience, days.OrderBy(d => ((int)d.Day + 6) % 7).ToList()),
            new Dictionary<string, string[]>());
    }

    private static bool TryEnum<T>(string? value, out T result) where T : struct, Enum =>
        Enum.TryParse(value?.Trim(), ignoreCase: true, out result) && Enum.IsDefined(result) && !int.TryParse(value, out _);
}
