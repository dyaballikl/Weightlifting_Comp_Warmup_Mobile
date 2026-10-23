using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Hosting;
using WeightliftingCompWarmup.Core.Services;
using WeightliftingCompWarmup.Maui.Services;
using WeightliftingCompWarmup.Maui.ViewModels;
using WeightliftingCompWarmup.Maui.Views;

namespace WeightliftingCompWarmup.Maui;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

        builder.Services.AddSingleton<ProfileStorageService>();
        builder.Services.AddSingleton<DevicePlatformService>();
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<MainPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
