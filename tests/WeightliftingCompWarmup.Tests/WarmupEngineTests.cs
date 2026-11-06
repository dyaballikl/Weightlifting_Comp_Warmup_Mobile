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
    public void BuildSteps_Snatch_AddsPresentationLiftBufferToLiftActiveAtCompetitionStart()
    {
        var profile = Defaults.CreateDemoProfile();
        // Default presentation lift buffer is 3 minutes (180s)
        Assert.Equal(180, profile.SnatchSecondsPresentationLift);
        Assert.Equal(3, profile.SnatchMinutesPresentationLift);

        // Without presentation buffer:
        profile.SnatchSecondsPresentationLift = 0;
        var baseSteps = WarmupCalculator.BuildSteps(LiftType.Snatch, profile);

        // Competition start is SnatchSecondsBreak (840s) + SnatchLiftsOut (4) * SnatchSecondsStage (50s) = 1040s before opener.
        // Presentation ends 180s later at 1040 - 180 = 860s before opener.
        // In Demo Snatch, 65kg spans [810s .. 960s] before opener without buffer, and [810s .. 1140s] with the 180s buffer,
        // so 65kg starts before competition start (1040s) and finishes after presentation ends (860s).
        var baseLift65 = baseSteps.Single(s => s.Weight == 65);
        Assert.Equal(150, baseLift65.Length);

        // With 3m (180s) presentation buffer:
        profile.SnatchMinutesPresentationLift = 3;
        var bufferedSteps = WarmupCalculator.BuildSteps(LiftType.Snatch, profile);
        var bufferedLift65 = bufferedSteps.Single(s => s.Weight == 65);
        Assert.Equal(330, bufferedLift65.Length); // 150s + 180s = 330s (5m30s)
        Assert.Equal(1140, bufferedLift65.TotalLengthReverse); // starts 19m0s before opener, ends 13m30s before opener

        // Total warmup duration increases by exactly 180s
        Assert.Equal(baseSteps[0].TotalLengthReverse + 180, bufferedSteps[0].TotalLengthReverse);
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
    public void ProfileStorageService_PersistsAndNormalizesThemePreference()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"wcw_theme_test_{Guid.NewGuid():N}.json");
        try
        {
            var storage = new ProfileStorageService(tempFile);
            storage.Load();
            Assert.Equal("System", storage.ThemePreference);

            storage.SetThemePreference("Light");
            Assert.Equal("Light", storage.ThemePreference);

            var reloaded = new ProfileStorageService(tempFile);
            reloaded.Load();
            Assert.Equal("Light", reloaded.ThemePreference);

            reloaded.SetThemePreference("dark");
            Assert.Equal("Dark", reloaded.ThemePreference);

            reloaded.SetThemePreference("invalid_value");
            Assert.Equal("System", reloaded.ThemePreference);
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

    [Fact]
    public void VersionManifestJson_ContainsValidSemanticVersions()
    {
        string repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        string manifestPath = Path.Combine(repoRoot, "version.json");
        Assert.True(File.Exists(manifestPath), $"Expected version.json at {manifestPath}");

        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = doc.RootElement;

        string? latest = root.GetProperty("latestVersion").GetString();
        string? androidLatest = root.GetProperty("androidLatestVersion").GetString();
        string? iosLatest = root.GetProperty("iosLatestVersion").GetString();

        Assert.False(string.IsNullOrWhiteSpace(latest));
        Assert.True(Version.TryParse(latest, out var parsedLatest));
        Assert.Equal(latest, androidLatest);
        Assert.Equal(latest, iosLatest);
        Assert.True(parsedLatest > new Version(2, 0, 0));
    }

    [Fact]
    public void SourceAndDocumentationFiles_ContainNoUtf8Mojibake()
    {
        string repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        string[] filesToCheck =
        [
            Path.Combine(repoRoot, "README.md"),
            Path.Combine(repoRoot, "src", "WeightliftingCompWarmup.Maui", "Views", "HelpAboutModalPage.cs"),
            Path.Combine(repoRoot, "src", "WeightliftingCompWarmup.Maui", "Views", "MainPage.cs")
        ];

        foreach (string path in filesToCheck)
        {
            string content = File.ReadAllText(path, System.Text.Encoding.UTF8);
            Assert.DoesNotContain("Ã", content);
            Assert.DoesNotContain("â€”", content);
            Assert.DoesNotContain("ðŸ", content);
        }
    }

    [Fact]
    public void ProfileStorageService_ExportsAndImportsBackupJson()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"compowl-test-{Guid.NewGuid():N}");
        try
        {
            var storage1 = new ProfileStorageService(Path.Combine(tempDir, "profiles1.json"));
            storage1.Load();
            storage1.SetThemePreference(ProfileStorageService.ThemeLight);
            var (added, err, created) = storage1.AddNewProfile("Athletes 89kg");
            Assert.True(added);
            Assert.Null(err);
            created.SnatchOpenerWeight = 135;
            created.CjOpenerWeight = 168;
            storage1.Save();

            string backupJson = storage1.ExportBackupJson();
            Assert.False(string.IsNullOrWhiteSpace(backupJson));
            Assert.Contains("Athletes 89kg", backupJson);

            var storage2 = new ProfileStorageService(Path.Combine(tempDir, "profiles2.json"));
            storage2.Load();

            var (imported, importErr, count) = storage2.ImportBackupJson(backupJson);
            Assert.True(imported);
            Assert.Null(importErr);
            Assert.Equal(2, count);
            Assert.Equal("Athletes 89kg", storage2.ActiveProfile.Name);
            Assert.Equal(135, storage2.ActiveProfile.SnatchOpenerWeight);
            Assert.Equal(168, storage2.ActiveProfile.CjOpenerWeight);
            Assert.Equal(ProfileStorageService.ThemeLight, storage2.ThemePreference);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void ProfileStorageService_SkipsWhatsNewOnFirstOpen_AndShowsChangesAfterUpdate()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"wcw_whatsnew_test_{Guid.NewGuid():N}.json");
        try
        {
            // 1. Brand new install (first open): HasSeenWelcomeHelp is false -> skips What's New
            var firstOpenStorage = new ProfileStorageService(tempFile);
            firstOpenStorage.Load();
            Assert.False(firstOpenStorage.HasSeenWelcomeHelp);

            var firstCheck = firstOpenStorage.ConsumeWhatsNewIfUpdated("2.0.28");
            Assert.False(firstCheck.HasUpdated);
            Assert.Empty(firstCheck.Notes);

            firstOpenStorage.MarkWelcomeHelpSeen("2.0.28");
            Assert.True(firstOpenStorage.HasSeenWelcomeHelp);
            Assert.Equal("2.0.28", firstOpenStorage.LastSeenAppVersion);

            // 2. Subsequent open on the same version (2.0.28) -> no update splash
            var sameVersionStorage = new ProfileStorageService(tempFile);
            sameVersionStorage.Load();
            var sameVersionCheck = sameVersionStorage.ConsumeWhatsNewIfUpdated("2.0.28");
            Assert.False(sameVersionCheck.HasUpdated);
            Assert.Empty(sameVersionCheck.Notes);

            // 3. App updates from 2.0.28 -> 2.0.30 -> shows summaries for 2.0.30 and 2.0.29
            var updatedStorage = new ProfileStorageService(tempFile);
            updatedStorage.Load();
            var updateCheck = updatedStorage.ConsumeWhatsNewIfUpdated("2.0.30");
            Assert.True(updateCheck.HasUpdated);
            Assert.Equal("2.0.28", updateCheck.PreviousVersion);
            Assert.Equal(["2.0.30", "2.0.29"], updateCheck.Notes.Select(n => n.Version).ToArray());

            // 4. Opening 2.0.30 again does not repeat the splash
            var postUpdateStorage = new ProfileStorageService(tempFile);
            postUpdateStorage.Load();
            var postUpdateCheck = postUpdateStorage.ConsumeWhatsNewIfUpdated("2.0.30");
            Assert.False(postUpdateCheck.HasUpdated);
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
    public void ScoreboardCalculator_SCurveIncrement_MatchesExpectedWeightsAndMaxCap()
    {
        // 1st -> 2nd attempt S-curve anchors: 20kg -> +1kg, 40kg -> +2kg, 65kg -> +3kg, 90kg -> +4kg, 130kg/190kg -> +5kg max cap
        Assert.Equal(1, ScoreboardCalculator.CalculateProjectedJumpKg(20, fromAttemptNumber: 1, ScoreboardAttemptStatus.Good));
        Assert.Equal(2, ScoreboardCalculator.CalculateProjectedJumpKg(40, fromAttemptNumber: 1, ScoreboardAttemptStatus.Good));
        Assert.Equal(3, ScoreboardCalculator.CalculateProjectedJumpKg(65, fromAttemptNumber: 1, ScoreboardAttemptStatus.Good));
        Assert.Equal(4, ScoreboardCalculator.CalculateProjectedJumpKg(90, fromAttemptNumber: 1, ScoreboardAttemptStatus.Good));
        Assert.Equal(5, ScoreboardCalculator.CalculateProjectedJumpKg(130, fromAttemptNumber: 1, ScoreboardAttemptStatus.Good));
        Assert.Equal(5, ScoreboardCalculator.CalculateProjectedJumpKg(190, fromAttemptNumber: 1, ScoreboardAttemptStatus.Good));
        // Missed attempt always repeats weight (+0 kg)
        Assert.Equal(0, ScoreboardCalculator.CalculateProjectedJumpKg(90, fromAttemptNumber: 1, ScoreboardAttemptStatus.Miss));
    }

    [Fact]
    public void ScoreboardCalculator_SecondToThirdJump_NeverStepsDownWhenFirstJumpIs3KgOrLess_AndStepsDownSlightlyAbove3Kg()
    {
        // Whenever the 1st -> 2nd jump is <= 3 kg (20kg..75kg), 2nd -> 3rd jump must never step down
        for (int weight = 20; weight <= 75; weight++)
        {
            int firstJump = ScoreboardCalculator.CalculateProjectedJumpKg(weight, fromAttemptNumber: 1, ScoreboardAttemptStatus.Good);
            Assert.True(firstJump <= 3);
            int secondJump = ScoreboardCalculator.CalculateProjectedJumpKg(
                weight + firstJump,
                fromAttemptNumber: 2,
                ScoreboardAttemptStatus.Good,
                firstJumpKg: firstJump);
            Assert.Equal(firstJump, secondJump);
        }

        // In the 4 kg bracket (76kg..103kg), 2nd -> 3rd jump steps down by at most 1 kg in the lower ~40% of the bracket
        int stepDownCountIn4KgBracket = 0;
        int totalIn4KgBracket = 0;
        for (int weight = 76; weight <= 103; weight++)
        {
            int firstJump = ScoreboardCalculator.CalculateProjectedJumpKg(weight, fromAttemptNumber: 1, ScoreboardAttemptStatus.Good);
            Assert.Equal(4, firstJump);
            int secondJump = ScoreboardCalculator.CalculateProjectedJumpKg(
                weight + firstJump,
                fromAttemptNumber: 2,
                ScoreboardAttemptStatus.Good,
                firstJumpKg: firstJump);
            Assert.InRange(secondJump, 3, 4);
            totalIn4KgBracket++;
            if (secondJump == 3)
            {
                stepDownCountIn4KgBracket++;
            }
        }

        double stepDownRatio = (double)stepDownCountIn4KgBracket / totalIn4KgBracket;
        Assert.InRange(stepDownRatio, 0.30, 0.50);
    }

    [Fact]
    public void ScoreboardCalculator_IwfPriorityRules_AttemptNumberBeatsLot_AndLowerLotBreaksFirstAttemptTies()
    {
        // Scenario:
        // - Lifter A (Lot 2) made 91 kg on 1st attempt and is declared at 95 kg on 2nd attempt (user overrode to 95 kg).
        // - Lifter B (Lot 5) opens at 95 kg on 1st attempt.
        // - User (Lot 12) also opens at 95 kg on 1st attempt.
        // Expected order at 95 kg:
        //   1. Lifter B (95 kg, 1st attempt, Lot 5) — 1st attempt beats 2nd attempt, and Lot 5 < Lot 12
        //   2. User     (95 kg, 1st attempt, Lot 12) — 1st attempt beats Lifter A's 2nd attempt despite Lot 12 > Lot 2!
        //   3. Lifter A (95 kg, 2nd attempt, Lot 2)
        var lifters = new List<ScoreboardLifterRow>
        {
            new()
            {
                OrderNumber = 1,
                LotNumber = 2,
                Name = "Lifter A (2nd @ 95)",
                Snatch1 = new ScoreboardAttemptCell(91, ScoreboardAttemptStatus.Good),
                Snatch2 = new ScoreboardAttemptCell(95, ScoreboardAttemptStatus.Pending),
                Snatch3 = new ScoreboardAttemptCell(null, ScoreboardAttemptStatus.Empty)
            },
            new()
            {
                OrderNumber = 2,
                LotNumber = 5,
                Name = "Lifter B (1st @ 95, Lot 5)",
                Snatch1 = new ScoreboardAttemptCell(95, ScoreboardAttemptStatus.Pending),
                Snatch2 = new ScoreboardAttemptCell(null, ScoreboardAttemptStatus.Empty),
                Snatch3 = new ScoreboardAttemptCell(null, ScoreboardAttemptStatus.Empty)
            },
            new()
            {
                OrderNumber = 3,
                LotNumber = 12,
                Name = "User (1st @ 95, Lot 12)",
                Snatch1 = new ScoreboardAttemptCell(95, ScoreboardAttemptStatus.Pending),
                Snatch2 = new ScoreboardAttemptCell(null, ScoreboardAttemptStatus.Empty),
                Snatch3 = new ScoreboardAttemptCell(null, ScoreboardAttemptStatus.Empty)
            }
        };

        var result = ScoreboardCalculator.AnalyzeScoreboard(
            lifters,
            LiftType.Snatch,
            userLifterIndex: 2,
            setupOpenerWeightKg: 95);

        // 1 lift has already passed (Lifter A's 91 kg 1st attempt)
        Assert.Equal(1, result.LiftsPassed);
        // Only Lifter B's 1st attempt at 95 kg goes before User's 1st attempt at 95 kg (Lifter A's 2nd attempt at 95 kg goes AFTER User)
        Assert.Equal(1, result.LiftsOut);
        Assert.Equal(95, result.UserOpenerWeightKg);
        Assert.Single(result.UpcomingCallsBeforeUser);
        Assert.Equal("Lifter B (1st @ 95, Lot 5)", result.UpcomingCallsBeforeUser[0].LifterName);
    }

    [Fact]
    public void ScoreboardCalculator_RetroactiveReplay_DeterminesPreviousAttemptSequenceForSecondAttemptTies()
    {
        // Mid-session scan where two lifters (C and D) are both going to 92 kg on their 2nd attempt,
        // before User opens at 95 kg:
        // - Lifter C (Lot 10) opened at 88 kg (Good)
        // - Lifter D (Lot 3)  opened at 89 kg (Good)
        // Even though Lifter D has a lower Lot (3 < 10), Lifter C lifted EARLIER on 1st attempt (88 kg < 89 kg),
        // so at 92 kg (2nd attempt vs 2nd attempt), Lifter C must be called before Lifter D.
        var lifters = new List<ScoreboardLifterRow>
        {
            new()
            {
                OrderNumber = 1,
                LotNumber = 3,
                Name = "Lifter D (Opened 89)",
                Snatch1 = new ScoreboardAttemptCell(89, ScoreboardAttemptStatus.Good),
                Snatch2 = new ScoreboardAttemptCell(92, ScoreboardAttemptStatus.Pending),
                Snatch3 = new ScoreboardAttemptCell(null, ScoreboardAttemptStatus.Empty)
            },
            new()
            {
                OrderNumber = 2,
                LotNumber = 10,
                Name = "Lifter C (Opened 88)",
                Snatch1 = new ScoreboardAttemptCell(88, ScoreboardAttemptStatus.Good),
                Snatch2 = new ScoreboardAttemptCell(92, ScoreboardAttemptStatus.Pending),
                Snatch3 = new ScoreboardAttemptCell(null, ScoreboardAttemptStatus.Empty)
            },
            new()
            {
                OrderNumber = 3,
                LotNumber = 15,
                Name = "User (Opener 95)",
                Snatch1 = new ScoreboardAttemptCell(95, ScoreboardAttemptStatus.Pending),
                Snatch2 = new ScoreboardAttemptCell(null, ScoreboardAttemptStatus.Empty),
                Snatch3 = new ScoreboardAttemptCell(null, ScoreboardAttemptStatus.Empty)
            }
        };

        var result = ScoreboardCalculator.AnalyzeScoreboard(
            lifters,
            LiftType.Snatch,
            userLifterIndex: 2,
            setupOpenerWeightKg: 95);

        Assert.Equal(2, result.LiftsPassed);
        // Upcoming calls #1 and #2 before User start with both lifters' 2nd attempts at 92 kg:
        // First 92 kg call must be Lifter C (who lifted earlier at 88 kg on Attempt 1), followed by Lifter D (who lifted at 89 kg on Attempt 1)
        Assert.True(result.UpcomingCallsBeforeUser.Count >= 2);
        Assert.Equal("Lifter C (Opened 88)", result.UpcomingCallsBeforeUser[0].LifterName);
        Assert.Equal("Lifter D (Opened 89)", result.UpcomingCallsBeforeUser[1].LifterName);
    }

    [Fact]
    public void LiveWarmupSession_ApplyScoreboardScan_UpdatesLiftsOutAndLiftsPassed()
    {
        var demo = Defaults.CreateDemoProfile();
        demo.SnatchLiftsOut = 10;
        demo.SnatchSecondsStage = 50;

        var session = new LiveWarmupSession(LiftType.Snatch, demo)
        {
            IsRunning = true
        };

        DateTime now = DateTime.Today.AddHours(10);
        // Apply mid-session scoreboard scan: 6 lifts out, 4 lifts already passed
        var snap = session.ApplyScoreboardScan(liftsOut: 6, liftsPassed: 4, now: now);

        Assert.Equal(6, snap.LiftsOut);
        Assert.Equal(4, snap.LiftsPassed);
        Assert.True(snap.IsStageLiftingStarted);
        Assert.False(snap.IsBreakActive);
        Assert.Equal(300, snap.SecondsToOpen); // 6 lifts out * 50s = 300s
    }

    [Fact]
    public void ScoreboardCalculator_IsLifterSnatchDone_DetectsCompletedSnatchAndSwitchesToCleanAndJerk()
    {
        var lifters = new List<ScoreboardLifterRow>
        {
            new()
            {
                OrderNumber = 1,
                LotNumber = 2,
                Name = "Lifter A",
                Snatch1 = new ScoreboardAttemptCell(85, ScoreboardAttemptStatus.Good),
                Snatch2 = new ScoreboardAttemptCell(88, ScoreboardAttemptStatus.Good),
                Snatch3 = new ScoreboardAttemptCell(90, ScoreboardAttemptStatus.Miss),
                Cj1 = new ScoreboardAttemptCell(108, ScoreboardAttemptStatus.Good),
                Cj2 = new ScoreboardAttemptCell(109, ScoreboardAttemptStatus.Pending),
                Cj3 = new ScoreboardAttemptCell(null, ScoreboardAttemptStatus.Empty)
            },
            new()
            {
                OrderNumber = 2,
                LotNumber = 5,
                Name = "Matt Cunningham",
                Snatch1 = new ScoreboardAttemptCell(90, ScoreboardAttemptStatus.Good),
                Snatch2 = new ScoreboardAttemptCell(93, ScoreboardAttemptStatus.Good),
                Snatch3 = new ScoreboardAttemptCell(95, ScoreboardAttemptStatus.Good),
                Cj1 = new ScoreboardAttemptCell(115, ScoreboardAttemptStatus.Pending),
                Cj2 = new ScoreboardAttemptCell(null, ScoreboardAttemptStatus.Empty),
                Cj3 = new ScoreboardAttemptCell(null, ScoreboardAttemptStatus.Empty)
            }
        };

        int matchedIdx = ScoreboardCalculator.AutoMatchUserLifterIndex(
            lifters,
            profileName: "Matt",
            setupOpenerWeightKg: 90,
            discipline: LiftType.Snatch,
            fallbackCjOpenerWeightKg: 115);

        Assert.Equal(1, matchedIdx);
        Assert.True(ScoreboardCalculator.IsLifterSnatchDone(lifters, matchedIdx));

        var cjResult = ScoreboardCalculator.AnalyzeScoreboard(
            lifters,
            LiftType.CleanAndJerk,
            userLifterIndex: matchedIdx,
            setupOpenerWeightKg: 115);

        Assert.Equal(1, cjResult.LiftsPassed);
        Assert.Equal(115, cjResult.UserOpenerWeightKg);
        Assert.Equal(0, cjResult.RemainingSnatchLifts);
        // Lifter A's 2nd attempt (108 + 5 = 113 kg) is before User's 115 kg opener; 3rd attempt (113 + 4 = 117 kg) is after
        Assert.Equal(1, cjResult.LiftsOut);
    }

    [Fact]
    public void GeminiModelResolver_ExtractsSuggestedReplacementModelAndRanksFutureVersionsAutomatically()
    {
        string deprecationError =
            "This model models/gemini-2.0-flash is no longer available. Please update your code to use models/gemini-3.8-flash for the latest features and improvements. We recommend you to use the Interactions API (https://ai.google.dev/gemini-api/docs/get-started).";

        string? suggested = GeminiModelResolver.ExtractSuggestedModelFromError(deprecationError, "gemini-2.0-flash");
        Assert.Equal("gemini-3.8-flash", suggested);

        string sampleModelsListJson = """
        {
          "models": [
            {
              "name": "models/text-embedding-004",
              "supportedGenerationMethods": ["embedContent"]
            },
            {
              "name": "models/gemini-2.0-flash",
              "supportedGenerationMethods": ["generateContent"]
            },
            {
              "name": "models/gemini-2.5-flash",
              "supportedGenerationMethods": ["generateContent"]
            },
            {
              "name": "models/gemini-flash-latest",
              "supportedGenerationMethods": ["generateContent"]
            },
            {
              "name": "models/gemini-3.8-flash",
              "supportedGenerationMethods": ["generateContent"]
            },
            {
              "name": "models/gemini-4.2-flash",
              "supportedGenerationMethods": ["generateContent"]
            },
            {
              "name": "models/gemini-3.8-pro",
              "supportedGenerationMethods": ["generateContent"]
            }
          ]
        }
        """;

        var ranked = GeminiModelResolver.RankAvailableVisionModelsFromJson(sampleModelsListJson);
        Assert.DoesNotContain("text-embedding-004", ranked);
        Assert.DoesNotContain("gemini-2.0-flash", ranked);
        Assert.Equal("gemini-4.2-flash", ranked[0]);
        Assert.Equal("gemini-3.8-flash", ranked[1]);
    }

    [Fact]
    public void ScoreboardCalculator_AlwaysUsesAppOpenerWeightInsteadOfStrategicBoardWeight()
    {
        // User declared 85 kg on the scoreboard for strategic reasons, but entered 95 kg in the app.
        // Lifter A already lifted 88 kg (Good) and is declared at 92 kg (2nd attempt).
        // Lifter B is declared at 90 kg (1st attempt) -> S-curve projects 94 kg (2nd) and 98 kg (3rd).
        // Even though 88 kg has already been lifted (> 85 kg on the board), 88 kg < 95 kg (app opener),
        // so LiftsOut must be calculated to 95 kg (90 kg, 92 kg, 94 kg = 3 lifts out).
        var lifters = new List<ScoreboardLifterRow>
        {
            new()
            {
                OrderNumber = 1,
                LotNumber = 2,
                Name = "Lifter A",
                Snatch1 = new ScoreboardAttemptCell(88, ScoreboardAttemptStatus.Good),
                Snatch2 = new ScoreboardAttemptCell(92, ScoreboardAttemptStatus.Pending),
                Snatch3 = new ScoreboardAttemptCell(null, ScoreboardAttemptStatus.Empty)
            },
            new()
            {
                OrderNumber = 2,
                LotNumber = 4,
                Name = "Lifter B",
                Snatch1 = new ScoreboardAttemptCell(90, ScoreboardAttemptStatus.Pending),
                Snatch2 = new ScoreboardAttemptCell(null, ScoreboardAttemptStatus.Empty),
                Snatch3 = new ScoreboardAttemptCell(null, ScoreboardAttemptStatus.Empty)
            },
            new()
            {
                OrderNumber = 3,
                LotNumber = 7,
                Name = "Matt Cunningham",
                Snatch1 = new ScoreboardAttemptCell(85, ScoreboardAttemptStatus.Pending),
                Snatch2 = new ScoreboardAttemptCell(null, ScoreboardAttemptStatus.Empty),
                Snatch3 = new ScoreboardAttemptCell(null, ScoreboardAttemptStatus.Empty)
            }
        };

        var result = ScoreboardCalculator.AnalyzeScoreboard(
            lifters,
            LiftType.Snatch,
            userLifterIndex: 2,
            setupOpenerWeightKg: 95);

        Assert.Equal(95, result.UserOpenerWeightKg);
        Assert.Equal(85, result.BoardDeclaredUserOpenerKg);
        Assert.False(result.IsBarPastUserOpener);
        Assert.Equal(1, result.LiftsPassed);
        // Upcoming before 95 kg: Lifter B Att 1 (90 kg), Lifter A Att 2 (92 kg), Lifter B Att 2 (94 kg) => 3 Lifts Out
        // (Lifter A Att 3 is 92 + 4 = 96 kg, which is after 95 kg)
        Assert.Equal(3, result.LiftsOut);
    }

    [Fact]
    public void ScoreboardCalculator_WhenCompletedWeightExceedsAppOpener_FlagsBarPastUserOpenerAndLeavesLiftsOutAsIs()
    {
        // 100 kg has already been lifted on the scoreboard, while the user has 95 kg entered in the app as their opener.
        var lifters = new List<ScoreboardLifterRow>
        {
            new()
            {
                OrderNumber = 1,
                LotNumber = 3,
                Name = "Lifter A",
                Snatch1 = new ScoreboardAttemptCell(100, ScoreboardAttemptStatus.Good),
                Snatch2 = new ScoreboardAttemptCell(104, ScoreboardAttemptStatus.Pending),
                Snatch3 = new ScoreboardAttemptCell(null, ScoreboardAttemptStatus.Empty)
            },
            new()
            {
                OrderNumber = 2,
                LotNumber = 8,
                Name = "Matt Cunningham",
                Snatch1 = new ScoreboardAttemptCell(85, ScoreboardAttemptStatus.Pending),
                Snatch2 = new ScoreboardAttemptCell(null, ScoreboardAttemptStatus.Empty),
                Snatch3 = new ScoreboardAttemptCell(null, ScoreboardAttemptStatus.Empty)
            }
        };

        var result = ScoreboardCalculator.AnalyzeScoreboard(
            lifters,
            LiftType.Snatch,
            userLifterIndex: 1,
            setupOpenerWeightKg: 95);

        Assert.Equal(95, result.UserOpenerWeightKg);
        Assert.True(result.IsBarPastUserOpener);
        Assert.Equal(100, result.MaxCompletedWeightKg);
        Assert.Contains("100 kg", result.BarPastUserOpenerMessage);
        Assert.Contains("95 kg", result.BarPastUserOpenerMessage);
        Assert.Equal(1, result.LiftsPassed);

        // Applying the scan with liftsOut: null must leave the existing LiftsOut untouched
        var demo = Defaults.CreateDemoProfile();
        demo.SnatchLiftsOut = 7;
        var session = new LiveWarmupSession(LiftType.Snatch, demo)
        {
            IsRunning = true
        };

        var snap = session.ApplyScoreboardScan(
            liftsOut: result.IsBarPastUserOpener ? null : result.LiftsOut,
            liftsPassed: result.LiftsPassed,
            now: DateTime.Today.AddHours(10));

        Assert.Equal(7, snap.LiftsOut);
        Assert.Equal(1, snap.LiftsPassed);
    }

    [Fact]
    public void ScoreboardCalculator_WhenUserOpenerAlreadyLifted_MarksUserOpenerCompletedWithoutCalculatingLiftsOut()
    {
        var lifters = new List<ScoreboardLifterRow>
        {
            new()
            {
                OrderNumber = 1,
                LotNumber = 2,
                Name = "Matt Cunningham",
                Snatch1 = new ScoreboardAttemptCell(95, ScoreboardAttemptStatus.Good),
                Snatch2 = new ScoreboardAttemptCell(99, ScoreboardAttemptStatus.Pending),
                Snatch3 = new ScoreboardAttemptCell(null, ScoreboardAttemptStatus.Empty),
                Cj1 = new ScoreboardAttemptCell(120, ScoreboardAttemptStatus.Good),
                Cj2 = new ScoreboardAttemptCell(125, ScoreboardAttemptStatus.Pending),
                Cj3 = new ScoreboardAttemptCell(null, ScoreboardAttemptStatus.Empty)
            },
            new()
            {
                OrderNumber = 2,
                LotNumber = 5,
                Name = "Lifter B",
                Snatch1 = new ScoreboardAttemptCell(102, ScoreboardAttemptStatus.Good),
                Snatch2 = new ScoreboardAttemptCell(106, ScoreboardAttemptStatus.Pending),
                Snatch3 = new ScoreboardAttemptCell(null, ScoreboardAttemptStatus.Empty),
                Cj1 = new ScoreboardAttemptCell(130, ScoreboardAttemptStatus.Good),
                Cj2 = new ScoreboardAttemptCell(135, ScoreboardAttemptStatus.Pending),
                Cj3 = new ScoreboardAttemptCell(null, ScoreboardAttemptStatus.Empty)
            }
        };

        var snatchRes = ScoreboardCalculator.AnalyzeScoreboard(lifters, LiftType.Snatch, userLifterIndex: 0, setupOpenerWeightKg: 95);
        Assert.True(snatchRes.IsUserOpenerCompleted);
        Assert.False(snatchRes.IsBarPastUserOpener);
        Assert.Equal(0, snatchRes.LiftsOut);
        Assert.Contains("already lifted your Snatch opener", snatchRes.UserOpenerCompletedMessage);

        var cjRes = ScoreboardCalculator.AnalyzeScoreboard(lifters, LiftType.CleanAndJerk, userLifterIndex: 0, setupOpenerWeightKg: 120);
        Assert.True(cjRes.IsUserOpenerCompleted);
        Assert.False(cjRes.IsBarPastUserOpener);
        Assert.Equal(0, cjRes.LiftsOut);
        Assert.Contains("already lifted your Clean & Jerk opener", cjRes.UserOpenerCompletedMessage);
    }

    [Fact]
    public void ScoreboardCalculator_WhenSnatchConcludedAndCjUpNext_IndicatesSnatchConcludedNotWeightPassed()
    {
        // Simulates a scoreboard where Snatch is completed (even if OCR left some Good Snatch cells as Pending)
        // and Clean & Jerk is up next.
        var lifters = new List<ScoreboardLifterRow>
        {
            new()
            {
                OrderNumber = 1,
                LotNumber = 3,
                Name = "Lifter A",
                Snatch1 = new ScoreboardAttemptCell(95, ScoreboardAttemptStatus.Pending), // OCR missed green background
                Snatch2 = new ScoreboardAttemptCell(100, ScoreboardAttemptStatus.Miss),
                Snatch3 = new ScoreboardAttemptCell(100, ScoreboardAttemptStatus.Good),
                Cj1 = new ScoreboardAttemptCell(115, ScoreboardAttemptStatus.Pending)
            },
            new()
            {
                OrderNumber = 2,
                LotNumber = 7,
                Name = "Matt Cunningham",
                Snatch1 = new ScoreboardAttemptCell(90, ScoreboardAttemptStatus.Pending),
                Snatch2 = new ScoreboardAttemptCell(94, ScoreboardAttemptStatus.Pending),
                Snatch3 = new ScoreboardAttemptCell(97, ScoreboardAttemptStatus.Miss),
                Cj1 = new ScoreboardAttemptCell(120, ScoreboardAttemptStatus.Pending)
            }
        };

        var snatchRes = ScoreboardCalculator.AnalyzeScoreboard(lifters, LiftType.Snatch, userLifterIndex: 1, setupOpenerWeightKg: 90);
        Assert.True(snatchRes.IsDisciplineConcluded);
        Assert.Equal("Snatching has concluded.", snatchRes.DisciplineConcludedMessage);
        Assert.False(snatchRes.IsBarPastUserOpener);
        Assert.Equal(0, snatchRes.LiftsOut);

        var cjRes = ScoreboardCalculator.AnalyzeScoreboard(lifters, LiftType.CleanAndJerk, userLifterIndex: 1, setupOpenerWeightKg: 120);
        Assert.False(cjRes.IsDisciplineConcluded);
        Assert.False(cjRes.IsUserOpenerCompleted);
        Assert.False(cjRes.IsBarPastUserOpener);
        Assert.Equal(0, cjRes.RemainingSnatchLifts);
        // Lifter A's 1st attempt is 115 kg (< 120 kg), and their projected 2nd attempt is 115 + 5 = 120 kg (Att 2 goes AFTER user's Att 1 @ 120 kg) => 1 lift out
        Assert.Equal(1, cjRes.LiftsOut);
    }

    [Fact]
    public void ScoreboardCalculator_WhenLifterRepeatsWeightAfterMissAndIncreasesOnThird_NormalizesSecondAttemptAsGood()
    {
        // JAKAITE (90, 90, 92) and MARTINEZ (93, 93, 97): even if raw status on Attempt 2 was misread as Miss,
        // the weight increase on Attempt 3 (92 > 90 and 97 > 93) proves Attempt 2 was a Good lift under IWF rules.
        var lifters = new List<ScoreboardLifterRow>
        {
            new()
            {
                OrderNumber = 2,
                LotNumber = 2,
                Name = "JAKAITE Lijana",
                Snatch1 = new ScoreboardAttemptCell(90, ScoreboardAttemptStatus.Miss),
                Snatch2 = new ScoreboardAttemptCell(90, ScoreboardAttemptStatus.Miss), // misread as Miss
                Snatch3 = new ScoreboardAttemptCell(92, ScoreboardAttemptStatus.Miss)
            },
            new()
            {
                OrderNumber = 3,
                LotNumber = 3,
                Name = "MARTINEZ A.",
                Snatch1 = new ScoreboardAttemptCell(93, ScoreboardAttemptStatus.Miss),
                Snatch2 = new ScoreboardAttemptCell(93, ScoreboardAttemptStatus.Miss), // misread as Miss
                Snatch3 = new ScoreboardAttemptCell(97, ScoreboardAttemptStatus.Miss)
            }
        };

        var result = ScoreboardCalculator.AnalyzeScoreboard(lifters, LiftType.Snatch, userLifterIndex: 0, setupOpenerWeightKg: 90);
        var jakaiteAttempts = result.LifterSummaries[0].Attempts;
        var martinezAttempts = result.LifterSummaries[1].Attempts;

        Assert.Equal(ScoreboardAttemptOrigin.CompletedMiss, jakaiteAttempts[0].Origin);
        Assert.Equal(ScoreboardAttemptOrigin.CompletedGood, jakaiteAttempts[1].Origin);
        Assert.Equal(ScoreboardAttemptOrigin.CompletedMiss, jakaiteAttempts[2].Origin);

        Assert.Equal(ScoreboardAttemptOrigin.CompletedMiss, martinezAttempts[0].Origin);
        Assert.Equal(ScoreboardAttemptOrigin.CompletedGood, martinezAttempts[1].Origin);
        Assert.Equal(ScoreboardAttemptOrigin.CompletedMiss, martinezAttempts[2].Origin);
    }

    [Fact]
    public void LiveWarmupSession_UpdateOpenerWeight_RebuildsWarmupStepsWhilePreservingLiveState()
    {
        var profile = Defaults.CreateDemoProfile();
        profile.SnatchOpenerWeight = 90;
        var session = new LiveWarmupSession(LiftType.Snatch, profile)
        {
            IsRunning = true
        };

        var now = DateTime.Today.AddHours(10);
        session.ApplyScoreboardScan(liftsOut: 4, liftsPassed: 6, now: now);
        session.ScrubStageLiftProgress(0.5, now);

        var updatedSnap = session.UpdateOpenerWeight(96, now);

        Assert.Equal(4, updatedSnap.LiftsOut);
        Assert.Equal(6, updatedSnap.LiftsPassed);
        Assert.True(updatedSnap.IsStageLiftingStarted);
        Assert.Equal(96, updatedSnap.Steps.Last().WeightKg);
        Assert.True(updatedSnap.Steps.Last().IsOpener);
    }

    [Fact]
    public void ScoreboardCalculator_AutoFlagPriorSessionRows_ExcludesPriorGroupLeadersFromAnalysis()
    {
        // Mirrors the IWF Women 69 kg Group C scoreboard where rows 1..3 are Group C lifters who finished Snatch
        // and have Pending C&J openers (117, 110, 115), while the bottom row is a Group D leader ("D") who already
        // completed all 3 C&J lifts (111, 115, 117).
        var lifters = new List<ScoreboardLifterRow>
        {
            new()
            {
                OrderNumber = 1,
                LotNumber = 1,
                LeftColumnText = "1",
                Name = "ENKILEDA Carja",
                Snatch1 = new ScoreboardAttemptCell(98, ScoreboardAttemptStatus.Good),
                Snatch2 = new ScoreboardAttemptCell(100, ScoreboardAttemptStatus.Good),
                Snatch3 = new ScoreboardAttemptCell(102, ScoreboardAttemptStatus.Good),
                Cj1 = new ScoreboardAttemptCell(117, ScoreboardAttemptStatus.Pending)
            },
            new()
            {
                OrderNumber = 2,
                LotNumber = 2,
                LeftColumnText = "2",
                Name = "JAKAITE Lijana",
                Snatch1 = new ScoreboardAttemptCell(90, ScoreboardAttemptStatus.Miss),
                Snatch2 = new ScoreboardAttemptCell(90, ScoreboardAttemptStatus.Good),
                Snatch3 = new ScoreboardAttemptCell(92, ScoreboardAttemptStatus.Miss),
                Cj1 = new ScoreboardAttemptCell(110, ScoreboardAttemptStatus.Pending)
            },
            new()
            {
                OrderNumber = 3,
                LotNumber = 3,
                LeftColumnText = "3",
                Name = "MARTINEZ A.",
                Snatch1 = new ScoreboardAttemptCell(93, ScoreboardAttemptStatus.Miss),
                Snatch2 = new ScoreboardAttemptCell(93, ScoreboardAttemptStatus.Good),
                Snatch3 = new ScoreboardAttemptCell(97, ScoreboardAttemptStatus.Miss),
                Cj1 = new ScoreboardAttemptCell(115, ScoreboardAttemptStatus.Pending)
            },
            new()
            {
                OrderNumber = 4,
                LotNumber = 18,
                LeftColumnText = "D",
                Name = "MCMONAGLE Gina",
                Snatch1 = new ScoreboardAttemptCell(89, ScoreboardAttemptStatus.Good),
                Snatch2 = new ScoreboardAttemptCell(92, ScoreboardAttemptStatus.Good),
                Snatch3 = new ScoreboardAttemptCell(94, ScoreboardAttemptStatus.Miss),
                Cj1 = new ScoreboardAttemptCell(111, ScoreboardAttemptStatus.Good),
                Cj2 = new ScoreboardAttemptCell(115, ScoreboardAttemptStatus.Good),
                Cj3 = new ScoreboardAttemptCell(117, ScoreboardAttemptStatus.Miss)
            }
        };

        int excludedCount = ScoreboardCalculator.AutoFlagPriorSessionRows(lifters);
        Assert.Equal(1, excludedCount);
        Assert.True(lifters[3].IsExcluded);

        // Analyzing C&J for MARTINEZ (opener 115 kg) must NOT see MCMONAGLE's 117 kg from Group D!
        var cjResult = ScoreboardCalculator.AnalyzeScoreboard(lifters, LiftType.CleanAndJerk, userLifterIndex: 2, setupOpenerWeightKg: 115);
        Assert.False(cjResult.IsBarPastUserOpener);
        Assert.Equal(0, cjResult.LiftsPassed);
        // JAKAITE opens at 110 kg (1st) and projects +5 kg -> 115 kg (2nd, which goes AFTER MARTINEZ's 115 kg 1st attempt) => 1 lift out
        Assert.Equal(1, cjResult.LiftsOut);
    }

    [Fact]
    public void EnforceRisingBarPendingConsistency_Kims89AndTanakas87ArePendingNotMisses()
    {
        // Mirrors Scenario 1 (Screenshot_20261106_135522.jpg / Screenshot_20261106_135349.jpg):
        // KIM Min-Jun (Lot #2): 85✓, 89 (not taken yet!), —
        // ROSSI Marco (Lot #4): 86✗, 86 (Pending on board), —
        // TANAKA Kenji (Lot #5): 86✓, 87 (not taken yet!), —
        // MORALES Diego (Lot #6): 88 (Pending on board), —
        // CUNNINGHAM Matthew (Lot #7): 88 (Pending on board), —
        var lifters = new List<ScoreboardLifterRow>
        {
            new()
            {
                OrderNumber = 1,
                LotNumber = 2,
                Name = "KIM Min-Jun",
                Snatch1 = new ScoreboardAttemptCell(85, ScoreboardAttemptStatus.Good),
                // Even if AI mistakenly marked 89 as Miss because 89 > BEST(85), Attempt 3 is Empty and 89 > 86 (Pending)
                Snatch2 = new ScoreboardAttemptCell(89, ScoreboardAttemptStatus.Miss),
                Snatch3 = new ScoreboardAttemptCell(null, ScoreboardAttemptStatus.Empty)
            },
            new()
            {
                OrderNumber = 2,
                LotNumber = 4,
                Name = "ROSSI Marco",
                Snatch1 = new ScoreboardAttemptCell(86, ScoreboardAttemptStatus.Miss),
                Snatch2 = new ScoreboardAttemptCell(86, ScoreboardAttemptStatus.Pending),
                Snatch3 = new ScoreboardAttemptCell(null, ScoreboardAttemptStatus.Empty)
            },
            new()
            {
                OrderNumber = 3,
                LotNumber = 5,
                Name = "TANAKA Kenji",
                Snatch1 = new ScoreboardAttemptCell(86, ScoreboardAttemptStatus.Good),
                // Even if AI mistakenly marked 87 as Miss because 87 > BEST(86), Attempt 3 is Empty and 87 > 86 (Pending)
                Snatch2 = new ScoreboardAttemptCell(87, ScoreboardAttemptStatus.Miss),
                Snatch3 = new ScoreboardAttemptCell(null, ScoreboardAttemptStatus.Empty)
            },
            new()
            {
                OrderNumber = 4,
                LotNumber = 6,
                Name = "MORALES Diego",
                Snatch1 = new ScoreboardAttemptCell(88, ScoreboardAttemptStatus.Pending)
            },
            new()
            {
                OrderNumber = 5,
                LotNumber = 7,
                Name = "CUNNINGHAM Matthew",
                Snatch1 = new ScoreboardAttemptCell(88, ScoreboardAttemptStatus.Pending)
            },
            new()
            {
                OrderNumber = 6,
                LotNumber = 11,
                Name = "SILVA Lucas",
                Snatch1 = new ScoreboardAttemptCell(95, ScoreboardAttemptStatus.Pending)
            },
            new()
            {
                OrderNumber = 7,
                LotNumber = 14,
                Name = "NOVAK Lukas",
                Snatch1 = new ScoreboardAttemptCell(97, ScoreboardAttemptStatus.Pending)
            }
        };

        ScoreboardCalculator.AutoFlagPriorSessionRows(lifters);

        // Both KIM's 89 and TANAKA's 87 must be normalized to Pending (not taken yet!)
        Assert.Equal(ScoreboardAttemptStatus.Pending, lifters[0].Snatch2.Status);
        Assert.Equal(ScoreboardAttemptStatus.Pending, lifters[2].Snatch2.Status);

        // Analyze for MORALES (index 3) with App Opener = 90 kg:
        // Completed lifts (Lifts Passed) = 3 (KIM 85✓, ROSSI 86✗, TANAKA 86✓)
        // Upcoming before MORALES 90 kg (Lifts Out) = 4 (ROSSI Att2 86, TANAKA Att2 87, CUNNINGHAM Att1 88, KIM Att2 89; ROSSI Att3 projects to ~90kg which goes after MORALES's 90kg Att1)
        var res = ScoreboardCalculator.AnalyzeScoreboard(lifters, LiftType.Snatch, userLifterIndex: 3, setupOpenerWeightKg: 90);
        Assert.Equal(3, res.LiftsPassed);
        Assert.Equal(4, res.LiftsOut);
    }

    [Fact]
    public void GeminiModelResolver_PrefersStableFlashOverPreviewAndFiltersDatedAndSpecialModels()
    {
        string modelsJson = """
        {
          "models": [
            { "name": "models/gemini-3-flash-preview", "supportedGenerationMethods": ["generateContent"] },
            { "name": "models/gemini-2.5-flash-preview-05-20", "supportedGenerationMethods": ["generateContent"] },
            { "name": "models/gemini-2.5-flash-image-preview", "supportedGenerationMethods": ["generateContent"] },
            { "name": "models/gemini-2.5-flash", "supportedGenerationMethods": ["generateContent"] },
            { "name": "models/gemini-flash-latest", "supportedGenerationMethods": ["generateContent"] },
            { "name": "models/gemini-2.5-flash-lite", "supportedGenerationMethods": ["generateContent"] }
          ]
        }
        """;

        var ranked = GeminiModelResolver.RankAvailableVisionModelsFromJson(modelsJson);
        Assert.DoesNotContain("gemini-2.5-flash-preview-05-20", ranked);
        Assert.DoesNotContain("gemini-2.5-flash-image-preview", ranked);
        Assert.Equal("gemini-2.5-flash", ranked[0]);
        Assert.Equal("gemini-flash-latest", ranked[1]);

        // Even if a preview or lite model was previously cached, BuildOrderedModelQueue prefers the stable model first
        var queue = GeminiModelResolver.BuildOrderedModelQueue("gemini-3-flash-preview", ranked);
        Assert.Equal("gemini-2.5-flash", queue[0]);
        Assert.True(queue.Count <= 4);

        var queueFromLite = GeminiModelResolver.BuildOrderedModelQueue("gemini-2.5-flash-lite", ranked);
        Assert.Equal("gemini-2.5-flash", queueFromLite[0]);
    }

    [Fact]
    public void UnstartedCjAttempts_WhenAtt2IsEmpty_AreNormalizedToPendingWithZeroPassed()
    {
        // Mirrors the IWF Women 69 kg Group A scoreboard where Snatch is concluded (all 3 attempts completed),
        // Group A lifters have C&J 1st-attempt openers posted (with C&J 2nd and 3rd empty),
        // and prior Group B/C leaders at the bottom have all 3 C&J attempts completed.
        // Even if the AI model mislabeled Group A's C&J 1st attempts as Good, because C&J Attempt 2 is Empty,
        // they have NOT been taken yet and must be normalized to Pending (0 Passed in C&J).
        var lifters = new List<ScoreboardLifterRow>
        {
            new()
            {
                OrderNumber = 1,
                LotNumber = 9,
                LeftColumnText = "9",
                Name = "DOLEGA Martyna",
                Snatch1 = new ScoreboardAttemptCell(100, ScoreboardAttemptStatus.Good),
                Snatch2 = new ScoreboardAttemptCell(103, ScoreboardAttemptStatus.Good),
                Snatch3 = new ScoreboardAttemptCell(105, ScoreboardAttemptStatus.Miss)
            },
            new()
            {
                OrderNumber = 2,
                LotNumber = 1,
                LeftColumnText = "1",
                Name = "RENGIFO CHIRINOS C.",
                Snatch1 = new ScoreboardAttemptCell(104, ScoreboardAttemptStatus.Good),
                Snatch2 = new ScoreboardAttemptCell(107, ScoreboardAttemptStatus.Miss),
                Snatch3 = new ScoreboardAttemptCell(107, ScoreboardAttemptStatus.Miss),
                Cj1 = new ScoreboardAttemptCell(125, ScoreboardAttemptStatus.Good) // misread as Good despite Cj2 being Empty!
            },
            new()
            {
                OrderNumber = 3,
                LotNumber = 2,
                LeftColumnText = "2",
                Name = "TOKO KEGNE Genna R.",
                Snatch1 = new ScoreboardAttemptCell(104, ScoreboardAttemptStatus.Good),
                Snatch2 = new ScoreboardAttemptCell(108, ScoreboardAttemptStatus.Miss),
                Snatch3 = new ScoreboardAttemptCell(110, ScoreboardAttemptStatus.Miss),
                Cj1 = new ScoreboardAttemptCell(127, ScoreboardAttemptStatus.Good) // misread as Good despite Cj2 being Empty!
            },
            new()
            {
                OrderNumber = 4,
                LotNumber = 11,
                LeftColumnText = "11",
                Name = "SCHWEIZER Lisa Marie",
                Snatch1 = new ScoreboardAttemptCell(103, ScoreboardAttemptStatus.Miss),
                Snatch2 = new ScoreboardAttemptCell(103, ScoreboardAttemptStatus.Miss),
                Snatch3 = new ScoreboardAttemptCell(104, ScoreboardAttemptStatus.Good),
                Cj1 = new ScoreboardAttemptCell(120, ScoreboardAttemptStatus.Good) // misread as Good despite Cj2 being Empty!
            },
            new()
            {
                OrderNumber = 5,
                LotNumber = 14,
                LeftColumnText = "B",
                Name = "IBANEZ Emily",
                Snatch1 = new ScoreboardAttemptCell(101, ScoreboardAttemptStatus.Good),
                Snatch2 = new ScoreboardAttemptCell(104, ScoreboardAttemptStatus.Good),
                Snatch3 = new ScoreboardAttemptCell(106, ScoreboardAttemptStatus.Good),
                Cj1 = new ScoreboardAttemptCell(127, ScoreboardAttemptStatus.Good),
                Cj2 = new ScoreboardAttemptCell(131, ScoreboardAttemptStatus.Good),
                Cj3 = new ScoreboardAttemptCell(134, ScoreboardAttemptStatus.Good)
            }
        };

        int excluded = ScoreboardCalculator.AutoFlagPriorSessionRows(lifters);
        Assert.Equal(1, excluded);
        Assert.True(lifters[4].IsExcluded);

        // All 3 Group A C&J openers must be normalized to Pending since their 2nd attempt is Empty
        Assert.Equal(ScoreboardAttemptStatus.Pending, lifters[1].Cj1.Status);
        Assert.Equal(ScoreboardAttemptStatus.Pending, lifters[2].Cj1.Status);
        Assert.Equal(ScoreboardAttemptStatus.Pending, lifters[3].Cj1.Status);

        // Clean & Jerk analysis for TOKO KEGNE (index 2, opener 127 kg):
        // 0 C&J lifts passed! SCHWEIZER (120, +5 -> 125) and RENGIFO (125) go before TOKO KEGNE (127) => 3 Lifts Out!
        var cjRes = ScoreboardCalculator.AnalyzeScoreboard(lifters, LiftType.CleanAndJerk, userLifterIndex: 2, setupOpenerWeightKg: 127);
        Assert.Equal(0, cjRes.LiftsPassed);
        Assert.Equal(0, cjRes.RemainingSnatchLifts);
        Assert.Equal(3, cjRes.LiftsOut);
    }
}
