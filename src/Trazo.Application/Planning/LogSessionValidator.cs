using Trazo.Domain.Planning;

namespace Trazo.Application.Planning;

public sealed record ParsedSet(string ExerciseId, int SetNumber, decimal? WeightKg, int? Repetitions, decimal? Rir, bool Completed, string? SkippedReason, bool Pain);

public sealed record ParsedLog(
    WorkoutOutcome Outcome, string NonCompletionReason, string Recovery, decimal? SessionRpe, bool Pain, string? Notes,
    IReadOnlyList<ParsedSet> Sets);

public sealed record ParsedLogResult(ParsedLog? Log, IReadOnlyDictionary<string, string[]> Errors)
{
    public bool Succeeded => Log is not null && Errors.Count == 0;
}

/// <summary>Validates the shape and ranges of a session log. Whether the exercises belong to the session is checked by the service.</summary>
public static class LogSessionValidator
{
    public const int MaximumSets = 60;
    public const int MaximumSetNumber = 12;
    public static readonly string[] Reasons = ["None", "Time", "Fatigue", "Equipment", "Motivation", "Illness", "Other"];
    public static readonly string[] Recoveries = ["Unknown", "Good", "Partial", "Insufficient"];
    public static readonly string[] SetSkipReasons = ["fatigue", "pain", "time", "equipment", "illness", "motivation", "load_too_heavy", "other"];

    public static ParsedLogResult Parse(LogSessionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var errors = new Dictionary<string, List<string>>();
        void Fail(string field, string message)
        {
            if (!errors.TryGetValue(field, out var list)) errors[field] = list = [];
            list.Add(message);
        }

        if (!Enum.TryParse<WorkoutOutcome>(request.Outcome?.Trim(), true, out var outcome) || !Enum.IsDefined(outcome) || int.TryParse(request.Outcome, out _))
            Fail("outcome", "El resultado debe ser Completed, Partial o NotCompleted.");

        var reason = Canonical(request.NonCompletionReason, Reasons, "None");
        if (reason is null) Fail("nonCompletionReason", $"El motivo debe ser uno de: {string.Join(", ", Reasons)}.");
        var recovery = Canonical(request.Recovery, Recoveries, "Unknown");
        if (recovery is null) Fail("recovery", $"La recuperación debe ser una de: {string.Join(", ", Recoveries)}.");
        if (request.SessionRpe is < 0 or > 10) Fail("sessionRpe", "El esfuerzo de la sesión (RPE) debe estar entre 0 y 10.");
        var notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        if (notes?.Length > 1000) Fail("notes", "Las notas admiten hasta 1000 caracteres.");

        var sets = new List<ParsedSet>();
        if ((request.Sets?.Length ?? 0) > MaximumSets) Fail("sets", $"Se admiten como máximo {MaximumSets} series.");
        var seen = new HashSet<(string, int)>();
        foreach (var (set, index) in (request.Sets ?? []).Take(MaximumSets).Select((s, i) => (s, i)))
        {
            var prefix = $"sets[{index}]";
            var id = set.ExerciseId?.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(id) || id.Length > 100) { Fail(prefix, "El identificador del ejercicio es obligatorio."); continue; }
            if (set.SetNumber is not (>= 1 and <= MaximumSetNumber)) { Fail(prefix, $"El número de serie debe estar entre 1 y {MaximumSetNumber}."); continue; }
            if (!seen.Add((id, set.SetNumber.Value))) { Fail(prefix, "La serie está repetida."); continue; }
            var completed = set.Completed ?? true;
            if (set.WeightKg is < 0 or > 1000) Fail(prefix, "El peso debe estar entre 0 y 1000 kg.");
            if (set.Repetitions is < 0 or > 100) Fail(prefix, "Las repeticiones deben estar entre 0 y 100.");
            if (completed && set.Repetitions is null) Fail(prefix, "Una serie completada necesita las repeticiones.");
            if (set.Rir is < 0 or > 10) Fail(prefix, "El RIR debe estar entre 0 y 10.");
            var skipped = string.IsNullOrWhiteSpace(set.SkippedReason) ? null : set.SkippedReason.Trim().ToLowerInvariant();
            if (skipped is not null && !SetSkipReasons.Contains(skipped)) Fail(prefix, $"El motivo de la serie debe ser uno de: {string.Join(", ", SetSkipReasons)}.");
            sets.Add(new ParsedSet(id, set.SetNumber.Value, set.WeightKg, set.Repetitions, set.Rir, completed, skipped,
                set.PainReported ?? string.Equals(skipped, "pain", StringComparison.Ordinal)));
        }

        if (errors.Count > 0) return new(null, errors.ToDictionary(p => p.Key, p => p.Value.ToArray()));
        return new(new ParsedLog(outcome, reason!, recovery!, request.SessionRpe, request.PainReported ?? false, notes, sets),
            new Dictionary<string, string[]>());
    }

    private static string? Canonical(string? value, string[] allowed, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback
        : allowed.FirstOrDefault(a => a.Equals(value.Trim(), StringComparison.OrdinalIgnoreCase));
}
