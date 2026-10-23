using System.Text.Json.Serialization;
using WeightliftingCompWarmup.Core.Services;

namespace WeightliftingCompWarmup.Core.Models;

/// <summary>
/// Competition lift discipline.
/// </summary>
public enum LiftType
{
    Snatch,
    CleanAndJerk
}

/// <summary>
/// Represents a non-barbell warmup step (e.g., Foam Roll, Stretch, Shoes/Tape).
/// Equivalent to <c>DataTypes.Extras</c> in the original WinForms application.
/// </summary>
public class ExtraItem
{
    public string Action { get; set; } = string.Empty;
    public int Length { get; set; }
    public int Order { get; set; }

    [JsonIgnore]
    public string FormattedLength => WarmupCalculator.FormatSeconds(Length);

    public ExtraItem()
    {
    }

    public ExtraItem(string action, int length, int order)
    {
        Action = action;
        Length = length;
        Order = order;
    }

    public ExtraItem Clone() => new(Action, Length, Order);
}

/// <summary>
/// Represents a weight jump rule (FromWeight -> Jump in kg).
/// </summary>
public class WeightJumpRule
{
    public int FromWeight { get; set; }
    public int Jump { get; set; }

    [JsonIgnore]
    public bool CanDelete => FromWeight > 1;

    public WeightJumpRule()
    {
    }

    public WeightJumpRule(int fromWeight, int jump)
    {
        FromWeight = fromWeight;
        Jump = jump;
    }
}

/// <summary>
/// Represents a warmup rest/interval time rule (FromWeight -> Duration in seconds).
/// </summary>
public class WarmupTimeRule
{
    public int FromWeight { get; set; }
    public int Seconds { get; set; }

    [JsonIgnore]
    public string FormattedTime => WarmupCalculator.FormatSeconds(Seconds);

    [JsonIgnore]
    public bool CanDelete => FromWeight > 1;

    public WarmupTimeRule()
    {
    }

    public WarmupTimeRule(int fromWeight, int seconds)
    {
        FromWeight = fromWeight;
        Seconds = seconds;
    }
}

/// <summary>
/// Represents a calculated warmup step (PreStep wait, Extra, Warmup Lift, or Competition Opener).
/// Equivalent to <c>DataTypes.Step</c> in the original WinForms application.
/// </summary>
public class WarmupStep
{
    public string Action { get; set; } = string.Empty;
    public int Weight { get; set; }
    public int Length { get; set; }
    public int TotalLength { get; set; }
    public int TotalLengthReverse { get; set; }
    public int Order { get; set; }
    public bool PreStep { get; set; }
    public bool IsOpener { get; set; }

    [JsonIgnore]
    public bool IsLift => Weight > 0 && !PreStep;

    [JsonIgnore]
    public string WeightDisplay => Weight > 0 ? $"{Weight} kg" : "—";

    [JsonIgnore]
    public string FormattedLength => WarmupCalculator.FormatSeconds(Length);

    [JsonIgnore]
    public string FormattedTotalLength => WarmupCalculator.FormatSeconds(TotalLength);

    [JsonIgnore]
    public string FormattedTotalLengthReverse => WarmupCalculator.FormatSeconds(TotalLengthReverse);

    public WarmupStep()
    {
    }

    public WarmupStep(
        string action,
        int weight,
        int length,
        int totalLength,
        int totalLengthReverse,
        int order,
        bool preStep,
        bool isOpener)
    {
        Action = action;
        Weight = weight;
        Length = length;
        TotalLength = totalLength;
        TotalLengthReverse = totalLengthReverse;
        Order = order;
        PreStep = preStep;
        IsOpener = isOpener;
    }

    public WarmupStep Clone() => new(
        Action,
        Weight,
        Length,
        TotalLength,
        TotalLengthReverse,
        Order,
        PreStep,
        IsOpener);
}

/// <summary>
/// Stores all competition warmup parameters for both Snatch and Clean &amp; Jerk.
/// Equivalent to <c>DataTypes.Profile</c> in the original WinForms application.
/// </summary>
public class Profile
{
    public string Name { get; set; } = string.Empty;
    public DateTime Start { get; set; } = DateTime.Today.AddHours(9);
    public int BarbellWeight { get; set; } = 20;
    public bool Beep { get; set; } = false;

    // Snatch configuration
    public int SnatchSecondsStage { get; set; } = 55;
    public int SnatchSecondsBreak { get; set; } = 840;
    public int SnatchOpenerWeight { get; set; } = 85;
    public bool SnatchOpenerInWarmup { get; set; } = true;
    public int SnatchSecondsEnd { get; set; } = 60;
    public int SnatchLiftsOut { get; set; } = 3;
    public List<ExtraItem> SnatchExtras { get; set; } = new();
    public Dictionary<int, int> SnatchJumps { get; set; } = new();
    public Dictionary<int, int> SnatchTimes { get; set; } = new();

    // Clean & Jerk configuration
    public int CjSecondsStage { get; set; } = 62;
    public int CjSecondsBreak { get; set; } = 600;
    public int CjOpenerWeight { get; set; } = 110;
    public bool CjOpenerInWarmup { get; set; } = false;
    public int CjSecondsEnd { get; set; } = 75;
    public int CjLiftsOut { get; set; } = 3;
    public int CjSnatchLiftsOut { get; set; } = 0;
    public List<ExtraItem> CjExtras { get; set; } = new();
    public Dictionary<int, int> CjJumps { get; set; } = new();
    public Dictionary<int, int> CjTimes { get; set; } = new();

    /// <summary>
    /// Presentation and pre-snatch clock duration expressed in minutes (0–200 min) for the Setup UI.
    /// Defaults to 14 minutes (840 seconds).
    /// </summary>
    [JsonIgnore]
    public int SnatchMinutesBreak
    {
        get => Math.Max(0, SnatchSecondsBreak / 60);
        set => SnatchSecondsBreak = Math.Clamp(value, 0, 200) * 60;
    }

    /// <summary>
    /// Break after snatch expressed in minutes (1–200 min) for the Setup UI,
    /// matching the original WinForms numeric up/down which displayed minutes and stored seconds.
    /// </summary>
    [JsonIgnore]
    public int CjMinutesBreak
    {
        get => Math.Max(1, CjSecondsBreak / 60);
        set => CjSecondsBreak = Math.Clamp(value, 1, 200) * 60;
    }

    public Profile Clone(string? newName = null)
    {
        return new Profile
        {
            Name = newName ?? Name,
            Start = Start,
            BarbellWeight = BarbellWeight,
            Beep = Beep,
            SnatchSecondsStage = SnatchSecondsStage,
            SnatchSecondsBreak = SnatchSecondsBreak,
            SnatchOpenerWeight = SnatchOpenerWeight,
            SnatchOpenerInWarmup = SnatchOpenerInWarmup,
            SnatchSecondsEnd = SnatchSecondsEnd,
            SnatchLiftsOut = SnatchLiftsOut,
            SnatchExtras = SnatchExtras.Select(e => e.Clone()).ToList(),
            SnatchJumps = new Dictionary<int, int>(SnatchJumps),
            SnatchTimes = new Dictionary<int, int>(SnatchTimes),
            CjSecondsStage = CjSecondsStage,
            CjSecondsBreak = CjSecondsBreak,
            CjOpenerWeight = CjOpenerWeight,
            CjOpenerInWarmup = CjOpenerInWarmup,
            CjSecondsEnd = CjSecondsEnd,
            CjLiftsOut = CjLiftsOut,
            CjSnatchLiftsOut = CjSnatchLiftsOut,
            CjExtras = CjExtras.Select(e => e.Clone()).ToList(),
            CjJumps = new Dictionary<int, int>(CjJumps),
            CjTimes = new Dictionary<int, int>(CjTimes)
        };
    }
}
