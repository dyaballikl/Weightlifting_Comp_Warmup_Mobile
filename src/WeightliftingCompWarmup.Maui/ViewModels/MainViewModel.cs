using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using WeightliftingCompWarmup.Core.Models;
using WeightliftingCompWarmup.Core.Services;
using WeightliftingCompWarmup.Maui.Services;

namespace WeightliftingCompWarmup.Maui.ViewModels;

/// <summary>
/// Primary ViewModel coordinating Profile Management, Snatch &amp; Clean &amp; Jerk Setup,
/// Barbell/Plate Loading Graphics, and the Live Competition Warmup Runner.
/// </summary>
public class MainViewModel : INotifyPropertyChanged
{
    private readonly ProfileStorageService _storageService;
    private readonly DevicePlatformService _platformService;
    private LiveWarmupSession? _liveSession;
    private bool _suppressProfileReload;

    private LiftType _selectedLiftType = LiftType.Snatch;
    private bool _isLiveMode;
    private int _setupMobileSubTab; // 0 = Plan & Steps, 1 = Rules & Extras
    private string _statusBannerText = string.Empty;

    // New item input buffers for Setup Rule Editors
    private string _newExtraAction = string.Empty;
    private string _newExtraSecondsText = "300";
    private string _newJumpFromWeightText = string.Empty;
    private string _newJumpKgText = string.Empty;
    private string _newTimeFromWeightText = string.Empty;
    private string _newTimeSecondsText = "150";

    // Live telemetry bindable properties
    private bool _isLiveTimerRunning;
    private bool _liveAutoAdvance;
    private string _liveCurrentTime = "--:--:--";
    private string _liveOpenTime = "--:--:--";
    private string _liveTimeToOpen = "0s";
    private string _liveTimeTillStart = "—";
    private bool _liveHasCompetitionStarted = true;
    private bool _liveIsStageLiftingStarted;
    private int _liveLiftsOut;
    private int _liveLiftsPassed;
    private double _liveStageProgress;
    private string _liveStageStatusText = string.Empty;
    private bool _isNextLiftHighlighted;

    private int _liveCjSnatchLiftsOut;
    private double _liveCjSnatchProgress;
    private string _liveCjSnatchStatusText = string.Empty;
    private bool _isNextSnatchHighlighted;

    private string _liveCjBreakRemainingDisplay = "0s";
    private int _liveCjBreakRemainingSeconds;
    private double _liveCjBreakProgress;
    private bool _isBreakActive;
    private bool _isCjSnatchPhaseCompleted;
    private bool _isCjBreakPhaseCompleted;
    private bool _isCjSnatchManuallyExpanded;
    private bool _isCjBreakManuallyExpanded;
    private int _activeStepOrder;
    private string _liveActiveStepDescription = "Wait to start";
    private string _liveActiveStepRemaining = "0s";
    private double _liveActiveStepProgress;
    private DateTime _lastTimerTickUtc;

    private ObservableCollection<SetupStepItemViewModel> _previewSteps = new();
    private ObservableCollection<ExtraItem> _extrasList = new();
    private ObservableCollection<WeightJumpRule> _weightJumpsList = new();
    private ObservableCollection<WarmupTimeRule> _warmupTimesList = new();
    private ObservableCollection<LiveStepItemViewModel> _liveSteps = new();

    public ObservableCollection<string> ProfileNames { get; } = new();

    public ObservableCollection<SetupStepItemViewModel> PreviewSteps
    {
        get => _previewSteps;
        private set
        {
            _previewSteps = value;
            OnPropertyChanged();
        }
    }

    public ObservableCollection<ExtraItem> ExtrasList
    {
        get => _extrasList;
        private set
        {
            _extrasList = value;
            OnPropertyChanged();
        }
    }

    public ObservableCollection<WeightJumpRule> WeightJumpsList
    {
        get => _weightJumpsList;
        private set
        {
            _weightJumpsList = value;
            OnPropertyChanged();
        }
    }

    public ObservableCollection<WarmupTimeRule> WarmupTimesList
    {
        get => _warmupTimesList;
        private set
        {
            _warmupTimesList = value;
            OnPropertyChanged();
        }
    }

    public ObservableCollection<LiveStepItemViewModel> LiveSteps
    {
        get => _liveSteps;
        private set
        {
            _liveSteps = value;
            OnPropertyChanged();
        }
    }

    public Profile ActiveProfile => _storageService.ActiveProfile;
    public bool HasSeenWelcomeHelp => _storageService.HasSeenWelcomeHelp;

    public void MarkWelcomeHelpSeen() => _storageService.MarkWelcomeHelpSeen();

    public string ActiveProfileName
    {
        get => ActiveProfile.Name;
        set
        {
            if (!_suppressProfileReload && !string.IsNullOrWhiteSpace(value) && value != ActiveProfile.Name)
            {
                _storageService.SelectProfile(value);
                RefreshAllFromActiveProfile();
            }
        }
    }

    public LiftType SelectedLiftType
    {
        get => _selectedLiftType;
        set
        {
            if (_selectedLiftType != value)
            {
                StopLiveTimer();
                _selectedLiftType = value;
                RefreshDisciplineProperties();
                if (_isLiveMode)
                {
                    InitializeLiveSession(autoStart: false);
                }

                OnPropertyChanged();
                OnPropertyChanged(nameof(IsSnatchSelected));
                OnPropertyChanged(nameof(IsCjSelected));
                OnPropertyChanged(nameof(CurrentLiftTitle));
            }
        }
    }

    public bool IsSnatchSelected => _selectedLiftType == LiftType.Snatch;
    public bool IsCjSelected => _selectedLiftType == LiftType.CleanAndJerk;
    public string CurrentLiftTitle => IsSnatchSelected ? "SNATCH" : "CLEAN & JERK";

    public bool IsLiveMode
    {
        get => _isLiveMode;
        set
        {
            if (_isLiveMode != value)
            {
                _isLiveMode = value;
                if (_isLiveMode)
                {
                    InitializeLiveSession(autoStart: true);
                }
                else
                {
                    StopLiveTimer();
                    RefreshSetupCalculations();
                }

                OnPropertyChanged();
                OnPropertyChanged(nameof(IsSetupMode));
            }
        }
    }

    public bool IsSetupMode => !_isLiveMode;

    public int SetupMobileSubTab
    {
        get => _setupMobileSubTab;
        set
        {
            if (_setupMobileSubTab != value)
            {
                _setupMobileSubTab = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsMobileOverviewTab));
                OnPropertyChanged(nameof(IsMobileRulesTab));
            }
        }
    }

    public bool IsMobileOverviewTab => _setupMobileSubTab == 0;
    public bool IsMobileRulesTab => _setupMobileSubTab == 1;

    public string StatusBannerText
    {
        get => _statusBannerText;
        set
        {
            if (_statusBannerText != value)
            {
                _statusBannerText = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasStatusBanner));
            }
        }
    }

    public bool HasStatusBanner => !string.IsNullOrWhiteSpace(_statusBannerText);

    // Global Profile Settings
    public int BarbellWeight
    {
        get => ActiveProfile.BarbellWeight;
        set
        {
            int clamped = Math.Clamp(value, Defaults.MinBarbellWeight, Defaults.MaxBarbellWeight);
            if (ActiveProfile.BarbellWeight != clamped)
            {
                ActiveProfile.BarbellWeight = clamped;
                if (ActiveProfile.SnatchOpenerWeight < clamped)
                {
                    ActiveProfile.SnatchOpenerWeight = clamped;
                }
                if (ActiveProfile.CjOpenerWeight < clamped)
                {
                    ActiveProfile.CjOpenerWeight = clamped;
                }

                _storageService.Save();
                OnPropertyChanged();
                OnPropertyChanged(nameof(OpenerWeight));
                RefreshSetupCalculations();
            }
        }
    }

    public bool BeepOnNextLift
    {
        get => ActiveProfile.Beep;
        set
        {
            if (ActiveProfile.Beep != value)
            {
                ActiveProfile.Beep = value;
                if (_liveSession != null)
                {
                    _liveSession.BeepEnabled = value;
                }
                _storageService.Save();
                OnPropertyChanged();

                if (value)
                {
                    _platformService.PlayStepTransitionBeep();
                }
            }
        }
    }

    public TimeSpan CompetitionStartTime
    {
        get => ActiveProfile.Start.TimeOfDay;
        set
        {
            DateTime updated = DateTime.Today.Add(value);
            if (ActiveProfile.Start.TimeOfDay != value)
            {
                ActiveProfile.Start = updated;
                _storageService.Save();
                OnPropertyChanged();
            }
        }
    }

    // Active Discipline Parameters (Snatch or Clean & Jerk)
    public int StageSeconds
    {
        get => IsSnatchSelected ? ActiveProfile.SnatchSecondsStage : ActiveProfile.CjSecondsStage;
        set
        {
            int clamped = Math.Clamp(value, 1, 200);
            if (IsSnatchSelected && ActiveProfile.SnatchSecondsStage != clamped)
            {
                ActiveProfile.SnatchSecondsStage = clamped;
                _storageService.Save();
                OnPropertyChanged();
            }
            else if (IsCjSelected && ActiveProfile.CjSecondsStage != clamped)
            {
                ActiveProfile.CjSecondsStage = clamped;
                _storageService.Save();
                OnPropertyChanged();
            }
        }
    }

    public int OpenerWeight
    {
        get => IsSnatchSelected ? ActiveProfile.SnatchOpenerWeight : ActiveProfile.CjOpenerWeight;
        set
        {
            int clamped = Math.Clamp(value, ActiveProfile.BarbellWeight, Defaults.MaxOpenerWeight);
            if (IsSnatchSelected && ActiveProfile.SnatchOpenerWeight != clamped)
            {
                ActiveProfile.SnatchOpenerWeight = clamped;
                _storageService.Save();
                OnPropertyChanged();
                RefreshSetupCalculations();
            }
            else if (IsCjSelected && ActiveProfile.CjOpenerWeight != clamped)
            {
                ActiveProfile.CjOpenerWeight = clamped;
                _storageService.Save();
                OnPropertyChanged();
                RefreshSetupCalculations();
            }
        }
    }

    public bool OpenerInWarmup
    {
        get => IsSnatchSelected ? ActiveProfile.SnatchOpenerInWarmup : ActiveProfile.CjOpenerInWarmup;
        set
        {
            if (IsSnatchSelected && ActiveProfile.SnatchOpenerInWarmup != value)
            {
                ActiveProfile.SnatchOpenerInWarmup = value;
                _storageService.Save();
                OnPropertyChanged();
                RefreshSetupCalculations();
            }
            else if (IsCjSelected && ActiveProfile.CjOpenerInWarmup != value)
            {
                ActiveProfile.CjOpenerInWarmup = value;
                _storageService.Save();
                OnPropertyChanged();
                RefreshSetupCalculations();
            }
        }
    }

    public int ExtraSecondsEnd
    {
        get => IsSnatchSelected ? ActiveProfile.SnatchSecondsEnd : ActiveProfile.CjSecondsEnd;
        set
        {
            int clamped = Math.Clamp(value, 0, 999);
            if (IsSnatchSelected && ActiveProfile.SnatchSecondsEnd != clamped)
            {
                ActiveProfile.SnatchSecondsEnd = clamped;
                _storageService.Save();
                OnPropertyChanged();
                RefreshSetupCalculations();
            }
            else if (IsCjSelected && ActiveProfile.CjSecondsEnd != clamped)
            {
                ActiveProfile.CjSecondsEnd = clamped;
                _storageService.Save();
                OnPropertyChanged();
                RefreshSetupCalculations();
            }
        }
    }

    public int SetupLiftsOut
    {
        get => IsSnatchSelected ? ActiveProfile.SnatchLiftsOut : ActiveProfile.CjLiftsOut;
        set
        {
            int clamped = Math.Clamp(value, 0, 99);
            if (IsSnatchSelected && ActiveProfile.SnatchLiftsOut != clamped)
            {
                ActiveProfile.SnatchLiftsOut = clamped;
                _storageService.Save();
                OnPropertyChanged();
            }
            else if (IsCjSelected && ActiveProfile.CjLiftsOut != clamped)
            {
                ActiveProfile.CjLiftsOut = clamped;
                _storageService.Save();
                OnPropertyChanged();
            }
        }
    }

    public int SnatchBreakMinutes
    {
        get => ActiveProfile.SnatchMinutesBreak;
        set
        {
            int clamped = Math.Clamp(value, 0, 200);
            if (ActiveProfile.SnatchMinutesBreak != clamped)
            {
                ActiveProfile.SnatchMinutesBreak = clamped;
                _storageService.Save();
                OnPropertyChanged();
                OnPropertyChanged(nameof(SnatchBreakFormattedPreview));
            }
        }
    }

    public string SnatchBreakFormattedPreview => WarmupCalculator.FormatSeconds(ActiveProfile.SnatchSecondsBreak);

    public int CjBreakMinutes
    {
        get => ActiveProfile.CjMinutesBreak;
        set
        {
            int clamped = Math.Clamp(value, 1, 200);
            if (ActiveProfile.CjMinutesBreak != clamped)
            {
                ActiveProfile.CjMinutesBreak = clamped;
                _storageService.Save();
                OnPropertyChanged();
                OnPropertyChanged(nameof(CjBreakFormattedPreview));
            }
        }
    }

    public string CjBreakFormattedPreview => WarmupCalculator.FormatSeconds(ActiveProfile.CjSecondsBreak);

    public int CjSnatchAttemptsRemaining
    {
        get => ActiveProfile.CjSnatchLiftsOut;
        set
        {
            int clamped = Math.Clamp(value, 0, 99);
            if (ActiveProfile.CjSnatchLiftsOut != clamped)
            {
                ActiveProfile.CjSnatchLiftsOut = clamped;
                _storageService.Save();
                OnPropertyChanged();
            }
        }
    }

    // Steps Preview Summary
    public string StepsSummaryHeader { get; private set; } = string.Empty;

    // Rule Editor Input Buffers
    public string NewExtraAction
    {
        get => _newExtraAction;
        set
        {
            if (_newExtraAction != value)
            {
                _newExtraAction = value;
                OnPropertyChanged();
            }
        }
    }

    public string NewExtraSecondsText
    {
        get => _newExtraSecondsText;
        set
        {
            if (_newExtraSecondsText != value)
            {
                _newExtraSecondsText = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(NewExtraFormattedPreview));
            }
        }
    }

    public string NewExtraFormattedPreview =>
        int.TryParse(_newExtraSecondsText, out int sec) && sec > 0
            ? WarmupCalculator.FormatSeconds(sec)
            : "0s";

    public string NewJumpFromWeightText
    {
        get => _newJumpFromWeightText;
        set
        {
            if (_newJumpFromWeightText != value)
            {
                _newJumpFromWeightText = value;
                OnPropertyChanged();
            }
        }
    }

    public string NewJumpKgText
    {
        get => _newJumpKgText;
        set
        {
            if (_newJumpKgText != value)
            {
                _newJumpKgText = value;
                OnPropertyChanged();
            }
        }
    }

    public string NewTimeFromWeightText
    {
        get => _newTimeFromWeightText;
        set
        {
            if (_newTimeFromWeightText != value)
            {
                _newTimeFromWeightText = value;
                OnPropertyChanged();
            }
        }
    }

    public string NewTimeSecondsText
    {
        get => _newTimeSecondsText;
        set
        {
            if (_newTimeSecondsText != value)
            {
                _newTimeSecondsText = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(NewTimeFormattedPreview));
            }
        }
    }

    public string NewTimeFormattedPreview =>
        int.TryParse(_newTimeSecondsText, out int sec) && sec > 0
            ? WarmupCalculator.FormatSeconds(sec)
            : "0s";

    // Live Mode Bindable Properties
    public bool IsLiveTimerRunning
    {
        get => _isLiveTimerRunning;
        private set
        {
            if (_isLiveTimerRunning != value)
            {
                _isLiveTimerRunning = value;
                _lastTimerTickUtc = value ? DateTime.UtcNow : default;
                _platformService.SetKeepScreenOn(value);
                _platformService.SetLiveMiniTimerEnabled(value && _isLiveMode);
                OnPropertyChanged();
                OnPropertyChanged(nameof(LiveTimerButtonText));
                OnPropertyChanged(nameof(LiveTimerButtonColorHex));
                OnPropertyChanged(nameof(LiveTimerButtonColor));
            }
        }
    }

    public string LiveTimerButtonText => IsLiveTimerRunning ? "⏸ PAUSE TIMER" : "▶ START TIMER";
    public string LiveTimerButtonColorHex => IsLiveTimerRunning ? "#D97706" : "#16A34A";
    public Color LiveTimerButtonColor => Color.FromArgb(LiveTimerButtonColorHex);

    public bool LiveAutoAdvance
    {
        get => _liveAutoAdvance;
        set
        {
            if (_liveAutoAdvance != value)
            {
                _liveAutoAdvance = value;
                if (_liveSession != null)
                {
                    _liveSession.AutoAdvance = value;
                }
                OnPropertyChanged();
            }
        }
    }

    public string LiveCurrentTime
    {
        get => _liveCurrentTime;
        private set { if (_liveCurrentTime != value) { _liveCurrentTime = value; OnPropertyChanged(); } }
    }

    public string LiveOpenTime
    {
        get => _liveOpenTime;
        private set { if (_liveOpenTime != value) { _liveOpenTime = value; OnPropertyChanged(); } }
    }

    public string LiveTimeToOpen
    {
        get => _liveTimeToOpen;
        private set { if (_liveTimeToOpen != value) { _liveTimeToOpen = value; OnPropertyChanged(); } }
    }

    public string LiveTimeTillStart
    {
        get => _liveTimeTillStart;
        private set { if (_liveTimeTillStart != value) { _liveTimeTillStart = value; OnPropertyChanged(); } }
    }

    public bool LiveHasCompetitionStarted
    {
        get => _liveHasCompetitionStarted;
        private set
        {
            if (_liveHasCompetitionStarted != value)
            {
                _liveHasCompetitionStarted = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanForceStartSnatchStage));
            }
        }
    }

    public bool CanForceStartSnatchStage => IsSnatchSelected && !LiveHasCompetitionStarted;

    public bool LiveIsStageLiftingStarted
    {
        get => _liveIsStageLiftingStarted;
        private set
        {
            if (_liveIsStageLiftingStarted != value)
            {
                _liveIsStageLiftingStarted = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(AdvanceStageLiftButtonTextPhone));
                OnPropertyChanged(nameof(AdvanceStageLiftButtonTextTablet));
            }
        }
    }

    public string AdvanceStageLiftButtonTextPhone => LiveIsStageLiftingStarted
        ? "⏭ ADVANCE: NEXT STAGE LIFT"
        : "ADVANCE TO STAGE LIFTING";

    public string AdvanceStageLiftButtonTextTablet => LiveIsStageLiftingStarted
        ? "⏭ ADVANCE: NEXT LIFT ON PLATFORM"
        : "ADVANCE TO STAGE LIFTING";

    public int LiveLiftsOut
    {
        get => _liveLiftsOut;
        private set { if (_liveLiftsOut != value) { _liveLiftsOut = value; OnPropertyChanged(); } }
    }

    public int LiveLiftsPassed
    {
        get => _liveLiftsPassed;
        private set { if (_liveLiftsPassed != value) { _liveLiftsPassed = value; OnPropertyChanged(); } }
    }

    public double LiveStageProgress
    {
        get => _liveStageProgress;
        private set { if (Math.Abs(_liveStageProgress - value) > 0.001) { _liveStageProgress = value; OnPropertyChanged(); } }
    }

    public string LiveStageStatusText
    {
        get => _liveStageStatusText;
        private set { if (_liveStageStatusText != value) { _liveStageStatusText = value; OnPropertyChanged(); } }
    }

    public bool IsNextLiftHighlighted
    {
        get => _isNextLiftHighlighted;
        private set
        {
            if (_isNextLiftHighlighted != value)
            {
                _isNextLiftHighlighted = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(NextLiftButtonColorHex));
                OnPropertyChanged(nameof(NextLiftButtonColor));
            }
        }
    }

    public string NextLiftButtonColorHex => IsNextLiftHighlighted ? "#16A34A" : "#334155";
    public Color NextLiftButtonColor => Color.FromArgb(NextLiftButtonColorHex);

    public int LiveCjSnatchLiftsOut
    {
        get => _liveCjSnatchLiftsOut;
        private set { if (_liveCjSnatchLiftsOut != value) { _liveCjSnatchLiftsOut = value; OnPropertyChanged(); } }
    }

    public double LiveCjSnatchProgress
    {
        get => _liveCjSnatchProgress;
        private set { if (Math.Abs(_liveCjSnatchProgress - value) > 0.001) { _liveCjSnatchProgress = value; OnPropertyChanged(); } }
    }

    public string LiveCjSnatchStatusText
    {
        get => _liveCjSnatchStatusText;
        private set { if (_liveCjSnatchStatusText != value) { _liveCjSnatchStatusText = value; OnPropertyChanged(); } }
    }

    public bool IsNextSnatchHighlighted
    {
        get => _isNextSnatchHighlighted;
        private set
        {
            if (_isNextSnatchHighlighted != value)
            {
                _isNextSnatchHighlighted = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(NextSnatchButtonColorHex));
                OnPropertyChanged(nameof(NextSnatchButtonColor));
            }
        }
    }

    public string NextSnatchButtonColorHex => IsNextSnatchHighlighted ? "#16A34A" : "#334155";
    public Color NextSnatchButtonColor => Color.FromArgb(NextSnatchButtonColorHex);

    public string LiveCjBreakRemainingDisplay
    {
        get => _liveCjBreakRemainingDisplay;
        private set { if (_liveCjBreakRemainingDisplay != value) { _liveCjBreakRemainingDisplay = value; OnPropertyChanged(); } }
    }

    public int LiveCjBreakRemainingSeconds => _liveCjBreakRemainingSeconds;

    public double LiveCjBreakProgress
    {
        get => _liveCjBreakProgress;
        private set { if (Math.Abs(_liveCjBreakProgress - value) > 0.001) { _liveCjBreakProgress = value; OnPropertyChanged(); } }
    }

    public bool IsBreakActive
    {
        get => _isBreakActive;
        private set
        {
            if (_isBreakActive != value)
            {
                _isBreakActive = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(BreakFillColorHex));
            }
        }
    }

    public string BreakFillColorHex => IsBreakActive ? "#EAB308" : "#0284C7";

    // Phone C&J Collapsible Phase State (Phase 1: Snatch Left, Phase 2: Break)
    public bool IsCjSnatchPhaseCompleted => _isCjSnatchPhaseCompleted;
    public bool IsCjBreakPhaseCompleted => _isCjBreakPhaseCompleted;
    public bool IsCjSnatchExpanded => !_isCjSnatchPhaseCompleted || _isCjSnatchManuallyExpanded;
    public bool IsCjSnatchCollapsed => !IsCjSnatchExpanded;
    public bool IsCjBreakExpanded => !_isCjBreakPhaseCompleted || _isCjBreakManuallyExpanded;
    public bool IsCjBreakCollapsed => !IsCjBreakExpanded;
    public string PhoneCjSnatchHeaderText => _isCjSnatchPhaseCompleted ? "▴ 1. Snatch Left:" : "1. Snatch Left:";
    public string PhoneCjBreakHeaderText => _isCjBreakPhaseCompleted ? "▴ 2. Break (tap bar/time):" : "2. Break (tap bar/time):";
    public string PhoneSnatchBreakHeaderText => _isCjBreakPhaseCompleted ? "▴ Presentation & Clock (tap bar/time):" : "Presentation & Clock (tap bar/time):";

    public int ActiveStepOrder
    {
        get => _activeStepOrder;
        private set { if (_activeStepOrder != value) { _activeStepOrder = value; OnPropertyChanged(); } }
    }

    public string LiveActiveStepDescription
    {
        get => _liveActiveStepDescription;
        private set { if (_liveActiveStepDescription != value) { _liveActiveStepDescription = value; OnPropertyChanged(); } }
    }

    public string LiveActiveStepRemaining
    {
        get => _liveActiveStepRemaining;
        private set { if (_liveActiveStepRemaining != value) { _liveActiveStepRemaining = value; OnPropertyChanged(); } }
    }

    public double LiveActiveStepProgress
    {
        get => _liveActiveStepProgress;
        private set { if (Math.Abs(_liveActiveStepProgress - value) > 0.001) { _liveActiveStepProgress = value; OnPropertyChanged(); } }
    }

    // Commands
    public ICommand SelectSnatchCommand { get; }
    public ICommand SelectCleanAndJerkCommand { get; }
    public ICommand SwitchToSetupCommand { get; }
    public ICommand StartLiveCommand { get; }
    public ICommand SelectMobileSubTabCommand { get; }

    public ICommand AdjustBarbellWeightCommand { get; }
    public ICommand AdjustOpenerWeightCommand { get; }
    public ICommand AdjustStageSecondsCommand { get; }
    public ICommand AdjustExtraSecondsEndCommand { get; }
    public ICommand AdjustSetupLiftsOutCommand { get; }
    public ICommand AdjustSnatchBreakMinutesCommand { get; }
    public ICommand AdjustCjBreakMinutesCommand { get; }
    public ICommand AdjustCjSnatchRemainingCommand { get; }

    public ICommand AddExtraCommand { get; }
    public ICommand MoveExtraUpCommand { get; }
    public ICommand MoveExtraDownCommand { get; }
    public ICommand DeleteExtraCommand { get; }

    public ICommand AddWeightJumpCommand { get; }
    public ICommand DeleteWeightJumpCommand { get; }

    public ICommand AddWarmupTimeCommand { get; }
    public ICommand DeleteWarmupTimeCommand { get; }

    // Live Runner Commands
    public ICommand ToggleLiveTimerCommand { get; }
    public ICommand ResetLiveSessionCommand { get; }
    public ICommand AdvanceStageLiftCommand { get; }
    public ICommand AdvanceCjSnatchLiftCommand { get; }
    public ICommand AdjustLiveLiftsOutCommand { get; }
    public ICommand AdjustLiveCjSnatchLiftsOutCommand { get; }
    public ICommand ScrubStageProgressCommand { get; }
    public ICommand ScrubCjSnatchProgressCommand { get; }
    public ICommand ScrubCjBreakProgressCommand { get; }
    public ICommand ForceStartStageNowCommand { get; }
    public ICommand ToggleCjSnatchExpandedCommand { get; }
    public ICommand ToggleCjBreakExpandedCommand { get; }

    public MainViewModel(ProfileStorageService? storageService = null, DevicePlatformService? platformService = null)
    {
        _storageService = storageService ?? new ProfileStorageService();
        _platformService = platformService ?? new DevicePlatformService();

        _storageService.Load();

        SelectSnatchCommand = new Command(() => SelectedLiftType = LiftType.Snatch);
        SelectCleanAndJerkCommand = new Command(() => SelectedLiftType = LiftType.CleanAndJerk);
        SwitchToSetupCommand = new Command(() => IsLiveMode = false);
        StartLiveCommand = new Command(() => IsLiveMode = true);
        SelectMobileSubTabCommand = new Command<string>(tab =>
        {
            if (int.TryParse(tab, out int idx))
            {
                SetupMobileSubTab = idx;
            }
        });

        AdjustBarbellWeightCommand = new Command<string>(deltaStr =>
        {
            if (int.TryParse(deltaStr, out int delta))
            {
                BarbellWeight += delta;
            }
        });

        AdjustOpenerWeightCommand = new Command<string>(deltaStr =>
        {
            if (int.TryParse(deltaStr, out int delta))
            {
                OpenerWeight += delta;
            }
        });

        AdjustStageSecondsCommand = new Command<string>(deltaStr =>
        {
            if (int.TryParse(deltaStr, out int delta))
            {
                StageSeconds += delta;
            }
        });

        AdjustExtraSecondsEndCommand = new Command<string>(deltaStr =>
        {
            if (int.TryParse(deltaStr, out int delta))
            {
                ExtraSecondsEnd += delta;
            }
        });

        AdjustSetupLiftsOutCommand = new Command<string>(deltaStr =>
        {
            if (int.TryParse(deltaStr, out int delta))
            {
                SetupLiftsOut += delta;
            }
        });

        AdjustSnatchBreakMinutesCommand = new Command<string>(deltaStr =>
        {
            if (int.TryParse(deltaStr, out int delta))
            {
                SnatchBreakMinutes += delta;
            }
        });

        AdjustCjBreakMinutesCommand = new Command<string>(deltaStr =>
        {
            if (int.TryParse(deltaStr, out int delta))
            {
                CjBreakMinutes += delta;
            }
        });

        AdjustCjSnatchRemainingCommand = new Command<string>(deltaStr =>
        {
            if (int.TryParse(deltaStr, out int delta))
            {
                CjSnatchAttemptsRemaining += delta;
            }
        });

        AddExtraCommand = new Command(ExecuteAddExtra);
        MoveExtraUpCommand = new Command<ExtraItem>(ExecuteMoveExtraUp);
        MoveExtraDownCommand = new Command<ExtraItem>(ExecuteMoveExtraDown);
        DeleteExtraCommand = new Command<ExtraItem>(ExecuteDeleteExtra);

        AddWeightJumpCommand = new Command(ExecuteAddWeightJump);
        DeleteWeightJumpCommand = new Command<WeightJumpRule>(ExecuteDeleteWeightJump);

        AddWarmupTimeCommand = new Command(ExecuteAddWarmupTime);
        DeleteWarmupTimeCommand = new Command<WarmupTimeRule>(ExecuteDeleteWarmupTime);

        ToggleLiveTimerCommand = new Command(ExecuteToggleLiveTimer);
        ResetLiveSessionCommand = new Command(() => InitializeLiveSession(autoStart: IsLiveTimerRunning));
        AdvanceStageLiftCommand = new Command(ExecuteAdvanceStageLift);
        AdvanceCjSnatchLiftCommand = new Command(ExecuteAdvanceCjSnatchLift);
        AdjustLiveLiftsOutCommand = new Command<string>(ExecuteAdjustLiveLiftsOut);
        AdjustLiveCjSnatchLiftsOutCommand = new Command<string>(ExecuteAdjustLiveCjSnatchLiftsOut);
        ScrubStageProgressCommand = new Command<double>(ExecuteScrubStageProgress);
        ScrubCjSnatchProgressCommand = new Command<double>(ExecuteScrubCjSnatchProgress);
        ScrubCjBreakProgressCommand = new Command<double>(ExecuteScrubCjBreakProgress);
        ForceStartStageNowCommand = new Command(ExecuteForceStartStageNow);
        ToggleCjSnatchExpandedCommand = new Command(ExecuteToggleCjSnatchExpanded);
        ToggleCjBreakExpandedCommand = new Command(ExecuteToggleCjBreakExpanded);

        RefreshAllFromActiveProfile();
    }

    public void RefreshAllFromActiveProfile()
    {
        _suppressProfileReload = true;
        ProfileNames.Clear();
        foreach (var profile in _storageService.Profiles)
        {
            ProfileNames.Add(profile.Name);
        }
        OnPropertyChanged(nameof(ActiveProfile));
        OnPropertyChanged(nameof(ActiveProfileName));
        OnPropertyChanged(nameof(BarbellWeight));
        OnPropertyChanged(nameof(BeepOnNextLift));
        OnPropertyChanged(nameof(CompetitionStartTime));
        _suppressProfileReload = false;

        RefreshDisciplineProperties();
    }

    public void RefreshDisciplineProperties()
    {
        OnPropertyChanged(nameof(StageSeconds));
        OnPropertyChanged(nameof(OpenerWeight));
        OnPropertyChanged(nameof(OpenerInWarmup));
        OnPropertyChanged(nameof(ExtraSecondsEnd));
        OnPropertyChanged(nameof(SetupLiftsOut));
        OnPropertyChanged(nameof(SnatchBreakMinutes));
        OnPropertyChanged(nameof(SnatchBreakFormattedPreview));
        OnPropertyChanged(nameof(CjBreakMinutes));
        OnPropertyChanged(nameof(CjBreakFormattedPreview));
        OnPropertyChanged(nameof(CjSnatchAttemptsRemaining));
        OnPropertyChanged(nameof(CanForceStartSnatchStage));

        RefreshRuleCollections();
        RefreshSetupCalculations();
    }

    private void RefreshRuleCollections()
    {
        var activeExtras = IsSnatchSelected ? ActiveProfile.SnatchExtras : ActiveProfile.CjExtras;
        var activeJumps = IsSnatchSelected ? ActiveProfile.SnatchJumps : ActiveProfile.CjJumps;
        var activeTimes = IsSnatchSelected ? ActiveProfile.SnatchTimes : ActiveProfile.CjTimes;

        var nextExtras = new ObservableCollection<ExtraItem>();
        int order = 1;
        foreach (var item in activeExtras.OrderBy(e => e.Order))
        {
            item.Order = order++;
            nextExtras.Add(item);
        }
        ExtrasList = nextExtras;

        var nextJumps = new ObservableCollection<WeightJumpRule>();
        int jumpIdx = 0;
        foreach (var kvp in activeJumps.OrderBy(k => k.Key))
        {
            nextJumps.Add(new WeightJumpRule(kvp.Key, kvp.Value, isBaseRule: jumpIdx == 0));
            jumpIdx++;
        }
        WeightJumpsList = nextJumps;

        var nextTimes = new ObservableCollection<WarmupTimeRule>();
        int timeIdx = 0;
        foreach (var kvp in activeTimes.OrderBy(k => k.Key))
        {
            nextTimes.Add(new WarmupTimeRule(kvp.Key, kvp.Value, isBaseRule: timeIdx == 0));
            timeIdx++;
        }
        WarmupTimesList = nextTimes;
    }

    public void RefreshSetupCalculations()
    {
        var allSteps = WarmupCalculator.BuildSteps(_selectedLiftType, ActiveProfile);
        var displaySteps = allSteps.Where(s => !s.PreStep).OrderBy(s => s.Order).ToList();

        var nextPreview = new ObservableCollection<SetupStepItemViewModel>();
        foreach (var step in displaySteps)
        {
            nextPreview.Add(SetupStepItemViewModel.FromStep(step, ActiveProfile.BarbellWeight));
        }
        PreviewSteps = nextPreview;

        int totalSteps = displaySteps.Count;
        int totalLifts = displaySteps.Count(s => s.Weight > 0);
        int totalDurationSec = allSteps.FirstOrDefault(s => s.PreStep)?.TotalLengthReverse ?? 0;

        StepsSummaryHeader = $"{totalSteps} steps ({totalLifts} lifts) • Total {WarmupCalculator.FormatSeconds(totalDurationSec)}";
        OnPropertyChanged(nameof(StepsSummaryHeader));
    }

    // Rule Editor Actions
    private void ExecuteAddExtra()
    {
        string action = NewExtraAction.Trim();
        if (string.IsNullOrWhiteSpace(action))
        {
            ShowBanner("Enter a description for the extra warmup step.");
            return;
        }

        if (!int.TryParse(NewExtraSecondsText, out int seconds) || seconds <= 0 || seconds > 7200)
        {
            ShowBanner("Extra duration must be between 1 and 7200 seconds.");
            return;
        }

        var targetExtras = IsSnatchSelected ? ActiveProfile.SnatchExtras : ActiveProfile.CjExtras;
        int nextOrder = targetExtras.Count > 0 ? targetExtras.Max(e => e.Order) + 1 : 1;
        targetExtras.Add(new ExtraItem(action, seconds, nextOrder));

        NewExtraAction = string.Empty;
        _storageService.Save();
        RefreshRuleCollections();
        RefreshSetupCalculations();
        ShowBanner($"Added extra '{action}' ({WarmupCalculator.FormatSeconds(seconds)}).");
    }

    private void ExecuteMoveExtraUp(ExtraItem? item)
    {
        if (item == null)
        {
            return;
        }

        var targetExtras = IsSnatchSelected ? ActiveProfile.SnatchExtras : ActiveProfile.CjExtras;
        var ordered = targetExtras.OrderBy(e => e.Order).ToList();
        int idx = ordered.IndexOf(item);
        if (idx > 0)
        {
            (ordered[idx - 1].Order, ordered[idx].Order) = (ordered[idx].Order, ordered[idx - 1].Order);
            _storageService.Save();
            RefreshRuleCollections();
            RefreshSetupCalculations();
        }
    }

    private void ExecuteMoveExtraDown(ExtraItem? item)
    {
        if (item == null)
        {
            return;
        }

        var targetExtras = IsSnatchSelected ? ActiveProfile.SnatchExtras : ActiveProfile.CjExtras;
        var ordered = targetExtras.OrderBy(e => e.Order).ToList();
        int idx = ordered.IndexOf(item);
        if (idx >= 0 && idx < ordered.Count - 1)
        {
            (ordered[idx + 1].Order, ordered[idx].Order) = (ordered[idx].Order, ordered[idx + 1].Order);
            _storageService.Save();
            RefreshRuleCollections();
            RefreshSetupCalculations();
        }
    }

    private void ExecuteDeleteExtra(ExtraItem? item)
    {
        if (item == null)
        {
            return;
        }

        var targetExtras = IsSnatchSelected ? ActiveProfile.SnatchExtras : ActiveProfile.CjExtras;
        targetExtras.Remove(item);
        _storageService.Save();
        RefreshRuleCollections();
        RefreshSetupCalculations();
    }

    private void ExecuteAddWeightJump()
    {
        if (!int.TryParse(NewJumpFromWeightText, out int fromWeight) || fromWeight < 1 || fromWeight > Defaults.MaxOpenerWeight)
        {
            ShowBanner($"From Weight must be between 1 and {Defaults.MaxOpenerWeight} kg.");
            return;
        }

        if (!int.TryParse(NewJumpKgText, out int jumpKg) || jumpKg < 1 || jumpKg > 100)
        {
            ShowBanner("Jump size must be between 1 and 100 kg.");
            return;
        }

        var targetJumps = IsSnatchSelected ? ActiveProfile.SnatchJumps : ActiveProfile.CjJumps;
        targetJumps[fromWeight] = jumpKg;

        NewJumpFromWeightText = string.Empty;
        NewJumpKgText = string.Empty;
        _storageService.Save();
        RefreshRuleCollections();
        RefreshSetupCalculations();
        ShowBanner($"Saved weight jump: From {fromWeight}kg → +{jumpKg}kg.");
    }

    private void ExecuteDeleteWeightJump(WeightJumpRule? rule)
    {
        if (rule == null || !rule.CanDelete)
        {
            return;
        }

        var targetJumps = IsSnatchSelected ? ActiveProfile.SnatchJumps : ActiveProfile.CjJumps;
        targetJumps.Remove(rule.FromWeight);
        _storageService.Save();
        RefreshRuleCollections();
        RefreshSetupCalculations();
    }

    private void ExecuteAddWarmupTime()
    {
        if (!int.TryParse(NewTimeFromWeightText, out int fromWeight) || fromWeight < 1 || fromWeight > Defaults.MaxOpenerWeight)
        {
            ShowBanner($"From Weight must be between 1 and {Defaults.MaxOpenerWeight} kg.");
            return;
        }

        if (!int.TryParse(NewTimeSecondsText, out int seconds) || seconds < 5 || seconds > 3600)
        {
            ShowBanner("Warmup interval time must be between 5 and 3600 seconds.");
            return;
        }

        var targetTimes = IsSnatchSelected ? ActiveProfile.SnatchTimes : ActiveProfile.CjTimes;
        targetTimes[fromWeight] = seconds;

        NewTimeFromWeightText = string.Empty;
        _storageService.Save();
        RefreshRuleCollections();
        RefreshSetupCalculations();
        ShowBanner($"Saved warmup time: From {fromWeight}kg → {WarmupCalculator.FormatSeconds(seconds)}.");
    }

    private void ExecuteDeleteWarmupTime(WarmupTimeRule? rule)
    {
        if (rule == null || !rule.CanDelete)
        {
            return;
        }

        var targetTimes = IsSnatchSelected ? ActiveProfile.SnatchTimes : ActiveProfile.CjTimes;
        targetTimes.Remove(rule.FromWeight);
        _storageService.Save();
        RefreshRuleCollections();
        RefreshSetupCalculations();
    }

    public void RenameExtra(ExtraItem? item, string? newAction)
    {
        if (item == null)
        {
            return;
        }

        string trimmed = (newAction ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            ShowBanner("Extra step description cannot be empty.");
            return;
        }

        item.Action = trimmed;
        _storageService.Save();
        RefreshRuleCollections();
        RefreshSetupCalculations();
        ShowBanner($"Renamed extra step #{item.Order} to '{trimmed}'.");
    }

    public void UpdateExtraDuration(ExtraItem? item, int newSeconds)
    {
        if (item == null)
        {
            return;
        }

        if (newSeconds <= 0 || newSeconds > 7200)
        {
            ShowBanner("Extra duration must be between 1 and 7200 seconds.");
            return;
        }

        item.Length = newSeconds;
        _storageService.Save();
        RefreshRuleCollections();
        RefreshSetupCalculations();
        ShowBanner($"Updated '{item.Action}' duration to {WarmupCalculator.FormatSeconds(newSeconds)}.");
    }

    public void UpdateWeightJumpFromWeight(WeightJumpRule? rule, int newFromWeight)
    {
        if (rule == null)
        {
            return;
        }

        if (newFromWeight < 1 || newFromWeight > Defaults.MaxOpenerWeight)
        {
            ShowBanner($"From Weight must be between 1 and {Defaults.MaxOpenerWeight} kg.");
            return;
        }

        if (newFromWeight == rule.FromWeight)
        {
            return;
        }

        var targetJumps = IsSnatchSelected ? ActiveProfile.SnatchJumps : ActiveProfile.CjJumps;
        int jumpKg = rule.Jump;
        targetJumps.Remove(rule.FromWeight);
        targetJumps[newFromWeight] = jumpKg;

        _storageService.Save();
        RefreshRuleCollections();
        RefreshSetupCalculations();
        ShowBanner($"Updated weight jump: From {newFromWeight}kg → +{jumpKg}kg.");
    }

    public void UpdateWeightJumpValue(WeightJumpRule? rule, int newJumpKg)
    {
        if (rule == null)
        {
            return;
        }

        if (newJumpKg < 1 || newJumpKg > 100)
        {
            ShowBanner("Jump size must be between 1 and 100 kg.");
            return;
        }

        var targetJumps = IsSnatchSelected ? ActiveProfile.SnatchJumps : ActiveProfile.CjJumps;
        targetJumps[rule.FromWeight] = newJumpKg;

        _storageService.Save();
        RefreshRuleCollections();
        RefreshSetupCalculations();
        ShowBanner($"Updated weight jump: From {rule.FromWeight}kg → +{newJumpKg}kg.");
    }

    public void UpdateWarmupTimeFromWeight(WarmupTimeRule? rule, int newFromWeight)
    {
        if (rule == null)
        {
            return;
        }

        if (newFromWeight < 1 || newFromWeight > Defaults.MaxOpenerWeight)
        {
            ShowBanner($"From Weight must be between 1 and {Defaults.MaxOpenerWeight} kg.");
            return;
        }

        if (newFromWeight == rule.FromWeight)
        {
            return;
        }

        var targetTimes = IsSnatchSelected ? ActiveProfile.SnatchTimes : ActiveProfile.CjTimes;
        int seconds = rule.Seconds;
        targetTimes.Remove(rule.FromWeight);
        targetTimes[newFromWeight] = seconds;

        _storageService.Save();
        RefreshRuleCollections();
        RefreshSetupCalculations();
        ShowBanner($"Updated warmup time: From {newFromWeight}kg → {WarmupCalculator.FormatSeconds(seconds)}.");
    }

    public void UpdateWarmupTimeSeconds(WarmupTimeRule? rule, int newSeconds)
    {
        if (rule == null)
        {
            return;
        }

        if (newSeconds < 5 || newSeconds > 3600)
        {
            ShowBanner("Warmup interval time must be between 5 and 3600 seconds.");
            return;
        }

        var targetTimes = IsSnatchSelected ? ActiveProfile.SnatchTimes : ActiveProfile.CjTimes;
        targetTimes[rule.FromWeight] = newSeconds;

        _storageService.Save();
        RefreshRuleCollections();
        RefreshSetupCalculations();
        ShowBanner($"Updated warmup time: From {rule.FromWeight}kg → {WarmupCalculator.FormatSeconds(newSeconds)}.");
    }

    // Profile CRUD Operations (invoked from Page dialogs / action sheets)
    public (bool Success, string? ErrorMessage) CreateNewProfile(string name)
    {
        var result = _storageService.AddNewProfile(name);
        if (result.Success)
        {
            RefreshAllFromActiveProfile();
            ShowBanner($"Created profile '{result.Profile.Name}'.");
        }
        return (result.Success, result.ErrorMessage);
    }

    public (bool Success, string? ErrorMessage) DuplicateCurrentProfile(string newName)
    {
        var result = _storageService.DuplicateActiveProfile(newName);
        if (result.Success)
        {
            RefreshAllFromActiveProfile();
            ShowBanner($"Duplicated to '{result.Profile.Name}'.");
        }
        return (result.Success, result.ErrorMessage);
    }

    public (bool Success, string? ErrorMessage) RenameCurrentProfile(string newName)
    {
        var result = _storageService.RenameActiveProfile(newName);
        if (result.Success)
        {
            RefreshAllFromActiveProfile();
            ShowBanner($"Renamed profile to '{ActiveProfile.Name}'.");
        }
        return result;
    }

    public void DeleteCurrentProfile()
    {
        string deletedName = ActiveProfile.Name;
        _storageService.DeleteActiveProfile();
        RefreshAllFromActiveProfile();
        ShowBanner($"Deleted profile '{deletedName}'. Active: '{ActiveProfile.Name}'.");
    }

    public void SaveCurrentProfile()
    {
        _storageService.Save();
        ShowBanner($"Saved settings for '{ActiveProfile.Name}'.");
    }

    public void ResetAllSettingsToDemo()
    {
        StopLiveTimer();
        _storageService.ResetAllSettings();
        RefreshAllFromActiveProfile();
        ShowBanner("Cleared all settings and restored 'Demo Profile'.");
    }

    // Live Warmup Runner Methods
    public void InitializeLiveSession(bool autoStart)
    {
        _liveSession = new LiveWarmupSession(_selectedLiftType, ActiveProfile)
        {
            IsRunning = autoStart,
            AutoAdvance = _liveAutoAdvance
        };

        _isCjSnatchManuallyExpanded = false;
        _isCjBreakManuallyExpanded = false;

        IsLiveTimerRunning = autoStart;
        _lastTimerTickUtc = autoStart ? DateTime.UtcNow : default;
        _platformService.SetLiveMiniTimerEnabled(autoStart && _isLiveMode);

        var snapshot = _liveSession.GetSnapshot(DateTime.Now);
        var nextLiveSteps = new ObservableCollection<LiveStepItemViewModel>();
        foreach (var step in snapshot.Steps)
        {
            var itemVm = new LiveStepItemViewModel
            {
                Order = step.Order,
                BarbellWeight = ActiveProfile.BarbellWeight,
                IsLift = step.IsLift,
                IsPreStep = step.IsPreStep,
                IsOpener = step.IsOpener
            };
            itemVm.UpdateFromSnapshot(step);
            nextLiveSteps.Add(itemVm);
        }

        LiveSteps = nextLiveSteps;
        ApplyLiveSnapshot(snapshot);
    }

    public void StopLiveTimer()
    {
        if (_liveSession != null)
        {
            _liveSession.IsRunning = false;
        }
        IsLiveTimerRunning = false;
        _lastTimerTickUtc = default;
        _platformService.SetLiveMiniTimerEnabled(false);
    }

    private void ExecuteToggleLiveTimer()
    {
        if (_liveSession == null)
        {
            InitializeLiveSession(autoStart: true);
            return;
        }

        IsLiveTimerRunning = !IsLiveTimerRunning;
        _liveSession.IsRunning = IsLiveTimerRunning;
        ApplyLiveSnapshot(_liveSession.GetSnapshot(DateTime.Now));
    }

    /// <summary>
    /// Called every 1 second by the UI dispatcher timer while the view is active or in floating mini-timer mode.
    /// </summary>
    public void OnTimerTick()
    {
        if (!_isLiveMode || _liveSession == null || !IsLiveTimerRunning)
        {
            return;
        }

        DateTime nowUtc = DateTime.UtcNow;
        int ticksToRun = 1;
        if (_lastTimerTickUtc != default)
        {
            double elapsedSeconds = (nowUtc - _lastTimerTickUtc).TotalSeconds;
            if (elapsedSeconds >= 1.75)
            {
                ticksToRun = Math.Clamp((int)Math.Round(elapsedSeconds), 1, 3600);
            }
        }
        _lastTimerTickUtc = nowUtc;

        DateTime localNow = DateTime.Now;
        LiveSessionSnapshot snapshot = _liveSession.Tick(localNow);
        bool triggerBeep = snapshot.TriggerBeep;

        for (int i = 1; i < ticksToRun; i++)
        {
            snapshot = _liveSession.Tick(localNow);
            triggerBeep |= snapshot.TriggerBeep;
        }

        if (triggerBeep && !snapshot.TriggerBeep)
        {
            snapshot = snapshot with { TriggerBeep = true };
        }

        ApplyLiveSnapshot(snapshot);
    }

    private void ExecuteAdvanceStageLift()
    {
        if (_liveSession == null)
        {
            return;
        }

        var snapshot = _liveSession.AdvanceStageLift(DateTime.Now);
        ApplyLiveSnapshot(snapshot);
    }

    private void ExecuteAdvanceCjSnatchLift()
    {
        if (_liveSession == null)
        {
            return;
        }

        var snapshot = _liveSession.AdvanceCjSnatchLift(DateTime.Now);
        ApplyLiveSnapshot(snapshot);
    }

    private void ExecuteAdjustLiveLiftsOut(string? deltaStr)
    {
        if (_liveSession == null || !int.TryParse(deltaStr, out int delta))
        {
            return;
        }

        var snapshot = _liveSession.SetLiftsOut(_liveSession.LiftsOut + delta, DateTime.Now);
        ApplyLiveSnapshot(snapshot);
    }

    private void ExecuteAdjustLiveCjSnatchLiftsOut(string? deltaStr)
    {
        if (_liveSession == null || !int.TryParse(deltaStr, out int delta))
        {
            return;
        }

        var snapshot = _liveSession.SetCjSnatchLiftsOut(_liveSession.CjSnatchLiftsOut + delta, DateTime.Now);
        ApplyLiveSnapshot(snapshot);
    }

    private void ExecuteScrubStageProgress(double fraction)
    {
        if (_liveSession == null)
        {
            return;
        }

        var snapshot = _liveSession.ScrubStageLiftProgress(fraction, DateTime.Now);
        ApplyLiveSnapshot(snapshot);
    }

    private void ExecuteScrubCjSnatchProgress(double fraction)
    {
        if (_liveSession == null)
        {
            return;
        }

        var snapshot = _liveSession.ScrubCjSnatchProgress(fraction, DateTime.Now);
        ApplyLiveSnapshot(snapshot);
    }

    private void ExecuteScrubCjBreakProgress(double fraction)
    {
        if (_liveSession == null)
        {
            return;
        }

        var snapshot = _liveSession.ScrubCjBreakProgress(fraction, DateTime.Now);
        ApplyLiveSnapshot(snapshot);
    }

    private void ExecuteForceStartStageNow()
    {
        if (_liveSession == null)
        {
            return;
        }

        var snapshot = _liveSession.ForceStartStageNow(DateTime.Now);
        ApplyLiveSnapshot(snapshot);
    }

    private void ExecuteToggleCjSnatchExpanded()
    {
        if (_isCjSnatchPhaseCompleted)
        {
            _isCjSnatchManuallyExpanded = !_isCjSnatchManuallyExpanded;
            NotifyCjPhaseExpansionChanged();
        }
    }

    private void ExecuteToggleCjBreakExpanded()
    {
        if (_isCjBreakPhaseCompleted)
        {
            _isCjBreakManuallyExpanded = !_isCjBreakManuallyExpanded;
            NotifyCjPhaseExpansionChanged();
        }
    }

    private void NotifyCjPhaseExpansionChanged()
    {
        OnPropertyChanged(nameof(IsCjSnatchPhaseCompleted));
        OnPropertyChanged(nameof(IsCjBreakPhaseCompleted));
        OnPropertyChanged(nameof(IsCjSnatchExpanded));
        OnPropertyChanged(nameof(IsCjSnatchCollapsed));
        OnPropertyChanged(nameof(IsCjBreakExpanded));
        OnPropertyChanged(nameof(IsCjBreakCollapsed));
        OnPropertyChanged(nameof(PhoneCjSnatchHeaderText));
        OnPropertyChanged(nameof(PhoneCjBreakHeaderText));
        OnPropertyChanged(nameof(PhoneSnatchBreakHeaderText));
    }

    public void SetCustomLiveBreakRemainingSeconds(int remainingSeconds)
    {
        if (_liveSession == null)
        {
            return;
        }

        var snapshot = _liveSession.SetCjBreakRemainingSeconds(remainingSeconds, DateTime.Now);
        ApplyLiveSnapshot(snapshot);
    }

    public void SetCustomLiveLiftsOut(int liftsOut)
    {
        if (_liveSession == null)
        {
            return;
        }

        var snapshot = _liveSession.SetLiftsOut(liftsOut, DateTime.Now);
        ApplyLiveSnapshot(snapshot);
    }

    private void ApplyLiveSnapshot(LiveSessionSnapshot snapshot)
    {
        LiveCurrentTime = snapshot.CurrentTimeDisplay;
        LiveOpenTime = snapshot.OpenTimeDisplay;
        LiveTimeToOpen = snapshot.TimeToOpenDisplay;
        LiveTimeTillStart = snapshot.TimeTillStartDisplay;
        LiveHasCompetitionStarted = snapshot.HasCompetitionStarted;
        LiveIsStageLiftingStarted = snapshot.IsStageLiftingStarted;

        LiveLiftsOut = snapshot.LiftsOut;
        LiveLiftsPassed = snapshot.LiftsPassed;
        LiveStageProgress = snapshot.StageLiftProgress;
        LiveStageStatusText = snapshot.StageLiftStatusText;
        IsNextLiftHighlighted = snapshot.IsNextLiftHighlighted;

        LiveCjSnatchLiftsOut = snapshot.CjSnatchLiftsOut;
        LiveCjSnatchProgress = snapshot.CjSnatchStageProgress;
        LiveCjSnatchStatusText = snapshot.CjSnatchStageStatusText;
        IsNextSnatchHighlighted = snapshot.IsNextSnatchHighlighted;

        LiveCjBreakRemainingDisplay = snapshot.CjBreakRemainingDisplay;
        _liveCjBreakRemainingSeconds = snapshot.CjBreakRemainingSeconds;
        LiveCjBreakProgress = snapshot.CjBreakProgress;
        IsBreakActive = snapshot.IsBreakActive;
        ActiveStepOrder = snapshot.ActiveStepOrder;

        bool snatchCompletedNow = snapshot.LiftType == LiftType.Snatch
            ? snapshot.HasCompetitionStarted
            : snapshot.CjSnatchLiftsOut <= 0;
        bool breakCompletedNow = snatchCompletedNow && snapshot.CjBreakRemainingSeconds <= 0;

        if (snatchCompletedNow != _isCjSnatchPhaseCompleted || !snatchCompletedNow)
        {
            _isCjSnatchPhaseCompleted = snatchCompletedNow;
            _isCjSnatchManuallyExpanded = false;
        }

        if (breakCompletedNow != _isCjBreakPhaseCompleted || !breakCompletedNow)
        {
            _isCjBreakPhaseCompleted = breakCompletedNow;
            _isCjBreakManuallyExpanded = false;
        }

        NotifyCjPhaseExpansionChanged();

        foreach (var stepSnap in snapshot.Steps)
        {
            var existing = LiveSteps.FirstOrDefault(s => s.Order == stepSnap.Order);
            existing?.UpdateFromSnapshot(stepSnap);
        }

        var activeStep = snapshot.Steps.FirstOrDefault(s => s.Order == snapshot.ActiveStepOrder)
                         ?? snapshot.Steps.FirstOrDefault(s => s.Status == StepRunStatus.Active);
        if (activeStep != null)
        {
            if (activeStep.IsPreStep)
            {
                LiveActiveStepDescription = "Wait to start";
            }
            else if (activeStep.IsOpener)
            {
                LiveActiveStepDescription = $"Open at {activeStep.WeightKg} kg";
            }
            else if (activeStep.IsLift && activeStep.WeightKg > 0)
            {
                LiveActiveStepDescription = $"Lift {activeStep.WeightKg} kg";
            }
            else
            {
                LiveActiveStepDescription = activeStep.ActionTitle;
            }

            LiveActiveStepRemaining = activeStep.CountdownBadgeText;
            LiveActiveStepProgress = activeStep.ProgressFraction;
        }
        else
        {
            LiveActiveStepDescription = CurrentLiftTitle;
            LiveActiveStepRemaining = snapshot.TimeToOpenDisplay;
            LiveActiveStepProgress = 0.0;
        }

        if (snapshot.TriggerBeep)
        {
            _platformService.PlayStepTransitionBeep();
        }
    }

    private void ShowBanner(string message)
    {
        StatusBannerText = message;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
