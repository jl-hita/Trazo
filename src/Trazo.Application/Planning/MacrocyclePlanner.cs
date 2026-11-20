using Trazo.Training;

namespace Trazo.Application.Planning;

/// <summary>
/// Where the deload weeks of a plan go when the user lets TRAZO decide. Rules: no tramo of training weeks is longer than
/// four (the engine cuts blocks at four weeks, so a fifth week would become a block of one), the training weeks are spread
/// evenly between the deloads (the longer tramos first), and the last week is never a deload. Plans of six weeks or less
/// get none. This lives here, and not in the engine, on purpose: see docs/shared-engine-package.md.
/// </summary>
public static class DeloadPlacement
{
    public const int LongestTrainingRun = 4;
    public const int ShortestPlanWithDeload = 7;

    /// <summary>1-based plan weeks that are deloads, ascending.</summary>
    public static IReadOnlyList<int> Weeks(int totalWeeks)
    {
        if (totalWeeks < ShortestPlanWithDeload) return [];
        var runs = (totalWeeks + LongestTrainingRun + 1) / (LongestTrainingRun + 1); // ceil((T + 1) / 5)
        var deloads = runs - 1;
        var training = totalWeeks - deloads;
        var weeks = new List<int>(deloads);
        var week = 0;
        for (var run = 0; run < deloads; run++)
        {
            week += training / runs + (run < training % runs ? 1 : 0);
            weeks.Add(++week);
        }
        return weeks;
    }
}

/// <summary>Builds the macrocycle of a profile: the engine's builder, with automatic deload placement on top when asked for.</summary>
public static class MacrocyclePlanner
{
    public static TrainingMacrocycleDraftResult Build(PlanProfile profile)
    {
        if (profile.PreparationWeeks <= 0) return BuildPlan(profile);
        // The preparation comes first and is not part of the plan proper: accumulation, intensification and deloads are laid over
        // the weeks that remain, as if the plan started when the preparation ends.
        var preparation = profile.PreparationWeeks;
        var rest = BuildPlan(profile with { StartDate = profile.StartDate.AddDays(7 * preparation), TotalWeeks = profile.TotalWeeks - preparation });
        if (rest.Draft is null) return rest;
        var first = new TrainingMacrocycleDraftPhase(
            "Preparación", 1, profile.StartDate, profile.StartDate.AddDays(7 * preparation - 1), PreparationPhase.Objective, PreparationPhase.PhaseType);
        var phases = new List<TrainingMacrocycleDraftPhase> { first };
        phases.AddRange(rest.Draft.Phases.Select(phase => phase with { SequenceNumber = phase.SequenceNumber + 1 }));
        var draft = rest.Draft with
        {
            StartDate = profile.StartDate,
            Notes = $"Las primeras {preparation} semanas son de preparación (pocas series, lejos del fallo); el plan empieza después. " +
                "Las cifras son una decisión de producto, no una pauta de ACSM. " + rest.Draft.Notes,
            Phases = phases
        };
        return new(draft, []);
    }

    private static TrainingMacrocycleDraftResult BuildPlan(PlanProfile profile)
    {
        var automatic = profile.IncludeDeloadPhases && profile.DeloadEveryTrainingWeeks == PlanProfileParser.AutomaticDeload;
        var interval = profile.DeloadEveryTrainingWeeks == PlanProfileParser.AutomaticDeload
            ? TrainingMacrocycleDraftBuilder.MinimumDeloadInterval : profile.DeloadEveryTrainingWeeks;
        var deloads = automatic ? DeloadPlacement.Weeks(profile.TotalWeeks) : [];

        var request = new TrainingMacrocycleDraftRequest(
            profile.StartDate, profile.TotalWeeks, profile.Goal, profile.Experience, profile.IncludeDeloadPhases && !automatic, interval);
        var result = TrainingMacrocycleDraftBuilder.Build(request);
        if (result.Draft is null || deloads.Count == 0) return result;

        // The engine draft without deloads gives the phase of every week; the deload weeks are laid over it.
        var weekTypes = Enumerable.Range(0, profile.TotalWeeks).Select(index =>
            deloads.Contains(index + 1) ? "deload"
            : result.Draft.Phases.First(p => p.StartDate <= profile.StartDate.AddDays(index * 7) && profile.StartDate.AddDays(index * 7) <= p.EndDate).PhaseType).ToList();
        var phases = new List<TrainingMacrocycleDraftPhase>();
        for (var start = 0; start < weekTypes.Count;)
        {
            var end = start;
            while (end + 1 < weekTypes.Count && weekTypes[end + 1] == weekTypes[start]) end++;
            var type = weekTypes[start];
            phases.Add(new($"{Label(type)} {phases.Count(p => p.PhaseType == type) + 1}", phases.Count + 1,
                profile.StartDate.AddDays(start * 7), profile.StartDate.AddDays((end + 1) * 7 - 1), ObjectiveOf(type, result.Draft), type));
            start = end + 1;
        }
        var draft = result.Draft with
        {
            Notes = "Las semanas de descarga se reparten automáticamente según la duración del plan.",
            Phases = phases
        };
        return new(draft, []);
    }

    private static string Label(string type) => type switch
    {
        "deload" => "Descarga",
        "intensification" => "Intensificación",
        _ => "Acumulación"
    };

    private static string ObjectiveOf(string type, TrainingMacrocycleDraft baseDraft) =>
        type == "deload"
            ? "Reducción temporal del estrés de entrenamiento para recuperar antes del siguiente bloque."
            : baseDraft.Phases.First(p => p.PhaseType == type).Objective;
}
