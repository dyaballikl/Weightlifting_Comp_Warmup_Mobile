using System.Globalization;
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
    string ProgressSummaryText,
    bool HasPresentationBuffer = false,
    int PresentationBufferSeconds = 0,
    bool IsFinalTenSeconds = false);

/// <summary>
/// Serializable snapshot of live session counters used to recover an active warmup timer
/// if the OS terminates the app in the background (e.g., while opening the camera app).
/// </summary>
public class PersistedLiveSessionState
{
    public string ProfileName { get; set; } = string.Empty;
    public LiftType LiftType { get; set; }
    public bool IsRunning { get; set; }
    public bool AutoAdvance { get; set; }
    public bool ForceCompetitionStarted { get; set; }
    public int LiftsOut { get; set; }
    public int LiftsPassed { get; set; }
    public int StageLiftElapsed { get; set; }
    public int CurrentStageLiftBonusSeconds { get; set; }
    public int CjSnatchLiftsOut { get; set; }
    public int CjSnatchStageElapsed { get; set; }
    public int CjBreakElapsed { get; set; }
    public int CjBreakMaximum { get; set; }
    public DateTime SavedUtc { get; set; }
}

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
    IReadOnlyList<LiveStepStatus> Steps,
    bool CanUndo = false,
    string NextUndoActionLabel = "",
    int CurrentStageLiftBonusSeconds = 0,
    bool IsStageFinalTenSeconds = false,
    string NextStepPreviewText = "");

/// <summary>
/// Stateful engine for running either Snatch LIVE or Clean &amp; Jerk LIVE warmup sessions.
/// Faithfully ports <c>timer_snatch_Tick</c>, <c>timer_cj_Tick</c>, and <c>TimerTickProcessing</c>
/// from <c>form_Main.cs</c> in the original WinForms application.
/// </summary>
public class LiveWarmupSession
{
    /// <summary>
    /// Optional platform-specific delegate returning <c>true</c> when the user's device is configured for 24-hour time,
    /// or <c>false</c> for 12-hour (AM/PM) time. Falls back to <see cref="CultureInfo.CurrentCulture"/> when unset.
    /// </summary>
    public static Func<bool>? Is24HourClockProvider { get; set; }

    public static bool Is24HourClock()
    {
        if (Is24HourClockProvider != null)
        {
            return Is24HourClockProvider();
        }

        var dtf = CultureInfo.CurrentCulture.DateTimeFormat;
        return dtf.LongTimePattern.Contains('H') || dtf.ShortTimePattern.Contains('H');
    }

    public static string FormatClockTime(DateTime time)
    {
        return Is24HourClock()
            ? time.ToString("HH:mm:ss", CultureInfo.InvariantCulture)
            : time.ToString("hh:mm:ss tt", CultureInfo.InvariantCulture);
    }

    private readonly Profile _profile;
    private readonly List<WarmupStep> _steps;
    private readonly List<UndoCheckpoint> _undoHistory = new();
    private const int MaxUndoHistory = 10;
    private DateTime _lastScrubCheckpointUtc;
    private int _previousActiveStepOrder = -1;
    private bool _forceCompetitionStarted;

    private readonly record struct UndoCheckpoint(
        string ActionLabel,
        bool ForceCompetitionStarted,
        int LiftsOut,
        int LiftsPassed,
        int StageLiftElapsed,
        int CurrentStageLiftBonusSeconds,
        int CjSnatchLiftsOut,
        int CjSnatchStageElapsed,
        int CjBreakElapsed,
        int CjBreakMaximum,
        int OpenerWeightKg);

    public LiftType LiftType { get; }
    public bool IsRunning { get; set; }
    public bool AutoAdvance { get; set; }
    public bool BeepEnabled { get; set; }
    public bool CanUndo => _undoHistory.Count > 0;
    public int UndoHistoryCount => _undoHistory.Count;
    public string NextUndoActionLabel => _undoHistory.Count > 0 ? _undoHistory[^1].ActionLabel : string.Empty;
    public string LastUndoneActionLabel { get; private set; } = string.Empty;
    public int CurrentOpenerWeightKg => LiftType == LiftType.Snatch ? _profile.SnatchOpenerWeight : _profile.CjOpenerWeight;

    // Shared stage lift tracking
    public int LiftsOut { get; private set; }
    public int LiftsPassed { get; private set; }
    public int StageLiftElapsed { get; private set; }
    public int BaseStageLiftMaximum { get; private set; }
    public int CurrentStageLiftBonusSeconds { get; private set; }
    public int StageLiftMaximum => BaseStageLiftMaximum + CurrentStageLiftBonusSeconds;

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
            BaseStageLiftMaximum = Math.Max(1, _profile.SnatchSecondsStage);
            CurrentStageLiftBonusSeconds = 0;
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
            BaseStageLiftMaximum = Math.Max(1, _profile.CjSecondsStage);
            CurrentStageLiftBonusSeconds = 0;
        }
    }

    private void PushUndoCheckpoint(string actionLabel, bool isScrub = false)
    {
        if (isScrub)
        {
            DateTime utcNow = DateTime.UtcNow;
            if (_undoHistory.Count > 0 &&
                _undoHistory[^1].ActionLabel == actionLabel &&
                (utcNow - _lastScrubCheckpointUtc).TotalSeconds < 1.5)
            {
                return;
            }
            _lastScrubCheckpointUtc = utcNow;
        }
        else
        {
            _lastScrubCheckpointUtc = default;
        }

        _undoHistory.Add(new UndoCheckpoint(
            actionLabel,
            _forceCompetitionStarted,
            LiftsOut,
            LiftsPassed,
            StageLiftElapsed,
            CurrentStageLiftBonusSeconds,
            CjSnatchLiftsOut,
            CjSnatchStageElapsed,
            CjBreakElapsed,
            CjBreakMaximum,
            CurrentOpenerWeightKg));

        if (_undoHistory.Count > MaxUndoHistory)
        {
            _undoHistory.RemoveAt(0);
        }
    }

    /// <summary>
    /// Reverts the most recent manual stage advance, Lifts Out edit, +1m extension, opener change, or scrub action.
    /// </summary>
    public LiveSessionSnapshot Undo(DateTime? now = null)
    {
        if (_undoHistory.Count > 0)
        {
            var prev = _undoHistory[^1];
            _undoHistory.RemoveAt(_undoHistory.Count - 1);

            LastUndoneActionLabel = prev.ActionLabel;
            _forceCompetitionStarted = prev.ForceCompetitionStarted;
            LiftsOut = prev.LiftsOut;
            LiftsPassed = prev.LiftsPassed;
            StageLiftElapsed = prev.StageLiftElapsed;
            CurrentStageLiftBonusSeconds = prev.CurrentStageLiftBonusSeconds;
            CjSnatchLiftsOut = prev.CjSnatchLiftsOut;
            CjSnatchStageElapsed = prev.CjSnatchStageElapsed;
            CjBreakElapsed = prev.CjBreakElapsed;
            CjBreakMaximum = prev.CjBreakMaximum;

            if (prev.OpenerWeightKg != CurrentOpenerWeightKg)
            {
                if (LiftType == LiftType.Snatch)
                {
                    _profile.SnatchOpenerWeight = prev.OpenerWeightKg;
                }
                else
                {
                    _profile.CjOpenerWeight = prev.OpenerWeightKg;
                }

                _steps.Clear();
                _steps.AddRange(WarmupCalculator.BuildLiveSteps(LiftType, _profile));
            }
        }
        else
        {
            LastUndoneActionLabel = string.Empty;
        }

        return CreateSnapshot(now ?? DateTime.Now, advanceClocks: false);
    }

    /// <summary>
    /// Extends only the currently active stage lift clock by <paramref name="bonusSeconds"/> (default +60s for a 2-minute self-follow clock)
    /// without altering <see cref="LiftsOut"/>. Automatically resets to 0 when the lift advances.
    /// </summary>
    public LiveSessionSnapshot AddCurrentStageLiftBonusSeconds(int bonusSeconds = 60, DateTime? now = null)
    {
        if (bonusSeconds <= 0)
        {
            return CreateSnapshot(now ?? DateTime.Now, advanceClocks: false);
        }

        int nextBonus = Math.Clamp(CurrentStageLiftBonusSeconds + bonusSeconds, 0, 300);
        if (nextBonus == CurrentStageLiftBonusSeconds)
        {
            return CreateSnapshot(now ?? DateTime.Now, advanceClocks: false);
        }

        PushUndoCheckpoint("+1m");
        CurrentStageLiftBonusSeconds = nextBonus;
        return CreateSnapshot(now ?? DateTime.Now, advanceClocks: false);
    }

    /// <summary>
    /// Exports a serializable snapshot of mutable live counters so an active warmup can be recovered
    /// if the OS terminates the app in the background.
    /// </summary>
    public PersistedLiveSessionState ExportPersistedState(string profileName, DateTime utcNow)
    {
        return new PersistedLiveSessionState
        {
            ProfileName = profileName,
            LiftType = LiftType,
            IsRunning = IsRunning,
            AutoAdvance = AutoAdvance,
            ForceCompetitionStarted = _forceCompetitionStarted,
            LiftsOut = LiftsOut,
            LiftsPassed = LiftsPassed,
            StageLiftElapsed = StageLiftElapsed,
            CurrentStageLiftBonusSeconds = CurrentStageLiftBonusSeconds,
            CjSnatchLiftsOut = CjSnatchLiftsOut,
            CjSnatchStageElapsed = CjSnatchStageElapsed,
            CjBreakElapsed = CjBreakElapsed,
            CjBreakMaximum = CjBreakMaximum,
            SavedUtc = utcNow
        };
    }

    /// <summary>
    /// Restores mutable live counters from a previously saved state and applies elapsed wall-clock catch-up ticks if it was running.
    /// </summary>
    public LiveSessionSnapshot RestorePersistedState(PersistedLiveSessionState state, DateTime nowLocal, DateTime nowUtc)
    {
        _undoHistory.Clear();
        _forceCompetitionStarted = state.ForceCompetitionStarted;
        LiftsOut = Math.Clamp(state.LiftsOut, 0, 99);
        LiftsPassed = Math.Clamp(state.LiftsPassed, 0, 999);
        CurrentStageLiftBonusSeconds = Math.Clamp(state.CurrentStageLiftBonusSeconds, 0, 300);
        StageLiftElapsed = Math.Clamp(state.StageLiftElapsed, 0, StageLiftMaximum);
        CjSnatchLiftsOut = Math.Clamp(state.CjSnatchLiftsOut, 0, 99);
        CjSnatchStageElapsed = Math.Clamp(state.CjSnatchStageElapsed, 0, CjSnatchStageMaximum);
        CjBreakMaximum = Math.Max(LiftType == LiftType.Snatch ? 0 : 1, state.CjBreakMaximum);
        CjBreakElapsed = Math.Clamp(state.CjBreakElapsed, 0, CjBreakMaximum);
        AutoAdvance = state.AutoAdvance;
        IsRunning = state.IsRunning;

        if (state.IsRunning && state.SavedUtc != default)
        {
            int elapsedSec = Math.Clamp((int)Math.Round((nowUtc - state.SavedUtc).TotalSeconds), 0, 7200);
            for (int i = 0; i < elapsedSec; i++)
            {
                DateTime steppedLocal = nowLocal.AddSeconds(-(elapsedSec - 1 - i));
                CreateSnapshot(steppedLocal, advanceClocks: true);
            }
        }

        return CreateSnapshot(nowLocal, advanceClocks: false);
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
            PushUndoCheckpoint(wasBreakOrSnatchActive ? "Skip Break" : "Advance");

            CjSnatchLiftsOut = 0;
            CjSnatchStageElapsed = CjSnatchStageMaximum;
            CjBreakElapsed = CjBreakMaximum;
            StageLiftElapsed = 0;
            CurrentStageLiftBonusSeconds = 0;

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
        PushUndoCheckpoint(wasPreStageActive ? "Skip Clock" : "Advance");

        _forceCompetitionStarted = true;
        CjBreakElapsed = CjBreakMaximum;
        StageLiftElapsed = 0;
        CurrentStageLiftBonusSeconds = 0;

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
            PushUndoCheckpoint("Next Snatch");
            CjSnatchLiftsOut--;
            CjSnatchStageElapsed = 0;
        }

        return CreateSnapshot(now ?? DateTime.Now, advanceClocks: false);
    }

    /// <summary>
    /// Updates the opener weight for this live session in-place, rebuilding the warmup step progression
    /// while preserving all live clock, break, and stage-lift progress state.
    /// </summary>
    public LiveSessionSnapshot UpdateOpenerWeight(int newOpenerWeightKg, DateTime? now = null)
    {
        int clamped = Math.Clamp(newOpenerWeightKg, _profile.BarbellWeight, Defaults.MaxOpenerWeight);
        if (clamped == CurrentOpenerWeightKg)
        {
            return CreateSnapshot(now ?? DateTime.Now, advanceClocks: false);
        }

        PushUndoCheckpoint("Opener");
        if (LiftType == LiftType.Snatch)
        {
            _profile.SnatchOpenerWeight = clamped;
        }
        else
        {
            _profile.CjOpenerWeight = clamped;
        }

        _steps.Clear();
        _steps.AddRange(WarmupCalculator.BuildLiveSteps(LiftType, _profile));

        return CreateSnapshot(now ?? DateTime.Now, advanceClocks: false);
    }

    /// <summary>
    /// Adjusts <see cref="LiftsOut"/> directly (via +/- buttons or numeric entry).
    /// </summary>
    public LiveSessionSnapshot SetLiftsOut(int liftsOut, DateTime? now = null)
    {
        int clamped = Math.Clamp(liftsOut, 0, 99);
        if (clamped != LiftsOut)
        {
            PushUndoCheckpoint("Lifts Out");
            LiftsOut = clamped;
        }

        if (LiftsOut == 0)
        {
            CurrentStageLiftBonusSeconds = 0;
            StageLiftElapsed = StageLiftMaximum;
        }
        else if (StageLiftElapsed >= StageLiftMaximum)
        {
            StageLiftElapsed = 0;
        }

        return CreateSnapshot(now ?? DateTime.Now, advanceClocks: false);
    }

    /// <summary>
    /// Applies a scoreboard scan result (optional <see cref="LiftsOut"/>, <see cref="LiftsPassed"/>, and optional
    /// remaining Snatch attempts for Clean &amp; Jerk) directly to the live session state.
    /// When <paramref name="liftsOut"/> is <c>null</c> (e.g. when a heavier weight than the user's app opener has already been lifted on stage),
    /// <see cref="LiftsOut"/> is left unchanged as-is.
    /// When <paramref name="liftsPassed"/> &gt; 0 for the current discipline, pre-stage clocks/breaks are
    /// automatically marked complete because platform lifting is already underway.
    /// </summary>
    public LiveSessionSnapshot ApplyScoreboardScan(
        int? liftsOut,
        int liftsPassed,
        int? remainingSnatchLifts = null,
        DateTime? now = null)
    {
        PushUndoCheckpoint("Board Scan");
        CurrentStageLiftBonusSeconds = 0;

        if (liftsOut.HasValue)
        {
            LiftsOut = Math.Clamp(liftsOut.Value, 0, 99);
        }

        LiftsPassed = Math.Clamp(liftsPassed, 0, 999);

        if (LiftType == LiftType.Snatch)
        {
            if (LiftsPassed > 0)
            {
                _forceCompetitionStarted = true;
                CjBreakElapsed = CjBreakMaximum;
                StageLiftElapsed = 0;
            }

            if (LiftsOut == 0)
            {
                StageLiftElapsed = StageLiftMaximum;
            }
            else if (StageLiftElapsed >= StageLiftMaximum)
            {
                StageLiftElapsed = 0;
            }
        }
        else
        {
            if (remainingSnatchLifts.HasValue)
            {
                CjSnatchLiftsOut = Math.Clamp(remainingSnatchLifts.Value, 0, 99);
            }

            if (LiftsPassed > 0)
            {
                // Clean & Jerk platform attempts have already started
                CjSnatchLiftsOut = 0;
                CjSnatchStageElapsed = CjSnatchStageMaximum;
                CjBreakElapsed = CjBreakMaximum;
                StageLiftElapsed = 0;
            }
            else if (remainingSnatchLifts.HasValue)
            {
                if (CjSnatchLiftsOut > 0)
                {
                    CjSnatchStageElapsed = 0;
                    CjBreakElapsed = 0;
                    StageLiftElapsed = 0;
                }
                else
                {
                    CjSnatchStageElapsed = CjSnatchStageMaximum;
                }
            }

            if (LiftsOut == 0)
            {
                StageLiftElapsed = StageLiftMaximum;
            }
            else if (StageLiftElapsed >= StageLiftMaximum)
            {
                StageLiftElapsed = 0;
            }
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
            int clamped = Math.Clamp(snatchLiftsOut, 0, 99);
            if (clamped != previous)
            {
                PushUndoCheckpoint("Snatch Left");
            }

            CjSnatchLiftsOut = clamped;
            if (CjSnatchLiftsOut > 0)
            {
                if (previous == 0)
                {
                    CjSnatchStageElapsed = 0;
                }
                CjBreakElapsed = 0;
                StageLiftElapsed = 0;
                CurrentStageLiftBonusSeconds = 0;
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
        PushUndoCheckpoint("Stage Scrub", isScrub: true);
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
            PushUndoCheckpoint("Snatch Scrub", isScrub: true);
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
        PushUndoCheckpoint("Break Scrub", isScrub: true);
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
        CurrentStageLiftBonusSeconds = 0;

        return CreateSnapshot(now ?? DateTime.Now, advanceClocks: false);
    }

    /// <summary>
    /// Directly sets the remaining presentation/break time in seconds during Snatch or Clean &amp; Jerk LIVE.
    /// Matches clicking <c>cjLIVE_label_BreakRemaining_Display</c> in the original WinForms app.
    /// </summary>
    public LiveSessionSnapshot SetCjBreakRemainingSeconds(int remainingSeconds, DateTime? now = null)
    {
        PushUndoCheckpoint("Break Time");
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
        CurrentStageLiftBonusSeconds = 0;

        return CreateSnapshot(now ?? DateTime.Now, advanceClocks: false);
    }

    /// <summary>
    /// Forces the Snatch competition start time to "passed" so the stage clock begins immediately.
    /// </summary>
    public LiveSessionSnapshot ForceStartStageNow(DateTime? now = null)
    {
        if (!_forceCompetitionStarted)
        {
            PushUndoCheckpoint("Start Now");
            _forceCompetitionStarted = true;
        }
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
                                CurrentStageLiftBonusSeconds = 0;
                            }
                        }
                        else
                        {
                            StageLiftElapsed++;
                        }
                    }

                    if (LiftsOut == 0)
                    {
                        CurrentStageLiftBonusSeconds = 0;
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
                    ? (LiftsOut - 1) * BaseStageLiftMaximum + Math.Max(0, StageLiftMaximum - StageLiftElapsed)
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
                    ? (LiftsOut - 1) * BaseStageLiftMaximum + Math.Max(0, StageLiftMaximum - StageLiftElapsed)
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
                            CurrentStageLiftBonusSeconds = 0;
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
                CurrentStageLiftBonusSeconds = 0;
                StageLiftElapsed = StageLiftMaximum;
                isNextLiftHighlighted = false;
            }

            int cjStageRemaining = LiftsOut > 0
                ? (LiftsOut - 1) * BaseStageLiftMaximum + Math.Max(0, StageLiftMaximum - StageLiftElapsed)
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

        var orderedSteps = _steps.OrderBy(s => s.Order).ToList();
        var stepStatuses = new List<LiveStepStatus>(orderedSteps.Count);
        foreach (var step in orderedSteps)
        {
            StepRunStatus status;
            double progressFraction;
            int elapsedSeconds;
            int durationSeconds = Math.Max(0, step.Length);
            string badgeText;
            string detailText;
            string progressSummaryText;
            bool isFinalTen = false;
            string presTag = step.HasPresentationBuffer && step.PresentationBufferSeconds > 0
                ? $" • 🎤 +{WarmupCalculator.FormatSeconds(step.PresentationBufferSeconds)}"
                : string.Empty;

            if (step.Order == activeStepOrder)
            {
                status = StepRunStatus.Active;
                int nextStepReverse = _steps.FirstOrDefault(s => s.Order == step.Order + 1)?.TotalLengthReverse ?? 0;
                int stepRemaining = Math.Max(0, secondsToOpen - nextStepReverse);
                elapsedSeconds = Math.Clamp(durationSeconds - stepRemaining, 0, durationSeconds);
                progressFraction = durationSeconds > 0 ? (double)elapsedSeconds / durationSeconds : 1.0;
                badgeText = step.IsOpener ? "OPEN NOW!" : WarmupCalculator.FormatSeconds(stepRemaining);
                isFinalTen = !step.IsOpener && stepRemaining > 0 && stepRemaining <= 10;
                detailText = step.PreStep
                    ? "Wait until warmup starts"
                    : step.IsOpener
                        ? step.Action
                        : $"{step.FormattedLength}{presTag} • Active Now";

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
                    : $"{step.FormattedLength}{presTag} (from {step.FormattedTotalLengthReverse} out)";
                progressSummaryText = string.Empty;
            }
            else
            {
                status = StepRunStatus.Completed;
                elapsedSeconds = durationSeconds;
                progressFraction = 1.0;
                badgeText = "Done";
                detailText = step.PreStep || step.IsOpener ? "Completed" : $"{step.FormattedLength}{presTag} • Completed";
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
                ProgressSummaryText: progressSummaryText,
                HasPresentationBuffer: step.HasPresentationBuffer,
                PresentationBufferSeconds: step.PresentationBufferSeconds,
                IsFinalTenSeconds: isFinalTen));
        }

        double stageProgress = StageLiftMaximum > 0 ? (double)StageLiftElapsed / StageLiftMaximum : 0.0;
        int stageRemainingSec = Math.Max(0, StageLiftMaximum - StageLiftElapsed);
        string bonusSuffix = CurrentStageLiftBonusSeconds > 0
            ? $" (+{CurrentStageLiftBonusSeconds}s)"
            : string.Empty;
        string stageStatusText = LiftsOut > 0
            ? $"{StageLiftElapsed}s / {StageLiftMaximum}s{bonusSuffix} ({stageRemainingSec}s left)"
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
        bool isStageFinalTen = isStageLiftingStarted && LiftsOut > 0 && stageRemainingSec <= 10;

        // Build concise "Next:" preview text for the active step / floating mini-timer
        var nextStep = orderedSteps.FirstOrDefault(s => s.Order > activeStepOrder);
        string nextStepPreview = string.Empty;
        if (nextStep != null)
        {
            if (nextStep.IsOpener)
            {
                nextStepPreview = $"Next: OPEN at {nextStep.Weight} kg";
            }
            else if (nextStep.IsLift && nextStep.Weight > 0)
            {
                var currentStep = orderedSteps.FirstOrDefault(s => s.Order == activeStepOrder);
                string jumpSuffix = (currentStep != null && currentStep.IsLift && currentStep.Weight > 0 && nextStep.Weight > currentStep.Weight)
                    ? $" (+{nextStep.Weight - currentStep.Weight} kg)"
                    : string.Empty;
                nextStepPreview = $"Next: {nextStep.Weight} kg{jumpSuffix}";
            }
            else
            {
                nextStepPreview = $"Next: {nextStep.Action}";
            }
        }

        return new LiveSessionSnapshot(
            LiftType: LiftType,
            IsRunning: IsRunning,
            AutoAdvance: AutoAdvance,
            CurrentTimeDisplay: FormatClockTime(now),
            OpenTimeDisplay: FormatClockTime(now.AddSeconds(secondsToOpen)),
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
            Steps: stepStatuses,
            CanUndo: CanUndo,
            NextUndoActionLabel: NextUndoActionLabel,
            CurrentStageLiftBonusSeconds: CurrentStageLiftBonusSeconds,
            IsStageFinalTenSeconds: isStageFinalTen,
            NextStepPreviewText: nextStepPreview);
    }
}
