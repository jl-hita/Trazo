namespace Trazo.Training;

/// <summary>How much external load an exercise accepts, read from its reviewed equipment and level.</summary>
internal enum TrainingLoadTier
{
    /// <summary>Entry-level bodyweight movement (knee push-up, bodyweight squat, glute bridge): a regression for a trained user.</summary>
    Bodyweight = 0,
    /// <summary>Loadable only a little: bands, suspension trainers and squats held in the hands (goblet, dumbbell squat).</summary>
    Limited = 1,
    /// <summary>Dumbbells, kettlebells, cables and demanding bodyweight lifts (pull-up, dip, pike push-up).</summary>
    Loadable = 2,
    /// <summary>Barbell, trap bar, Smith or plate/stack machine, or explicitly weighted: fit for low repetitions and high loads.</summary>
    Heavy = 3
}

/// <summary>Family of implement, used to keep the rotation pool of a pattern varied (one barbell lift, one machine...).</summary>
internal enum TrainingImplementClass
{
    Barbell,
    Machine,
    Cable,
    FreeWeight,
    Bodyweight
}

/// <summary>
/// Engine-side ranking of the candidates of a movement pattern. The catalog's SelectionPriority stays the base value
/// (administrators curate it), and the engine corrects it with what the profile needs: an exercise that can be loaded
/// when the user is past the beginner stage and a barbell lift in strength plans.
/// The corrections are derived from reviewed metadata (pattern, muscles, equipment, minimum level), never from names,
/// so they also apply to exercises added later and to a production catalog this repository has never seen.
/// All figures are internal product heuristics, not guideline values.
/// </summary>
internal static class TrainingExerciseSelection
{
    private static readonly HashSet<string> BarbellTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "barbell", "trap-bar", "hex-bar", "safety-bar"
    };

    private static readonly HashSet<string> FreeWeightTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "dumbbell", "dumbbells", "kettlebell", "ez-bar", "weight-plate", "plate", "medicine-ball", "sandbag"
    };

    private static readonly HashSet<string> ElasticTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "resistance-band", "band", "suspension-trainer", "trx", "rings"
    };

    // Supports that hold the body or the implement but add no load by themselves.
    private static readonly HashSet<string> SupportTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "bench", "pull-up-bar", "dip-station", "squat-rack", "power-rack", "rack", "stability-ball", "ab-wheel",
        "glute-ham-developer", "box", "step", "mat", "wall", "chair"
    };

    private static readonly HashSet<TrainingMovementPattern> MainPatterns =
    [
        TrainingMovementPattern.Squat, TrainingMovementPattern.Hinge, TrainingMovementPattern.HorizontalPush,
        TrainingMovementPattern.VerticalPush, TrainingMovementPattern.HorizontalPull, TrainingMovementPattern.VerticalPull,
        TrainingMovementPattern.SingleLeg
    ];

    public static bool IsMainPattern(TrainingMovementPattern pattern) => MainPatterns.Contains(pattern);

    private static IEnumerable<string> Tokens(TrainingExerciseCandidate candidate) =>
        candidate.RequiredEquipment.Select(TrainingProgramGenerator.NormalizeEquipmentToken).Where(token => token.Length > 0);

    private static bool IsMachineToken(string token) =>
        token.Equals("machine", StringComparison.OrdinalIgnoreCase) || token.EndsWith("-machine", StringComparison.OrdinalIgnoreCase);

    public static TrainingImplementClass ImplementClass(TrainingExerciseCandidate candidate)
    {
        var tokens = Tokens(candidate).ToArray();
        if (tokens.Any(BarbellTokens.Contains)) return TrainingImplementClass.Barbell;
        if (tokens.Any(IsMachineToken)) return TrainingImplementClass.Machine;
        if (tokens.Any(token => token.Equals("cable", StringComparison.OrdinalIgnoreCase))) return TrainingImplementClass.Cable;
        if (tokens.Any(FreeWeightTokens.Contains)) return TrainingImplementClass.FreeWeight;
        return TrainingImplementClass.Bodyweight;
    }

    public static TrainingLoadTier LoadTier(TrainingExerciseCandidate candidate)
    {
        var tokens = Tokens(candidate).ToArray();
        if (tokens.Any(BarbellTokens.Contains) || tokens.Any(IsMachineToken))
            return TrainingLoadTier.Heavy;
        if (tokens.Any(token => token.Equals("cable", StringComparison.OrdinalIgnoreCase)))
            return TrainingLoadTier.Loadable;
        if (tokens.Any(FreeWeightTokens.Contains))
            // A weight held in the hands limits a squat long before the legs are the limit.
            return candidate.MovementPattern == TrainingMovementPattern.Squat ? TrainingLoadTier.Limited : TrainingLoadTier.Loadable;
        if (tokens.Any(ElasticTokens.Contains))
            return TrainingLoadTier.Limited;
        // An implement this table does not know is treated as a real load rather than as a regression.
        if (tokens.Any(token => !SupportTokens.Contains(token)))
            return TrainingLoadTier.Loadable;
        // Bodyweight only. The catalog marks the demanding ones (pull-up, dip, pike push-up, pistol) as intermediate or
        // advanced; the beginner ones are the entry-level variants.
        return candidate.MinimumExperience == TrainingExperience.Beginner ? TrainingLoadTier.Bodyweight : TrainingLoadTier.Loadable;
    }

    public static bool IsHeavy(TrainingExerciseCandidate candidate) => LoadTier(candidate) == TrainingLoadTier.Heavy;

    /// <summary>The curated priority corrected for this goal and level. Core and accessory exercises keep the catalog value.</summary>
    public static int Score(TrainingExerciseCandidate candidate, TrainingGoal goal, TrainingExperience experience)
    {
        var score = candidate.SelectionPriority;
        if (!IsMainPattern(candidate.MovementPattern)) return score;

        var beginner = experience == TrainingExperience.Beginner;
        var strength = goal == TrainingGoal.Strength;
        score += LoadTier(candidate) switch
        {
            TrainingLoadTier.Bodyweight => beginner && !strength ? -12 : -30,
            TrainingLoadTier.Limited => beginner ? (strength ? -12 : 0) : -25,
            TrainingLoadTier.Loadable => strength ? -6 : 0,
            _ => 0
        };

        if (strength)
        {
            if (ImplementClass(candidate) == TrainingImplementClass.Barbell)
                score += 12;
            // Between lifts of the same pattern, the one that moves more muscle groups as prime movers is the better
            // main lift (a deadlift before a Romanian deadlift).
            score += Math.Min(4, 2 * (candidate.PrimaryMuscleGroups.Count - 1));
        }

        return score;
    }

    /// <summary>
    /// Deterministic order for candidates with the same score. It depends on the plan seed, so equally good exercises
    /// are not always resolved in favour of the alphabetically first id, and it is stable across runtimes
    /// (string.GetHashCode is randomized per process).
    /// </summary>
    public static uint TieBreak(int seed, string exerciseId)
    {
        unchecked
        {
            var hash = 2166136261u ^ (uint)seed;
            hash *= 16777619u;
            foreach (var character in exerciseId)
            {
                hash ^= char.ToLowerInvariant(character);
                hash *= 16777619u;
            }
            // Final avalanche so ids that differ in one character do not stay adjacent.
            hash ^= hash >> 16;
            hash *= 0x85ebca6bu;
            hash ^= hash >> 13;
            return hash;
        }
    }
}
