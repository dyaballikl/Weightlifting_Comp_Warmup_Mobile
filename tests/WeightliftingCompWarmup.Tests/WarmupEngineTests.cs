using WeightliftingCompWarmup.Core.Models;
using WeightliftingCompWarmup.Core.Services;
using Xunit;

namespace WeightliftingCompWarmup.Tests;

public class WarmupEngineTests
{
    [Theory]
    [InlineData(0, "0s")]
    [InlineData(55, "55s")]
    [InlineData(60, "1m0s")]
    [InlineData(150, "2m30s")]
    [InlineData(3665, "1h1m5s")]
    public void FormatSeconds_MatchesOriginalWinFormsFormatting(int seconds, string expected)
    {
        Assert.Equal(expected, WarmupCalculator.FormatSeconds(seconds));
    }

    [Fact]
    public void CalculateWarmupWeights_DemoSnatch_AppliesSmoothedMidpointFix()
    {
        var demo = Defaults.CreateDemoProfile();

        // Demo Snatch: Bar=20, Opener=85, OpenerInWarmup=true
        // Unsmoothed top lifts would be 75 -> 80 -> 84 -> 85.
        // With SmoothLastJumps midpoint fix, 84 is replaced by Floor((85-80)/2 + 80) = 82.
        var weights = WarmupCalculator.CalculateWarmupWeights(
            demo.BarbellWeight,
            demo.SnatchOpenerWeight,
            demo.SnatchOpenerInWarmup,
            demo.SnatchJumps);

        Assert.Equal([20, 40, 50, 55, 60, 65, 70, 75, 80, 82, 85], weights);
    }

    [Fact]
    public void CalculateWarmupWeights_DemoCleanAndJerk_AppliesSmoothedMidpointBeforeOpener()
    {
        var demo = Defaults.CreateDemoProfile();

        // Demo C&J: Bar=20, Opener=110, OpenerInWarmup=false
        // Unsmoothed top lifts would be 90 -> 97 -> 104 -> 109 (1kg below 110 opener).
        // With SmoothLastJumps midpoint fix against 110 opener, 109 is replaced by Floor((110-104)/2 + 104) = 107.
        var weights = WarmupCalculator.CalculateWarmupWeights(
            demo.BarbellWeight,
            demo.CjOpenerWeight,
            demo.CjOpenerInWarmup,
            demo.CjJumps);

        Assert.Equal([20, 50, 60, 70, 80, 90, 97, 104, 107], weights);
    }

    [Fact]
    public void BuildSteps_IncludesPreStepExtrasAndLiftsWithAccurateReverseTotals()
    {
        var demo = Defaults.CreateDemoProfile();
        var steps = WarmupCalculator.BuildSteps(LiftType.Snatch, demo);

        // 1 PreStep ("wait") + 5 Extras + 11 Warmup Lifts = 17 steps
        Assert.Equal(17, steps.Count);
        Assert.True(steps[0].PreStep);
        Assert.Equal("wait", steps[0].Action);
        Assert.Equal(0, steps[0].Order);

        // Final warmup lift (85kg) has 150s base time + 60s SnatchSecondsEnd = 210s
        var lastLift = steps.Last();
        Assert.Equal(85, lastLift.Weight);
        Assert.Equal(210, lastLift.Length);
        Assert.Equal(210, lastLift.TotalLengthReverse);

        // PreStep TotalLengthReverse equals sum of all non-prestep lengths
        int sumLengths = steps.Where(s => !s.PreStep).Sum(s => s.Length);
        Assert.Equal(sumLengths, steps[0].TotalLengthReverse);
    }

    [Fact]
    public void BarbellLoadCalculator_PlacesFractionalChangePlatesOutsideCompetitionCollar()
    {
        // 20kg bar + 5kg comp collars + (25 + 10 + 1.5)*2 = 98kg opener
        var loadout = BarbellLoadCalculator.Calculate(liftWeightKg: 98, barWeightKg: 20, isOpener: true);

        Assert.True(loadout.HasCompetitionCollars);
        // Expected order from inner sleeve outward: 25kg (Red), 10kg (Green), 2.5kg Collar, 1.5kg (Yellow change plate outside collar)
        Assert.Equal(4, loadout.SleeveElements.Count);
        Assert.Equal(25.0m, loadout.SleeveElements[0].WeightKg);
        Assert.Equal(10.0m, loadout.SleeveElements[1].WeightKg);
        Assert.True(loadout.SleeveElements[2].IsCollar);
        Assert.Equal(1.5m, loadout.SleeveElements[3].WeightKg);
    }

    [Fact]
    public void BarbellLoadCalculator_UsesFullSizePlasticWhite5KgAndRed2Point5KgPlatesWhenBelowGreen10KgMinimum()
    {
        // 30kg warmup lift on 20kg bar -> 5kg per side (below 10kg green minimum -> full size white 5kg plastic plate)
        var load30 = BarbellLoadCalculator.Calculate(liftWeightKg: 30, barWeightKg: 20, isOpener: false);
        Assert.Single(load30.SleeveElements);
        Assert.Equal(5.0m, load30.SleeveElements[0].WeightKg);
        Assert.Equal(13, load30.SleeveElements[0].WidthUnits);
        Assert.Equal(1.00, load30.SleeveElements[0].HeightRatio);
        Assert.Equal(PlateColorType.White, load30.SleeveElements[0].ColorType);

        // 25kg warmup lift on 20kg bar -> 2.5kg per side (below 10kg green minimum -> full size red 2.5kg plastic plate)
        var load25 = BarbellLoadCalculator.Calculate(liftWeightKg: 25, barWeightKg: 20, isOpener: false);
        Assert.Single(load25.SleeveElements);
        Assert.Equal(2.5m, load25.SleeveElements[0].WeightKg);
        Assert.Equal(13, load25.SleeveElements[0].WidthUnits);
        Assert.Equal(1.00, load25.SleeveElements[0].HeightRatio);
        Assert.Equal(PlateColorType.Red, load25.SleeveElements[0].ColorType);

        // 35kg warmup lift on 20kg bar -> 5kg + 2.5kg per side (still below 10kg green minimum -> BOTH are full size 25kg-sized plastic plates!)
        var load35 = BarbellLoadCalculator.Calculate(liftWeightKg: 35, barWeightKg: 20, isOpener: false);
        Assert.Equal(2, load35.SleeveElements.Count);
        Assert.Equal(5.0m, load35.SleeveElements[0].WeightKg);
        Assert.Equal(13, load35.SleeveElements[0].WidthUnits);
        Assert.Equal(1.00, load35.SleeveElements[0].HeightRatio);
        Assert.Equal(PlateColorType.White, load35.SleeveElements[0].ColorType);
        Assert.Equal(2.5m, load35.SleeveElements[1].WeightKg);
        Assert.Equal(13, load35.SleeveElements[1].WidthUnits);
        Assert.Equal(1.00, load35.SleeveElements[1].HeightRatio);
        Assert.Equal(PlateColorType.Red, load35.SleeveElements[1].ColorType);
    }

    [Fact]
    public void LiveWarmupSession_Snatch_TransitionsStepsAndTriggersBeep()
    {
        var demo = Defaults.CreateDemoProfile();
        demo.Beep = true;
        demo.SnatchLiftsOut = 2;
        demo.SnatchSecondsStage = 50;

        Assert.Equal(840, demo.SnatchSecondsBreak); // 14 minutes default presentation + 10m clock
        Assert.Equal(14, demo.SnatchMinutesBreak);

        var session = new LiveWarmupSession(LiftType.Snatch, demo)
        {
            IsRunning = true
        };

        // Pass a time after 09:00:00 so competition start is reached and presentation/pre-snatch clock is active
        DateTime now = DateTime.Today.AddHours(10);
        var snap1 = session.GetSnapshot(now);
        Assert.True(snap1.HasCompetitionStarted);
        Assert.False(snap1.IsStageLiftingStarted);
        Assert.True(snap1.IsBreakActive);
        Assert.Equal("passed", snap1.TimeTillStartDisplay);
        // 840s presentation/clock + 2 lifts out * 50s = 940s to open
        Assert.Equal(940, snap1.SecondsToOpen);

        // Verify active step ProgressSummaryText does not include percentage prefix
        var activeStep = snap1.Steps.Single(s => s.Status == StepRunStatus.Active);
        Assert.DoesNotContain("%", activeStep.ProgressSummaryText);

        // First AdvanceStageLift ("ADVANCE TO STAGE LIFTING") skips the 14m presentation/clock and starts stage lifts without reducing LiftsOut
        var snapStageStarted = session.AdvanceStageLift(now);
        Assert.True(snapStageStarted.IsStageLiftingStarted);
        Assert.False(snapStageStarted.IsBreakActive);
        Assert.Equal(2, snapStageStarted.LiftsOut);
        Assert.Equal(0, snapStageStarted.LiftsPassed);
        Assert.Equal(100, snapStageStarted.SecondsToOpen);

        // Subsequent AdvanceStageLift ("ADVANCE: NEXT STAGE LIFT") advances to 1 lift out (50s to open)
        var snap2 = session.AdvanceStageLift(now);
        Assert.Equal(1, snap2.LiftsOut);
        Assert.Equal(1, snap2.LiftsPassed);
        Assert.Equal(50, snap2.SecondsToOpen);
    }

    [Fact]
    public void LiveWarmupSession_CleanAndJerk_ProgressesThroughThreePhases()
    {
        var demo = Defaults.CreateDemoProfile();
        demo.SnatchSecondsStage = 50;
        demo.CjSnatchLiftsOut = 1;
        demo.CjSecondsBreak = 600;
        demo.CjLiftsOut = 2;
        demo.CjSecondsStage = 60;

        var session = new LiveWarmupSession(LiftType.CleanAndJerk, demo)
        {
            IsRunning = true
        };

        DateTime now = DateTime.Today.AddHours(11);
        var initial = session.GetSnapshot(now);
        // 50s snatch + 600s break + 120s C&J stage = 770s
        Assert.Equal(770, initial.SecondsToOpen);
        Assert.True(initial.IsNextSnatchHighlighted);
        Assert.False(initial.IsBreakActive);

        // Advance snatch lift -> enters Break phase (600s break + 120s C&J = 720s)
        var afterSnatch = session.AdvanceCjSnatchLift(now);
        Assert.Equal(0, afterSnatch.CjSnatchLiftsOut);
        Assert.Equal(720, afterSnatch.SecondsToOpen);

        // Custom set break remaining to 30s -> 30s break + 120s C&J = 150s
        var customBreak = session.SetCjBreakRemainingSeconds(30, now);
        Assert.Equal(30, customBreak.CjBreakRemainingSeconds);
        Assert.Equal(150, customBreak.SecondsToOpen);

        // Advance stage lift while break is active -> skips break and starts C&J stage lifts WITHOUT reducing LiftsOut
        var stageStarted = session.AdvanceStageLift(now);
        Assert.Equal(0, stageStarted.CjBreakRemainingSeconds);
        Assert.Equal(2, stageStarted.LiftsOut);
        Assert.Equal(0, stageStarted.LiftsPassed);
        Assert.Equal(120, stageStarted.SecondsToOpen);
        Assert.True(stageStarted.IsNextLiftHighlighted);

        // Subsequent AdvanceStageLift once C&J stage lifts are active -> decrements LiftsOut to 1 (60s left)
        var stageActive = session.AdvanceStageLift(now);
        Assert.Equal(0, stageActive.CjBreakRemainingSeconds);
        Assert.Equal(1, stageActive.LiftsOut);
        Assert.Equal(1, stageActive.LiftsPassed);
        Assert.Equal(60, stageActive.SecondsToOpen);
    }

    [Fact]
    public void LiveWarmupSession_WhenSecondsToOpenExceedsTotalWarmup_ActivatesWaitPreStepInsteadOfOpener()
    {
        var profile = Defaults.CreateDemoProfile();
        profile.SnatchLiftsOut = 3;
        var session = new LiveWarmupSession(LiftType.Snatch, profile);

        // Pass 03:00 AM (6 hours before the 09:00 AM competition start time, well beyond the ~45m warmup duration)
        DateTime now = DateTime.Today.AddHours(3);
        var snapshot = session.GetSnapshot(now);
        Assert.True(snapshot.SecondsToOpen > 3600);
        Assert.Equal(0, snapshot.ActiveStepOrder);

        var waitStep = snapshot.Steps.Single(s => s.Order == 0);
        Assert.Equal(StepRunStatus.Active, waitStep.Status);
        Assert.True(waitStep.IsPreStep);

        var openerStep = snapshot.Steps.Single(s => s.IsOpener);
        Assert.Equal(StepRunStatus.Upcoming, openerStep.Status);
        Assert.All(snapshot.Steps.Where(s => s.Order >= 1), s => Assert.Equal(StepRunStatus.Upcoming, s.Status));
    }

    [Fact]
    public void ProfileStorageService_SupportsFullCrudLifecycle()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"wcw_test_{Guid.NewGuid():N}.json");
        try
        {
            var storage = new ProfileStorageService(tempFile);
            var initial = storage.Load();
            Assert.Equal("Demo Profile", initial.Name);

            var (added, _, _) = storage.AddNewProfile("Athlete A");
            Assert.True(added);
            Assert.Equal("Athlete A", storage.ActiveProfile.Name);

            var (duped, _, _) = storage.DuplicateActiveProfile("Athlete A Copy");
            Assert.True(duped);
            Assert.Equal(3, storage.Profiles.Count);

            var (renamed, _) = storage.RenameActiveProfile("Athlete B");
            Assert.True(renamed);
            Assert.Equal("Athlete B", storage.ActiveProfile.Name);

            storage.DeleteActiveProfile();
            Assert.Equal(2, storage.Profiles.Count);

            storage.ResetAllSettings();
            Assert.Single(storage.Profiles);
            Assert.Equal("Demo Profile", storage.ActiveProfile.Name);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public void WarmupCalculator_HandlesEditedLowestFromWeightAboveBarbellWeight()
    {
        var profile = Defaults.CreateDemoProfile();
        // Edit the base jump rule from 1kg -> 25kg (above 20kg barbell)
        int baseJump = profile.SnatchJumps[1];
        profile.SnatchJumps.Remove(1);
        profile.SnatchJumps[25] = baseJump;

        // Edit the base time rule from 1kg -> 25kg (above 20kg barbell)
        int baseTime = profile.SnatchTimes[1];
        profile.SnatchTimes.Remove(1);
        profile.SnatchTimes[25] = baseTime;

        var weights = WarmupCalculator.CalculateWarmupWeights(
            profile.BarbellWeight,
            profile.SnatchOpenerWeight,
            profile.SnatchOpenerInWarmup,
            profile.SnatchJumps);

        Assert.Equal([20, 40, 50, 55, 60, 65, 70, 75, 80, 82, 85], weights);

        var steps = WarmupCalculator.BuildSteps(LiftType.Snatch, profile);
        Assert.Equal(17, steps.Count);

        var ruleBase = new WeightJumpRule(25, 20, isBaseRule: true);
        Assert.False(ruleBase.CanDelete);
        var ruleNonBase = new WeightJumpRule(40, 10, isBaseRule: false);
        Assert.True(ruleNonBase.CanDelete);
    }
}
