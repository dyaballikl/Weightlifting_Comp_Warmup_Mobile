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
        return new Window(new NavigationPage(_mainPage))
        {
            Title = "CompOWL — Weightlifting Comp Warmup"
        };
    }
}
