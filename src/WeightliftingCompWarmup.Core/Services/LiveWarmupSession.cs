using WeightliftingCompWarmup.Core.Models;

namespace WeightliftingCompWarmup.Core.Services;

/// <summary>
/// Execution state of an individual step during a live warmup countdown.
/// </summary>
public enum StepRunStatus
{
    Completed,
    Active,
    Upcoming
}

/// <summary>
/// Snapshot of a single step's live progress, countdown badge, and formatting.
/// </summary>
public record LiveStepStatus(
    int Order,
    string ActionTitle,
    string ActionDetail,
    int WeightKg,
    bool IsLift,
    bool IsPreStep,
    bool IsOpener,
    StepRunStatus Status,
    double ProgressFraction,
    int ElapsedSeconds,
    int DurationSeconds,
    string CountdownBadgeText,
    string ProgressSummaryText);

/// <summary>
/// Complete telemetry snapshot produced on each tick or user interaction in the Live Warmup Runner.
/// </summary>
public record LiveSessionSnapshot(
    LiftType LiftType,
    bool IsRunning,
    bool AutoAdvance,
    string CurrentTimeDisplay,
    string OpenTimeDisplay,
    string TimeToOpenDisplay,
    int SecondsToOpen,
    string TimeTillStartDisplay,
    bool HasCompetitionStarted,
    bool IsStageLiftingStarted,
    int LiftsOut,
    int LiftsPassed,
    int StageLiftElapsed,
    int StageLiftMaximum,
    double StageLiftProgress,
    string StageLiftStatusText,
    bool IsNextLiftHighlighted,
    int CjSnatchLiftsOut,
    int CjSnatchStageElapsed,
    int CjSnatchStageMaximum,
    double CjSnatchStageProgress,
    string CjSnatchStageStatusText,
    bool IsNextSnatchHighlighted,
    int CjBreakElapsed,
    int CjBreakMaximum,
    int CjBreakRemainingSeconds,
    double CjBreakProgress,
    string CjBreakRemainingDisplay,
    bool IsBreakActive,
    int ActiveStepOrder,
    bool TriggerBeep,
    IReadOnlyList<LiveStepStatus> Steps);

/// <summary>
/// Stateful engine for running either Snatch LIVE or Clean &amp; Jerk LIVE warmup sessions.
/// Faithfully ports <c>timer_snatch_Tick</c>, <c>timer_cj_Tick</c>, and <c>TimerTickProcessing</c>
/// from <c>form_Main.cs</c> in the original WinForms application.
/// </summary>
public class LiveWarmupSession
{
    private readonly Profile _profile;
    private readonly List<WarmupStep> _steps;
    private int _previousActiveStepOrder = -1;
    private bool _forceCompetitionStarted;

    public LiftType LiftType { get; }
    public bool IsRunning { get; set; }
    public bool AutoAdvance { get; set; }
    public bool BeepEnabled { get; set; }

    // Shared stage lift tracking
    public int LiftsOut { get; private set; }
    public int LiftsPassed { get; private set; }
    public int StageLiftElapsed { get; private set; }
    public int StageLiftMaximum { get; private set; }

    // Clean & Jerk specific phase tracking
    public int CjSnatchLiftsOut { get; private set; }
    public int CjSnatchStageElapsed { get; private set; }
    public int CjSnatchStageMaximum { get; private set; }
    public int CjBreakElapsed { get; private set; }
    public int CjBreakMaximum { get; private set; }

    public IReadOnlyList<WarmupStep> Steps => _steps;

    public LiveWarmupSession(LiftType liftType, Profile profile)
    {
        LiftType = liftType;
        _profile = profile.Clone();
        BeepEnabled = _profile.Beep;
        _steps = WarmupCalculator.BuildLiveSteps(liftType, _profile);

        if (liftType == LiftType.Snatch)
        {
            CjBreakElapsed = 0;
            CjBreakMaximum = Math.Max(0, _profile.SnatchSecondsBreak);

            LiftsOut = Math.Clamp(_profile.SnatchLiftsOut, 0, 99);
            LiftsPassed = 0;
            StageLiftElapsed = 0;
            StageLiftMaximum = Math.Max(1, _profile.SnatchSecondsStage);
        }
        else
        {
            CjSnatchLiftsOut = Math.Clamp(_profile.CjSnatchLiftsOut, 0, 99);
            CjSnatchStageElapsed = 0;
            CjSnatchStageMaximum = Math.Max(1, _profile.SnatchSecondsStage);

            CjBreakElapsed = 0;
            CjBreakMaximum = Math.Max(1, _profile.CjSecondsBreak);

            LiftsOut = Math.Clamp(_profile.CjLiftsOut, 0, 99);
            LiftsPassed = 0;
            StageLiftElapsed = 0;
            StageLiftMaximum = Math.Max(1, _profile.CjSecondsStage);
        }
    }

    /// <summary>
    /// Advances the current stage lift ("Advance: Next Lift" / "Advance to Stage Lifting" button).
    /// When pre-stage phases (start countdown, presentation/break timer, or snatch phase in C&amp;J) are still active,
    /// clicking this skips straight to the end of the pre-stage phase and starts stage lifts without reducing <see cref="LiftsOut"/>.
    /// </summary>
    public LiveSessionSnapshot AdvanceStageLift(DateTime? now = null)
    {
        DateTime currentNow = now ?? DateTime.Now;

        if (LiftType == LiftType.CleanAndJerk)
        {
            bool wasBreakOrSnatchActive = CjSnatchLiftsOut > 0 || CjBreakElapsed < CjBreakMaximum;

            CjSnatchLiftsOut = 0;
            CjSnatchStageElapsed = CjSnatchStageMaximum;
            CjBreakElapsed = CjBreakMaximum;
            StageLiftElapsed = 0;

            if (!wasBreakOrSnatchActive && LiftsOut > 0)
            {
                LiftsOut--;
                LiftsPassed++;
            }

            return CreateSnapshot(currentNow, advanceClocks: false);
        }

        DateTime startToday = currentNow.Date.Add(_profile.Start.TimeOfDay);
        bool wasPreStageActive = (!_forceCompetitionStarted && (int)Math.Round((startToday - currentNow).TotalSeconds) > 0)
                                 || CjBreakElapsed < CjBreakMaximum;

        _forceCompetitionStarted = true;
        CjBreakElapsed = CjBreakMaximum;
        StageLiftElapsed = 0;

        if (!wasPreStageActive && LiftsOut > 0)
        {
            LiftsOut--;
            LiftsPassed++;
        }

        return CreateSnapshot(currentNow, advanceClocks: false);
    }

    /// <summary>
    /// Advances a remaining snatch attempt during Clean &amp; Jerk LIVE ("Advance: Next Snatch" button).
    /// Matches <c>cj_Advance_SnatchLift</c>.
    /// </summary>
    public LiveSessionSnapshot AdvanceCjSnatchLift(DateTime? now = null)
    {
        if (LiftType == LiftType.CleanAndJerk && CjSnatchLiftsOut > 0)
        {
            CjSnatchLiftsOut--;
            CjSnatchStageElapsed = 0;
        }

        return CreateSnapshot(now ?? DateTime.Now, advanceClocks: false);
    }

    /// <summary>
    /// Adjusts <see cref="LiftsOut"/> directly (via +/- buttons or numeric entry).
    /// </summary>
    public LiveSessionSnapshot SetLiftsOut(int liftsOut, DateTime? now = null)
    {
        LiftsOut = Math.Clamp(liftsOut, 0, 99);
        if (LiftsOut == 0)
        {
            StageLiftElapsed = StageLiftMaximum;
        }
        else if (StageLiftElapsed >= StageLiftMaximum)
        {
            StageLiftElapsed = 0;
        }

        return CreateSnapshot(now ?? DateTime.Now, advanceClocks: false);
    }

    /// <summary>
    /// Adjusts <see cref="CjSnatchLiftsOut"/> directly during Clean &amp; Jerk LIVE.
    /// </summary>
    public LiveSessionSnapshot SetCjSnatchLiftsOut(int snatchLiftsOut, DateTime? now = null)
    {
        if (LiftType == LiftType.CleanAndJerk)
        {
            int previous = CjSnatchLiftsOut;
            CjSnatchLiftsOut = Math.Clamp(snatchLiftsOut, 0, 99);
            if (CjSnatchLiftsOut > 0)
            {
                if (previous == 0)
                {
                    CjSnatchStageElapsed = 0;
                }
                CjBreakElapsed = 0;
                StageLiftElapsed = 0;
            }
            else
            {
                CjSnatchStageElapsed = CjSnatchStageMaximum;
            }
        }

        return CreateSnapshot(now ?? DateTime.Now, advanceClocks: false);
    }

    /// <summary>
    /// Scrubs the active Stage Lift progress bar by fraction (0.0 to 1.0) or elapsed seconds.
    /// Matches <c>stageLift_MouseDown</c> in <c>form_Main.cs</c>.
    /// </summary>
    public LiveSessionSnapshot ScrubStageLiftProgress(double fraction, DateTime? now = null)
    {
        fraction = Math.Clamp(fraction, 0.0, 1.0);
        StageLiftElapsed = (int)Math.Round(fraction * StageLiftMaximum);

        if (LiftType == LiftType.CleanAndJerk)
        {
            CjSnatchLiftsOut = 0;
            CjSnatchStageElapsed = CjSnatchStageMaximum;
            CjBreakElapsed = CjBreakMaximum;
        }
        else
        {
            _forceCompetitionStarted = true;
            CjBreakElapsed = CjBreakMaximum;
        }

        return CreateSnapshot(now ?? DateTime.Now, advanceClocks: false);
    }

    /// <summary>
    /// Scrubs the Clean &amp; Jerk Snatch-Attempts-Remaining progress bar by fraction (0.0 to 1.0).
    /// </summary>
    public LiveSessionSnapshot ScrubCjSnatchProgress(double fraction, DateTime? now = null)
    {
        if (LiftType == LiftType.CleanAndJerk && CjSnatchLiftsOut > 0)
        {
            fraction = Math.Clamp(fraction, 0.0, 1.0);
            CjSnatchStageElapsed = (int)Math.Round(fraction * CjSnatchStageMaximum);
        }

        return CreateSnapshot(now ?? DateTime.Now, advanceClocks: false);
    }

    /// <summary>
    /// Scrubs the Pre-Snatch Presentation/Clock or Clean &amp; Jerk Break progress bar by fraction (0.0 to 1.0).
    /// </summary>
    public LiveSessionSnapshot ScrubCjBreakProgress(double fraction, DateTime? now = null)
    {
        fraction = Math.Clamp(fraction, 0.0, 1.0);
        if (LiftType == LiftType.CleanAndJerk)
        {
            CjSnatchLiftsOut = 0;
            CjSnatchStageElapsed = CjSnatchStageMaximum;
        }
        else
        {
            _forceCompetitionStarted = true;
        }

        CjBreakElapsed = (int)Math.Round(fraction * CjBreakMaximum);
        StageLiftElapsed = 0;

        return CreateSnapshot(now ?? DateTime.Now, advanceClocks: false);
    }

    /// <summary>
    /// Directly sets the remaining presentation/break time in seconds during Snatch or Clean &amp; Jerk LIVE.
    /// Matches clicking <c>cjLIVE_label_BreakRemaining_Display</c> in the original WinForms app.
    /// </summary>
    public LiveSessionSnapshot SetCjBreakRemainingSeconds(int remainingSeconds, DateTime? now = null)
    {
        if (LiftType == LiftType.CleanAndJerk)
        {
            CjSnatchLiftsOut = 0;
            CjSnatchStageElapsed = CjSnatchStageMaximum;
        }
        else
        {
            _forceCompetitionStarted = true;
        }

        remainingSeconds = Math.Clamp(remainingSeconds, 0, 12000);
        if (remainingSeconds > CjBreakMaximum)
        {
            CjBreakMaximum = remainingSeconds;
        }

        CjBreakElapsed = Math.Clamp(CjBreakMaximum - remainingSeconds, 0, CjBreakMaximum);
        StageLiftElapsed = 0;

        return CreateSnapshot(now ?? DateTime.Now, advanceClocks: false);
    }

    /// <summary>
    /// Forces the Snatch competition start time to "passed" so the stage clock begins immediately.
    /// </summary>
    public LiveSessionSnapshot ForceStartStageNow(DateTime? now = null)
    {
        _forceCompetitionStarted = true;
        return CreateSnapshot(now ?? DateTime.Now, advanceClocks: false);
    }

    /// <summary>
    /// Advances the session by 1 second (when running) and returns the updated telemetry snapshot.
    /// </summary>
    public LiveSessionSnapshot Tick(DateTime? now = null)
    {
        return CreateSnapshot(now ?? DateTime.Now, advanceClocks: IsRunning);
    }

    /// <summary>
    /// Produces a current snapshot without advancing elapsed stage/break counters.
    /// </summary>
    public LiveSessionSnapshot GetSnapshot(DateTime? now = null)
    {
        return CreateSnapshot(now ?? DateTime.Now, advanceClocks: false);
    }

    private LiveSessionSnapshot CreateSnapshot(DateTime now, bool advanceClocks)
    {
        int secondsToOpen;
        string timeTillStartDisplay = "—";
        bool hasCompetitionStarted = true;
        bool isNextLiftHighlighted = false;
        bool isNextSnatchHighlighted = false;
        bool isBreakActive = false;

        if (LiftType == LiftType.Snatch)
        {
            DateTime startToday = now.Date.Add(_profile.Start.TimeOfDay);
            int secondsToStart = _forceCompetitionStarted
                ? 0
                : (int)Math.Round((startToday - now).TotalSeconds);

            if (secondsToStart <= 0)
            {
                hasCompetitionStarted = true;
                timeTillStartDisplay = "passed";

                // Phase 1: Presentation & pre-snatch clock
                if (CjBreakElapsed < CjBreakMaximum)
                {
                    if (advanceClocks)
                    {
                        CjBreakElapsed = Math.Min(CjBreakMaximum, CjBreakElapsed + 1);
                    }

                    isBreakActive = CjBreakElapsed < CjBreakMaximum;
                }
                else
                {
                    isBreakActive = false;
                }

                // Phase 2: Snatch Stage Lifts
                if (CjBreakElapsed >= CjBreakMaximum)
                {
                    isNextLiftHighlighted = LiftsOut > 0;

                    if (advanceClocks && LiftsOut > 0)
                    {
                        if (StageLiftElapsed >= StageLiftMaximum)
                        {
                            if (LiftsOut == 1 || AutoAdvance)
                            {
                                LiftsOut--;
                                LiftsPassed++;
                                StageLiftElapsed = 0;
                            }
                        }
                        else
                        {
                            StageLiftElapsed++;
                        }
                    }

                    if (LiftsOut == 0)
                    {
                        StageLiftElapsed = StageLiftMaximum;
                        isNextLiftHighlighted = false;
                    }
                }
                else
                {
                    isNextLiftHighlighted = false;
                }

                int breakRemaining = Math.Max(0, CjBreakMaximum - CjBreakElapsed);
                int snStageRemaining = LiftsOut > 0
                    ? (LiftsOut - 1) * StageLiftMaximum + (StageLiftMaximum - StageLiftElapsed)
                    : 0;
                secondsToOpen = Math.Max(0, breakRemaining + snStageRemaining);
            }
            else
            {
                hasCompetitionStarted = false;
                timeTillStartDisplay = WarmupCalculator.FormatSeconds(secondsToStart);
                isBreakActive = false;
                isNextLiftHighlighted = false;
                StageLiftElapsed = 0;
                int breakRemaining = Math.Max(0, CjBreakMaximum - CjBreakElapsed);
                int snStageRemaining = LiftsOut > 0
                    ? (LiftsOut - 1) * StageLiftMaximum + (StageLiftMaximum - StageLiftElapsed)
                    : 0;
                secondsToOpen = secondsToStart + breakRemaining + snStageRemaining;
            }
        }
        else
        {
            // Clean & Jerk 3-Phase Live Logic
            // Phase 1: Snatch Attempts Remaining
            if (CjSnatchLiftsOut > 0)
            {
                isNextSnatchHighlighted = true;
                if (advanceClocks)
                {
                    if (CjSnatchStageElapsed >= CjSnatchStageMaximum)
                    {
                        if (AutoAdvance)
                        {
                            CjSnatchLiftsOut--;
                            CjSnatchStageElapsed = 0;
                        }
                    }
                    else
                    {
                        CjSnatchStageElapsed++;
                    }
                }
            }

            if (CjSnatchLiftsOut == 0)
            {
                isNextSnatchHighlighted = false;
                CjSnatchStageElapsed = CjSnatchStageMaximum;
            }

            // Phase 2: Break After Snatch
            if (CjSnatchLiftsOut == 0 && CjBreakElapsed < CjBreakMaximum)
            {
                if (advanceClocks)
                {
                    CjBreakElapsed = Math.Min(CjBreakMaximum, CjBreakElapsed + 1);
                }

                isBreakActive = CjBreakElapsed < CjBreakMaximum;
            }
            else
            {
                isBreakActive = false;
            }

            // Phase 3: C&J Stage Lifts
            if (LiftsOut > 0 && CjBreakElapsed >= CjBreakMaximum && CjSnatchLiftsOut == 0)
            {
                isNextLiftHighlighted = true;
                if (advanceClocks)
                {
                    if (StageLiftElapsed >= StageLiftMaximum)
                    {
                        if (LiftsOut == 1 || AutoAdvance)
                        {
                            LiftsOut--;
                            LiftsPassed++;
                            StageLiftElapsed = 0;
                        }
                    }
                    else
                    {
                        StageLiftElapsed++;
                    }
                }
            }
            else
            {
                isNextLiftHighlighted = false;
            }

            if (LiftsOut == 0)
            {
                StageLiftElapsed = StageLiftMaximum;
                isNextLiftHighlighted = false;
            }

            int cjStageRemaining = LiftsOut > 0
                ? (LiftsOut - 1) * StageLiftMaximum + (StageLiftMaximum - StageLiftElapsed)
                : 0;
            int breakRemaining = Math.Max(0, CjBreakMaximum - CjBreakElapsed);
            int snatchRemaining = CjSnatchLiftsOut > 0
                ? (CjSnatchLiftsOut - 1) * CjSnatchStageMaximum + (CjSnatchStageMaximum - CjSnatchStageElapsed)
                : 0;

            secondsToOpen = Math.Max(0, cjStageRemaining + breakRemaining + snatchRemaining);
        }

        secondsToOpen = Math.Max(0, secondsToOpen);

        // Evaluate active step and progress bars (TimerTickProcessing in WinForms)
        var matchingOrders = _steps
            .Where(s => s.TotalLengthReverse >= secondsToOpen)
            .Select(s => s.Order)
            .ToList();

        // When secondsToOpen exceeds total warmup time, no step has TotalLengthReverse >= secondsToOpen,
        // so we fall back to Order 0 (the "wait" PreStep), matching WinForms .DefaultIfEmpty().Max().
        int activeStepOrder = matchingOrders.Count > 0 ? matchingOrders.Max() : 0;

        // Dynamically size PreStep ("wait", Order == 0) if secondsToOpen exceeds total warmup time
        var preStep = _steps.FirstOrDefault(s => s.Order == 0);
        int firstStepReverse = _steps.FirstOrDefault(s => s.Order == 1)?.TotalLengthReverse ?? 0;
        if (preStep != null && activeStepOrder == 0)
        {
            int waitNeeded = Math.Max(0, secondsToOpen - firstStepReverse);
            if (waitNeeded > preStep.Length)
            {
                preStep.Length = waitNeeded;
            }
        }

        bool triggerBeep = false;
        if (BeepEnabled &&
            _previousActiveStepOrder >= 0 &&
            activeStepOrder != _previousActiveStepOrder)
        {
            triggerBeep = true;
        }

        _previousActiveStepOrder = activeStepOrder;

        var stepStatuses = new List<LiveStepStatus>(_steps.Count);
        foreach (var step in _steps.OrderBy(s => s.Order))
        {
            StepRunStatus status;
            double progressFraction;
            int elapsedSeconds;
            int durationSeconds = Math.Max(0, step.Length);
            string badgeText;
            string detailText;
            string progressSummaryText;

            if (step.Order == activeStepOrder)
            {
                status = StepRunStatus.Active;
                int nextStepReverse = _steps.FirstOrDefault(s => s.Order == step.Order + 1)?.TotalLengthReverse ?? 0;
                int stepRemaining = Math.Max(0, secondsToOpen - nextStepReverse);
                elapsedSeconds = Math.Clamp(durationSeconds - stepRemaining, 0, durationSeconds);
                progressFraction = durationSeconds > 0 ? (double)elapsedSeconds / durationSeconds : 1.0;
                badgeText = step.IsOpener ? "OPEN NOW!" : WarmupCalculator.FormatSeconds(stepRemaining);
                detailText = step.PreStep
                    ? "Wait until warmup starts"
                    : step.IsOpener
                        ? step.Action
                        : $"{step.FormattedLength} • Active Now";

                if (step.IsOpener)
                {
                    progressSummaryText = "Ready for Platform";
                }
                else
                {
                    progressSummaryText = $"{WarmupCalculator.FormatSeconds(elapsedSeconds)} / {WarmupCalculator.FormatSeconds(durationSeconds)} ({WarmupCalculator.FormatSeconds(stepRemaining)} to next step)";
                }
            }
            else if (step.Order > activeStepOrder)
            {
                status = StepRunStatus.Upcoming;
                elapsedSeconds = 0;
                progressFraction = 0.0;
                int startsIn = Math.Max(0, secondsToOpen - step.TotalLengthReverse);
                badgeText = $"in {WarmupCalculator.FormatSeconds(startsIn)}";
                detailText = step.PreStep || step.IsOpener
                    ? $"from {step.FormattedTotalLengthReverse} out"
                    : $"{step.FormattedLength} (from {step.FormattedTotalLengthReverse} out)";
                progressSummaryText = string.Empty;
            }
            else
            {
                status = StepRunStatus.Completed;
                elapsedSeconds = durationSeconds;
                progressFraction = 1.0;
                badgeText = "Done";
                detailText = step.PreStep || step.IsOpener ? "Completed" : $"{step.FormattedLength} • Completed";
                progressSummaryText = string.Empty;
            }

            stepStatuses.Add(new LiveStepStatus(
                Order: step.Order,
                ActionTitle: step.Action,
                ActionDetail: detailText,
                WeightKg: step.Weight,
                IsLift: step.IsLift,
                IsPreStep: step.PreStep,
                IsOpener: step.IsOpener,
                Status: status,
                ProgressFraction: progressFraction,
                ElapsedSeconds: elapsedSeconds,
                DurationSeconds: durationSeconds,
                CountdownBadgeText: badgeText,
                ProgressSummaryText: progressSummaryText));
        }

        double stageProgress = StageLiftMaximum > 0 ? (double)StageLiftElapsed / StageLiftMaximum : 0.0;
        int stageRemainingSec = Math.Max(0, StageLiftMaximum - StageLiftElapsed);
        string stageStatusText = LiftsOut > 0
            ? $"{StageLiftElapsed}s / {StageLiftMaximum}s ({stageRemainingSec}s left)"
            : "Stage Complete";

        double cjSnatchProgress = CjSnatchStageMaximum > 0 ? (double)CjSnatchStageElapsed / CjSnatchStageMaximum : 1.0;
        int cjSnatchRemSec = Math.Max(0, CjSnatchStageMaximum - CjSnatchStageElapsed);
        string cjSnatchStatusText = CjSnatchLiftsOut > 0
            ? $"{CjSnatchStageElapsed}s / {CjSnatchStageMaximum}s ({cjSnatchRemSec}s left)"
            : "Snatch Complete";

        int cjBreakRemaining = Math.Max(0, CjBreakMaximum - CjBreakElapsed);
        double cjBreakProgress = CjBreakMaximum > 0 ? (double)CjBreakElapsed / CjBreakMaximum : 1.0;
        bool isStageLiftingStarted = LiftType == LiftType.Snatch
            ? (hasCompetitionStarted && CjBreakElapsed >= CjBreakMaximum)
            : (CjSnatchLiftsOut == 0 && CjBreakElapsed >= CjBreakMaximum);

        return new LiveSessionSnapshot(
            LiftType: LiftType,
            IsRunning: IsRunning,
            AutoAdvance: AutoAdvance,
            CurrentTimeDisplay: now.ToString("hh:mm:ss tt"),
            OpenTimeDisplay: now.AddSeconds(secondsToOpen).ToString("hh:mm:ss tt"),
            TimeToOpenDisplay: WarmupCalculator.FormatSeconds(secondsToOpen),
            SecondsToOpen: secondsToOpen,
            TimeTillStartDisplay: timeTillStartDisplay,
            HasCompetitionStarted: hasCompetitionStarted,
            IsStageLiftingStarted: isStageLiftingStarted,
            LiftsOut: LiftsOut,
            LiftsPassed: LiftsPassed,
            StageLiftElapsed: StageLiftElapsed,
            StageLiftMaximum: StageLiftMaximum,
            StageLiftProgress: Math.Clamp(stageProgress, 0.0, 1.0),
            StageLiftStatusText: stageStatusText,
            IsNextLiftHighlighted: isNextLiftHighlighted,
            CjSnatchLiftsOut: CjSnatchLiftsOut,
            CjSnatchStageElapsed: CjSnatchStageElapsed,
            CjSnatchStageMaximum: CjSnatchStageMaximum,
            CjSnatchStageProgress: Math.Clamp(cjSnatchProgress, 0.0, 1.0),
            CjSnatchStageStatusText: cjSnatchStatusText,
            IsNextSnatchHighlighted: isNextSnatchHighlighted,
            CjBreakElapsed: CjBreakElapsed,
            CjBreakMaximum: CjBreakMaximum,
            CjBreakRemainingSeconds: cjBreakRemaining,
            CjBreakProgress: Math.Clamp(cjBreakProgress, 0.0, 1.0),
            CjBreakRemainingDisplay: WarmupCalculator.FormatSeconds(cjBreakRemaining),
            IsBreakActive: isBreakActive,
            ActiveStepOrder: activeStepOrder,
            TriggerBeep: triggerBeep,
            Steps: stepStatuses);
    }
}
