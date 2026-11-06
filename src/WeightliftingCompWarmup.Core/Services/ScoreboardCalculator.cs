using System;
using System.Collections.Generic;
using System.Linq;
using WeightliftingCompWarmup.Core.Models;

namespace WeightliftingCompWarmup.Core.Services;

/// <summary>
/// Status of a single attempt cell (1st, 2nd, or 3rd attempt) on a competition scoreboard.
/// </summary>
public enum ScoreboardAttemptStatus
{
    /// <summary>Cell is blank / not yet declared.</summary>
    Empty = 0,
    /// <summary>Attempt has been completed and judged a Good Lift.</summary>
    Good = 1,
    /// <summary>Attempt has been completed and judged a No Lift (Miss).</summary>
    Miss = 2,
    /// <summary>Athlete passed/forfeited this attempt ('-').</summary>
    Pass = 3,
    /// <summary>Attempt weight is currently declared on the board as an upcoming lift.</summary>
    Pending = 4
}

/// <summary>
/// Indicates how an attempt's weight was determined in the simulation.
/// </summary>
public enum ScoreboardAttemptOrigin
{
    CompletedGood,
    CompletedMiss,
    CompletedPass,
    DeclaredOnBoard,
    ProjectedFromAutoOneKg,
    ProjectedSCurve,
    UserOverride
}

/// <summary>
/// Represents a single raw attempt cell extracted from the scoreboard for a lifter.
/// </summary>
public record ScoreboardAttemptCell(
    int? WeightKg,
    ScoreboardAttemptStatus Status);

/// <summary>
/// Represents one athlete row extracted from a competition scoreboard.
/// </summary>
public class ScoreboardLifterRow
{
    /// <summary>1-based top-to-bottom row index on the scoreboard.</summary>
    public int OrderNumber { get; set; }

    /// <summary>Explicit Lot / Start / Seed number if shown on the scoreboard; falls back to <see cref="OrderNumber"/>.</summary>
    public int? LotNumber { get; set; }

    /// <summary>Effective seed/lot number used for Attempt 1 IWF tie-breaking.</summary>
    public int EffectiveSeedNumber => LotNumber is > 0 ? LotNumber.Value : OrderNumber;

    /// <summary>Athlete display name.</summary>
    public string Name { get; set; } = string.Empty;

    public ScoreboardAttemptCell Snatch1 { get; set; } = new(null, ScoreboardAttemptStatus.Empty);
    public ScoreboardAttemptCell Snatch2 { get; set; } = new(null, ScoreboardAttemptStatus.Empty);
    public ScoreboardAttemptCell Snatch3 { get; set; } = new(null, ScoreboardAttemptStatus.Empty);

    public ScoreboardAttemptCell Cj1 { get; set; } = new(null, ScoreboardAttemptStatus.Empty);
    public ScoreboardAttemptCell Cj2 { get; set; } = new(null, ScoreboardAttemptStatus.Empty);
    public ScoreboardAttemptCell Cj3 { get; set; } = new(null, ScoreboardAttemptStatus.Empty);

    /// <summary>Optional user manual weight overrides (in whole kg) for Snatch attempts.</summary>
    public int? OverrideAttempt1Kg { get; set; }
    public int? OverrideAttempt2Kg { get; set; }
    public int? OverrideAttempt3Kg { get; set; }

    /// <summary>Optional user manual weight overrides (in whole kg) for Clean &amp; Jerk attempts.</summary>
    public int? OverrideCjAttempt1Kg { get; set; }
    public int? OverrideCjAttempt2Kg { get; set; }
    public int? OverrideCjAttempt3Kg { get; set; }

    /// <summary>Raw text printed in the leftmost column before the athlete's name (e.g. "1".."11" or a prior group letter such as "D").</summary>
    public string? LeftColumnText { get; set; }

    /// <summary>True if the row is excluded from the analysis (either automatically as a prior-session row or manually by the user).</summary>
    public bool IsExcluded { get; set; }
}

/// <summary>
/// Resolved state of one attempt (1, 2, or 3) for a lifter after applying S-curve projections and overrides.
/// </summary>
public record ResolvedLifterAttempt(
    int LifterIndex,
    string LifterName,
    int SeedNumber,
    int AttemptNumber,
    int WeightKg,
    ScoreboardAttemptOrigin Origin,
    bool IsCompleted,
    bool IsPass,
    int? JumpFromPreviousKg);

/// <summary>
/// A single chronological platform call in the reconstructed + projected competition sequence.
/// </summary>
public record ScoreboardCallEntry(
    int GlobalCallNumber,
    int PhaseCallNumber,
    int LifterIndex,
    string LifterName,
    int SeedNumber,
    int AttemptNumber,
    int WeightKg,
    ScoreboardAttemptOrigin Origin,
    bool IsCompleted,
    bool IsUserTargetOpener,
    bool IsBeforeUserOpener,
    string SequenceReasonNote);

/// <summary>
/// Per-lifter summary for the "Show Your Work" breakdown table.
/// </summary>
public record ScoreboardLifterSummary(
    int LifterIndex,
    int OrderNumber,
    int SeedNumber,
    string Name,
    bool IsUser,
    bool IsExcluded,
    IReadOnlyList<ResolvedLifterAttempt> Attempts,
    int LiftsBeforeUserCount,
    int CompletedLiftsCount);

/// <summary>
/// Complete output of the deterministic IWF scoreboard analysis and call-order simulation.
/// </summary>
public record ScoreboardAnalysisResult(
    LiftType Discipline,
    int UserLifterIndex,
    string UserLifterName,
    int UserOpenerWeightKg,
    int LiftsOut,
    int LiftsPassed,
    int? RemainingSnatchLifts,
    IReadOnlyList<ScoreboardLifterSummary> LifterSummaries,
    IReadOnlyList<ScoreboardCallEntry> CompletedCalls,
    IReadOnlyList<ScoreboardCallEntry> UpcomingCallsBeforeUser,
    ScoreboardCallEntry? UserOpenerCall,
    IReadOnlyList<ScoreboardCallEntry> UpcomingCallsAfterUser,
    bool IsBarPastUserOpener = false,
    int? MaxCompletedWeightKg = null,
    string? BarPastUserOpenerMessage = null,
    int? BoardDeclaredUserOpenerKg = null,
    bool IsDisciplineConcluded = false,
    string? DisciplineConcludedMessage = null,
    bool IsUserOpenerCompleted = false,
    string? UserOpenerCompletedMessage = null,
    bool HasDisciplineWeightsOnBoard = true);

/// <summary>
/// Live step-by-step progress update emitted during AI scoreboard scanning.
/// </summary>
public record ScoreboardScanProgress(
    int StepNumber,
    int TotalSteps,
    string StageTitle,
    string DetailMessage,
    double ProgressFraction,
    bool IsFallbackOrRetry = false);

/// <summary>
/// Deterministic IWF competition sequencing simulator and S-curve attempt increment projector.
/// </summary>
public static class ScoreboardCalculator
{
    /// <summary>
    /// Computes the raw unrounded logistic S-curve increment bounded between 1.0 kg and 5.0 kg,
    /// centered at 65 kg (where the increment is 3.0 kg).
    /// </summary>
    public static double CalculateRawSCurveIncrement(int currentWeightKg)
    {
        return 1.0 + (4.0 / (1.0 + Math.Exp(-0.05 * (currentWeightKg - 65.0))));
    }

    /// <summary>
    /// Calculates the expected whole-kilogram jump for a lifter's next attempt using the S-curve model:
    /// <list type="bullet">
    ///   <item>After a missed lift (<see cref="ScoreboardAttemptStatus.Miss"/>), returns <c>0</c> kg (repeat weight).</item>
    ///   <item>From 1st to 2nd attempt: rounds <see cref="CalculateRawSCurveIncrement"/> to whole kg in [1, 5]
    ///         (e.g. 20kg -> +1kg, 40kg -> +2kg, 65kg -> +3kg, 90kg -> +4kg, 130kg+ -> +5kg).</item>
    ///   <item>From 2nd to 3rd attempt: steps down slightly so roughly 40% of the time in the 4kg/5kg brackets
    ///         it is 1kg less than the 1st->2nd jump, while never stepping down if the first jump was 3kg or less.</item>
    /// </list>
    /// </summary>
    public static int CalculateProjectedJumpKg(
        int previousWeightKg,
        int fromAttemptNumber,
        ScoreboardAttemptStatus previousStatus,
        int? firstJumpKg = null)
    {
        if (previousWeightKg <= 0)
        {
            return 0;
        }

        if (previousStatus == ScoreboardAttemptStatus.Miss)
        {
            return 0;
        }

        double rawAtCurrent = CalculateRawSCurveIncrement(previousWeightKg);
        int standardJump = Math.Clamp((int)Math.Round(rawAtCurrent, MidpointRounding.AwayFromZero), 1, 5);

        if (fromAttemptNumber <= 1)
        {
            return standardJump;
        }

        // 2nd -> 3rd attempt jump:
        int effectiveFirstJump = firstJumpKg is > 0 ? firstJumpKg.Value : standardJump;

        // Rule: If the first jump was 3 kg or less, do not let it step down from there.
        if (effectiveFirstJump <= 3)
        {
            return effectiveFirstJump;
        }

        // First jump was 4 kg or 5 kg: step down by 1 kg roughly 40% of the time within that bracket,
        // never dropping below 3 kg.
        int baseWeightForCurve = previousWeightKg - effectiveFirstJump;
        if (baseWeightForCurve < 15)
        {
            baseWeightForCurve = previousWeightKg;
        }

        double rawAtFirst = CalculateRawSCurveIncrement(baseWeightForCurve);

        if (effectiveFirstJump == 4)
        {
            // In the 4 kg bracket, rawAtFirst spans [3.50, 4.50).
            // Subtracting 0.40 causes [3.50, 3.90) (exactly 40% of the unit interval) to round down to 3 kg.
            int stepped = (int)Math.Round(rawAtFirst - 0.40, MidpointRounding.AwayFromZero);
            return Math.Clamp(stepped, 3, 4);
        }
        else
        {
            // In the 5 kg bracket (105 kg+), rawAtFirst approaches 5.0 asymptotically from 4.50.
            // Raw < 4.78 corresponds to 105..121 kg (~40% of typical 105..150 kg heavyweight openers) stepping down to 4 kg.
            return rawAtFirst < 4.78 ? 4 : 5;
        }
    }

    /// <summary>
    /// Automatically detects and marks rows belonging to a prior competition session/group (such as Group D
    /// leader rows shown at the bottom of a Group C broadcast scoreboard) by setting <see cref="ScoreboardLifterRow.IsExcluded"/> = <c>true</c>.
    /// Returns the number of rows auto-excluded.
    /// </summary>
    public static int AutoFlagPriorSessionRows(IReadOnlyList<ScoreboardLifterRow> rows)
    {
        if (rows == null || rows.Count < 3)
        {
            return 0;
        }

        int excludedCount = 0;

        // 1. Letter-group prefix detection:
        // On IWF broadcast scoreboards, active session lifters have a numeric start/lot number in the leftmost column ("1".."15"),
        // whereas comparison leaders from an earlier group have a group letter ("B", "C", "D", "E") in the leftmost column.
        int numericLeftColCount = rows.Count(r =>
            !string.IsNullOrWhiteSpace(r.LeftColumnText) &&
            r.LeftColumnText!.Trim().All(char.IsDigit));

        if (numericLeftColCount >= 2)
        {
            foreach (var r in rows)
            {
                string left = (r.LeftColumnText ?? string.Empty).Trim();
                if (!string.IsNullOrEmpty(left) &&
                    left.Any(char.IsLetter) &&
                    !left.Any(char.IsDigit) &&
                    !r.IsExcluded)
                {
                    r.IsExcluded = true;
                    excludedCount++;
                }
            }
        }

        // 2. Rising-bar impossibility / unstarted C&J session detection:
        // If the majority of rows on the scoreboard only have a 1st Clean & Jerk attempt populated (meaning C&J has not started
        // for the current session, or is at the very beginning), any minority of rows that have ALREADY completed all 3 C&J attempts
        // (especially at weights heavier than or equal to the 1st-attempt openers of the active session) must be from a prior session.
        var unstartedCjOpeners = rows
            .Where(r => !r.IsExcluded &&
                        (r.Cj1.WeightKg ?? 0) > 0 &&
                        !HasCellContent(r.Cj2) &&
                        !HasCellContent(r.Cj3))
            .Select(r => r.Cj1.WeightKg!.Value)
            .ToList();

        var completedAllThreeCjRows = rows
            .Where(r => !r.IsExcluded &&
                        HasCellContent(r.Cj1) &&
                        HasCellContent(r.Cj2) &&
                        HasCellContent(r.Cj3))
            .ToList();

        if (unstartedCjOpeners.Count >= 2 &&
            completedAllThreeCjRows.Count > 0 &&
            unstartedCjOpeners.Count > completedAllThreeCjRows.Count)
        {
            int minPendingCjOpener = unstartedCjOpeners.Min();
            foreach (var r in completedAllThreeCjRows)
            {
                int maxCjAttempt = Math.Max(r.Cj1.WeightKg ?? 0, Math.Max(r.Cj2.WeightKg ?? 0, r.Cj3.WeightKg ?? 0));
                if (maxCjAttempt >= minPendingCjOpener)
                {
                    r.IsExcluded = true;
                    excludedCount++;
                }
            }
        }

        EnforceRisingBarPendingConsistency(rows);

        return excludedCount;
    }

    /// <summary>
    /// Enforces the IWF next-attempt and rising-bar rules on active (non-excluded) rows:
    /// <list type="bullet">
    ///   <item>When a lifter completes Attempt 1 or Attempt 2, the scoreboard always populates the next attempt slot (with a weight or Pass). Therefore, if Attempt 1 has a weight and Attempt 2 &amp; 3 are Empty, Attempt 1 has NOT been taken yet (<see cref="ScoreboardAttemptStatus.Pending"/>). Likewise, if Attempt 2 has a weight and Attempt 3 is Empty, Attempt 2 has NOT been taken yet (<see cref="ScoreboardAttemptStatus.Pending"/>).</item>
    ///   <item>If the session is in progress and has pending attempts on the board at weight <c>minPendingKg</c>, any terminal attempt at a heavier weight cannot have been lifted yet and is normalized to <see cref="ScoreboardAttemptStatus.Pending"/>.</item>
    /// </list>
    /// </summary>
    public static void EnforceRisingBarPendingConsistency(IReadOnlyList<ScoreboardLifterRow> rows)
    {
        if (rows == null || rows.Count == 0)
        {
            return;
        }

        EnforceRisingBarForDiscipline(rows, isSnatch: true);
        EnforceRisingBarForDiscipline(rows, isSnatch: false);
    }

    private static void EnforceRisingBarForDiscipline(IReadOnlyList<ScoreboardLifterRow> rows, bool isSnatch)
    {
        var activeRows = rows.Where(r => !r.IsExcluded).ToList();
        if (activeRows.Count == 0)
        {
            return;
        }

        // First, apply the subsequent-attempt rule unless the entire discipline has already concluded:
        // If Attempt 1 has a weight and Attempt 2 & 3 are both Empty (no weight and not Pass), Attempt 1 is Pending.
        // If Attempt 2 has a weight and Attempt 3 is Empty (no weight and not Pass), Attempt 2 is Pending.
        bool allActiveHaveAttempt3OrPass = activeRows
            .Where(r => HasCellContent(isSnatch ? r.Snatch1 : r.Cj1) ||
                        HasCellContent(isSnatch ? r.Snatch2 : r.Cj2) ||
                        HasCellContent(isSnatch ? r.Snatch3 : r.Cj3))
            .All(r =>
            {
                var c1 = isSnatch ? r.Snatch1 : r.Cj1;
                var c2 = isSnatch ? r.Snatch2 : r.Cj2;
                var c3 = isSnatch ? r.Snatch3 : r.Cj3;
                return c1.Status == ScoreboardAttemptStatus.Pass ||
                       c2.Status == ScoreboardAttemptStatus.Pass ||
                       HasCellContent(c3);
            });

        if (!allActiveHaveAttempt3OrPass)
        {
            foreach (var r in activeRows)
            {
                var a1 = isSnatch ? r.Snatch1 : r.Cj1;
                var a2 = isSnatch ? r.Snatch2 : r.Cj2;
                var a3 = isSnatch ? r.Snatch3 : r.Cj3;

                if (a1.WeightKg is int w1 && w1 > 0 && !HasCellContent(a2) && !HasCellContent(a3))
                {
                    a1 = new ScoreboardAttemptCell(w1, ScoreboardAttemptStatus.Pending);
                    if (isSnatch) r.Snatch1 = a1;
                    else r.Cj1 = a1;
                }

                if (a2.WeightKg is int w2Init && w2Init > 0 && !HasCellContent(a3))
                {
                    a2 = new ScoreboardAttemptCell(w2Init, ScoreboardAttemptStatus.Pending);
                    if (isSnatch) r.Snatch2 = a2;
                    else r.Cj2 = a2;
                }
            }
        }

        int minPendingAny = int.MaxValue;
        int minPendingAtt1 = int.MaxValue;

        foreach (var r in activeRows)
        {
            var a1 = isSnatch ? r.Snatch1 : r.Cj1;
            var a2 = isSnatch ? r.Snatch2 : r.Cj2;
            var a3 = isSnatch ? r.Snatch3 : r.Cj3;

            if (a1.Status == ScoreboardAttemptStatus.Pending && a1.WeightKg is > 0)
            {
                minPendingAny = Math.Min(minPendingAny, a1.WeightKg.Value);
                minPendingAtt1 = Math.Min(minPendingAtt1, a1.WeightKg.Value);
            }
            if (a2.Status == ScoreboardAttemptStatus.Pending && a2.WeightKg is > 0)
            {
                minPendingAny = Math.Min(minPendingAny, a2.WeightKg.Value);
            }
            if (a3.Status == ScoreboardAttemptStatus.Pending && a3.WeightKg is > 0)
            {
                minPendingAny = Math.Min(minPendingAny, a3.WeightKg.Value);
            }
        }

        if (minPendingAny == int.MaxValue)
        {
            return;
        }

        foreach (var r in activeRows)
        {
            var a1 = isSnatch ? r.Snatch1 : r.Cj1;
            var a2 = isSnatch ? r.Snatch2 : r.Cj2;
            var a3 = isSnatch ? r.Snatch3 : r.Cj3;

            // If Attempt 3 is empty/not declared yet, Attempt 2 is the lifter's latest declared cell.
            // Under the IWF rising-bar rule:
            // - If Attempt 2's weight is strictly heavier than the lowest Pending weight on the board (minPendingAny), OR
            // - If Attempt 2's weight is >= the lowest Pending 1st attempt on the board (minPendingAtt1, since 1st attempts always lift before 2nd attempts at the same weight!),
            // then Attempt 2 CANNOT have been taken on the platform yet!
            if (!HasCellContent(a3) &&
                a2.WeightKg is int w2 && w2 > 0 &&
                a2.Status is ScoreboardAttemptStatus.Good or ScoreboardAttemptStatus.Miss)
            {
                if (w2 > minPendingAny || w2 >= minPendingAtt1)
                {
                    a2 = new ScoreboardAttemptCell(w2, ScoreboardAttemptStatus.Pending);
                    if (isSnatch) r.Snatch2 = a2;
                    else r.Cj2 = a2;
                }
            }

            // Similarly, if Attempt 3 has a weight strictly heavier than minPendingAny (or >= minPendingAtt1)
            // while earlier weights are still pending on the board, Attempt 3 cannot have been taken yet
            // unless all 3 attempts on this row were completed in a prior session (already filtered by IsExcluded).
            if (a3.WeightKg is int w3 && w3 > 0 &&
                a3.Status is ScoreboardAttemptStatus.Good or ScoreboardAttemptStatus.Miss &&
                (w3 > minPendingAny || w3 >= minPendingAtt1) &&
                a2.Status == ScoreboardAttemptStatus.Pending)
            {
                a3 = new ScoreboardAttemptCell(w3, ScoreboardAttemptStatus.Pending);
                if (isSnatch) r.Snatch3 = a3;
                else r.Cj3 = a3;
            }
        }
    }

    /// <summary>
    /// Automatically finds the best matching lifter row index for the user based on profile name and opener weight.
    /// </summary>
    public static int AutoMatchUserLifterIndex(
        IReadOnlyList<ScoreboardLifterRow> rows,
        string? profileName,
        int setupOpenerWeightKg,
        LiftType discipline,
        int? fallbackCjOpenerWeightKg = null)
    {
        if (rows == null || rows.Count == 0)
        {
            return -1;
        }

        string cleanProfile = (profileName ?? string.Empty).Trim();
        bool hasMeaningfulName = !string.IsNullOrWhiteSpace(cleanProfile)
            && !cleanProfile.Equals("Demo Profile", StringComparison.OrdinalIgnoreCase)
            && !cleanProfile.StartsWith("New Profile", StringComparison.OrdinalIgnoreCase)
            && !cleanProfile.StartsWith("Profile ", StringComparison.OrdinalIgnoreCase);

        if (hasMeaningfulName)
        {
            // 1. Exact or substring match on profile name
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].IsExcluded)
                {
                    continue;
                }

                string rowName = (rows[i].Name ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(rowName))
                {
                    continue;
                }

                if (rowName.Equals(cleanProfile, StringComparison.OrdinalIgnoreCase) ||
                    rowName.Contains(cleanProfile, StringComparison.OrdinalIgnoreCase) ||
                    cleanProfile.Contains(rowName, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            // 2. Token match (e.g. "Matt Cunningham" vs "CUNNINGHAM Matthew" or "Matt")
            string[] tokens = cleanProfile
                .Split([' ', ',', '-', '_', '.'], StringSplitOptions.RemoveEmptyEntries)
                .Where(t => t.Length >= 3)
                .ToArray();

            if (tokens.Length > 0)
            {
                int bestIndex = -1;
                int bestScore = 0;
                for (int i = 0; i < rows.Count; i++)
                {
                    if (rows[i].IsExcluded)
                    {
                        continue;
                    }

                    string rowName = (rows[i].Name ?? string.Empty).Trim();
                    int matchCount = tokens.Count(t => rowName.Contains(t, StringComparison.OrdinalIgnoreCase));
                    if (matchCount > 0)
                    {
                        int firstWeight = GetAttempt1Cell(rows[i], discipline).WeightKg ?? 0;
                        int score = (matchCount * 10) + (firstWeight == setupOpenerWeightKg ? 5 : 0);
                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestIndex = i;
                        }
                    }
                }

                if (bestIndex >= 0)
                {
                    return bestIndex;
                }
            }
        }

        // 3. Match by opener weight on Attempt 1 (preferring a pending/uncompleted 1st attempt)
        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i].IsExcluded)
            {
                continue;
            }

            var cell1 = GetAttempt1Cell(rows[i], discipline);
            if (cell1.WeightKg == setupOpenerWeightKg &&
                cell1.Status is not (ScoreboardAttemptStatus.Good or ScoreboardAttemptStatus.Miss))
            {
                return i;
            }
        }

        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i].IsExcluded)
            {
                continue;
            }

            var cell1 = GetAttempt1Cell(rows[i], discipline);
            if (cell1.WeightKg == setupOpenerWeightKg)
            {
                return i;
            }
        }

        // 4. If accessed from Snatch setup when Snatch is already done, also check C&J Attempt 1 opener weight
        if (discipline == LiftType.Snatch && fallbackCjOpenerWeightKg is > 0)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].IsExcluded)
                {
                    continue;
                }

                var cj1 = rows[i].Cj1;
                if (cj1.WeightKg == fallbackCjOpenerWeightKg.Value)
                {
                    return i;
                }
            }
        }

        // If Snatch is already concluded on the board, prefer an active lifter who actually has Clean & Jerk weights on the board
        // (rather than a lifter at index 0 who withdrew from Clean & Jerk with empty C&J cells).
        if (IsDisciplineConcludedOnBoard(rows, LiftType.Snatch))
        {
            for (int i = 0; i < rows.Count; i++)
            {
                if (!rows[i].IsExcluded && HasCellContent(rows[i].Cj1))
                {
                    return i;
                }
            }
        }

        for (int i = 0; i < rows.Count; i++)
        {
            if (!rows[i].IsExcluded)
            {
                return i;
            }
        }

        return 0;
    }

    /// <summary>
    /// Returns <c>true</c> when the selected lifter's Snatch is already finished on the scoreboard
    /// (either their Snatch opener/attempts are already completed or passed, or the board only contains Clean &amp; Jerk data),
    /// indicating that a scan initiated from Snatch setup should automatically calculate for Clean &amp; Jerk.
    /// </summary>
    public static bool IsLifterSnatchDone(IReadOnlyList<ScoreboardLifterRow> rows, int userLifterIndex)
    {
        if (rows == null || rows.Count == 0)
        {
            return false;
        }

        int idx = Math.Clamp(userLifterIndex, 0, rows.Count - 1);
        var row = rows[idx];

        // In Comp OWL, warmup is only for the Opener (1st attempt).
        // Once the lifter's 1st Snatch attempt (or any Snatch attempt) is completed (Good, Miss, or Pass),
        // or if Snatch2/Snatch3 already has a weight on the board, their Snatch warmup is over.
        bool anySnatchCompleted =
            row.Snatch1.Status is ScoreboardAttemptStatus.Good or ScoreboardAttemptStatus.Miss or ScoreboardAttemptStatus.Pass ||
            row.Snatch2.Status is ScoreboardAttemptStatus.Good or ScoreboardAttemptStatus.Miss or ScoreboardAttemptStatus.Pass ||
            row.Snatch3.Status is ScoreboardAttemptStatus.Good or ScoreboardAttemptStatus.Miss or ScoreboardAttemptStatus.Pass ||
            HasCellContent(row.Snatch2) ||
            HasCellContent(row.Snatch3);

        if (anySnatchCompleted)
        {
            return true;
        }

        if (IsDisciplineConcludedOnBoard(rows, LiftType.Snatch))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Runs the full-session retroactive and forward IWF calling-order simulation to calculate
    /// <c>LiftsOut</c>, <c>LiftsPassed</c>, expected attempts for every lifter, and the chronological platform call order.
    /// </summary>
    /// <param name="rows">Extracted scoreboard lifter rows.</param>
    /// <param name="discipline">Active competition lift (<see cref="LiftType.Snatch"/> or <see cref="LiftType.CleanAndJerk"/>).</param>
    /// <param name="userLifterIndex">Index in <paramref name="rows"/> representing the user (or -1 if not on board).</param>
    /// <param name="setupOpenerWeightKg">User's target opener weight from the app (always used by default rather than strategic board placeholder weights).</param>
    /// <param name="useSetupOpenerForUser">When true (default), always uses <paramref name="setupOpenerWeightKg"/> as the user's 1st attempt weight rather than what is shown on the scoreboard.</param>
    /// <param name="replaceAutoOneKgAfterMake">When true, treats a pending +1 kg board weight immediately following a Good Lift (at &gt;= 27 kg) as an unconfirmed OWLCMS placeholder and projects the expected jump instead.</param>
    public static ScoreboardAnalysisResult AnalyzeScoreboard(
        IReadOnlyList<ScoreboardLifterRow> rows,
        LiftType discipline,
        int userLifterIndex,
        int setupOpenerWeightKg,
        bool useSetupOpenerForUser = true,
        bool replaceAutoOneKgAfterMake = true)
    {
        if (rows == null || rows.Count == 0)
        {
            return new ScoreboardAnalysisResult(
                discipline,
                -1,
                "You",
                setupOpenerWeightKg,
                LiftsOut: 0,
                LiftsPassed: 0,
                RemainingSnatchLifts: discipline == LiftType.CleanAndJerk ? 0 : null,
                LifterSummaries: [],
                CompletedCalls: [],
                UpcomingCallsBeforeUser: [],
                UserOpenerCall: null,
                UpcomingCallsAfterUser: [],
                HasDisciplineWeightsOnBoard: false);
        }

        int clampedUserIndex = userLifterIndex >= 0 && userLifterIndex < rows.Count
            ? userLifterIndex
            : 0;
        if (rows[clampedUserIndex].IsExcluded)
        {
            int firstActive = -1;
            for (int i = 0; i < rows.Count; i++)
            {
                if (!rows[i].IsExcluded)
                {
                    firstActive = i;
                    break;
                }
            }

            if (firstActive >= 0)
            {
                clampedUserIndex = firstActive;
            }
        }

        bool hasDisciplineWeightsOnBoard = HasAnyDisciplineData(rows, discipline);
        bool isDisciplineConcluded = IsDisciplineConcludedOnBoard(rows, discipline);

        // 1. Resolve all 3 attempts for each lifter in the active discipline
        var resolvedPerLifter = new List<ResolvedLifterAttempt>[rows.Count];
        for (int i = 0; i < rows.Count; i++)
        {
            resolvedPerLifter[i] = ResolveLifterAttempts(
                rows[i],
                lifterIndex: i,
                discipline: discipline,
                isUser: i == clampedUserIndex,
                setupOpenerWeightKg: setupOpenerWeightKg,
                useSetupOpenerForUser: useSetupOpenerForUser,
                replaceAutoOneKgAfterMake: replaceAutoOneKgAfterMake,
                isDisciplineConcluded: isDisciplineConcluded);
        }

        // 2. Phase A: Replay all COMPLETED attempts in exact IWF calling order from Lift #1
        //    to retroactively establish each lifter's preceding-attempt call sequence number.
        var nextAttemptSlot = new int[rows.Count]; // 0, 1, or 2 (index into resolvedPerLifter[i])
        var lastCallSequenceByLifter = new int[rows.Count];
        var lastAttempt1CallByLifter = new int[rows.Count];
        var lastAttempt2CallByLifter = new int[rows.Count];

        var completedCalls = new List<ScoreboardCallEntry>();
        int globalCallCounter = 0;

        while (true)
        {
            // Skip past any completed Pass cells first (they do not consume a platform lift)
            bool advancedPass = false;
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].IsExcluded)
                {
                    nextAttemptSlot[i] = 3;
                    continue;
                }

                var attempts = resolvedPerLifter[i];
                while (nextAttemptSlot[i] < attempts.Count && attempts[nextAttemptSlot[i]].IsPass)
                {
                    nextAttemptSlot[i]++;
                    advancedPass = true;
                }
            }

            if (advancedPass)
            {
                continue;
            }

            int bestLifter = -1;
            ResolvedLifterAttempt? bestAttempt = null;

            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].IsExcluded)
                {
                    continue;
                }

                var attempts = resolvedPerLifter[i];
                int slot = nextAttemptSlot[i];
                if (slot >= attempts.Count)
                {
                    continue;
                }

                var candidate = attempts[slot];
                if (!candidate.IsCompleted)
                {
                    continue;
                }

                if (bestAttempt == null || CompareIwfCallPriority(
                        candidate,
                        bestAttempt,
                        lastCallSequenceByLifter[i],
                        lastCallSequenceByLifter[bestLifter],
                        rows[i].OrderNumber,
                        rows[bestLifter].OrderNumber) < 0)
                {
                    bestLifter = i;
                    bestAttempt = candidate;
                }
            }

            if (bestLifter < 0 || bestAttempt == null)
            {
                break;
            }

            globalCallCounter++;
            int phaseCallNum = completedCalls.Count + 1;
            lastCallSequenceByLifter[bestLifter] = globalCallCounter;
            if (bestAttempt.AttemptNumber == 1)
            {
                lastAttempt1CallByLifter[bestLifter] = globalCallCounter;
            }
            else if (bestAttempt.AttemptNumber == 2)
            {
                lastAttempt2CallByLifter[bestLifter] = globalCallCounter;
            }

            completedCalls.Add(new ScoreboardCallEntry(
                GlobalCallNumber: globalCallCounter,
                PhaseCallNumber: phaseCallNum,
                LifterIndex: bestLifter,
                LifterName: bestAttempt.LifterName,
                SeedNumber: bestAttempt.SeedNumber,
                AttemptNumber: bestAttempt.AttemptNumber,
                WeightKg: bestAttempt.WeightKg,
                Origin: bestAttempt.Origin,
                IsCompleted: true,
                IsUserTargetOpener: bestLifter == clampedUserIndex && bestAttempt.AttemptNumber == 1,
                IsBeforeUserOpener: false,
                SequenceReasonNote: bestAttempt.Origin == ScoreboardAttemptOrigin.CompletedGood
                    ? "Completed — Good Lift"
                    : "Completed — No Lift (Miss)"));

            nextAttemptSlot[bestLifter]++;
        }

        // 3. Phase B: Simulate all UPCOMING (Declared + Projected) attempts using the retroactive
        //    lastCallSequenceByLifter established in Phase A.
        var rawUpcoming = new List<(ResolvedLifterAttempt Attempt, int GlobalCallNum, int PriorCallSeq)>();

        while (true)
        {
            int bestLifter = -1;
            ResolvedLifterAttempt? bestAttempt = null;

            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].IsExcluded)
                {
                    continue;
                }

                var attempts = resolvedPerLifter[i];
                while (nextAttemptSlot[i] < attempts.Count && attempts[nextAttemptSlot[i]].IsPass)
                {
                    nextAttemptSlot[i]++;
                }

                int slot = nextAttemptSlot[i];
                if (slot >= attempts.Count)
                {
                    continue;
                }

                var candidate = attempts[slot];
                if (bestAttempt == null || CompareIwfCallPriority(
                        candidate,
                        bestAttempt,
                        lastCallSequenceByLifter[i],
                        lastCallSequenceByLifter[bestLifter],
                        rows[i].OrderNumber,
                        rows[bestLifter].OrderNumber) < 0)
                {
                    bestLifter = i;
                    bestAttempt = candidate;
                }
            }

            if (bestLifter < 0 || bestAttempt == null)
            {
                break;
            }

            int priorSeq = lastCallSequenceByLifter[bestLifter];
            globalCallCounter++;
            lastCallSequenceByLifter[bestLifter] = globalCallCounter;
            if (bestAttempt.AttemptNumber == 1)
            {
                lastAttempt1CallByLifter[bestLifter] = globalCallCounter;
            }
            else if (bestAttempt.AttemptNumber == 2)
            {
                lastAttempt2CallByLifter[bestLifter] = globalCallCounter;
            }

            rawUpcoming.Add((bestAttempt, globalCallCounter, priorSeq));
            nextAttemptSlot[bestLifter]++;
        }

        // 4. Locate the user's Opener (Attempt 1) in the sequence and partition into Before / Target / After
        int userTargetUpcomingIdx = rawUpcoming.FindIndex(x =>
            x.Attempt.LifterIndex == clampedUserIndex && x.Attempt.AttemptNumber == 1);

        var userAttempts = resolvedPerLifter[clampedUserIndex];
        int userOpenerWeight = userAttempts.FirstOrDefault(a => a.AttemptNumber == 1)?.WeightKg ?? setupOpenerWeightKg;
        int userSeed = rows[clampedUserIndex].EffectiveSeedNumber;
        var userRawCell1 = GetAttempt1Cell(rows[clampedUserIndex], discipline);
        int? boardDeclaredOpener = userRawCell1.WeightKg is > 0 ? userRawCell1.WeightKg : null;

        var userNormalizedCells = NormalizeDisciplineCells(rows[clampedUserIndex], discipline, isDisciplineConcluded);
        bool userOpenerAlreadyCompleted =
            completedCalls.Any(c => c.LifterIndex == clampedUserIndex && c.AttemptNumber == 1) ||
            userNormalizedCells[0].Status is ScoreboardAttemptStatus.Good or ScoreboardAttemptStatus.Miss or ScoreboardAttemptStatus.Pass ||
            HasCellContent(userNormalizedCells[1]) ||
            HasCellContent(userNormalizedCells[2]);

        string liftTitle = discipline == LiftType.Snatch ? "Snatch" : "Clean & Jerk";
        string? disciplineConcludedMessage = isDisciplineConcluded
            ? (discipline == LiftType.Snatch
                ? "Snatching has concluded."
                : "Clean & Jerk has concluded.")
            : null;

        string? userOpenerCompletedMessage = userOpenerAlreadyCompleted
            ? $"You have already lifted your {liftTitle} opener on stage — the app is done with {liftTitle}."
            : null;

        int? maxCompletedWeightKg = completedCalls.Count > 0
            ? completedCalls.Max(c => c.WeightKg)
            : null;
        bool isBarPastUserOpener = !isDisciplineConcluded
            && !userOpenerAlreadyCompleted
            && maxCompletedWeightKg.HasValue
            && maxCompletedWeightKg.Value > userOpenerWeight;
        string? barPastUserOpenerMessage = isBarPastUserOpener
            ? $"Cannot determine Lifts Out: {maxCompletedWeightKg!.Value} kg has already been lifted on the scoreboard, which is heavier than your {userOpenerWeight} kg Opener in the app. Lifts Out will be left as-is."
            : null;

        var upcomingBeforeUser = new List<ScoreboardCallEntry>();
        ScoreboardCallEntry? userOpenerCall = null;
        var upcomingAfterUser = new List<ScoreboardCallEntry>();

        for (int idx = 0; idx < rawUpcoming.Count; idx++)
        {
            var (attempt, globalCallNum, priorSeq) = rawUpcoming[idx];
            bool isUserOpener = idx == userTargetUpcomingIdx;
            bool isBefore = !isDisciplineConcluded
                && !userOpenerAlreadyCompleted
                && !isBarPastUserOpener
                && userTargetUpcomingIdx >= 0
                && idx < userTargetUpcomingIdx;

            string reason = isBarPastUserOpener && isUserOpener
                ? $"⚠️ {maxCompletedWeightKg!.Value} kg has already been lifted on stage (> your {userOpenerWeight} kg app opener) — Lifts Out cannot be determined"
                : BuildCallReasonNote(
                    attempt,
                    priorSeq,
                    userOpenerWeight,
                    userSeed,
                    isUserOpener,
                    isBefore,
                    boardDeclaredOpener);

            int phaseNum = isBefore
                ? upcomingBeforeUser.Count + 1
                : (isUserOpener ? 0 : upcomingAfterUser.Count + 1);

            var entry = new ScoreboardCallEntry(
                GlobalCallNumber: globalCallNum,
                PhaseCallNumber: phaseNum,
                LifterIndex: attempt.LifterIndex,
                LifterName: attempt.LifterName,
                SeedNumber: attempt.SeedNumber,
                AttemptNumber: attempt.AttemptNumber,
                WeightKg: attempt.WeightKg,
                Origin: attempt.Origin,
                IsCompleted: false,
                IsUserTargetOpener: isUserOpener,
                IsBeforeUserOpener: isBefore,
                SequenceReasonNote: reason);

            if (isUserOpener)
            {
                userOpenerCall = entry;
            }
            else if (isBefore)
            {
                upcomingBeforeUser.Add(entry);
            }
            else
            {
                upcomingAfterUser.Add(entry);
            }
        }

        // If user's opener was already completed on the board, locate it in completedCalls
        userOpenerCall ??= completedCalls.FirstOrDefault(c => c.IsUserTargetOpener);

        // 5. Build per-lifter summaries for the "Show Your Work" table
        var lifterSummaries = new List<ScoreboardLifterSummary>(rows.Count);
        for (int i = 0; i < rows.Count; i++)
        {
            int beforeCount = upcomingBeforeUser.Count(c => c.LifterIndex == i);
            int completedCount = completedCalls.Count(c => c.LifterIndex == i);
            lifterSummaries.Add(new ScoreboardLifterSummary(
                LifterIndex: i,
                OrderNumber: rows[i].OrderNumber,
                SeedNumber: rows[i].EffectiveSeedNumber,
                Name: string.IsNullOrWhiteSpace(rows[i].Name) ? $"Lifter #{rows[i].OrderNumber}" : rows[i].Name,
                IsUser: i == clampedUserIndex,
                IsExcluded: rows[i].IsExcluded,
                Attempts: resolvedPerLifter[i],
                LiftsBeforeUserCount: beforeCount,
                CompletedLiftsCount: completedCount));
        }

        // 6. If discipline is Clean & Jerk, also calculate remaining Snatch attempts if Snatch is still in progress
        int? remainingSnatchLifts = null;
        if (discipline == LiftType.CleanAndJerk)
        {
            remainingSnatchLifts = CalculateRemainingSnatchAttempts(rows, cjCompletedCount: completedCalls.Count);
        }

        int finalLiftsOut = (isDisciplineConcluded || userOpenerAlreadyCompleted)
            ? 0
            : upcomingBeforeUser.Count;

        return new ScoreboardAnalysisResult(
            Discipline: discipline,
            UserLifterIndex: clampedUserIndex,
            UserLifterName: lifterSummaries[clampedUserIndex].Name,
            UserOpenerWeightKg: userOpenerWeight,
            LiftsOut: finalLiftsOut,
            LiftsPassed: completedCalls.Count,
            RemainingSnatchLifts: remainingSnatchLifts,
            LifterSummaries: lifterSummaries,
            CompletedCalls: completedCalls,
            UpcomingCallsBeforeUser: upcomingBeforeUser,
            UserOpenerCall: userOpenerCall,
            UpcomingCallsAfterUser: upcomingAfterUser,
            IsBarPastUserOpener: isBarPastUserOpener,
            MaxCompletedWeightKg: maxCompletedWeightKg,
            BarPastUserOpenerMessage: barPastUserOpenerMessage,
            BoardDeclaredUserOpenerKg: boardDeclaredOpener,
            IsDisciplineConcluded: isDisciplineConcluded,
            DisciplineConcludedMessage: disciplineConcludedMessage,
            IsUserOpenerCompleted: userOpenerAlreadyCompleted,
            UserOpenerCompletedMessage: userOpenerCompletedMessage,
            HasDisciplineWeightsOnBoard: hasDisciplineWeightsOnBoard);
    }

    /// <summary>
    /// Compares two candidate attempts according to the IWF Technical and Competition Rules &amp; Regulations:
    /// <list type="number">
    ///   <item>Lower barbell weight goes first (rising bar).</item>
    ///   <item>Lower attempt number (1st before 2nd before 3rd) goes first at the same weight.</item>
    ///   <item>At the same weight and same attempt number:
    ///     <list type="bullet">
    ///       <item>For Attempt 1: lower Lot / Seed number goes first.</item>
    ///       <item>For Attempt 2 or 3: whoever lifted earlier on the preceding attempt (<paramref name="priorCallSeqA"/> vs <paramref name="priorCallSeqB"/>) goes first.</item>
    ///     </list>
    ///   </item>
    /// </list>
    /// </summary>
    private static int CompareIwfCallPriority(
        ResolvedLifterAttempt a,
        ResolvedLifterAttempt b,
        int priorCallSeqA,
        int priorCallSeqB,
        int rowOrderA,
        int rowOrderB)
    {
        // 1. Barbell weight ascending
        int weightCmp = a.WeightKg.CompareTo(b.WeightKg);
        if (weightCmp != 0)
        {
            return weightCmp;
        }

        // 2. Attempt number ascending (1st < 2nd < 3rd)
        int attemptCmp = a.AttemptNumber.CompareTo(b.AttemptNumber);
        if (attemptCmp != 0)
        {
            return attemptCmp;
        }

        // 3. Same weight AND same attempt number:
        //    For 2nd & 3rd attempts, order by who lifted earlier on the preceding attempt.
        if (a.AttemptNumber > 1 && priorCallSeqA > 0 && priorCallSeqB > 0 && priorCallSeqA != priorCallSeqB)
        {
            return priorCallSeqA.CompareTo(priorCallSeqB);
        }

        // 4. For 1st attempts (or fallback if prior sequence wasn't distinct): order by Lot / Seed number
        int seedCmp = a.SeedNumber.CompareTo(b.SeedNumber);
        if (seedCmp != 0)
        {
            return seedCmp;
        }

        return rowOrderA.CompareTo(rowOrderB);
    }

    private static List<ResolvedLifterAttempt> ResolveLifterAttempts(
        ScoreboardLifterRow row,
        int lifterIndex,
        LiftType discipline,
        bool isUser,
        int setupOpenerWeightKg,
        bool useSetupOpenerForUser,
        bool replaceAutoOneKgAfterMake,
        bool isDisciplineConcluded)
    {
        string lifterName = string.IsNullOrWhiteSpace(row.Name) ? $"Lifter #{row.OrderNumber}" : row.Name.Trim();
        int seed = row.EffectiveSeedNumber;

        var rawCells = NormalizeDisciplineCells(row, discipline, isDisciplineConcluded);

        var overrides = discipline == LiftType.Snatch
            ? new[] { row.OverrideAttempt1Kg, row.OverrideAttempt2Kg, row.OverrideAttempt3Kg }
            : new[]
            {
                row.OverrideCjAttempt1Kg ?? row.OverrideAttempt1Kg,
                row.OverrideCjAttempt2Kg ?? row.OverrideAttempt2Kg,
                row.OverrideCjAttempt3Kg ?? row.OverrideAttempt3Kg
            };
        var resolved = new List<ResolvedLifterAttempt>(3);

        // If all 3 cells on the board are completely empty and not the user, this lifter may not be competing in this discipline
        bool anyData = isUser || rawCells.Any(HasCellContent)
                              || overrides.Any(o => o is > 0);
        if (!anyData || row.IsExcluded)
        {
            return resolved;
        }

        // If the entire discipline has concluded and this lifter has no cells on the board for this discipline, do not fabricate an upcoming opener
        if (isDisciplineConcluded && !rawCells.Any(HasCellContent) && !overrides.Any(o => o is > 0))
        {
            return resolved;
        }

        bool hasPassedRemainder = false;
        int prevWeight = 0;
        ScoreboardAttemptStatus prevStatusForJump = ScoreboardAttemptStatus.Good;
        int? firstJumpKg = null;

        for (int idx = 0; idx < 3; idx++)
        {
            int attemptNum = idx + 1;
            var cell = rawCells[idx];
            int? manualOverride = overrides[idx];

            if (hasPassedRemainder)
            {
                break;
            }

            if (cell.Status == ScoreboardAttemptStatus.Pass)
            {
                hasPassedRemainder = true;
                resolved.Add(new ResolvedLifterAttempt(
                    lifterIndex,
                    lifterName,
                    seed,
                    attemptNum,
                    WeightKg: prevWeight,
                    Origin: ScoreboardAttemptOrigin.CompletedPass,
                    IsCompleted: true,
                    IsPass: true,
                    JumpFromPreviousKg: null));
                continue;
            }

            // Completed Good or Miss on the scoreboard
            if (cell.Status is ScoreboardAttemptStatus.Good or ScoreboardAttemptStatus.Miss && (cell.WeightKg ?? 0) > 0)
            {
                int w = cell.WeightKg!.Value;
                int? jump = prevWeight > 0 ? w - prevWeight : null;
                if (attemptNum == 2 && jump is > 0)
                {
                    firstJumpKg = jump;
                }

                resolved.Add(new ResolvedLifterAttempt(
                    lifterIndex,
                    lifterName,
                    seed,
                    attemptNum,
                    WeightKg: w,
                    Origin: cell.Status == ScoreboardAttemptStatus.Good
                        ? ScoreboardAttemptOrigin.CompletedGood
                        : ScoreboardAttemptOrigin.CompletedMiss,
                    IsCompleted: true,
                    IsPass: false,
                    JumpFromPreviousKg: jump));

                prevWeight = w;
                prevStatusForJump = cell.Status;
                continue;
            }

            // User manual override in the "Show Your Work" screen takes highest priority for upcoming attempts
            if (manualOverride is > 0)
            {
                int w = Math.Max(prevWeight, manualOverride.Value);
                int? jump = prevWeight > 0 ? w - prevWeight : null;
                if (attemptNum == 2 && jump is > 0)
                {
                    firstJumpKg = jump;
                }

                resolved.Add(new ResolvedLifterAttempt(
                    lifterIndex,
                    lifterName,
                    seed,
                    attemptNum,
                    WeightKg: w,
                    Origin: ScoreboardAttemptOrigin.UserOverride,
                    IsCompleted: false,
                    IsPass: false,
                    JumpFromPreviousKg: jump));

                prevWeight = w;
                prevStatusForJump = ScoreboardAttemptStatus.Good;
                continue;
            }

            // For the user's 1st attempt (Opener)
            if (isUser && attemptNum == 1)
            {
                int boardOpener = cell.WeightKg ?? 0;
                int openerWeight = (useSetupOpenerForUser || boardOpener <= 0)
                    ? setupOpenerWeightKg
                    : boardOpener;

                resolved.Add(new ResolvedLifterAttempt(
                    lifterIndex,
                    lifterName,
                    seed,
                    AttemptNumber: 1,
                    WeightKg: openerWeight,
                    Origin: boardOpener > 0 && !useSetupOpenerForUser
                        ? ScoreboardAttemptOrigin.DeclaredOnBoard
                        : ScoreboardAttemptOrigin.UserOverride,
                    IsCompleted: false,
                    IsPass: false,
                    JumpFromPreviousKg: null));

                // User only cares up to their Opener (Attempt 1)
                break;
            }

            // Pending / Declared weight on the scoreboard
            if ((cell.WeightKg ?? 0) > 0)
            {
                int declaredW = cell.WeightKg!.Value;
                bool isAutoOneKgPlaceholder = replaceAutoOneKgAfterMake
                    && attemptNum > 1
                    && prevWeight >= 27
                    && prevStatusForJump == ScoreboardAttemptStatus.Good
                    && declaredW == prevWeight + 1;

                if (isAutoOneKgPlaceholder)
                {
                    int projectedJump = CalculateProjectedJumpKg(
                        prevWeight,
                        fromAttemptNumber: attemptNum - 1,
                        previousStatus: prevStatusForJump,
                        firstJumpKg: firstJumpKg);
                    int projectedW = prevWeight + projectedJump;
                    if (attemptNum == 2)
                    {
                        firstJumpKg = projectedJump;
                    }

                    resolved.Add(new ResolvedLifterAttempt(
                        lifterIndex,
                        lifterName,
                        seed,
                        attemptNum,
                        WeightKg: projectedW,
                        Origin: ScoreboardAttemptOrigin.ProjectedFromAutoOneKg,
                        IsCompleted: false,
                        IsPass: false,
                        JumpFromPreviousKg: projectedJump));

                    prevWeight = projectedW;
                    prevStatusForJump = ScoreboardAttemptStatus.Good;
                }
                else
                {
                    int w = Math.Max(prevWeight, declaredW);
                    int? jump = prevWeight > 0 ? w - prevWeight : null;
                    if (attemptNum == 2 && jump is > 0)
                    {
                        firstJumpKg = jump;
                    }

                    resolved.Add(new ResolvedLifterAttempt(
                        lifterIndex,
                        lifterName,
                        seed,
                        attemptNum,
                        WeightKg: w,
                        Origin: ScoreboardAttemptOrigin.DeclaredOnBoard,
                        IsCompleted: false,
                        IsPass: false,
                        JumpFromPreviousKg: jump));

                    prevWeight = w;
                    prevStatusForJump = ScoreboardAttemptStatus.Good;
                }

                continue;
            }

            // Empty cell: project using the S-curve if we have a previous weight for this lifter
            if (prevWeight > 0)
            {
                int jump = CalculateProjectedJumpKg(
                    prevWeight,
                    fromAttemptNumber: attemptNum - 1,
                    previousStatus: prevStatusForJump,
                    firstJumpKg: firstJumpKg);
                int projectedW = prevWeight + jump;
                if (attemptNum == 2)
                {
                    firstJumpKg = jump > 0 ? jump : null;
                }

                resolved.Add(new ResolvedLifterAttempt(
                    lifterIndex,
                    lifterName,
                    seed,
                    attemptNum,
                    WeightKg: projectedW,
                    Origin: ScoreboardAttemptOrigin.ProjectedSCurve,
                    IsCompleted: false,
                    IsPass: false,
                    JumpFromPreviousKg: jump));

                prevWeight = projectedW;
                // Assume projected retry/attempt is made for projecting any subsequent attempt
                prevStatusForJump = ScoreboardAttemptStatus.Good;
            }
        }

        return resolved;
    }

    private static string BuildCallReasonNote(
        ResolvedLifterAttempt attempt,
        int priorCallSeq,
        int userOpenerWeight,
        int userSeed,
        bool isUserOpener,
        bool isBeforeUser,
        int? boardDeclaredOpener = null)
    {
        if (isUserOpener)
        {
            if (boardDeclaredOpener.HasValue && boardDeclaredOpener.Value != attempt.WeightKg)
            {
                return $"YOUR OPENER — 1st attempt @ {attempt.WeightKg} kg from App (Board shows {boardDeclaredOpener.Value} kg • Seed #{attempt.SeedNumber})";
            }

            return $"YOUR OPENER — 1st attempt @ {attempt.WeightKg} kg (Seed #{attempt.SeedNumber})";
        }

        string originTag = attempt.Origin switch
        {
            ScoreboardAttemptOrigin.DeclaredOnBoard => "Declared on board",
            ScoreboardAttemptOrigin.ProjectedFromAutoOneKg => $"Estimated +{attempt.JumpFromPreviousKg ?? 0}kg jump (replaced +1kg board auto-increment)",
            ScoreboardAttemptOrigin.ProjectedSCurve => attempt.JumpFromPreviousKg == 0
                ? "Estimated repeat (+0kg after miss)"
                : $"Estimated jump (+{attempt.JumpFromPreviousKg ?? 0}kg)",
            ScoreboardAttemptOrigin.UserOverride => "Edited weight",
            _ => "Estimated"
        };

        if (attempt.WeightKg < userOpenerWeight)
        {
            return $"{originTag} • {attempt.WeightKg} kg < your {userOpenerWeight} kg opener";
        }

        if (attempt.WeightKg > userOpenerWeight)
        {
            return $"{originTag} • {attempt.WeightKg} kg > your {userOpenerWeight} kg opener (After you)";
        }

        // Exact same weight as user's opener!
        if (attempt.AttemptNumber > 1)
        {
            string ord = attempt.AttemptNumber == 2 ? "2nd" : "3rd";
            return $"{originTag} • Same weight ({attempt.WeightKg} kg), but {ord} attempt goes AFTER your 1st attempt";
        }

        // Both on 1st attempt at the same weight -> decided by Seed / Lot number
        if (isBeforeUser)
        {
            return $"{originTag} • Tied 1st attempt @ {attempt.WeightKg} kg — Seed #{attempt.SeedNumber} goes BEFORE your Seed #{userSeed}";
        }

        return $"{originTag} • Tied 1st attempt @ {attempt.WeightKg} kg — Seed #{attempt.SeedNumber} goes AFTER your Seed #{userSeed}";
    }

    private static bool HasCellContent(ScoreboardAttemptCell cell)
        => (cell.WeightKg ?? 0) > 0 || cell.Status != ScoreboardAttemptStatus.Empty;

    private static bool HasAnyDisciplineData(IReadOnlyList<ScoreboardLifterRow> rows, LiftType discipline)
    {
        return rows.Any(r =>
        {
            if (r.IsExcluded)
            {
                return false;
            }

            return discipline == LiftType.Snatch
                ? HasCellContent(r.Snatch1) || HasCellContent(r.Snatch2) || HasCellContent(r.Snatch3) ||
                  r.OverrideAttempt1Kg is > 0 || r.OverrideAttempt2Kg is > 0 || r.OverrideAttempt3Kg is > 0
                : HasCellContent(r.Cj1) || HasCellContent(r.Cj2) || HasCellContent(r.Cj3) ||
                  r.OverrideCjAttempt1Kg is > 0 || r.OverrideCjAttempt2Kg is > 0 || r.OverrideCjAttempt3Kg is > 0;
        });
    }

    /// <summary>
    /// Normalizes a lifter's 3 raw attempt cells so that if a later attempt (2nd or 3rd) is already populated
    /// on the scoreboard, or if the entire discipline has concluded, earlier attempts are marked completed
    /// (<see cref="ScoreboardAttemptStatus.Good"/> or <see cref="ScoreboardAttemptStatus.Miss"/>) even when
    /// OCR failed to detect cell background colors.
    /// </summary>
    private static ScoreboardAttemptCell[] NormalizeDisciplineCells(
        ScoreboardLifterRow row,
        LiftType discipline,
        bool isDisciplineConcluded)
    {
        var c1 = discipline == LiftType.Snatch ? row.Snatch1 : row.Cj1;
        var c2 = discipline == LiftType.Snatch ? row.Snatch2 : row.Cj2;
        var c3 = discipline == LiftType.Snatch ? row.Snatch3 : row.Cj3;

        bool c2Present = HasCellContent(c2);
        bool c3Present = HasCellContent(c3);

        if ((c1.WeightKg ?? 0) > 0 &&
            c1.Status is ScoreboardAttemptStatus.Pending or ScoreboardAttemptStatus.Empty &&
            (c2Present || c3Present || isDisciplineConcluded))
        {
            var inferredStatus = (c2.WeightKg ?? 0) > 0 && c1.WeightKg.HasValue && c2.WeightKg!.Value == c1.WeightKg.Value
                ? ScoreboardAttemptStatus.Miss
                : ScoreboardAttemptStatus.Good;
            c1 = new ScoreboardAttemptCell(c1.WeightKg, inferredStatus);
        }

        if ((c2.WeightKg ?? 0) > 0 &&
            c2.Status is ScoreboardAttemptStatus.Pending or ScoreboardAttemptStatus.Empty &&
            (c3Present || isDisciplineConcluded))
        {
            var inferredStatus = (c3.WeightKg ?? 0) > 0 && c2.WeightKg.HasValue && c3.WeightKg!.Value == c2.WeightKg.Value
                ? ScoreboardAttemptStatus.Miss
                : ScoreboardAttemptStatus.Good;
            c2 = new ScoreboardAttemptCell(c2.WeightKg, inferredStatus);
        }

        if ((c3.WeightKg ?? 0) > 0 &&
            c3.Status is ScoreboardAttemptStatus.Pending or ScoreboardAttemptStatus.Empty &&
            isDisciplineConcluded)
        {
            c3 = new ScoreboardAttemptCell(c3.WeightKg, ScoreboardAttemptStatus.Good);
        }

        // In Olympic weightlifting, the bar must increase by at least 1 kg after a Good lift.
        // Therefore, if the next attempt has the exact same weight (or lower), the earlier attempt MUST have been a Miss.
        if ((c1.WeightKg ?? 0) > 0 && (c2.WeightKg ?? 0) > 0 && c2.WeightKg!.Value <= c1.WeightKg!.Value)
        {
            c1 = new ScoreboardAttemptCell(c1.WeightKg, ScoreboardAttemptStatus.Miss);
        }

        if ((c2.WeightKg ?? 0) > 0 && (c3.WeightKg ?? 0) > 0 && c3.WeightKg!.Value <= c2.WeightKg!.Value)
        {
            c2 = new ScoreboardAttemptCell(c2.WeightKg, ScoreboardAttemptStatus.Miss);
        }

        // If Attempt 1 and Attempt 2 had the same weight (so Attempt 1 was a Miss) and Attempt 3 increased in weight (e.g. 90, 90, 92),
        // Attempt 2 was a Good lift!
        if ((c1.WeightKg ?? 0) > 0 &&
            (c2.WeightKg ?? 0) > 0 &&
            (c3.WeightKg ?? 0) > 0 &&
            c1.WeightKg!.Value == c2.WeightKg!.Value &&
            c3.WeightKg!.Value > c2.WeightKg!.Value)
        {
            c2 = new ScoreboardAttemptCell(c2.WeightKg, ScoreboardAttemptStatus.Good);
        }

        return [c1, c2, c3];
    }

    private static bool IsDisciplineConcludedOnBoard(IReadOnlyList<ScoreboardLifterRow> rows, LiftType discipline)
    {
        bool anySnatchData = HasAnyDisciplineData(rows, LiftType.Snatch);
        bool anyCjData = HasAnyDisciplineData(rows, LiftType.CleanAndJerk);

        if (discipline == LiftType.Snatch)
        {
            // 1. If any Clean & Jerk attempt has already been completed or any lifter has Cj2/Cj3 populated, Snatch is 100% concluded.
            bool cjStarted = rows.Any(r =>
                !r.IsExcluded &&
                (r.Cj1.Status is ScoreboardAttemptStatus.Good or ScoreboardAttemptStatus.Miss or ScoreboardAttemptStatus.Pass ||
                 r.Cj2.Status is ScoreboardAttemptStatus.Good or ScoreboardAttemptStatus.Miss or ScoreboardAttemptStatus.Pass ||
                 r.Cj3.Status is ScoreboardAttemptStatus.Good or ScoreboardAttemptStatus.Miss or ScoreboardAttemptStatus.Pass ||
                 HasCellContent(r.Cj2) ||
                 HasCellContent(r.Cj3)));

            if (cjStarted)
            {
                return true;
            }

            // 2. If the scoreboard is displaying Clean & Jerk only (no Snatch weights on board), Snatch has concluded.
            if (!anySnatchData && anyCjData)
            {
                return true;
            }

            if (!anySnatchData)
            {
                return false;
            }

            // 3. Check if 0 Snatch attempts remain after sequential normalization
            if (CalculateRemainingSnatchAttempts(rows, cjCompletedCount: 0) == 0)
            {
                return true;
            }

            // 4. If every active lifter with Snatch data has all 3 Snatch cells populated (or Passed) AND C&J openers are on the board,
            //    check whether Snatch has finished (including when OCR didn't detect green/red fill on completed Snatch3 cells).
            var activeSnatchRows = rows
                .Where(r => !r.IsExcluded && (HasCellContent(r.Snatch1) || HasCellContent(r.Snatch2) || HasCellContent(r.Snatch3)))
                .ToList();

            bool allReachedSnatch3OrPassed = activeSnatchRows.Count > 0 && activeSnatchRows.All(r =>
                r.Snatch1.Status == ScoreboardAttemptStatus.Pass ||
                r.Snatch2.Status == ScoreboardAttemptStatus.Pass ||
                HasCellContent(r.Snatch3));

            if (allReachedSnatch3OrPassed && anyCjData)
            {
                return true;
            }

            return false;
        }
        else
        {
            if (!anyCjData)
            {
                return false;
            }

            bool anyCjCompleted = false;
            int remainingCj = 0;

            foreach (var row in rows)
            {
                if (row.IsExcluded)
                {
                    continue;
                }

                var cells = NormalizeDisciplineCells(row, LiftType.CleanAndJerk, isDisciplineConcluded: false);
                if (!cells.Any(HasCellContent))
                {
                    continue;
                }

                foreach (var c in cells)
                {
                    if (c.Status == ScoreboardAttemptStatus.Pass)
                    {
                        anyCjCompleted = true;
                        break;
                    }

                    if (c.Status is ScoreboardAttemptStatus.Good or ScoreboardAttemptStatus.Miss)
                    {
                        anyCjCompleted = true;
                        continue;
                    }

                    remainingCj++;
                }
            }

            return anyCjCompleted && remainingCj == 0;
        }
    }

    private static int CalculateRemainingSnatchAttempts(IReadOnlyList<ScoreboardLifterRow> rows, int cjCompletedCount)
    {
        // If Clean & Jerk attempts have already started on the platform, 0 Snatch attempts remain
        if (cjCompletedCount > 0)
        {
            return 0;
        }

        bool cjStarted = rows.Any(r =>
            !r.IsExcluded &&
            (r.Cj1.Status is ScoreboardAttemptStatus.Good or ScoreboardAttemptStatus.Miss or ScoreboardAttemptStatus.Pass ||
             r.Cj2.Status is ScoreboardAttemptStatus.Good or ScoreboardAttemptStatus.Miss or ScoreboardAttemptStatus.Pass ||
             r.Cj3.Status is ScoreboardAttemptStatus.Good or ScoreboardAttemptStatus.Miss or ScoreboardAttemptStatus.Pass ||
             HasCellContent(r.Cj2) ||
             HasCellContent(r.Cj3)));
        if (cjStarted)
        {
            return 0;
        }

        bool anySnatchData = HasAnyDisciplineData(rows, LiftType.Snatch);
        if (!anySnatchData)
        {
            return 0;
        }

        var activeSnatchRows = rows
            .Where(r => !r.IsExcluded && (HasCellContent(r.Snatch1) || HasCellContent(r.Snatch2) || HasCellContent(r.Snatch3)))
            .ToList();

        if (activeSnatchRows.Count > 0 &&
            HasAnyDisciplineData(rows, LiftType.CleanAndJerk) &&
            activeSnatchRows.All(r =>
                r.Snatch1.Status == ScoreboardAttemptStatus.Pass ||
                r.Snatch2.Status == ScoreboardAttemptStatus.Pass ||
                HasCellContent(r.Snatch3)))
        {
            return 0;
        }

        int remaining = 0;
        foreach (var row in rows)
        {
            if (row.IsExcluded)
            {
                continue;
            }

            var cells = NormalizeDisciplineCells(row, LiftType.Snatch, isDisciplineConcluded: false);
            bool rowHasSnatch = cells.Any(HasCellContent);
            if (!rowHasSnatch)
            {
                continue;
            }

            foreach (var c in cells)
            {
                if (c.Status == ScoreboardAttemptStatus.Pass)
                {
                    break;
                }

                if (c.Status is ScoreboardAttemptStatus.Good or ScoreboardAttemptStatus.Miss)
                {
                    continue;
                }

                remaining++;
            }
        }

        return remaining;
    }

    private static ScoreboardAttemptCell GetAttempt1Cell(ScoreboardLifterRow row, LiftType discipline)
        => discipline == LiftType.Snatch ? row.Snatch1 : row.Cj1;
}
