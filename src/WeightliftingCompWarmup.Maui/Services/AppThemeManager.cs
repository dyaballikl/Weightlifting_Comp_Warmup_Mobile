using System;
using Microsoft.Maui;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using WeightliftingCompWarmup.Core.Services;

namespace WeightliftingCompWarmup.Maui.Services;

public static class AppThemeManager
{
    public static string CurrentPreference { get; private set; } = ProfileStorageService.ThemeSystem;
    public static bool IsLightTheme { get; private set; }

    public static event Action? ThemeChanged;

    public static void Initialize(string? preference)
    {
        CurrentPreference = ProfileStorageService.NormalizeThemePreference(preference);
        ApplyToApplication();
        IsLightTheme = ResolveIsLightTheme();
        UpdatePlatformStatusBar();
    }

    public static void SetPreference(string? preference)
    {
        var normalized = ProfileStorageService.NormalizeThemePreference(preference);
        var prevPref = CurrentPreference;
        var prevLight = IsLightTheme;

        CurrentPreference = normalized;
        ApplyToApplication();
        IsLightTheme = ResolveIsLightTheme();
        UpdatePlatformStatusBar();

        if (prevPref != CurrentPreference || prevLight != IsLightTheme)
        {
            ThemeChanged?.Invoke();
        }
    }

    public static string CycleNextPreference()
    {
        var next = CurrentPreference switch
        {
            ProfileStorageService.ThemeSystem => ProfileStorageService.ThemeDark,
            ProfileStorageService.ThemeDark => ProfileStorageService.ThemeLight,
            _ => ProfileStorageService.ThemeSystem
        };
        SetPreference(next);
        return next;
    }

    public static void RefreshFromSystemTheme()
    {
        var resolved = ResolveIsLightTheme();
        if (resolved != IsLightTheme)
        {
            IsLightTheme = resolved;
            UpdatePlatformStatusBar();
            ThemeChanged?.Invoke();
        }
    }

    private static void ApplyToApplication()
    {
        if (Application.Current == null) return;

        Application.Current.UserAppTheme = CurrentPreference switch
        {
            ProfileStorageService.ThemeDark => AppTheme.Dark,
            ProfileStorageService.ThemeLight => AppTheme.Light,
            _ => AppTheme.Unspecified
        };
    }

    private static bool ResolveIsLightTheme()
    {
        if (CurrentPreference == ProfileStorageService.ThemeLight)
            return true;
        if (CurrentPreference == ProfileStorageService.ThemeDark)
            return false;

#if ANDROID
        try
        {
            var uiMode = Android.App.Application.Context?.Resources?.Configuration?.UiMode;
            if (uiMode.HasValue)
            {
                var nightMask = uiMode.Value & Android.Content.Res.UiMode.NightMask;
                if (nightMask == Android.Content.Res.UiMode.NightNo)
                    return true;
                if (nightMask == Android.Content.Res.UiMode.NightYes)
                    return false;
            }
        }
        catch
        {
            // Ignore and fall back to MAUI PlatformAppTheme
        }
#endif

        if (Application.Current != null)
        {
            var platformTheme = Application.Current.PlatformAppTheme;
            if (platformTheme == AppTheme.Light)
                return true;
            if (platformTheme == AppTheme.Dark)
                return false;
        }

        return false;
    }

    public static void UpdatePlatformStatusBar()
    {
#if ANDROID
        try
        {
            var activity = Platform.CurrentActivity;
            var window = activity?.Window;
            if (window == null) return;

            var statusBarColor = IsLightTheme
                ? Android.Graphics.Color.ParseColor("#E2E8F0")
                : Android.Graphics.Color.ParseColor("#0B1120");
#pragma warning disable CA1422, CS0618
            window.SetStatusBarColor(statusBarColor);
            window.SetNavigationBarColor(statusBarColor);

            if (OperatingSystem.IsAndroidVersionAtLeast(30))
            {
                var controller = window.InsetsController;
                if (controller != null)
                {
                    int appearance = IsLightTheme
                        ? (int)(Android.Views.WindowInsetsControllerAppearance.LightStatusBars | Android.Views.WindowInsetsControllerAppearance.LightNavigationBars)
                        : 0;
                    int mask = (int)(Android.Views.WindowInsetsControllerAppearance.LightStatusBars | Android.Views.WindowInsetsControllerAppearance.LightNavigationBars);
                    controller.SetSystemBarsAppearance(appearance, mask);
                }
            }
            else if (OperatingSystem.IsAndroidVersionAtLeast(23) && window.DecorView != null)
            {
                var flags = window.DecorView.SystemUiVisibility;
                if (IsLightTheme)
                    flags |= (Android.Views.StatusBarVisibility)Android.Views.SystemUiFlags.LightStatusBar;
                else
                    flags &= ~(Android.Views.StatusBarVisibility)Android.Views.SystemUiFlags.LightStatusBar;
                window.DecorView.SystemUiVisibility = flags;
            }
#pragma warning restore CA1422, CS0618
        }
        catch
        {
            // Ignore status bar styling errors
        }
#endif
    }

    public static Color NeutralButtonBackground =>
        IsLightTheme ? Microsoft.Maui.Graphics.Color.FromArgb("#E2E8F0") : Microsoft.Maui.Graphics.Color.FromArgb("#334155");

    public static Color NeutralButtonText =>
        IsLightTheme ? Microsoft.Maui.Graphics.Color.FromArgb("#0F172A") : Colors.White;

    public static Color EntryBackground =>
        IsLightTheme ? Microsoft.Maui.Graphics.Color.FromArgb("#F1F5F9") : Microsoft.Maui.Graphics.Color.FromArgb("#0F172A");

    /// <summary>
    /// Maps a dark-mode hex color token to its semantic equivalent in the currently active theme.
    /// </summary>
    public static Color Color(string darkHex)
    {
        if (!IsLightTheme)
            return Microsoft.Maui.Graphics.Color.FromArgb(darkHex);

        var mapped = darkHex.ToUpperInvariant() switch
        {
            // Page / Root Canvas background
            "#0B1120" => "#E2E8F0", // Slate 200 canvas so white cards pop cleanly

            // Card / Modal / Elevated Surface background
            "#1E293B" => "#FFFFFF", // Pure white card surface

            // Inset / Secondary Surface / Table Header / Inactive Tab background
            "#0F172A" => "#F1F5F9", // Slate 100 inset surface

            // Borders & Dividers
            "#334155" => "#CBD5E1", // Slate 300 card border
            "#475569" => "#94A3B8", // Slate 400 stronger control border

            // Typography (Inverted for Light Mode)
            "#F8FAFC" => "#0F172A", // Slate 900 primary heading/value text
            "#E2E8F0" => "#1E293B", // Slate 800 primary label text
            "#CBD5E1" => "#334155", // Slate 700 body text
            "#94A3B8" => "#475569", // Slate 600 secondary label text
            "#64748B" => "#64748B", // Slate 500 muted text

            // Accents & Highlights on Light Surfaces
            "#38BDF8" => "#0284C7", // Sky 600 accent text/border
            "#7DD3FC" => "#0369A1", // Sky 700 banner action text
            "#E0F2FE" => "#0C4A6E", // Sky 900 banner body text
            "#0C4A6E" => "#E0F2FE", // Sky 100 status banner background

            // Gold / Amber accents on light cards
            "#FBBF24" => "#B45309", // Amber 700 text on light surface
            "#FACC15" => "#B45309", // Amber 700 text/accent on light surface

            // Green status text on light cards
            "#4ADE80" => "#15803D", // Green 700 text on light surface

            // Update Banner
            "#422006" => "#FEF3C7", // Amber 100 banner background
            "#FEF08A" => "#78350F", // Amber 900 banner title text
            "#FDE047" => "#92400E", // Amber 800 banner subtitle text

            // Help Scoreboard Callout
            "#1E1B4B" => "#FFFBEB", // Amber 50 callout background
            "#312E81" => "#FDE68A", // Amber 200 callout divider

            // Modal Overlay Backdrop
            "#B3020617" => "#800F172A",

            _ => darkHex
        };

        return Microsoft.Maui.Graphics.Color.FromArgb(mapped);
    }
}
