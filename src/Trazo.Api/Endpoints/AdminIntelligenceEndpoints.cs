using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Trazo.Domain.Planning;
using Trazo.Infrastructure.Persistence;

namespace Trazo.Api.Endpoints;

/// <summary>Research-grade training analytics. Exports contain pseudonymous keys, never account identifiers or free-text notes.</summary>
public static class AdminIntelligenceEndpoints
{
    public static IEndpointRouteBuilder MapAdminIntelligenceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/admin/intelligence").WithTags("Admin intelligence")
            .RequireAuthorization(SetupEndpoints.SuperadminPolicy);
        group.MapGet("/summary", SummaryAsync);
        group.MapGet("/export", ExportAsync);
        return endpoints;
    }

    private static (DateTimeOffset From, DateTimeOffset To) Period(DateTimeOffset? from, DateTimeOffset? to, TimeProvider clock)
    {
        var end = to ?? clock.GetUtcNow();
        var start = from ?? end.AddDays(-90);
        if (start >= end || end - start > TimeSpan.FromDays(731))
            throw new ArgumentException("El periodo debe ser válido y no superar dos años.");
        return (start, end);
    }

    private static async Task<IResult> SummaryAsync(
        TrazoDbContext db, TimeProvider clock, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
    {
        (DateTimeOffset start, DateTimeOffset end) range;
        try { range = Period(from, to, clock); }
        catch (ArgumentException e) { return Results.BadRequest(new { message = e.Message }); }

        var (start, end) = range;
        var plans = await db.TrainingPlans.AsNoTracking().Where(x => x.CreatedAt >= start && x.CreatedAt < end)
            .Select(x => new { x.Id, x.Origin, x.EngineVersion }).ToListAsync(ct);
        var planIds = plans.Select(x => x.Id).ToArray();
        var sessions = await db.PlannedSessions.AsNoTracking().Where(x => planIds.Contains(x.PlanId))
            .Select(x => new { x.Id, x.OriginalPrescriptionJson, x.PrescriptionSource }).ToListAsync(ct);
        var workouts = await db.WorkoutLogs.AsNoTracking().Where(x => x.PerformedAt >= start && x.PerformedAt < end)
            .Select(x => new { x.Id, x.PlanId, x.Outcome }).ToListAsync(ct);
        var adaptations = await db.PlanAdaptations.AsNoTracking().Where(x => x.AppliedAt >= start && x.AppliedAt < end)
            .GroupBy(x => new { x.Kind, x.Source }).Select(g => new { g.Key.Kind, g.Key.Source, Count = g.Count() }).ToListAsync(ct);
        var observations = await db.MuscleWeekObservations.AsNoTracking().Where(x => x.UpdatedAt >= start && x.UpdatedAt < end)
            .GroupBy(x => new { x.Muscle, x.Quality }).Select(g => new
            {
                g.Key.Muscle, g.Key.Quality, Count = g.Count(),
                PrescribedSets = g.Sum(x => x.PrescribedSets), PerformedSets = g.Sum(x => x.PerformedSets),
                PerformanceSamples = g.Sum(x => x.PerformanceSamples), FatigueSignals = g.Sum(x => x.FatigueSignals),
                AveragePerformanceChange = g.Where(x => x.PerformanceChangePercent != null).Average(x => (double?)x.PerformanceChangePercent)
            }).ToListAsync(ct);
        var decisions = await db.LongitudinalDecisions.AsNoTracking().Where(x => x.CreatedAt >= start && x.CreatedAt < end)
            .GroupBy(x => new { x.Action, x.PolicyVersion }).Select(g => new { g.Key.Action, g.Key.PolicyVersion, Count = g.Count() }).ToListAsync(ct);

        return Results.Ok(new
        {
            from = start, to = end, generatedAt = clock.GetUtcNow(),
            plans = new { total = plans.Count, generated = plans.Count(x => x.Origin == TrainingPlanOrigin.Generated),
                manual = plans.Count(x => x.Origin == TrainingPlanOrigin.Manual),
                engineVersions = plans.Select(x => x.EngineVersion).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().Order().ToArray() },
            prescriptions = new { sessions = sessions.Count, withOriginalSnapshot = sessions.Count(x => x.OriginalPrescriptionJson != null),
                withoutOriginalSnapshot = sessions.Count(x => x.OriginalPrescriptionJson == null) },
            workouts = new { total = workouts.Count, completed = workouts.Count(x => x.Outcome == WorkoutOutcome.Completed),
                partial = workouts.Count(x => x.Outcome == WorkoutOutcome.Partial), notCompleted = workouts.Count(x => x.Outcome == WorkoutOutcome.NotCompleted) },
            adaptations, observations, decisions
        });
    }

    private static async Task<IResult> ExportAsync(
        TrazoDbContext db, TimeProvider clock, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
    {
        (DateTimeOffset start, DateTimeOffset end) range;
        try { range = Period(from, to, clock); }
        catch (ArgumentException e) { return Results.BadRequest(new { message = e.Message }); }
        var (start, end) = range;

        var workouts = await db.WorkoutLogs.AsNoTracking().Where(x => x.PerformedAt >= start && x.PerformedAt < end)
            .Select(x => new { x.Id, x.UserId, x.PlanId, x.PlannedSessionId, x.PerformedAt, x.PerformedOn, x.Outcome,
                x.NonCompletionReason, x.Recovery, x.SessionRpe, x.PainReported, x.ExerciseFeedbackJson }).ToListAsync(ct);
        var adaptations = await db.PlanAdaptations.AsNoTracking().Where(x => x.AppliedAt >= start && x.AppliedAt < end)
            .Select(x => new { x.Id, x.PlanId, x.WorkoutLogId, x.Kind, x.ExerciseIds, x.SessionsChanged, x.AppliedAt,
                x.EvidenceAt, x.Source, x.EngineVersion, x.SchemaVersion, x.ChangesJson }).ToListAsync(ct);
        var observations = await db.MuscleWeekObservations.AsNoTracking().Where(x => x.UpdatedAt >= start && x.UpdatedAt < end)
            .Select(x => new { x.Id, x.UserId, x.PlanId, x.Period, x.MesocycleSequence, x.IsDeload, x.Muscle, x.Quality,
                x.Reason, x.PrescribedSets, x.PerformedSets, x.SessionsPlanned, x.SessionsDone, x.Exposures, x.CompletedSets,
                x.BelowRangeSets, x.PerformanceChangePercent, x.PerformanceSamples, x.FatigueSignals, x.FeedbackAnswers,
                x.LowStimulusAnswers, x.DiscomfortAnswers, x.PainSessions, x.ReturnFromAbsence, x.ProcessedThrough, x.UpdatedAt }).ToListAsync(ct);
        var decisions = await db.LongitudinalDecisions.AsNoTracking().Where(x => x.CreatedAt >= start && x.CreatedAt < end)
            .Select(x => new { x.Id, x.UserId, x.PlanId, x.MesocycleSequence, x.Muscle, x.Action, x.BaselineSets,
                x.PreviousSets, x.ProposedSets, x.AnalyzedFromPeriod, x.AnalyzedToPeriod, x.SignalsJson, x.Reason,
                x.PolicyVersion, x.CreatedAt }).ToListAsync(ct);

        var planIds = workouts.Select(x => x.PlanId).Concat(adaptations.Select(x => x.PlanId))
            .Concat(observations.Select(x => x.PlanId)).Concat(decisions.Select(x => x.PlanId)).Distinct().ToArray();
        var plans = await db.TrainingPlans.AsNoTracking().Where(x => planIds.Contains(x.Id))
            .Select(x => new { x.Id, x.UserId, x.Status, x.Origin, x.Goal, x.Experience, x.Split, x.StartDate,
                x.TotalWeeks, x.SchedulingMode, x.SessionsPerPeriod, x.MaxSessionMinutes, x.AvailableEquipment,
                x.IncludeDeloadPhases, x.DeloadEveryTrainingWeeks, x.Seed, x.EngineVersion, x.MacrocycleName, x.CreatedAt, x.UpdatedAt }).ToListAsync(ct);
        var mesocycles = await db.TrainingMesocycles.AsNoTracking().Where(x => planIds.Contains(x.PlanId))
            .Select(x => new { x.Id, x.PlanId, x.Sequence, x.PhaseSequenceNumber, x.PhaseType, x.IsDeload,
                x.StartWeek, x.Weeks, x.GenerationSnapshotJson, x.SnapshotSchemaVersion, x.EngineVersion,
                x.CatalogFingerprint, x.GeneratedAt, x.CreatedAt }).ToListAsync(ct);
        var sessions = await db.PlannedSessions.AsNoTracking().Where(x => planIds.Contains(x.PlanId))
            .Select(x => new { x.Id, x.PlanId, x.MesocycleId, x.WeekNumber, x.Date, x.SequenceIndex, x.Title,
                x.EstimatedDurationMinutes, x.PrescriptionJson, x.OriginalPrescriptionJson, x.OriginalPrescriptionSchemaVersion,
                x.PrescriptionSource, x.Status, x.Version, x.UpdatedAt }).ToListAsync(ct);
        var workoutIds = workouts.Select(x => x.Id).ToArray();
        var sets = await db.WorkoutSetLogs.AsNoTracking().Where(x => workoutIds.Contains(x.WorkoutLogId))
            .Select(x => new { x.Id, x.WorkoutLogId, x.ExerciseId, x.SetNumber, x.WeightKg, x.Repetitions, x.Rir,
                x.Completed, x.SkippedReason, x.PainReported }).ToListAsync(ct);

        // A fresh secret per package makes keys joinable inside this export, but not across exports or back to account IDs.
        var salt = RandomNumberGenerator.GetBytes(32);
        string Key(Guid id) => Convert.ToHexString(HMACSHA256.HashData(salt, id.ToByteArray()))[..24].ToLowerInvariant();
        var package = new
        {
            schemaVersion = 1, generatedAt = clock.GetUtcNow(), period = new { from = start, to = end },
            privacy = new { identifiers = "HMAC-SHA256 pseudonyms with a per-export secret; stable only inside this package",
                excluded = new[] { "names", "emails", "account IDs", "free-text workout notes", "plan limitations", "credentials" },
                warning = "Pseudonymised research data, not anonymous data. Keep the package private." },
            plans = plans.Select(x => new { id = Key(x.Id), user = Key(x.UserId), x.Status, x.Origin, x.Goal, x.Experience, x.Split,
                x.StartDate, x.TotalWeeks, x.SchedulingMode, x.SessionsPerPeriod, x.MaxSessionMinutes, x.AvailableEquipment,
                x.IncludeDeloadPhases, x.DeloadEveryTrainingWeeks, x.Seed, x.EngineVersion, x.MacrocycleName, x.CreatedAt, x.UpdatedAt }).ToArray(),
            mesocycles = mesocycles.Select(x => new { id = Key(x.Id), plan = Key(x.PlanId), x.Sequence, x.PhaseSequenceNumber,
                x.PhaseType, x.IsDeload, x.StartWeek, x.Weeks, x.GenerationSnapshotJson, x.SnapshotSchemaVersion,
                x.EngineVersion, x.CatalogFingerprint, x.GeneratedAt, x.CreatedAt }).ToArray(),
            sessions = sessions.Select(x => new { id = Key(x.Id), plan = Key(x.PlanId), mesocycle = Key(x.MesocycleId),
                x.WeekNumber, x.Date, x.SequenceIndex, x.Title, x.EstimatedDurationMinutes, x.PrescriptionJson,
                x.OriginalPrescriptionJson, x.OriginalPrescriptionSchemaVersion, x.PrescriptionSource, x.Status, x.Version, x.UpdatedAt }).ToArray(),
            adaptations = adaptations.Select(x => new { id = Key(x.Id), plan = Key(x.PlanId), workout = x.WorkoutLogId is null ? null : Key(x.WorkoutLogId.Value),
                x.Kind, x.ExerciseIds, x.SessionsChanged, x.AppliedAt, x.EvidenceAt, x.Source, x.EngineVersion, x.SchemaVersion, x.ChangesJson }).ToArray(),
            workouts = workouts.Select(x => new { id = Key(x.Id), user = Key(x.UserId), plan = Key(x.PlanId), session = Key(x.PlannedSessionId),
                x.PerformedAt, x.PerformedOn, x.Outcome, x.NonCompletionReason, x.Recovery, x.SessionRpe, x.PainReported, x.ExerciseFeedbackJson }).ToArray(),
            sets = sets.Select(x => new { id = Key(x.Id), workout = Key(x.WorkoutLogId), x.ExerciseId, x.SetNumber, x.WeightKg,
                x.Repetitions, x.Rir, x.Completed, x.SkippedReason, x.PainReported }).ToArray(),
            observations = observations.Select(x => new { id = Key(x.Id), user = Key(x.UserId), plan = Key(x.PlanId), x.Period,
                x.MesocycleSequence, x.IsDeload, x.Muscle, x.Quality, x.Reason, x.PrescribedSets, x.PerformedSets,
                x.SessionsPlanned, x.SessionsDone, x.Exposures, x.CompletedSets, x.BelowRangeSets, x.PerformanceChangePercent,
                x.PerformanceSamples, x.FatigueSignals, x.FeedbackAnswers, x.LowStimulusAnswers, x.DiscomfortAnswers,
                x.PainSessions, x.ReturnFromAbsence, x.ProcessedThrough, x.UpdatedAt }).ToArray(),
            decisions = decisions.Select(x => new { id = Key(x.Id), user = Key(x.UserId), plan = Key(x.PlanId), x.MesocycleSequence,
                x.Muscle, x.Action, x.BaselineSets, x.PreviousSets, x.ProposedSets, x.AnalyzedFromPeriod, x.AnalyzedToPeriod,
                x.SignalsJson, x.Reason, x.PolicyVersion, x.CreatedAt }).ToArray()
        };

        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, true))
        {
            AddJson(archive, "manifest.json", new { package.schemaVersion, package.generatedAt, package.period, package.privacy,
                tables = new[] { "plans", "mesocycles", "sessions", "adaptations", "workouts", "sets", "observations", "decisions" } });
            AddJson(archive, "research.json", package);
            AddCsv(archive, "plans.csv", package.plans);
            AddCsv(archive, "mesocycles.csv", package.mesocycles);
            AddCsv(archive, "sessions.csv", package.sessions);
            AddCsv(archive, "adaptations.csv", package.adaptations);
            AddCsv(archive, "workouts.csv", package.workouts);
            AddCsv(archive, "sets.csv", package.sets);
            AddCsv(archive, "observations.csv", package.observations);
            AddCsv(archive, "decisions.csv", package.decisions);
        }
        return Results.File(memory.ToArray(), "application/zip", $"trazo-intelligence-{start:yyyyMMdd}-{end:yyyyMMdd}.zip");
    }

    private static void AddJson<T>(ZipArchive archive, string name, T value)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        JsonSerializer.Serialize(stream, value, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
    }

    private static void AddCsv<T>(ZipArchive archive, string name, IReadOnlyCollection<T> rows)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(true));
        var properties = typeof(T).GetProperties(BindingFlags.Instance | BindingFlags.Public);
        writer.WriteLine(string.Join(",", properties.Select(p => Csv(p.Name))));
        foreach (var row in rows)
            writer.WriteLine(string.Join(",", properties.Select(p => Csv(p.GetValue(row) is { } value
                ? value is string text ? text : JsonSerializer.Serialize(value) : ""))));
    }

    private static string Csv(string value) => "\"" + value.Replace("\"", "\"\"").Replace("\r", " ").Replace("\n", " ") + "\"";
}