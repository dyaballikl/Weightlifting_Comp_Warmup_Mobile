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
    SupportsPictureInPicture = true,
    ResizeableActivity = true,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode |
                           ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
[Layout(
    DefaultWidth = "144dp",
    DefaultHeight = "60dp",
    MinWidth = "120dp",
    MinHeight = "50dp",
    Gravity = "bottom|end")]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnUserLeaveHint()
    {
        base.OnUserLeaveHint();
        DevicePlatformService.TryEnterMiniTimerPip(this);
    }

    public override void OnPictureInPictureModeChanged(bool isInPictureInPictureMode, Configuration? newConfig)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            base.OnPictureInPictureModeChanged(isInPictureInPictureMode, newConfig);
        }
        DevicePlatformService.NotifyPipModeChanged(isInPictureInPictureMode);
    }

    protected override void OnResume()
    {
        base.OnResume();
        if (OperatingSystem.IsAndroidVersionAtLeast(26) && !IsInPictureInPictureMode)
        {
            DevicePlatformService.NotifyPipModeChanged(false);
        }
    }
}
#endif
