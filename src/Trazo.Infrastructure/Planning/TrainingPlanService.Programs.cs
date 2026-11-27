using Trazo.Application.Planning;
using Trazo.Training;

namespace Trazo.Infrastructure.Planning;

/// <summary>The prebuilt programs: the list and the sheet shown before starting one.</summary>
public sealed partial class TrainingPlanService
{
    /// <summary>The same seed for every sheet, so a program looks the same each time it is read.</summary>
    private const int ProgramSheetSeed = 1;
    /// <summary>The week the sheet describes: the second one, past the lighter introduction.</summary>
    private const int ProgramSheetWeek = 2;

    public async Task<IReadOnlyList<ProgramView>> GetProgramsAsync(Guid userId, CancellationToken ct)
    {
        var locked = !await subscriptions.HasPremiumAsync(userId, ct);
        return ProgramLibrary.All.Select(recipe => ToProgramView(recipe, locked)).ToList();
    }

    public async Task<PlanResult<ProgramSheetView>> GetProgramSheetAsync(Guid userId, string programId, PlanProfileInput own, CancellationToken ct)
    {
        await UseUserClockAsync(userId, ct);
        if (ProgramLibrary.Find(programId) is not { } recipe)
            return PlanResult<ProgramSheetView>.Fail(PlanFailure.SessionNotFound, ProgramLibrary.NotFound);
        // The sheet shows the program itself: at its own level, whatever the reader's.
        var (request, _) = ProgramLibrary.Request(recipe, own with { Experience = null, TrainingDays = null });
        var parsed = PlanProfileParser.Parse(request, Today);
        if (!parsed.Succeeded)
            return PlanResult<ProgramSheetView>.Fail(PlanFailure.InvalidRequest, "Revisa los datos del perfil.", parsed.Errors);

        var candidates = await LoadCandidatesAsync(ct);
        var profile = parsed.Profile!;
        var generation = MesocycleGenerator.Generate(
            profile, ProgramSheetSeed, new MesocycleSlot(1, 1, "accumulation", 1, 4), candidates.Candidates,
            techniques: client.Techniques ? TechniqueAllowance.NoPain : null);
        if (!generation.Succeeded)
            return PlanResult<ProgramSheetView>.Fail(PlanFailure.GenerationFailed, "No se pudo generar un plan con ese perfil.",
                new Dictionary<string, string[]> { ["plan"] = generation.Errors.ToArray() });

        _generatedPlan = true;
        var week = generation.Sessions.Where(s => s.WeekNumber == ProgramSheetWeek)
            .Select(s => (Session: s, Exercises: PrescriptionJson.Read(s.PrescriptionJson))).ToList();
        var used = generation.Sessions.SelectMany(s => PrescriptionJson.Read(s.PrescriptionJson)).Select(e => e.ExerciseId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var equipment = candidates.Candidates.Where(c => used.Contains(c.ExerciseId)).SelectMany(c => c.RequiredEquipment)
            .Select(PlanProfileParser.NormalizeToken).Where(t => t.Length > 0 && t != "machine").Distinct().Order(StringComparer.Ordinal).ToList();
        var sets = week.SelectMany(s => s.Exercises).SelectMany(e => e.StimulatedMuscles().Select(m => (Muscle: m, e.Sets)))
            .GroupBy(x => x.Muscle).OrderByDescending(g => g.Sum(x => x.Sets)).ThenBy(g => g.Key)
            .Select(g => new ProgramMuscleSets(g.Key.ToString(), g.Sum(x => x.Sets))).ToList();
        var first = week[0];
        var sample = new ProgramSampleSession(TitleOf(first.Session.Title), first.Session.EstimatedDurationMinutes,
            first.Exercises.OrderBy(e => e.Order).Select(e => new ProgramSampleExercise(
                e.ExerciseId, NameOf(e.ExerciseId, e.ExerciseNameSnapshot), e.Sets, e.MinRepetitions, e.MaxRepetitions)).ToList());
        var locked = !await subscriptions.HasPremiumAsync(userId, ct);
        return PlanResult<ProgramSheetView>.Ok(new ProgramSheetView(ToProgramView(recipe, locked), equipment, sets, sample));
    }

    private ProgramView ToProgramView(ProgramRecipe recipe, bool locked) => new(
        recipe.Id, T(recipe.Name), T(recipe.Description), recipe.MinimumExperience.ToString(), recipe.SessionsPerWeek,
        recipe.SchedulingMode.ToString(), recipe.Split.ToString(), recipe.SessionMinutes, recipe.TotalWeeks,
        recipe.Emphasised.Select(m => m.ToString()).ToList(), locked);
}
