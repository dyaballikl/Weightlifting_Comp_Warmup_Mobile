using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Handlers;
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

        EntryHandler.Mapper.AppendToMapping("SelectAllOnFocus", (handler, _) =>
        {
#if ANDROID
            handler.PlatformView.SetSelectAllOnFocus(true);
            handler.PlatformView.FocusChange += (s, e) =>
            {
                if (e.HasFocus && s is Android.Widget.EditText et && !string.IsNullOrEmpty(et.Text))
                {
                    et.Post(() => et.SelectAll());
                }
            };
#elif IOS || MACCATALYST
            handler.PlatformView.EditingDidBegin += (s, _) =>
            {
                if (s is UIKit.UITextField tf && !string.IsNullOrEmpty(tf.Text))
                {
                    tf.PerformSelector(new ObjCRuntime.Selector("selectAll:"), null, 0.0f);
                }
            };
#elif WINDOWS
            handler.PlatformView.GotFocus += (s, _) =>
            {
                if (s is Microsoft.UI.Xaml.Controls.TextBox tb && !string.IsNullOrEmpty(tb.Text))
                {
                    tb.SelectAll();
                }
            };
#endif
        });

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
