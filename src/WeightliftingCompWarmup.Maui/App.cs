using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.PlatformConfiguration.AndroidSpecific;
using WeightliftingCompWarmup.Maui.Services;
using WeightliftingCompWarmup.Maui.Views;

namespace WeightliftingCompWarmup.Maui;

public class App : Microsoft.Maui.Controls.Application
{
    private readonly MainPage _mainPage;

    public App(MainPage mainPage)
    {
        _mainPage = mainPage;
        this.On<Microsoft.Maui.Controls.PlatformConfiguration.Android>()
            .UseWindowSoftInputModeAdjust(WindowSoftInputModeAdjust.Resize);
        AppThemeManager.Initialize(AppThemeManager.CurrentPreference);
        RequestedThemeChanged += (_, _) => AppThemeManager.RefreshFromSystemTheme();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new NavigationPage(_mainPage))
        {
            Title = "Comp OWL — Weightlifting Comp Warmup"
        };

        bool wasBackgrounded = false;
        window.Stopped += (_, _) => wasBackgrounded = true;
        window.Resumed += (_, _) =>
        {
            wasBackgrounded = false;
            AppThemeManager.RefreshFromSystemTheme();
            _mainPage.CheckForAppUpdate();
        };
        window.Activated += (_, _) =>
        {
            AppThemeManager.UpdatePlatformStatusBar();
            if (wasBackgrounded)
            {
                wasBackgrounded = false;
                AppThemeManager.RefreshFromSystemTheme();
                _mainPage.CheckForAppUpdate();
            }
        };

        return window;
    }
}
