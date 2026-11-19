using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trazo.Application.Planning;
using Trazo.Domain.Identity;
using Trazo.Domain.Planning;
using Trazo.Infrastructure.Persistence;
using Trazo.Training;
using Xunit;

namespace Trazo.UnitTests.Planning;

/// <summary>
/// The loads a NEW plan starts from come from what the user last did. Pain precedence must hold there too: a load reached in a session
/// with pain, in a deload, or registered long afterwards must not seed an automatic load (the estimator can even propose a heavier one),
/// while the history itself is never touched. Scenarios run on the real service and database.
/// </summary>
public abstract class LoadReferencePainScenarios(string? postgres) : PlansApiBase(enforced: false, postgres)
{
    private static readonly string[] Equipment = ["barbell", "bench", "squat-rack", "dumbbell", "cable", "machine", "pull-up-bar"];

    private async Task<T> InDbAsync<T>(Func<TrazoDbContext, Task<T>> work)
    {
        using var scope = _factory.Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<TrazoDbContext>());
    }

    /// <summary>The first exercise of the user's first session, and a workout log of it at the given load, top of the range, three reps in reserve.</summary>
    private sealed record Seeded(string ExerciseId);

    private async Task<Seeded> LogAsync(
        Guid userId, int dayOffset, decimal weightKg, bool generalPain = false, bool setPain = false, int registeredDaysLater = 0,
        string? exerciseId = null, int occurrence = 0, bool adaptationDeload = false, bool blockIsDeload = false)
    {
        return await InDbAsync(async db =>
        {
            var plan = await db.TrainingPlans.SingleAsync(p => p.UserId == userId);
            var sessions = (await db.PlannedSessions.Where(s => s.PlanId == plan.Id).OrderBy(s => s.SequenceIndex).ToListAsync())
                .Where(s => exerciseId is null || PrescriptionJson.Read(s.PrescriptionJson).Any(e => e.ExerciseId == exerciseId)).ToList();
            var session = sessions[occurrence];
            if (adaptationDeload)
            {
                var node = System.Text.Json.Nodes.JsonNode.Parse(session.PrescriptionJson)!.AsObject();
                node["adaptationDeload"] = true;
                session.PrescriptionJson = node.ToJsonString();
            }
            if (blockIsDeload) (await db.TrainingMesocycles.SingleAsync(m => m.Id == session.MesocycleId)).IsDeload = true;
            var exercise = PrescriptionJson.Read(session.PrescriptionJson).First(e => exerciseId is null || e.ExerciseId == exerciseId);
            var at = Now.AddDays(dayOffset);
            var log = new WorkoutLog
            {
                Id = Guid.NewGuid(), UserId = userId, PlanId = plan.Id, PlannedSessionId = session.Id, LoggedAt = at.AddDays(registeredDaysLater).AddHours(1),
                PerformedAt = at, PerformedOn = DateOnly.FromDateTime(at.UtcDateTime), TimeZoneId = "UTC", Outcome = WorkoutOutcome.Completed,
                Recovery = "Good", PainReported = generalPain
            };
            for (var set = 1; set <= 3; set++)
                log.Sets.Add(new WorkoutSetLog
                {
                    Id = Guid.NewGuid(), WorkoutLogId = log.Id, ExerciseId = exercise.ExerciseId, SetNumber = set, WeightKg = weightKg,
                    Repetitions = exercise.MaxRepetitions, Rir = 3m, Completed = true, PainReported = setPain && set == 1
                });
            db.WorkoutLogs.Add(log);
            await db.SaveChangesAsync();
            return new Seeded(exercise.ExerciseId);
        });
    }

    /// <summary>The target load week 1 of the user's NEW plan prescribes for the exercise (null when it prescribes none).</summary>
    private async Task<decimal?> NewPlanLoadAsync(TrazoUser user, string exerciseId)
    {
        var client = ClientFor(user);
        await client.PostAsync("/api/v1/plans/current/archive", null);
        using var scope = _factory.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<Trazo.Infrastructure.Planning.ITrainingPlanService>()
            .CreateAsync(user.Id, PlanTestCatalog.Profile(days: 4, equipment: Equipment), 42, CancellationToken.None);
        Assert.True(result.Succeeded, result.Message);
        return await InDbAsync(async db =>
        {
            var plan = await db.TrainingPlans.SingleAsync(p => p.UserId == user.Id && p.Status == TrainingPlanStatus.Active);
            var sessions = await db.PlannedSessions.Where(s => s.PlanId == plan.Id && s.WeekNumber == 1).ToListAsync();
            return sessions.SelectMany(s => PrescriptionJson.Read(s.PrescriptionJson)).Where(e => e.ExerciseId == exerciseId)
                .Select(e => e.TargetLoadKg).FirstOrDefault();
        });
    }

    /// <summary>A user whose plan is the same as every other test user's (same profile, same seed) and who did the exercise cleanly at 50 kg.</summary>
    private async Task<(TrazoUser User, string Exercise)> UserWithCleanHistoryAsync(string email, string? exerciseId = null)
    {
        var (user, _, _) = await StartPlanAsync(email);
        var seeded = await LogAsync(user.Id, -14, 50m, exerciseId: exerciseId);
        return (user, seeded.ExerciseId);
    }

    [ScenarioFact]
    public async Task A_load_reached_in_a_session_with_pain_does_not_seed_a_heavier_load_in_the_new_plan()
    {
        var (control, exercise) = await UserWithCleanHistoryAsync("clean@example.com");
        var (hurt, _) = await UserWithCleanHistoryAsync("hurt@example.com", exercise);
        await LogAsync(hurt.Id, -3, 60m, generalPain: true, exerciseId: exercise, occurrence: 1);

        var fromClean = await NewPlanLoadAsync(control, exercise);
        var fromPain = await NewPlanLoadAsync(hurt, exercise);
        Assert.NotNull(fromClean);
        // The new plan must not prescribe anything heavier because of the session with pain (it may prescribe nothing at all).
        Assert.True(fromPain is null || fromPain <= fromClean, $"clean history {fromClean} kg, session with pain {fromPain} kg");
    }

    [ScenarioFact]
    public async Task Pain_in_the_exercise_itself_does_not_seed_a_heavier_load()
    {
        var (control, exercise) = await UserWithCleanHistoryAsync("clean2@example.com");
        var (hurt, _) = await UserWithCleanHistoryAsync("hurt2@example.com", exercise);
        await LogAsync(hurt.Id, -3, 60m, setPain: true, exerciseId: exercise, occurrence: 1);

        var fromClean = await NewPlanLoadAsync(control, exercise);
        var fromPain = await NewPlanLoadAsync(hurt, exercise);
        Assert.True(fromPain is null || fromPain <= fromClean, $"clean history {fromClean} kg, pain on the exercise {fromPain} kg");
    }

    [ScenarioFact]
    public async Task A_workout_registered_long_after_it_happened_does_not_seed_a_load()
    {
        var (control, exercise) = await UserWithCleanHistoryAsync("clean3@example.com");
        var (late, _) = await UserWithCleanHistoryAsync("late3@example.com", exercise);
        await LogAsync(late.Id, -3, 60m, registeredDaysLater: 30, exerciseId: exercise, occurrence: 1);

        var fromClean = await NewPlanLoadAsync(control, exercise);
        var fromLate = await NewPlanLoadAsync(late, exercise);
        Assert.Equal(fromClean, fromLate);
    }

    [ScenarioFact]
    public async Task A_deload_session_does_not_seed_a_load()
    {
        var (control, exercise) = await UserWithCleanHistoryAsync("clean4@example.com");
        var (deload, _) = await UserWithCleanHistoryAsync("deload4@example.com", exercise);
        await LogAsync(deload.Id, -3, 60m, exerciseId: exercise, occurrence: 1, adaptationDeload: true);

        var fromClean = await NewPlanLoadAsync(control, exercise);
        Assert.NotNull(fromClean);
        Assert.Equal(fromClean, await NewPlanLoadAsync(deload, exercise)); // the clean exposure still seeds; the deload one is ignored
    }

    [ScenarioFact]
    public async Task A_history_made_only_of_a_deload_block_leaves_the_exercise_without_a_load()
    {
        var (user, exercise) = await UserWithCleanHistoryAsync("onlydeload@example.com");
        await LogAsync(user.Id, -3, 60m, exerciseId: exercise, occurrence: 1, blockIsDeload: true);
        Assert.Null(await NewPlanLoadAsync(user, exercise));
    }

    [ScenarioFact]
    public async Task Without_a_safe_reference_the_exercise_has_no_prescribed_load_and_the_history_is_untouched()
    {
        var (user, _, _) = await StartPlanAsync("nosafe@example.com");
        var exercise = (await LogAsync(user.Id, -3, 60m, setPain: true)).ExerciseId;
        var before = await InDbAsync(async db => string.Join("|", (await db.WorkoutSetLogs.Where(x => x.ExerciseId == exercise).OrderBy(x => x.SetNumber).ToListAsync())
            .Select(x => $"{x.Id}:{x.WeightKg}:{x.Repetitions}:{x.PainReported}")));
        Assert.Null(await NewPlanLoadAsync(user, exercise));
        var after = await InDbAsync(async db => string.Join("|", (await db.WorkoutSetLogs.Where(x => x.ExerciseId == exercise).OrderBy(x => x.SetNumber).ToListAsync())
            .Select(x => $"{x.Id}:{x.WeightKg}:{x.Repetitions}:{x.PainReported}")));
        Assert.Equal(before, after);
        Assert.Equal(1, await InDbAsync(db => db.WorkoutLogs.CountAsync(l => l.UserId == user.Id)));
    }

    [ScenarioTheory]
    [InlineData(1, true)]   // pain in the most recent exposure
    [InlineData(2, true)]
    [InlineData(3, true)]   // the third most recent still counts
    [InlineData(4, false)]  // the fourth is outside the window of three exposures
    public async Task Pain_in_any_of_the_last_three_exposures_of_the_exercise_blocks_the_reference(int painfulExposureFromLatest, bool blocked)
    {
        var (user, exercise) = await UserWithCleanHistoryAsync($"window{painfulExposureFromLatest}@example.com");
        // Four exposures, oldest first: the first one (50 kg, clean) is already there; add three more.
        for (var n = 1; n <= 3; n++)
        {
            var fromLatest = 4 - n; // exposure n (1 = second oldest) is this far from the latest
            await LogAsync(user.Id, -14 + n * 3, 52m + n, setPain: fromLatest == painfulExposureFromLatest, exerciseId: exercise, occurrence: n);
        }
        if (painfulExposureFromLatest == 4) await InDbAsync(async db =>
        {
            var oldest = (await db.WorkoutLogs.Include(l => l.Sets).ToListAsync()).Where(l => l.Sets.Any(x => x.ExerciseId == exercise)).OrderBy(l => l.PerformedAt).First().Id;
            foreach (var set in await db.WorkoutSetLogs.Where(x => x.WorkoutLogId == oldest).ToListAsync()) set.PainReported = set.SetNumber == 1;
            await db.SaveChangesAsync();
            return 0;
        });
        var load = await NewPlanLoadAsync(user, exercise);
        if (blocked) Assert.Null(load);
        else Assert.NotNull(load);
    }

    [ScenarioTheory]
    [InlineData(1, true)]   // pain declared in the most recent workout
    [InlineData(5, true)]   // the fifth most recent still counts
    [InlineData(6, false)]  // the sixth is outside the window of five
    public async Task Pain_declared_in_the_last_five_workouts_blocks_every_reference(int painfulWorkoutFromLatest, bool blocked)
    {
        var (user, _, _) = await StartPlanAsync($"five{painfulWorkoutFromLatest}@example.com");
        string? latest = null;
        for (var i = 0; i < 6; i++)
        {
            var fromLatest = 6 - i;
            latest = (await LogAsync(user.Id, -20 + i * 3, 50m, generalPain: fromLatest == painfulWorkoutFromLatest, occurrence: i)).ExerciseId;
        }
        var load = await NewPlanLoadAsync(user, latest!);
        if (blocked) Assert.Null(load);
        else Assert.NotNull(load);
    }

    [ScenarioFact]
    public async Task A_clean_history_still_seeds_the_load_and_nothing_of_it_is_deleted()
    {
        var (user, exercise) = await UserWithCleanHistoryAsync("keeps@example.com");
        var before = await InDbAsync(db => db.WorkoutLogs.CountAsync(l => l.UserId == user.Id));
        var load = await NewPlanLoadAsync(user, exercise);
        Assert.NotNull(load);
        Assert.Equal(before, await InDbAsync(db => db.WorkoutLogs.CountAsync(l => l.UserId == user.Id)));
    }
}

public sealed class LoadReferencePainTests() : LoadReferencePainScenarios(null);

[Trait("Category", "Postgres")]
public sealed class PostgresLoadReferencePainTests() : LoadReferencePainScenarios(PostgresSettings.ConnectionString);
