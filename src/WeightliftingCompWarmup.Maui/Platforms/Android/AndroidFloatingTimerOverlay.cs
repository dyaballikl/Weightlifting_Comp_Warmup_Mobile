#if ANDROID
using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Provider;
using Android.Runtime;
using Android.Text;
using Android.Util;
using Android.Views;
using Android.Widget;
using WeightliftingCompWarmup.Core.Services;
using AApplication = Android.App.Application;
using AColor = Android.Graphics.Color;
using AView = Android.Views.View;

namespace WeightliftingCompWarmup.Maui.Services;

/// <summary>
/// Renders a compact, draggable floating mini-timer overlay on Android using
/// <see cref="IWindowManager"/> and <see cref="WindowManagerTypes.ApplicationOverlay"/>.
/// Displays the active step description, remaining countdown, simplified IWF plate stack
/// (when the active step is a lift), and progress bar.
/// </summary>
public sealed class AndroidFloatingTimerOverlay
{
    private const int PillWidthDp = 228;
    private const int PillCornerRadiusDp = 18;
    private const int ProgressBarTrackWidthDp = 200;
    private const int ProgressBarHeightDp = 4;

    // Plate rectangle dimensions: big plates are single-width (8dp) normal-height (28dp);
    // change plates are half-width (4dp) and 2/3 height (~19dp).
    private const float BigPlateWidthDp = 8.0f;
    private const float BigPlateHeightDp = 28.0f;
    private const float ChangePlateWidthDp = 4.0f;
    private const float ChangePlateHeightDp = 18.67f;

    public static AndroidFloatingTimerOverlay Shared { get; } = new();

    private readonly Handler _mainHandler = new(Looper.MainLooper!);
    private IWindowManager? _windowManager;
    private WindowManagerLayoutParams? _layoutParams;
    private LinearLayout? _rootPillView;
    private GradientDrawable? _pillBgDrawable;
    private TextView? _compStatusTextView;
    private TextView? _advanceBtnView;
    private GradientDrawable? _closeBgDrawable;
    private TextView? _closeBtnView;
    private AView? _dividerView;
    private TextView? _stepTextView;
    private TextView? _remainingTextView;
    private LinearLayout? _platesRow;
    private GradientDrawable? _trackBgDrawable;
    private AView? _progressFillView;

    private string _compStatusText = "Live Warmup";
    private bool _canAdvanceStage;
    private string _advanceButtonText = "⏭ Advance";
    private string _stepDescription = "Wait to start";
    private string _remainingText = "0s";
    private double _progressFraction;
    private int _liftWeightKg;
    private int _barWeightKg = 20;
    private int _lastRenderedLiftWeightKg = -1;
    private int _lastRenderedBarWeightKg = -1;
    private bool _lastRenderedSystemLightTheme;
    private float _density = 2.75f;
    private bool _isShowing;
    private bool _tickScheduled;
    private int _trackWidthPx;

    private AndroidFloatingTimerOverlay()
    {
    }

    public bool IsShowing => _isShowing;

    public static bool CanDrawOverlays(Context? context = null)
    {
        try
        {
            var ctx = context ?? AApplication.Context;
            if (ctx == null)
            {
                return false;
            }

            return Settings.CanDrawOverlays(ctx);
        }
        catch
        {
            return false;
        }
    }

    public void UpdateState(
        string stepDescription,
        string remainingText,
        double progressFraction,
        int liftWeightKg = 0,
        int barWeightKg = 20,
        string compStatusText = "",
        bool canAdvanceStage = false,
        string advanceButtonText = "⏭ Advance")
    {
        _stepDescription = string.IsNullOrWhiteSpace(stepDescription) ? "Live Warmup" : stepDescription.Trim();
        _remainingText = string.IsNullOrWhiteSpace(remainingText) ? "0s" : remainingText.Trim();
        _progressFraction = Math.Clamp(progressFraction, 0.0, 1.0);
        _liftWeightKg = Math.Max(0, liftWeightKg);
        _barWeightKg = barWeightKg > 0 ? barWeightKg : 20;
        _compStatusText = string.IsNullOrWhiteSpace(compStatusText) ? "Live Warmup" : compStatusText.Trim();
        _canAdvanceStage = canAdvanceStage;
        _advanceButtonText = string.IsNullOrWhiteSpace(advanceButtonText) ? "⏭ Advance" : advanceButtonText.Trim();

        if (!_isShowing)
        {
            return;
        }

        _mainHandler.Post(ApplyTextAndProgressToViews);
    }

    public bool Show(Context? context = null)
    {
        var ctx = context?.ApplicationContext ?? AApplication.Context;
        if (ctx == null || !CanDrawOverlays(ctx))
        {
            return false;
        }

        if (_isShowing && _rootPillView != null)
        {
            ApplyTextAndProgressToViews();
            return true;
        }

        try
        {
            var wmObj = ctx.GetSystemService(Context.WindowService);
            _windowManager = wmObj.JavaCast<IWindowManager>();
            if (_windowManager == null)
            {
                return false;
            }

            _density = ctx.Resources?.DisplayMetrics?.Density ?? 2.75f;
            int widthPx = DpToPx(PillWidthDp, _density);
            _trackWidthPx = DpToPx(ProgressBarTrackWidthDp, _density);
            _lastRenderedLiftWeightKg = -1;
            _lastRenderedBarWeightKg = -1;

            var windowType = OperatingSystem.IsAndroidVersionAtLeast(26)
                ? WindowManagerTypes.ApplicationOverlay
                : WindowManagerTypes.Phone;

            _layoutParams = new WindowManagerLayoutParams(
                widthPx,
                ViewGroup.LayoutParams.WrapContent,
                windowType,
                WindowManagerFlags.NotFocusable |
                WindowManagerFlags.NotTouchModal |
                WindowManagerFlags.LayoutInScreen,
                Format.Translucent)
            {
                Gravity = GravityFlags.Bottom | GravityFlags.End,
                X = DpToPx(16, _density),
                Y = DpToPx(92, _density)
            };

            _rootPillView = BuildPillView(ctx, _density);
            _isShowing = true;
            ApplyTextAndProgressToViews();

            _windowManager.AddView(_rootPillView, _layoutParams);
            StartBackgroundTickLoop();
            return true;
        }
        catch
        {
            _isShowing = false;
            _rootPillView = null;
            return false;
        }
    }

    public void Hide()
    {
        StopBackgroundTickLoop();

        if (!_isShowing && _rootPillView == null)
        {
            return;
        }

        try
        {
            if (_windowManager != null && _rootPillView != null)
            {
                _windowManager.RemoveViewImmediate(_rootPillView);
            }
        }
        catch
        {
            // Ignore if view was already detached
        }
        finally
        {
            _isShowing = false;
            _rootPillView = null;
            _pillBgDrawable = null;
            _compStatusTextView = null;
            _advanceBtnView = null;
            _closeBgDrawable = null;
            _closeBtnView = null;
            _dividerView = null;
            _stepTextView = null;
            _remainingTextView = null;
            _platesRow = null;
            _trackBgDrawable = null;
            _progressFillView = null;
            _lastRenderedLiftWeightKg = -1;
            _lastRenderedBarWeightKg = -1;
        }
    }

    public void RefreshSystemTheme()
    {
        if (!_isShowing)
        {
            return;
        }

        _mainHandler.Post(() =>
        {
            if (!_isShowing)
            {
                return;
            }

            bool isSystemLight = AppThemeManager.IsSystemLightTheme;
            _lastRenderedSystemLightTheme = isSystemLight;
            ApplyThemeToViews(isSystemLight);
        });
    }

    private LinearLayout BuildPillView(Context ctx, float density)
    {
        _pillBgDrawable = new GradientDrawable();
        _pillBgDrawable.SetShape(ShapeType.Rectangle);
        _pillBgDrawable.SetCornerRadius(DpToPx(PillCornerRadiusDp, density));

        var root = new LinearLayout(ctx)
        {
            Orientation = Orientation.Vertical,
            Background = _pillBgDrawable,
            Elevation = DpToPx(8, density)
        };
        root.SetPadding(
            DpToPx(12, density),
            DpToPx(8, density),
            DpToPx(10, density),
            DpToPx(8, density));

        // 1. Upper Section: Competition Status + Advance Lift Button + Close Button
        var upperRow = new LinearLayout(ctx)
        {
            Orientation = Orientation.Horizontal
        };
        upperRow.SetGravity(GravityFlags.CenterVertical);
        var upperRowParams = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.WrapContent);

        _compStatusTextView = new TextView(ctx)
        {
            Text = _compStatusText,
            Typeface = Typeface.DefaultBold,
            Ellipsize = TextUtils.TruncateAt.End
        };
        _compStatusTextView.SetSingleLine(true);
        _compStatusTextView.SetTextSize(ComplexUnitType.Dip, 11.0f);
        _compStatusTextView.SetIncludeFontPadding(false);
        var compStatusParams = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f);

        var advanceBg = new GradientDrawable();
        advanceBg.SetShape(ShapeType.Rectangle);
        advanceBg.SetCornerRadius(DpToPx(6, density));
        advanceBg.SetColor(AColor.ParseColor("#0284C7"));

        _advanceBtnView = new TextView(ctx)
        {
            Text = _advanceButtonText,
            Typeface = Typeface.DefaultBold,
            Gravity = GravityFlags.Center,
            Background = advanceBg,
            Visibility = _canAdvanceStage ? ViewStates.Visible : ViewStates.Gone
        };
        _advanceBtnView.SetSingleLine(true);
        _advanceBtnView.SetTextColor(AColor.ParseColor("#FFFFFF"));
        _advanceBtnView.SetTextSize(ComplexUnitType.Dip, 10.5f);
        _advanceBtnView.SetIncludeFontPadding(false);
        _advanceBtnView.SetPadding(
            DpToPx(7, density),
            DpToPx(4, density),
            DpToPx(7, density),
            DpToPx(4, density));
        var advanceParams = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.WrapContent,
            ViewGroup.LayoutParams.WrapContent)
        {
            LeftMargin = DpToPx(5, density)
        };
        _advanceBtnView.Click += (_, _) =>
        {
            DevicePlatformService.RaiseOverlayAdvanceRequested();
        };

        int closeSizePx = DpToPx(22, density);
        _closeBgDrawable = new GradientDrawable();
        _closeBgDrawable.SetShape(ShapeType.Oval);

        _closeBtnView = new TextView(ctx)
        {
            Text = "✕",
            Typeface = Typeface.DefaultBold,
            Gravity = GravityFlags.Center,
            Background = _closeBgDrawable
        };
        _closeBtnView.SetTextSize(ComplexUnitType.Dip, 10.5f);
        _closeBtnView.SetIncludeFontPadding(false);

        var closeParams = new LinearLayout.LayoutParams(closeSizePx, closeSizePx)
        {
            LeftMargin = DpToPx(6, density)
        };

        _closeBtnView.Click += (_, _) =>
        {
            Hide();
        };

        upperRow.AddView(_compStatusTextView, compStatusParams);
        upperRow.AddView(_advanceBtnView, advanceParams);
        upperRow.AddView(_closeBtnView, closeParams);

        // Divider between Competition Status and Active Warmup Step
        _dividerView = new AView(ctx);
        var dividerParams = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            Math.Max(1, DpToPx(1, density)))
        {
            TopMargin = DpToPx(5, density),
            BottomMargin = DpToPx(5, density)
        };

        // 2. Lower Section: Step/Countdown text on the left, loaded plate rectangles on the right, progress bar below
        var topRow = new LinearLayout(ctx)
        {
            Orientation = Orientation.Horizontal
        };
        topRow.SetGravity(GravityFlags.CenterVertical);
        var topRowParams = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.WrapContent);

        var textColumn = new LinearLayout(ctx)
        {
            Orientation = Orientation.Vertical
        };
        textColumn.SetGravity(GravityFlags.CenterVertical);
        var textColParams = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f);

        _stepTextView = new TextView(ctx)
        {
            Text = _stepDescription,
            Typeface = Typeface.DefaultBold,
            Ellipsize = TextUtils.TruncateAt.End
        };
        _stepTextView.SetSingleLine(true);
        _stepTextView.SetTextSize(ComplexUnitType.Dip, 12.0f);
        _stepTextView.SetIncludeFontPadding(false);

        _remainingTextView = new TextView(ctx)
        {
            Text = _remainingText,
            Typeface = Typeface.DefaultBold
        };
        _remainingTextView.SetSingleLine(true);
        _remainingTextView.SetTextSize(ComplexUnitType.Dip, 18.0f);
        _remainingTextView.SetIncludeFontPadding(false);

        textColumn.AddView(_stepTextView);
        textColumn.AddView(_remainingTextView);

        _platesRow = new LinearLayout(ctx)
        {
            Orientation = Orientation.Horizontal,
            Visibility = ViewStates.Gone
        };
        _platesRow.SetGravity(GravityFlags.CenterVertical);
        var platesRowParams = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.WrapContent,
            ViewGroup.LayoutParams.WrapContent)
        {
            LeftMargin = DpToPx(6, density)
        };

        topRow.AddView(textColumn, textColParams);
        topRow.AddView(_platesRow, platesRowParams);

        int barHeightPx = Math.Max(2, DpToPx(ProgressBarHeightDp, density));
        float barRadiusPx = barHeightPx / 2f;

        _trackBgDrawable = new GradientDrawable();
        _trackBgDrawable.SetShape(ShapeType.Rectangle);
        _trackBgDrawable.SetCornerRadius(barRadiusPx);

        var progressTrack = new FrameLayout(ctx)
        {
            Background = _trackBgDrawable
        };
        var trackParams = new LinearLayout.LayoutParams(_trackWidthPx, barHeightPx)
        {
            TopMargin = DpToPx(4, density)
        };

        var fillBg = new GradientDrawable();
        fillBg.SetShape(ShapeType.Rectangle);
        fillBg.SetCornerRadius(barRadiusPx);
        fillBg.SetColor(AColor.ParseColor("#16A34A"));

        _progressFillView = new AView(ctx)
        {
            Background = fillBg
        };
        var fillParams = new FrameLayout.LayoutParams(0, barHeightPx)
        {
            Gravity = GravityFlags.Start | GravityFlags.CenterVertical
        };
        progressTrack.AddView(_progressFillView, fillParams);

        root.AddView(upperRow, upperRowParams);
        root.AddView(_dividerView, dividerParams);
        root.AddView(topRow, topRowParams);
        root.AddView(progressTrack, trackParams);

        _lastRenderedSystemLightTheme = AppThemeManager.IsSystemLightTheme;
        ApplyThemeToViews(_lastRenderedSystemLightTheme);

        root.SetOnTouchListener(new PillDragTouchListener(this, density));
        return root;
    }

    private void ApplyThemeToViews(bool isSystemLight)
    {
        try
        {
            if (_pillBgDrawable != null)
            {
                _pillBgDrawable.SetColor(AColor.ParseColor(isSystemLight ? "#F7FFFFFF" : "#F50F172A"));
                _pillBgDrawable.SetStroke(
                    Math.Max(1, DpToPx(1.2f, _density)),
                    AColor.ParseColor(isSystemLight ? "#94A3B8" : "#334155"));
            }

            _compStatusTextView?.SetTextColor(AColor.ParseColor(isSystemLight ? "#0284C7" : "#38BDF8"));

            if (_closeBgDrawable != null)
            {
                _closeBgDrawable.SetColor(AColor.ParseColor(isSystemLight ? "#E2E8F0" : "#26FFFFFF"));
            }

            _closeBtnView?.SetTextColor(AColor.ParseColor(isSystemLight ? "#334155" : "#CBD5E1"));
            _dividerView?.SetBackgroundColor(AColor.ParseColor(isSystemLight ? "#CBD5E1" : "#334155"));
            _stepTextView?.SetTextColor(AColor.ParseColor(isSystemLight ? "#0F172A" : "#E2E8F0"));
            _remainingTextView?.SetTextColor(AColor.ParseColor(isSystemLight ? "#B45309" : "#FACC15"));

            if (_trackBgDrawable != null)
            {
                _trackBgDrawable.SetColor(AColor.ParseColor(isSystemLight ? "#CBD5E1" : "#334155"));
            }
        }
        catch
        {
            // Ignore transient styling errors
        }
    }

    private void ApplyTextAndProgressToViews()
    {
        if (!_isShowing)
        {
            return;
        }

        try
        {
            bool isSystemLight = AppThemeManager.IsSystemLightTheme;
            if (isSystemLight != _lastRenderedSystemLightTheme)
            {
                _lastRenderedSystemLightTheme = isSystemLight;
                ApplyThemeToViews(isSystemLight);
            }

            if (_compStatusTextView != null && _compStatusTextView.Text != _compStatusText)
            {
                _compStatusTextView.Text = _compStatusText;
            }

            if (_advanceBtnView != null)
            {
                var desiredVisibility = _canAdvanceStage ? ViewStates.Visible : ViewStates.Gone;
                if (_advanceBtnView.Visibility != desiredVisibility)
                {
                    _advanceBtnView.Visibility = desiredVisibility;
                }

                if (_advanceBtnView.Text != _advanceButtonText)
                {
                    _advanceBtnView.Text = _advanceButtonText;
                }
            }

            if (_stepTextView != null && _stepTextView.Text != _stepDescription)
            {
                _stepTextView.Text = _stepDescription;
            }

            if (_remainingTextView != null && _remainingTextView.Text != _remainingText)
            {
                _remainingTextView.Text = _remainingText;
            }

            if (_progressFillView != null && _trackWidthPx > 0)
            {
                int targetWidth = (int)Math.Round(_trackWidthPx * _progressFraction);
                var lp = _progressFillView.LayoutParameters;
                if (lp != null && lp.Width != targetWidth)
                {
                    lp.Width = targetWidth;
                    _progressFillView.LayoutParameters = lp;
                }
            }

            if (_liftWeightKg != _lastRenderedLiftWeightKg || _barWeightKg != _lastRenderedBarWeightKg)
            {
                _lastRenderedLiftWeightKg = _liftWeightKg;
                _lastRenderedBarWeightKg = _barWeightKg;
                RebuildPlateViews();
            }
        }
        catch
        {
            // Ignore transient UI update errors
        }
    }

    private void RebuildPlateViews()
    {
        if (_platesRow == null)
        {
            return;
        }

        var ctx = _platesRow.Context;
        if (ctx == null)
        {
            return;
        }

        _platesRow.RemoveAllViews();

        if (_liftWeightKg <= 0 || _barWeightKg <= 0 || _liftWeightKg <= _barWeightKg)
        {
            _platesRow.Visibility = ViewStates.Gone;
            return;
        }

        var loadout = BarbellLoadCalculator.Calculate(_liftWeightKg, _barWeightKg, isOpener: false);
        var plates = loadout.SleeveElements.Where(e => !e.IsCollar).ToList();
        if (plates.Count == 0)
        {
            _platesRow.Visibility = ViewStates.Gone;
            return;
        }

        // Slightly compact width if 5+ plates per side are loaded so it always fits comfortably
        float widthScale = plates.Count >= 5 ? 0.82f : 1.0f;
        int bigW = Math.Max(4, DpToPx(BigPlateWidthDp * widthScale, _density));
        int bigH = DpToPx(BigPlateHeightDp, _density);
        int changeW = Math.Max(2, DpToPx(ChangePlateWidthDp * widthScale, _density));
        int changeH = DpToPx(ChangePlateHeightDp, _density);
        int gapPx = Math.Max(1, DpToPx(1.2f, _density));
        int strokePx = Math.Max(1, DpToPx(0.8f, _density));

        for (int i = 0; i < plates.Count; i++)
        {
            var plate = plates[i];
            bool isBigPlate = plate.WeightKg >= 10.0m || plate.HeightRatio >= 0.99;

            int w = isBigPlate ? bigW : changeW;
            int h = isBigPlate ? bigH : changeH;
            float radiusPx = DpToPx(isBigPlate ? 2.0f : 1.5f, _density);

            var plateDrawable = new GradientDrawable();
            plateDrawable.SetShape(ShapeType.Rectangle);
            plateDrawable.SetCornerRadius(radiusPx);
            plateDrawable.SetColor(AColor.ParseColor(plate.HexColor));
            plateDrawable.SetStroke(strokePx, AColor.ParseColor(plate.BorderHexColor));

            var plateView = new AView(ctx)
            {
                Background = plateDrawable
            };

            var plateParams = new LinearLayout.LayoutParams(w, h)
            {
                Gravity = GravityFlags.CenterVertical,
                LeftMargin = i > 0 ? gapPx : 0
            };

            _platesRow.AddView(plateView, plateParams);
        }

        _platesRow.Visibility = ViewStates.Visible;
    }

    private void UpdateDragOffset(int deltaRawX, int deltaRawY, int startX, int startY)
    {
        if (!_isShowing || _windowManager == null || _rootPillView == null || _layoutParams == null)
        {
            return;
        }

        try
        {
            // Because Gravity is Bottom | End, X is distance from right edge and Y is distance from bottom edge
            _layoutParams.X = Math.Max(0, startX - deltaRawX);
            _layoutParams.Y = Math.Max(0, startY - deltaRawY);
            _windowManager.UpdateViewLayout(_rootPillView, _layoutParams);
        }
        catch
        {
            // Ignore layout update errors during teardown
        }
    }

    private void BringAppToForeground()
    {
        try
        {
            var ctx = AApplication.Context;
            if (ctx == null)
            {
                return;
            }

            var intent = ctx.PackageManager?.GetLaunchIntentForPackage(ctx.PackageName ?? "com.weightlifting.compwarmup")
                         ?? new Intent(ctx, typeof(MainActivity));
            intent.SetAction(Intent.ActionMain);
            intent.AddCategory(Intent.CategoryLauncher);
            intent.AddFlags(
                ActivityFlags.NewTask |
                ActivityFlags.SingleTop |
                ActivityFlags.ReorderToFront |
                ActivityFlags.ResetTaskIfNeeded);

            // Launch while the overlay window is still visible so Android 10-15 grants immediate foregrounding
            bool launched = false;
            if (OperatingSystem.IsAndroidVersionAtLeast(34))
            {
                try
                {
                    using var options = ActivityOptions.MakeBasic();
                    options?.SetPendingIntentBackgroundActivityStartMode(BackgroundActivityStartMode.Allowed);
                    using var pendingIntent = PendingIntent.GetActivity(
                        ctx,
                        0,
                        intent,
                        PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
                    if (pendingIntent != null)
                    {
                        pendingIntent.Send(ctx, 0, null, null, null, null, options?.ToBundle());
                        launched = true;
                    }
                }
                catch
                {
                    launched = false;
                }
            }

            if (!launched)
            {
                ctx.StartActivity(intent);
            }

            _mainHandler.PostDelayed(Hide, 180);
        }
        catch
        {
            Hide();
        }
    }

    private void StartBackgroundTickLoop()
    {
        if (_tickScheduled)
        {
            return;
        }

        _tickScheduled = true;
        ScheduleNextTick();
    }

    private void StopBackgroundTickLoop()
    {
        _tickScheduled = false;
        _mainHandler.RemoveCallbacksAndMessages(null);
    }

    private void ScheduleNextTick()
    {
        if (!_tickScheduled || !_isShowing)
        {
            return;
        }

        _mainHandler.PostDelayed(() =>
        {
            if (!_tickScheduled || !_isShowing)
            {
                return;
            }

            if (!DevicePlatformService.IsLiveMiniTimerActive)
            {
                Hide();
                return;
            }

            DevicePlatformService.RaiseBackgroundOverlayTick();
            ScheduleNextTick();
        }, 1000);
    }

    private static int DpToPx(float dp, float density)
        => (int)Math.Round(dp * density);

    private sealed class PillDragTouchListener : Java.Lang.Object, AView.IOnTouchListener
    {
        private readonly AndroidFloatingTimerOverlay _owner;
        private readonly float _touchSlopPx;
        private int _initialParamX;
        private int _initialParamY;
        private float _downRawX;
        private float _downRawY;
        private bool _isDragging;

        public PillDragTouchListener(AndroidFloatingTimerOverlay owner, float density)
        {
            _owner = owner;
            _touchSlopPx = 8f * density;
        }

        public bool OnTouch(AView? v, MotionEvent? e)
        {
            if (e == null || _owner._layoutParams == null)
            {
                return false;
            }

            switch (e.ActionMasked)
            {
                case MotionEventActions.Down:
                    _initialParamX = _owner._layoutParams.X;
                    _initialParamY = _owner._layoutParams.Y;
                    _downRawX = e.RawX;
                    _downRawY = e.RawY;
                    _isDragging = false;
                    return true;

                case MotionEventActions.Move:
                    float dx = e.RawX - _downRawX;
                    float dy = e.RawY - _downRawY;
                    if (!_isDragging && (Math.Abs(dx) > _touchSlopPx || Math.Abs(dy) > _touchSlopPx))
                    {
                        _isDragging = true;
                    }

                    if (_isDragging)
                    {
                        _owner.UpdateDragOffset((int)Math.Round(dx), (int)Math.Round(dy), _initialParamX, _initialParamY);
                    }
                    return true;

                case MotionEventActions.Up:
                    if (!_isDragging)
                    {
                        _owner.BringAppToForeground();
                    }
                    return true;
            }

            return false;
        }
    }
}
#endif
