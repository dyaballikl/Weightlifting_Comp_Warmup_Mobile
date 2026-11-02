#if ANDROID
using System;
using Android.App;
using Android.Content.PM;
using Android.Content.Res;
using Android.OS;
using Android.Views;
using AndroidX.Activity;
using AndroidX.Core.View;
using WeightliftingCompWarmup.Maui.Services;

namespace WeightliftingCompWarmup.Maui;

[Activity(
    Label = "Comp OWL",
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
    ResizeableActivity = true,
    WindowSoftInputMode = SoftInput.AdjustResize,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode |
                           ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        EdgeToEdge.Enable(this);
        base.OnCreate(savedInstanceState);

        var contentView = Window?.DecorView?.FindViewById( global::Android.Resource.Id.Content );
        if (contentView != null)
        {
            ViewCompat.SetOnApplyWindowInsetsListener(contentView, new EdgeToEdgeAndImeInsetsListener());
        }
    }

    private sealed class EdgeToEdgeAndImeInsetsListener : Java.Lang.Object, IOnApplyWindowInsetsListener
    {
        public WindowInsetsCompat? OnApplyWindowInsets(global::Android.Views.View? v, WindowInsetsCompat? insets)
        {
            if (v == null || insets == null)
            {
                return insets;
            }

            var systemBars = insets.GetInsets(WindowInsetsCompat.Type.SystemBars());
            var ime = insets.GetInsets(WindowInsetsCompat.Type.Ime());
            if (systemBars != null && ime != null)
            {
                int bottomInset = Math.Max(systemBars.Bottom, ime.Bottom);
                v.SetPadding(systemBars.Left, systemBars.Top, systemBars.Right, bottomInset);
            }

            return insets;
        }
    }

    protected override void OnUserLeaveHint()
    {
        base.OnUserLeaveHint();
        DevicePlatformService.TryEnterMiniTimerPip(this);
    }

    protected override void OnStop()
    {
        base.OnStop();
        DevicePlatformService.OnAndroidActivityStopped(this);
    }

    protected override void OnResume()
    {
        base.OnResume();
        AndroidFloatingTimerOverlay.Shared.Hide();
        AppThemeManager.RefreshFromSystemTheme();
        AppThemeManager.UpdatePlatformStatusBar();
    }

    public override void OnConfigurationChanged(Configuration newConfig)
    {
        base.OnConfigurationChanged(newConfig);
        AppThemeManager.RefreshFromSystemTheme();
    }
}
#endif
