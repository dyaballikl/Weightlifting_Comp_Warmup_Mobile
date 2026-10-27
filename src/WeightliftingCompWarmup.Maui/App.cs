using Microsoft.Maui;
using Microsoft.Maui.Controls;
using WeightliftingCompWarmup.Maui.Views;

namespace WeightliftingCompWarmup.Maui;

public class App : Application
{
    private readonly MainPage _mainPage;

    public App(MainPage mainPage)
    {
        _mainPage = mainPage;
        UserAppTheme = AppTheme.Dark;
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
            _mainPage.CheckForAppUpdate();
        };
        window.Activated += (_, _) =>
        {
            if (wasBackgrounded)
            {
                wasBackgrounded = false;
                _mainPage.CheckForAppUpdate();
            }
        };

        return window;
    }
}
