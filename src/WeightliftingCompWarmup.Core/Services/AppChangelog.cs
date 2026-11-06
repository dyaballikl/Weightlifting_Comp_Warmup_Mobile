using System;
using System.Collections.Generic;
using System.Linq;

namespace WeightliftingCompWarmup.Core.Services;

/// <summary>
/// Represents the release notes for a single application version.
/// </summary>
public sealed record AppReleaseNote(
    string Version,
    string Title,
    IReadOnlyList<string> Changes);

/// <summary>
/// Provides versioned release notes and determines which changes have occurred
/// since the version the user last opened.
/// </summary>
public static class AppChangelog
{
    /// <summary>
    /// Baseline version assumed for existing users upgrading from builds prior to when
    /// <c>LastSeenAppVersion</c> tracking was introduced.
    /// </summary>
    public const string LegacyBaselineVersion = "2.0.27";

    private static readonly IReadOnlyList<AppReleaseNote> AllReleases =
    [
        new AppReleaseNote(
            Version: "2.0.45",
            Title: "AI Scoreboard Scanner (Beta) — Lifts Out, Passed & Call Order",
            Changes:
            [
                "AI Scoreboard Scanner (Beta): Snap or pick a photo of the warmup room scoreboard from Setup or Live mode to automatically calculate Lifts Out, Lifts Passed, and Remaining Snatch Lifts using your own free Google AI Studio (Gemini Flash) API key.",
                "Side-by-Side Snatch & Clean & Jerk Analysis: View both disciplines simultaneously, switch or exclude prior-session lifter rows, and tap any attempt cell to override weights or statuses.",
                "Official IWF Calling-Order & S-Curve Projections: Replays completed attempts and projects undeclared 2nd/3rd attempts in whole kilograms using official IWF tie-break priority (Lowest Weight → Earlier Attempt # → Earlier Previous Attempt Sequence → Lower Lot #).",
                "Show Your Work, Photo Zoom & Error Reporting: Inspect the projected platform call order, expand/zoom the scoreboard photo up to 3x, clear results anytime, or email an analysis diagnostic report straight from the scanner."
            ]),
        new AppReleaseNote(
            Version: "2.0.31",
            Title: "Streamlined Help Guide, System Clock Format & PiP Theme Sync",
            Changes:
            [
                "Streamlined Help & Pro Tips: Redesigned the How to Use guide with a concise 4-step Quick Start and expandable feature & competition tip sections (including handling 2-minute self-follow timers).",
                "System 12h / 24h Clock Support: Live telemetry clocks (Current Time & Est. Open Time) and the Competition Start Time picker now automatically match your device's AM/PM or 24-hour clock setting.",
                "System-Matched Floating / PiP Timer Theme: The floating overlay pill and Picture-in-Picture mini-timer now follow your device's system light/dark mode."
            ]),
        new AppReleaseNote(
            Version: "2.0.30",
            Title: "Presentation Buffer, Opener Redesign & Floating Timer Controls",
            Changes:
            [
                "Snatch Presentation Lift Buffer: Added a configurable time buffer (default 3 min) to stretch the Snatch warmup lift active during athlete presentation so you aren't behind when returning to the warmup room.",
                "Opener Barbell Placement: Moved the competition opener barbell graphic directly beneath the Opener Weight input in Setup.",
                "Distinct 'OPEN NOW!' Final Card: Formatted the final opener step in Live Runner with the full competition barbell graphic, no redundant progress bar, and an untruncated callout badge.",
                "Enhanced Floating / PiP Mini-Timer: Expanded the floating timer with an upper competition status bar, an interactive 'Advance' button during onstage lifting, and automatic cleanup once warmup is complete.",
                "Phone Landscape & Keyboard Fixes: Enabled vertical scrolling for phones in landscape mode and prevented the on-screen keyboard from covering the bottom of the Setup page.",
                "What's New Summary: Added this automatic release notes splash screen whenever the app updates."
            ]),
        new AppReleaseNote(
            Version: "2.0.29",
            Title: "Profile Backup Export/Import & Setup Improvements",
            Changes:
            [
                "Profile Backup Export & Import: Export all profiles and settings to a JSON backup file (with Google Drive backup support) and import backups anytime from the Profile menu.",
                "Automatic Profile Saving: Removed the redundant manual 'Save Profile Settings' menu option since all changes save automatically.",
                "Cleaner New Profile Prompt: Creating a new profile now starts with a blank name field instead of filler text.",
                "Snatch Presentation Lift Buffer: Added a configurable time buffer (default 3 min) for the Snatch warmup lift spanning athlete presentation.",
                "Opener Card & Floating Timer Upgrades: Moved the opener barbell under Opener Weight, redesigned the final 'OPEN NOW!' card, and added competition status + an 'Advance' button to the floating timer.",
                "Phone Landscape & Keyboard Scrolling: Fixed phone landscape scrolling and ensured the Setup screen scrolls fully above the keyboard."
            ]),
        new AppReleaseNote(
            Version: "2.0.28",
            Title: "Back Button Protection & Floating Pill Plate Stack",
            Changes:
            [
                "Active Timer Back-Button Guard: Added a confirmation prompt when pressing the Android back button while the live warmup timer is running.",
                "Floating Timer Plate Stack: Added a compact competition plate stack graphic inside the floating overlay pill during warmup lift steps."
            ]),
        new AppReleaseNote(
            Version: "2.0.27",
            Title: "Light, Dark & System Appearance Modes",
            Changes:
            [
                "Tri-Mode Theme Switcher: Added Light, Dark, and System (Auto) appearance modes accessible from the top header bar and the Help & About dialog."
            ]),
        new AppReleaseNote(
            Version: "2.0.25",
            Title: "Floating Overlay Pill & One-Tap Return",
            Changes:
            [
                "Floating Warmup Pill: Added a draggable floating overlay timer on Android and Picture-in-Picture mini-timer on iOS when minimizing the app during a live warmup.",
                "One-Tap Return: Tap anywhere on the floating timer pill to jump straight back into Comp OWL."
            ])
    ];

    public static IReadOnlyList<AppReleaseNote> Releases => AllReleases;

    /// <summary>
    /// Returns all release notes for versions strictly newer than <paramref name="lastSeenVersion"/>
    /// and up to <paramref name="currentVersion"/> (ordered newest first).
    /// </summary>
    public static IReadOnlyList<AppReleaseNote> GetChangesSince(string? lastSeenVersion, string? currentVersion)
    {
        if (!TryParseVersion(currentVersion, out Version current))
        {
            return [];
        }

        string effectiveLastSeen = string.IsNullOrWhiteSpace(lastSeenVersion)
            ? LegacyBaselineVersion
            : lastSeenVersion.Trim();

        if (!TryParseVersion(effectiveLastSeen, out Version previous))
        {
            previous = new Version(2, 0, 27);
        }

        if (current <= previous)
        {
            return [];
        }

        var matched = AllReleases
            .Where(r => TryParseVersion(r.Version, out Version rv) && rv > previous && rv <= current)
            .OrderByDescending(r => ParseVersionOrDefault(r.Version))
            .ToList();

        // If v2.0.30 is included alongside v2.0.29 (e.g. upgrading from 2.0.27 -> 2.0.30),
        // keep v2.0.29 focused on its distinct backup/profile items so bullets aren't duplicated.
        if (matched.Any(r => r.Version == "2.0.30") && matched.Any(r => r.Version == "2.0.29"))
        {
            for (int i = 0; i < matched.Count; i++)
            {
                if (matched[i].Version == "2.0.29")
                {
                    matched[i] = new AppReleaseNote(
                        Version: "2.0.29",
                        Title: "Profile Backup Export/Import & Streamlined Profile Setup",
                        Changes:
                        [
                            "Profile Backup Export & Import: Export all profiles and settings to a JSON backup file (with Google Drive backup support) and import backups anytime from the Profile menu.",
                            "Automatic Profile Saving: Removed the redundant manual 'Save Profile Settings' menu option since all changes save automatically.",
                            "Cleaner New Profile Prompt: Creating a new profile now starts with a blank name field instead of filler text."
                        ]);
                }
            }
        }

        // Fallback if currentVersion was bumped beyond the highest cataloged entry
        if (matched.Count == 0)
        {
            matched.Add(new AppReleaseNote(
                Version: currentVersion!.Trim(),
                Title: $"Updated to v{currentVersion.Trim()}",
                Changes: AllReleases[0].Changes));
        }

        return matched;
    }

    public static bool TryParseVersion(string? raw, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        string cleaned = raw.Trim().TrimStart('v', 'V');
        return Version.TryParse(cleaned, out version!);
    }

    private static Version ParseVersionOrDefault(string? raw)
    {
        return TryParseVersion(raw, out Version v) ? v : new Version(0, 0, 0);
    }
}
