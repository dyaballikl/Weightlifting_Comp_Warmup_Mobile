#if ANDROID
using Android.App;
using Android.Content.PM;
using Android.Content.Res;
using WeightliftingCompWarmup.Maui.Services;

namespace WeightliftingCompWarmup.Maui;

[Activity(
    Label = "Comp OWL",
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
    ResizeableActivity = true,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode |
                           ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
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
    }
}
#endif
