using System.Text.RegularExpressions;
using Trazo.Training;

namespace Trazo.Application.Planning;

/// <summary>What the client sends to create a plan. Everything is optional text so validation can explain every problem.</summary>
public sealed record PlanProfileInput(
    string? Goal,
    string? Experience,
    DateOnly? StartDate,
    int? TotalWeeks,
    string[]? TrainingDays,
    int? MaxSessionMinutes,
    string[]? AvailableEquipment,
    string[]? ExcludedExerciseIds,
    string[]? Limitations,
    string? Split,
    bool? IncludeDeloadPhases,
    int? DeloadEveryTrainingWeeks);

/// <summary>A validated, typed profile the engine can use.</summary>
public sealed record PlanProfile(
    TrainingGoal Goal,
    TrainingExperience Experience,
    DateOnly StartDate,
    int TotalWeeks,
    IReadOnlyList<DayOfWeek> TrainingDays,
    int MaxSessionMinutes,
    IReadOnlyList<string> AvailableEquipment,
    IReadOnlyList<string> ExcludedExerciseIds,
    IReadOnlyList<string> Limitations,
    TrainingProgramSplit Split,
    bool IncludeDeloadPhases,
    int DeloadEveryTrainingWeeks);

public sealed record PlanProfileResult(PlanProfile? Profile, IReadOnlyDictionary<string, string[]> Errors)
{
    public bool Succeeded => Profile is not null && Errors.Count == 0;
}

public static partial class PlanProfileParser
{
    public const int DefaultTotalWeeks = 12;
    public const int MinimumTotalWeeks = 4;
    public const int MaximumTotalWeeks = 52;
    public const int MaximumEquipmentItems = 30;
    public const int MaximumExcludedExercises = 50;
    public const int StartDateMaxDaysInThePast = 7;
    public const int StartDateMaxDaysInTheFuture = 60;

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,39}$")]
    private static partial Regex EquipmentToken();

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,99}$")]
    private static partial Regex ExerciseId();

    public static PlanProfileResult Parse(PlanProfileInput input, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(input);
        var errors = new Dictionary<string, List<string>>();
        void Fail(string field, string message)
        {
            if (!errors.TryGetValue(field, out var list)) errors[field] = list = [];
            list.Add(message);
        }

        var goal = ParseEnum<TrainingGoal>(input.Goal, "goal", "El objetivo no es válido.", Fail);
        var experience = ParseEnum<TrainingExperience>(input.Experience, "experience", "El nivel de experiencia no es válido.", Fail);

        var split = TrainingProgramSplit.Automatic;
        if (!string.IsNullOrWhiteSpace(input.Split) && !TryEnum(input.Split, out split))
            Fail("split", "La distribución no es válida.");

        var days = new List<DayOfWeek>();
        foreach (var name in input.TrainingDays ?? [])
        {
            if (TryEnum<DayOfWeek>(name, out var day)) days.Add(day);
            else Fail("trainingDays", $"El día '{Clip(name)}' no es válido.");
        }
        if (days.Distinct().Count() != days.Count) Fail("trainingDays", "No se puede repetir un día de entrenamiento.");
        if (days.Distinct().Count() is < 2 or > 6) Fail("trainingDays", "Selecciona entre 2 y 6 días de entrenamiento por semana.");

        var minutes = input.MaxSessionMinutes ?? 60;
        if (minutes is < 35 or > 180) Fail("maxSessionMinutes", "La duración máxima de sesión debe estar entre 35 y 180 minutos.");

        var totalWeeks = input.TotalWeeks ?? DefaultTotalWeeks;
        if (totalWeeks is < MinimumTotalWeeks or > MaximumTotalWeeks)
            Fail("totalWeeks", $"La duración del plan debe estar entre {MinimumTotalWeeks} y {MaximumTotalWeeks} semanas.");

        var includeDeload = input.IncludeDeloadPhases ?? true;
        var deloadEvery = input.DeloadEveryTrainingWeeks ?? 4;
        if (includeDeload && deloadEvery is < TrainingMacrocycleDraftBuilder.MinimumDeloadInterval or > TrainingMacrocycleDraftBuilder.MaximumDeloadInterval)
            Fail("deloadEveryTrainingWeeks",
                $"El intervalo de descarga debe estar entre {TrainingMacrocycleDraftBuilder.MinimumDeloadInterval} y {TrainingMacrocycleDraftBuilder.MaximumDeloadInterval} semanas.");

        var start = input.StartDate ?? today;
        if (start < today.AddDays(-StartDateMaxDaysInThePast) || start > today.AddDays(StartDateMaxDaysInTheFuture))
            Fail("startDate", $"La fecha de inicio debe estar entre {StartDateMaxDaysInThePast} días atrás y {StartDateMaxDaysInTheFuture} días adelante.");

        var equipment = new List<string>();
        if ((input.AvailableEquipment?.Length ?? 0) > MaximumEquipmentItems)
            Fail("availableEquipment", $"Se admiten como máximo {MaximumEquipmentItems} elementos de material.");
        foreach (var raw in (input.AvailableEquipment ?? []).Take(MaximumEquipmentItems))
        {
            var token = NormalizeToken(raw);
            if (EquipmentToken().IsMatch(token)) { if (!equipment.Contains(token)) equipment.Add(token); }
            else Fail("availableEquipment", $"El material '{Clip(raw)}' no es válido.");
        }

        var excluded = new List<string>();
        if ((input.ExcludedExerciseIds?.Length ?? 0) > MaximumExcludedExercises)
            Fail("excludedExerciseIds", $"Se admiten como máximo {MaximumExcludedExercises} ejercicios excluidos.");
        foreach (var raw in (input.ExcludedExerciseIds ?? []).Take(MaximumExcludedExercises))
        {
            var id = (raw ?? string.Empty).Trim().ToLowerInvariant();
            if (ExerciseId().IsMatch(id)) { if (!excluded.Contains(id)) excluded.Add(id); }
            else Fail("excludedExerciseIds", $"El identificador de ejercicio '{Clip(raw)}' no es válido.");
        }

        var limitations = new List<string>();
        foreach (var raw in input.Limitations ?? [])
        {
            var area = LimitationRules.Canonical(raw);
            if (area is null) Fail("limitations", $"La zona '{Clip(raw)}' no es válida. Zonas admitidas: {string.Join(", ", LimitationRules.Areas)}.");
            else if (!limitations.Contains(area)) limitations.Add(area);
        }

        if (errors.Count > 0)
            return new(null, errors.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray()));
        return new(new PlanProfile(goal, experience, start, totalWeeks, days.Distinct().ToList(), minutes, equipment,
            excluded, limitations, split, includeDeload, deloadEvery), new Dictionary<string, string[]>());
    }

    /// <summary>Same rule as the engine: lower case, words joined by hyphens.</summary>
    public static string NormalizeToken(string? value) =>
        string.Join("-", (value ?? string.Empty).Trim().ToLowerInvariant().Replace('_', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static T ParseEnum<T>(string? value, string field, string message, Action<string, string> fail) where T : struct, Enum
    {
        if (TryEnum<T>(value, out var parsed)) return parsed;
        fail(field, message);
        return default;
    }

    private static bool TryEnum<T>(string? value, out T result) where T : struct, Enum =>
        Enum.TryParse(value?.Trim(), ignoreCase: true, out result) && Enum.IsDefined(result)
        && !int.TryParse(value, out _);

    private static string Clip(string? value) =>
        (value ?? string.Empty).Length > 40 ? (value ?? string.Empty)[..40] + "…" : value ?? string.Empty;
}
