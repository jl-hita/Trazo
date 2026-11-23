using System.Text.RegularExpressions;
using Trazo.Domain.Planning;

namespace Trazo.Infrastructure.Accounts;

/// <summary>
/// Builds the anonymous research rows kept when an account is deleted (docs/account-deletion.md, "Anonymous engine research").
/// It is the single place that decides what leaves the user's data, and it works by ALLOW-LIST: every value is either a number,
/// a flag, a member of a closed list, or a short token that matches a strict pattern; anything else becomes null or is dropped.
/// The output holds no user id, plan id, original row id or date, and no mapping is kept anywhere.
/// </summary>
public static class EngineResearchSampler
{
    /// <summary>Codes the observer writes in <c>MuscleWeekObservation.Reason</c>; any other value is dropped.</summary>
    public static readonly IReadOnlySet<string> ObservationReasons = new HashSet<string>(StringComparer.Ordinal)
    {
        "deload", "preparation", "pain", "return_from_absence", "date_unreliable", "no_valid_exposure", "low_completion"
    };

    /// <summary>Actions of <c>LongitudinalPolicy</c>.</summary>
    public static readonly IReadOnlySet<string> DecisionActions = new HashSet<string>(StringComparer.Ordinal)
    {
        "decrease", "restore", "hold", "abstain"
    };

    private static readonly IReadOnlySet<string> Qualities = new HashSet<string>(StringComparer.Ordinal) { "valid", "partial", "excluded" };
    private static readonly IReadOnlySet<string> Readiness = new HashSet<string>(StringComparer.Ordinal) { "Regular", "New", "Returning" };
    // Names of enum-like values (goal, experience, split, muscle): letters, digits, underscore and hyphen only, so no sentence fits.
    private static readonly Regex Token = new("^[A-Za-z][A-Za-z0-9_-]{0,29}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    // Version labels such as "engine-4.2" or "longitudinal-2.0".
    private static readonly Regex Version = new("^[A-Za-z0-9][A-Za-z0-9._-]{0,39}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public sealed record Result(IReadOnlyList<EngineResearchObservation> Observations, IReadOnlyList<EngineResearchDecision> Decisions);

    /// <param name="grouped">True only with the user's research consent: rows of one plan then share a new random <c>SampleGroupId</c>.</param>
    public static Result Build(
        IReadOnlyCollection<TrainingPlan> plans, IReadOnlyCollection<MuscleWeekObservation> observations,
        IReadOnlyCollection<LongitudinalDecision> decisions, bool grouped)
    {
        var contexts = plans.ToDictionary(p => p.Id, p => (Context: ContextOf(p), Group: grouped ? Guid.NewGuid() : (Guid?)null));
        var outObservations = new List<EngineResearchObservation>();
        var outDecisions = new List<EngineResearchDecision>();

        foreach (var o in observations)
        {
            if (!contexts.TryGetValue(o.PlanId, out var plan) || !Token.IsMatch(o.Muscle) || !Qualities.Contains(o.Quality)) continue;
            outObservations.Add(new EngineResearchObservation
            {
                Id = Guid.NewGuid(), SampleGroupId = plan.Group, Period = o.Period, MesocycleSequence = o.MesocycleSequence,
                IsDeload = o.IsDeload, Muscle = o.Muscle, Quality = o.Quality,
                Reason = o.Reason is not null && ObservationReasons.Contains(o.Reason) ? o.Reason : null,
                PrescribedSets = o.PrescribedSets, PerformedSets = o.PerformedSets, SessionsPlanned = o.SessionsPlanned,
                SessionsDone = o.SessionsDone, Exposures = o.Exposures, CompletedSets = o.CompletedSets, BelowRangeSets = o.BelowRangeSets,
                PerformanceChangePercent = o.PerformanceChangePercent, PerformanceSamples = o.PerformanceSamples,
                FatigueSignals = o.FatigueSignals, FeedbackAnswers = o.FeedbackAnswers, LowStimulusAnswers = o.LowStimulusAnswers,
                DiscomfortAnswers = o.DiscomfortAnswers, PainSessions = o.PainSessions, ReturnFromAbsence = o.ReturnFromAbsence,
                PlanGoal = plan.Context.Goal, PlanExperience = plan.Context.Experience, PlanSplit = plan.Context.Split,
                PlanReadiness = plan.Context.Readiness, PlanEngineVersion = plan.Context.EngineVersion,
                PlanSessionsPerPeriod = plan.Context.SessionsPerPeriod, PlanTotalPeriodsBand = plan.Context.TotalPeriodsBand
            });
        }

        // The decision's Reason (generated prose) and SignalsJson (unversioned JSON) are never copied.
        foreach (var d in decisions)
        {
            if (!contexts.TryGetValue(d.PlanId, out var plan) || !Token.IsMatch(d.Muscle) || !DecisionActions.Contains(d.Action)
                || !Version.IsMatch(d.PolicyVersion)) continue;
            outDecisions.Add(new EngineResearchDecision
            {
                Id = Guid.NewGuid(), SampleGroupId = plan.Group, MesocycleSequence = d.MesocycleSequence, Muscle = d.Muscle, Action = d.Action,
                BaselineSets = d.BaselineSets, PreviousSets = d.PreviousSets, ProposedSets = d.ProposedSets,
                AnalyzedFromPeriod = d.AnalyzedFromPeriod, AnalyzedToPeriod = d.AnalyzedToPeriod, PolicyVersion = d.PolicyVersion,
                PlanGoal = plan.Context.Goal, PlanExperience = plan.Context.Experience, PlanSplit = plan.Context.Split,
                PlanReadiness = plan.Context.Readiness, PlanEngineVersion = plan.Context.EngineVersion,
                PlanSessionsPerPeriod = plan.Context.SessionsPerPeriod, PlanTotalPeriodsBand = plan.Context.TotalPeriodsBand
            });
        }
        return new(outObservations, outDecisions);
    }

    /// <summary>The generalised profile of a plan: closed vocabularies and coarse numbers only.</summary>
    public static EngineResearchPlanContext ContextOf(TrainingPlan p) => new()
    {
        Goal = TokenOrNull(p.Goal), Experience = TokenOrNull(p.Experience), Split = TokenOrNull(p.Split),
        Readiness = Readiness.Contains(p.Readiness) ? p.Readiness : null,
        EngineVersion = Version.IsMatch(p.EngineVersion) ? p.EngineVersion : null,
        SessionsPerPeriod = Math.Clamp(p.SessionsPerPeriod, 0, 7),
        TotalPeriodsBand = Math.Min(52, (Math.Max(p.TotalWeeks, 1) + 3) / 4 * 4)
    };

    private static string? TokenOrNull(string value) => Token.IsMatch(value) ? value : null;
}
