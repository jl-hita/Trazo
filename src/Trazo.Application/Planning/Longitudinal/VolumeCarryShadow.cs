namespace Trazo.Application.Planning.Longitudinal;

/// <summary>One set the user's answers added (+1) or took away (-1) in an exercise during a block, by the muscles it trains directly.</summary>
public sealed record BlockSetChange(IReadOnlyList<string> Muscles, int Sets);

/// <summary>What a block showed about one muscle.</summary>
public sealed record BlockMuscleResult(string Muscle, int DecliningPeriods, int PainSessions);

/// <param name="NetSets">Sets the answers added (positive) or took away during the block.</param>
/// <param name="ProposedSets">Weekly sets that WOULD be carried into the next block. Never applied.</param>
public sealed record ShadowCarryMuscle(string Muscle, int NetSets, int ProposedSets, int DecliningPeriods, int PainSessions);

/// <summary>The carry that was worked out, and kept without applying it, when a block was generated.</summary>
public sealed record ShadowCarry(string PolicyVersion, int FromSequence, IReadOnlyList<ShadowCarryMuscle> Muscles);

/// <summary>
/// "Shadow" carry of volume between blocks: it works out how many of the sets a person's answers added in a block would be carried
/// into the next one, and only that. Nothing reads the result to build a plan: it is stored with the block so it can be compared with
/// what really happened before anyone decides to apply it. The limits are product values, not measured ones.
/// </summary>
public static class VolumeCarryShadow
{
    public const string Version = "carry-shadow-1";
    public const int MaximumCarriedSets = 2;

    public static ShadowCarry Compute(
        int fromSequence, IReadOnlyList<BlockSetChange> changes, IReadOnlyList<BlockMuscleResult> results)
    {
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(results);
        var muscles = changes.SelectMany(c => c.Muscles.Distinct(StringComparer.Ordinal).Select(m => (Muscle: m, c.Sets)))
            .GroupBy(x => x.Muscle, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g =>
            {
                var net = g.Sum(x => x.Sets);
                var declining = results.Where(r => r.Muscle == g.Key).Sum(r => r.DecliningPeriods);
                var pain = results.Where(r => r.Muscle == g.Key).Sum(r => r.PainSessions);
                // Only a gain could be carried, and only when the block held: lowering is already the longitudinal layer's job.
                var held = declining < LongitudinalPolicy.RepeatedUnfavorablePeriods && pain == 0;
                return new ShadowCarryMuscle(g.Key, net, held ? Math.Clamp(net, 0, MaximumCarriedSets) : 0, declining, pain);
            })
            .Where(m => m.NetSets != 0)
            .ToList();
        return new ShadowCarry(Version, fromSequence, muscles);
    }
}
