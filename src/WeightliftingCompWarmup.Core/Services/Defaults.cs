using WeightliftingCompWarmup.Core.Models;

namespace WeightliftingCompWarmup.Core.Services;

/// <summary>
/// Provides default and demonstration profile values matching the original WinForms <c>Defaults</c> class.
/// </summary>
public static class Defaults
{
    public const int DefaultBarbellWeight = 20;
    public const int MinBarbellWeight = 10;
    public const int MaxBarbellWeight = 45;
    public const int MaxOpenerWeight = 300;

    // Snatch defaults
    public const int DefaultSnatchSecondsStage = 55;
    public const int DefaultSnatchSecondsBreak = 840; // 14 minutes (presentation + 10m clock)
    public const int DefaultSnatchOpenerWeight = 20;
    public const bool DefaultSnatchOpenerInWarmup = false;
    public const int DefaultSnatchSecondsEnd = 60;
    public const int DefaultSnatchLiftsOut = 3;

    // Snatch demo overrides
    public const int DemoSnatchOpenerWeight = 85;
    public const bool DemoSnatchOpenerInWarmup = true;
    public const int DemoSnatchSecondsEnd = 60;

    // Clean & Jerk defaults
    public const int DefaultCjSecondsStage = 62;
    public const int DefaultCjSecondsBreak = 600; // 10 minutes
    public const int DefaultCjOpenerWeight = 20;
    public const bool DefaultCjOpenerInWarmup = false;
    public const int DefaultCjSecondsEnd = 60;
    public const int DefaultCjLiftsOut = 3;
    public const int DefaultCjSnatchLiftsOut = 0;

    // Clean & Jerk demo overrides
    public const int DemoCjOpenerWeight = 110;
    public const bool DemoCjOpenerInWarmup = false;
    public const int DemoCjSecondsEnd = 75;

    /// <summary>
    /// Creates the pre-populated "Demo Profile" matching the original WinForms application.
    /// </summary>
    public static Profile CreateDemoProfile()
    {
        return new Profile
        {
            Name = "Demo Profile",
            Start = DateTime.Today.AddHours(9), // 09:00:00
            BarbellWeight = DefaultBarbellWeight,
            Beep = false,

            // Snatch Demo
            SnatchSecondsStage = DefaultSnatchSecondsStage,
            SnatchSecondsBreak = DefaultSnatchSecondsBreak,
            SnatchOpenerWeight = DemoSnatchOpenerWeight,
            SnatchOpenerInWarmup = DemoSnatchOpenerInWarmup,
            SnatchSecondsEnd = DemoSnatchSecondsEnd,
            SnatchLiftsOut = DefaultSnatchLiftsOut,
            SnatchExtras = new List<ExtraItem>
            {
                new("Ibuprofen, Coffee, Pre", 1200, 1),
                new("Foam Roll", 300, 2),
                new("Shoes, tape, etc.", 300, 3),
                new("Stretch", 600, 4),
                new("Empty bar stretch", 300, 5)
            },
            SnatchJumps = new Dictionary<int, int>
            {
                [1] = 20,
                [40] = 10,
                [50] = 5,
                [80] = 4,
                [89] = 3
            },
            SnatchTimes = new Dictionary<int, int>
            {
                [1] = 210,
                [41] = 150
            },

            // Clean & Jerk Demo
            CjSecondsStage = DefaultCjSecondsStage,
            CjSecondsBreak = DefaultCjSecondsBreak,
            CjOpenerWeight = DemoCjOpenerWeight,
            CjOpenerInWarmup = DemoCjOpenerInWarmup,
            CjSecondsEnd = DemoCjSecondsEnd,
            CjLiftsOut = DefaultCjLiftsOut,
            CjSnatchLiftsOut = DefaultCjSnatchLiftsOut,
            CjExtras = new List<ExtraItem>
            {
                new("Stretch", 300, 1)
            },
            CjJumps = new Dictionary<int, int>
            {
                [1] = 30,
                [50] = 10,
                [90] = 7,
                [100] = 5,
                [105] = 4
            },
            CjTimes = new Dictionary<int, int>
            {
                [1] = 300,
                [21] = 140,
                [100] = 150
            }
        };
    }

    /// <summary>
    /// Creates a new blank profile initialized with standard baseline defaults.
    /// </summary>
    public static Profile CreateBlankProfile(string name)
    {
        return new Profile
        {
            Name = name,
            Start = DateTime.Today.AddHours(9),
            BarbellWeight = DefaultBarbellWeight,
            Beep = false,

            SnatchSecondsStage = DefaultSnatchSecondsStage,
            SnatchSecondsBreak = DefaultSnatchSecondsBreak,
            SnatchOpenerWeight = DefaultSnatchOpenerWeight,
            SnatchOpenerInWarmup = DefaultSnatchOpenerInWarmup,
            SnatchSecondsEnd = DefaultSnatchSecondsEnd,
            SnatchLiftsOut = DefaultSnatchLiftsOut,
            SnatchExtras = new List<ExtraItem>(),
            SnatchJumps = new Dictionary<int, int> { [1] = 20 },
            SnatchTimes = new Dictionary<int, int> { [1] = 140 },

            CjSecondsStage = DefaultCjSecondsStage,
            CjSecondsBreak = DefaultCjSecondsBreak,
            CjOpenerWeight = DefaultCjOpenerWeight,
            CjOpenerInWarmup = DefaultCjOpenerInWarmup,
            CjSecondsEnd = DefaultCjSecondsEnd,
            CjLiftsOut = DefaultCjLiftsOut,
            CjSnatchLiftsOut = DefaultCjSnatchLiftsOut,
            CjExtras = new List<ExtraItem>(),
            CjJumps = new Dictionary<int, int> { [1] = 30 },
            CjTimes = new Dictionary<int, int> { [1] = 160 }
        };
    }
}
