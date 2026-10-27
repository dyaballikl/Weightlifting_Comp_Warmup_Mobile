using WeightliftingCompWarmup.Core.Models;

namespace WeightliftingCompWarmup.Core.Services;

/// <summary>
/// Core warmup calculation engine ported from <c>Utilities.cs</c> in the original WinForms application.
/// Calculates warmup weight progressions, jump smoothing, step durations, and reverse countdown offsets.
/// </summary>
public static class WarmupCalculator
{
    /// <summary>
    /// Formats a duration in seconds into human-readable <c>XhYmZs</c>, <c>YmZs</c>, or <c>Zs</c>.
    /// Matches <c>Utilities.FormatSeconds</c> in the original WinForms app.
    /// </summary>
    public static string FormatSeconds(int totalSeconds)
    {
        if (totalSeconds < 0)
        {
            totalSeconds = 0;
        }

        TimeSpan time = TimeSpan.FromSeconds(totalSeconds);
        string seconds = $"{time.Seconds}s";
        string minutes = time.TotalMinutes >= 1 ? $"{time.Minutes}m" : string.Empty;
        string hours = time.TotalHours >= 1 ? $"{(int)time.TotalHours}h" : string.Empty;

        return $"{hours}{minutes}{seconds}";
    }

    /// <summary>
    /// Determines the weight jump (in kg) from a given <paramref name="currentWeight"/> using the profile's jump table.
    /// Matches <c>Utilities.GetJumpForWeight</c> in the original WinForms app.
    /// </summary>
    public static int GetJumpForWeight(IReadOnlyDictionary<int, int> jumps, int currentWeight)
    {
        if (jumps == null || jumps.Count == 0)
        {
            return 0;
        }

        // Phase 1: Find the active jump rule with the highest threshold <= currentWeight
        var baseJumpKvp = jumps
            .Where(kvp => kvp.Key <= currentWeight && kvp.Key != 0 && kvp.Value != 0)
            .OrderByDescending(kvp => kvp.Key)
            .FirstOrDefault();

        if (baseJumpKvp.Key == 0 && baseJumpKvp.Value == 0)
        {
            baseJumpKvp = jumps
                .Where(kvp => kvp.Key > 0 && kvp.Value > 0)
                .OrderBy(kvp => kvp.Key)
                .FirstOrDefault();
        }

        if (baseJumpKvp.Key == 0 && baseJumpKvp.Value == 0)
        {
            return 0;
        }

        // Phase 2: Check if a higher threshold rule would land at or below (currentWeight + baseJump)
        var nextJumpKvp = jumps
            .Where(kvp =>
                kvp.Key > baseJumpKvp.Key &&
                kvp.Key + kvp.Value <= currentWeight + baseJumpKvp.Value &&
                kvp.Key != 0 &&
                kvp.Value != 0)
            .OrderBy(kvp => kvp.Key)
            .FirstOrDefault();

        if (nextJumpKvp.Key != 0)
        {
            return nextJumpKvp.Key - currentWeight + nextJumpKvp.Value;
        }

        return baseJumpKvp.Value;
    }

    /// <summary>
    /// Smooths the final warmup jump so the athlete doesn't make an unevenly tiny jump right before their opener.
    /// Includes the midpoint re-insertion fix so the smoothed second-to-last warmup weight replaces the unsmoothed weight.
    /// </summary>
    public static void SmoothLastJumps(HashSet<int> weights, int openerWeight)
    {
        if (weights == null || weights.Count == 0)
        {
            return;
        }

        var sortedDescending = weights.OrderByDescending(x => x).ToList();
        int finalWarmup = sortedDescending.ElementAtOrDefault(0);
        int secondToFinalWarmup = sortedDescending.ElementAtOrDefault(1);
        int thirdToFinalWarmup = sortedDescending.ElementAtOrDefault(2);

        if (openerWeight > finalWarmup)
        {
            thirdToFinalWarmup = secondToFinalWarmup;
            secondToFinalWarmup = finalWarmup;
            finalWarmup = openerWeight;
        }

        if (finalWarmup > 0 && secondToFinalWarmup > 0 && thirdToFinalWarmup > 0)
        {
            if (finalWarmup - thirdToFinalWarmup > 3)
            {
                if (finalWarmup - secondToFinalWarmup != secondToFinalWarmup - thirdToFinalWarmup &&
                    finalWarmup - secondToFinalWarmup != secondToFinalWarmup - thirdToFinalWarmup + 1)
                {
                    weights.Remove(secondToFinalWarmup);
                    int smoothedMidpoint = (int)Math.Floor((finalWarmup - thirdToFinalWarmup) / 2m + thirdToFinalWarmup);
                    if (smoothedMidpoint > thirdToFinalWarmup && smoothedMidpoint < finalWarmup)
                    {
                        weights.Add(smoothedMidpoint);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Generates the ordered warmup barbell weights for the specified parameters.
    /// Matches <c>Utilities.hashSet_Weights</c> in the original WinForms app.
    /// </summary>
    public static List<int> CalculateWarmupWeights(
        int barbellWeight,
        int openerWeight,
        bool openerInWarmup,
        IReadOnlyDictionary<int, int> jumps)
    {
        var weights = new HashSet<int>();
        int currentWeight = 0;
        int jump = barbellWeight;

        while (currentWeight + jump < openerWeight)
        {
            currentWeight += jump;
            weights.Add(currentWeight);

            jump = GetJumpForWeight(jumps, currentWeight);
            if (jump == 0)
            {
                break;
            }
        }

        if (openerInWarmup)
        {
            weights.Add(openerWeight);
        }

        if (weights.Count == 0)
        {
            weights.Add(barbellWeight);
        }

        SmoothLastJumps(weights, openerWeight);

        return weights.OrderBy(w => w).ToList();
    }

    /// <summary>
    /// Builds the complete sequence of warmup steps (including the leading <c>PreStep</c> "wait" step at index 0,
    /// all configured <see cref="ExtraItem"/> steps, and all calculated barbell warmup lifts).
    /// Matches <c>Utilities.x_Steps</c> in the original WinForms app.
    /// </summary>
    public static List<WarmupStep> BuildSteps(LiftType liftType, Profile profile)
    {
        int openerWeight;
        int secondsEnd;
        bool openerInWarmup;
        List<ExtraItem> extras;
        Dictionary<int, int> jumps;
        Dictionary<int, int> times;

        if (liftType == LiftType.Snatch)
        {
            openerWeight = profile.SnatchOpenerWeight;
            secondsEnd = profile.SnatchSecondsEnd;
            openerInWarmup = profile.SnatchOpenerInWarmup;
            extras = profile.SnatchExtras;
            jumps = profile.SnatchJumps;
            times = profile.SnatchTimes;
        }
        else
        {
            openerWeight = profile.CjOpenerWeight;
            secondsEnd = profile.CjSecondsEnd;
            openerInWarmup = profile.CjOpenerInWarmup;
            extras = profile.CjExtras;
            jumps = profile.CjJumps;
            times = profile.CjTimes;
        }

        var steps = new List<WarmupStep>();
        int totalSeconds = extras.Sum(x => x.Length);
        int order = 1;

        foreach (var extra in extras.OrderBy(x => x.Order))
        {
            steps.Add(new WarmupStep(
                action: extra.Action,
                weight: 0,
                length: extra.Length,
                totalLength: totalSeconds,
                totalLengthReverse: 0,
                order: order++,
                preStep: false,
                isOpener: false));
        }

        var weights = CalculateWarmupWeights(profile.BarbellWeight, openerWeight, openerInWarmup, jumps);
        int maxWeight = weights.Count > 0 ? weights.Max() : 0;

        foreach (int weight in weights)
        {
            int stepSeconds = times
                .Where(kvp => kvp.Key <= weight)
                .OrderByDescending(kvp => kvp.Key)
                .Select(kvp => kvp.Value)
                .FirstOrDefault();

            if (stepSeconds == 0 && times.Count > 0)
            {
                stepSeconds = times
                    .OrderBy(kvp => kvp.Key)
                    .Select(kvp => kvp.Value)
                    .FirstOrDefault();
            }

            if (weight == maxWeight)
            {
                stepSeconds += secondsEnd;
            }

            totalSeconds += stepSeconds;

            steps.Add(new WarmupStep(
                action: "Lift",
                weight: weight,
                length: stepSeconds,
                totalLength: totalSeconds,
                totalLengthReverse: 0,
                order: order++,
                preStep: false,
                isOpener: false));
        }

        int reverseTotal = 0;
        for (int i = steps.Count - 1; i >= 0; i--)
        {
            reverseTotal += steps[i].Length;
            steps[i].TotalLengthReverse = reverseTotal;
        }

        steps.Insert(0, new WarmupStep(
            action: "wait",
            weight: 0,
            length: 0,
            totalLength: 0,
            totalLengthReverse: totalSeconds,
            order: 0,
            preStep: true,
            isOpener: false));

        return steps;
    }

    /// <summary>
    /// Builds the step list used in the Live Warmup Runner, which includes the trailing
    /// <c>"open at {openerWeight}"</c> competition opener step.
    /// </summary>
    public static List<WarmupStep> BuildLiveSteps(LiftType liftType, Profile profile)
    {
        var steps = BuildSteps(liftType, profile);
        int openerWeight = liftType == LiftType.Snatch ? profile.SnatchOpenerWeight : profile.CjOpenerWeight;

        steps.Add(new WarmupStep(
            action: $"open at {openerWeight}",
            weight: openerWeight,
            length: 0,
            totalLength: 0,
            totalLengthReverse: 0,
            order: steps.Count,
            preStep: false,
            isOpener: true));

        return steps;
    }
}
