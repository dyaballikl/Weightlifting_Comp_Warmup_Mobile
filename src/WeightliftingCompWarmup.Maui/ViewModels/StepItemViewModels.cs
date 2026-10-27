using System.ComponentModel;
using System.Runtime.CompilerServices;
using WeightliftingCompWarmup.Core.Models;
using WeightliftingCompWarmup.Core.Services;
using WeightliftingCompWarmup.Maui.Services;

namespace WeightliftingCompWarmup.Maui.ViewModels;

/// <summary>
/// ViewModel for a single row in the Setup Warmup Steps preview list.
/// </summary>
public class SetupStepItemViewModel
{
    public int Order { get; init; }
    public string Action { get; init; } = string.Empty;
    public int Weight { get; init; }
    public int BarbellWeight { get; init; }
    public bool IsLift { get; init; }
    public string WeightDisplay { get; init; } = string.Empty;
    public string FormattedLength { get; init; } = string.Empty;
    public string FormattedTotalLength { get; init; } = string.Empty;
    public string FormattedTotalLengthReverse { get; init; } = string.Empty;

    public static SetupStepItemViewModel FromStep(WarmupStep step, int barbellWeight)
    {
        return new SetupStepItemViewModel
        {
            Order = step.Order,
            Action = step.Action,
            Weight = step.Weight,
            BarbellWeight = barbellWeight,
            IsLift = step.IsLift,
            WeightDisplay = step.WeightDisplay,
            FormattedLength = step.FormattedLength,
            FormattedTotalLength = step.FormattedTotalLength,
            FormattedTotalLengthReverse = $"from {step.FormattedTotalLengthReverse} out"
        };
    }
}

/// <summary>
/// ViewModel for a single row in the Live Warmup Stepper list.
/// Updates properties in-place on each 1-second timer tick for smooth scrolling and zero UI flicker.
/// </summary>
public class LiveStepItemViewModel : INotifyPropertyChanged
{
    private string _actionTitle = string.Empty;
    private string _actionDetail = string.Empty;
    private int _weightKg;
    private StepRunStatus _status = StepRunStatus.Upcoming;
    private double _progressFraction;
    private string _progressSummaryText = string.Empty;
    private string _countdownBadgeText = string.Empty;

    public int Order { get; init; }
    public int BarbellWeight { get; init; }
    public bool IsLift { get; init; }
    public bool IsPreStep { get; init; }
    public bool IsOpener { get; init; }

    public string ActionTitle
    {
        get => _actionTitle;
        set
        {
            if (_actionTitle != value)
            {
                _actionTitle = value;
                OnPropertyChanged();
            }
        }
    }

    public string ActionDetail
    {
        get => _actionDetail;
        set
        {
            if (_actionDetail != value)
            {
                _actionDetail = value;
                OnPropertyChanged();
            }
        }
    }

    public int WeightKg
    {
        get => _weightKg;
        set
        {
            if (_weightKg != value)
            {
                _weightKg = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(WeightDisplay));
            }
        }
    }

    public string WeightDisplay => WeightKg > 0 ? $"{WeightKg} kg" : (IsPreStep ? "WAIT" : "EXTRA");

    public StepRunStatus Status
    {
        get => _status;
        set
        {
            if (_status != value)
            {
                _status = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsActive));
                OnPropertyChanged(nameof(IsCompleted));
                OnPropertyChanged(nameof(RowBackgroundHex));
                OnPropertyChanged(nameof(RowBorderHex));
                OnPropertyChanged(nameof(PrimaryTextHex));
                OnPropertyChanged(nameof(SecondaryTextHex));
                OnPropertyChanged(nameof(BadgeBackgroundHex));
                OnPropertyChanged(nameof(BadgeTextHex));
                OnPropertyChanged(nameof(ProgressColorHex));
            }
        }
    }

    public bool IsActive => Status == StepRunStatus.Active;
    public bool IsCompleted => Status == StepRunStatus.Completed;

    // Matches WinForms Yellow highlight for active step with high-contrast dark text
    public string RowBackgroundHex => Status switch
    {
        StepRunStatus.Active => "#FACC15",     // High-visibility Competition Yellow
        StepRunStatus.Completed => AppThemeManager.IsLightTheme ? "#F1F5F9" : "#0F172A",
        _ => AppThemeManager.IsLightTheme ? "#FFFFFF" : "#1E293B"
    };

    public string RowBorderHex => Status switch
    {
        StepRunStatus.Active => AppThemeManager.IsLightTheme ? "#EAB308" : "#FEF08A",
        StepRunStatus.Completed => AppThemeManager.IsLightTheme ? "#E2E8F0" : "#1E293B",
        _ => AppThemeManager.IsLightTheme ? "#CBD5E1" : "#334155"
    };

    public string PrimaryTextHex => Status switch
    {
        StepRunStatus.Active => "#0F172A",
        StepRunStatus.Completed => "#64748B",
        _ => AppThemeManager.IsLightTheme ? "#0F172A" : "#F8FAFC"
    };

    public string SecondaryTextHex => Status switch
    {
        StepRunStatus.Active => "#1E293B",
        StepRunStatus.Completed => AppThemeManager.IsLightTheme ? "#94A3B8" : "#475569",
        _ => AppThemeManager.IsLightTheme ? "#475569" : "#94A3B8"
    };

    public string BadgeBackgroundHex => Status switch
    {
        StepRunStatus.Active => "#0F172A",
        StepRunStatus.Completed => AppThemeManager.IsLightTheme ? "#E2E8F0" : "#1E293B",
        _ => AppThemeManager.IsLightTheme ? "#F1F5F9" : "#0F172A"
    };

    public string BadgeTextHex => Status switch
    {
        StepRunStatus.Active => "#FACC15",
        StepRunStatus.Completed => "#64748B",
        _ => AppThemeManager.IsLightTheme ? "#0284C7" : "#38BDF8"
    };

    public string ProgressColorHex => Status switch
    {
        StepRunStatus.Active => "#16A34A",
        StepRunStatus.Completed => AppThemeManager.IsLightTheme ? "#94A3B8" : "#334155",
        _ => "#0284C7"
    };

    public void RefreshThemeProperties()
    {
        OnPropertyChanged(nameof(RowBackgroundHex));
        OnPropertyChanged(nameof(RowBorderHex));
        OnPropertyChanged(nameof(PrimaryTextHex));
        OnPropertyChanged(nameof(SecondaryTextHex));
        OnPropertyChanged(nameof(BadgeBackgroundHex));
        OnPropertyChanged(nameof(BadgeTextHex));
        OnPropertyChanged(nameof(ProgressColorHex));
    }

    public double ProgressFraction
    {
        get => _progressFraction;
        set
        {
            if (Math.Abs(_progressFraction - value) > 0.001)
            {
                _progressFraction = value;
                OnPropertyChanged();
            }
        }
    }

    public string ProgressSummaryText
    {
        get => _progressSummaryText;
        set
        {
            if (_progressSummaryText != value)
            {
                _progressSummaryText = value;
                OnPropertyChanged();
            }
        }
    }

    public string CountdownBadgeText
    {
        get => _countdownBadgeText;
        set
        {
            if (_countdownBadgeText != value)
            {
                _countdownBadgeText = value;
                OnPropertyChanged();
            }
        }
    }

    public void UpdateFromSnapshot(LiveStepStatus snapshot)
    {
        ActionTitle = snapshot.ActionTitle;
        ActionDetail = snapshot.ActionDetail;
        WeightKg = snapshot.WeightKg;
        Status = snapshot.Status;
        ProgressFraction = snapshot.ProgressFraction;
        ProgressSummaryText = snapshot.ProgressSummaryText;
        CountdownBadgeText = snapshot.CountdownBadgeText;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
