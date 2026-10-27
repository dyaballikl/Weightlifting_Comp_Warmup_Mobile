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
using AApplication = Android.App.Application;
using AColor = Android.Graphics.Color;
using AView = Android.Views.View;

namespace WeightliftingCompWarmup.Maui.Services;

/// <summary>
/// Renders a compact, draggable, pill-shaped floating mini-timer overlay over the Android home screen
/// (matching the size and capsule style of Samsung Clock's floating mini-timer) using
/// <see cref="IWindowManager"/> and <see cref="WindowManagerTypes.ApplicationOverlay"/>.
/// </summary>
public sealed class AndroidFloatingTimerOverlay
{
    private const int PillWidthDp = 172;
    private const int PillHeightDp = 58;
    private const int PillCornerRadiusDp = 29;
    private const int ProgressBarTrackWidthDp = 112;
    private const int ProgressBarHeightDp = 3;

    public static AndroidFloatingTimerOverlay Shared { get; } = new();

    private readonly Handler _mainHandler = new(Looper.MainLooper!);
    private IWindowManager? _windowManager;
    private WindowManagerLayoutParams? _layoutParams;
    private LinearLayout? _rootPillView;
    private TextView? _stepTextView;
    private TextView? _remainingTextView;
    private AView? _progressFillView;

    private string _stepDescription = "Wait to start";
    private string _remainingText = "0s";
    private double _progressFraction;
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

    public void UpdateState(string stepDescription, string remainingText, double progressFraction)
    {
        _stepDescription = string.IsNullOrWhiteSpace(stepDescription) ? "Live Warmup" : stepDescription.Trim();
        _remainingText = string.IsNullOrWhiteSpace(remainingText) ? "0s" : remainingText.Trim();
        _progressFraction = Math.Clamp(progressFraction, 0.0, 1.0);

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

            float density = ctx.Resources?.DisplayMetrics?.Density ?? 2.75f;
            int widthPx = DpToPx(PillWidthDp, density);
            int heightPx = DpToPx(PillHeightDp, density);
            _trackWidthPx = DpToPx(ProgressBarTrackWidthDp, density);

            var windowType = OperatingSystem.IsAndroidVersionAtLeast(26)
                ? WindowManagerTypes.ApplicationOverlay
                : WindowManagerTypes.Phone;

            _layoutParams = new WindowManagerLayoutParams(
                widthPx,
                heightPx,
                windowType,
                WindowManagerFlags.NotFocusable |
                WindowManagerFlags.NotTouchModal |
                WindowManagerFlags.LayoutInScreen,
                Format.Translucent)
            {
                Gravity = GravityFlags.Bottom | GravityFlags.End,
                X = DpToPx(16, density),
                Y = DpToPx(92, density)
            };

            _rootPillView = BuildPillView(ctx, density);
            ApplyTextAndProgressToViews();

            _windowManager.AddView(_rootPillView, _layoutParams);
            _isShowing = true;
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
            _stepTextView = null;
            _remainingTextView = null;
            _progressFillView = null;
        }
    }

    private LinearLayout BuildPillView(Context ctx, float density)
    {
        var pillBg = new GradientDrawable();
        pillBg.SetShape(ShapeType.Rectangle);
        pillBg.SetCornerRadius(DpToPx(PillCornerRadiusDp, density));
        pillBg.SetColor(AColor.ParseColor("#F2111827"));

        var root = new LinearLayout(ctx)
        {
            Orientation = Orientation.Horizontal,
            Background = pillBg,
            Elevation = DpToPx(8, density)
        };
        root.SetGravity(GravityFlags.CenterVertical);
        root.SetPadding(
            DpToPx(14, density),
            DpToPx(6, density),
            DpToPx(10, density),
            DpToPx(6, density));

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
        _stepTextView.SetTextColor(AColor.ParseColor("#E2E8F0"));
        _stepTextView.SetTextSize(ComplexUnitType.Dip, 11.5f);
        _stepTextView.SetIncludeFontPadding(false);

        _remainingTextView = new TextView(ctx)
        {
            Text = _remainingText,
            Typeface = Typeface.DefaultBold
        };
        _remainingTextView.SetSingleLine(true);
        _remainingTextView.SetTextColor(AColor.ParseColor("#FACC15"));
        _remainingTextView.SetTextSize(ComplexUnitType.Dip, 17.5f);
        _remainingTextView.SetIncludeFontPadding(false);

        int barHeightPx = Math.Max(2, DpToPx(ProgressBarHeightDp, density));
        float barRadiusPx = barHeightPx / 2f;

        var trackBg = new GradientDrawable();
        trackBg.SetShape(ShapeType.Rectangle);
        trackBg.SetCornerRadius(barRadiusPx);
        trackBg.SetColor(AColor.ParseColor("#334155"));

        var progressTrack = new FrameLayout(ctx)
        {
            Background = trackBg
        };
        var trackParams = new LinearLayout.LayoutParams(_trackWidthPx, barHeightPx)
        {
            TopMargin = DpToPx(3, density)
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

        textColumn.AddView(_stepTextView);
        textColumn.AddView(_remainingTextView);
        textColumn.AddView(progressTrack, trackParams);

        int closeSizePx = DpToPx(26, density);
        var closeBg = new GradientDrawable();
        closeBg.SetShape(ShapeType.Oval);
        closeBg.SetColor(AColor.ParseColor("#26FFFFFF"));

        var closeBtn = new TextView(ctx)
        {
            Text = "✕",
            Typeface = Typeface.DefaultBold,
            Gravity = GravityFlags.Center,
            Background = closeBg
        };
        closeBtn.SetTextColor(AColor.ParseColor("#CBD5E1"));
        closeBtn.SetTextSize(ComplexUnitType.Dip, 11.5f);
        closeBtn.SetIncludeFontPadding(false);

        var closeParams = new LinearLayout.LayoutParams(closeSizePx, closeSizePx)
        {
            LeftMargin = DpToPx(6, density)
        };

        closeBtn.Click += (_, _) =>
        {
            Hide();
        };

        root.AddView(textColumn, textColParams);
        root.AddView(closeBtn, closeParams);

        root.SetOnTouchListener(new PillDragTouchListener(this, density));
        return root;
    }

    private void ApplyTextAndProgressToViews()
    {
        if (!_isShowing)
        {
            return;
        }

        try
        {
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
        }
        catch
        {
            // Ignore transient UI update errors
        }
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

            Hide();

            var intent = new Intent(ctx, typeof(MainActivity));
            intent.AddFlags(ActivityFlags.NewTask | ActivityFlags.SingleTop | ActivityFlags.ReorderToFront);
            ctx.StartActivity(intent);
        }
        catch
        {
            // Ignore if activity launch fails
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
