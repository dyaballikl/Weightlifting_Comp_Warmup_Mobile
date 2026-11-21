using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Maui;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Graphics;
using WeightliftingCompWarmup.Core.Services;
using WeightliftingCompWarmup.Maui.Services;
using WeightliftingCompWarmup.Maui.ViewModels;

namespace WeightliftingCompWarmup.Maui.Views;

/// <summary>
/// Active tab in the unified Settings, Help &amp; About modal.
/// </summary>
public enum AppMenuTab
{
    Settings,
    Help,
    About
}

/// <summary>
/// Unified 3-tab modal providing App Settings (Theme, Audio, Gemini API Key, Scanner defaults,
/// Android Floating Timer permissions, Backups/Reset), "How to Use (Help)" guide, and "About Comp OWL".
/// Automatically opens to Help on first launch and opens to Settings from the top header menu button (☰).
/// </summary>
public class HelpAboutModalPage : ContentPage
{
    private ContentView _tabContentContainer = null!;
    private Button _settingsTabBtn = null!;
    private Button _helpTabBtn = null!;
    private Button _aboutTabBtn = null!;
    private AppMenuTab _selectedTab;
    private readonly MainViewModel? _viewModel;
    private readonly Action<string>? _onSelectTheme;
    private readonly Func<Task>? _onExportBackup;
    private readonly Func<Task>? _onImportBackup;
    private readonly Func<Task>? _onResetAllSettings;
    private readonly GeminiScoreboardVisionService _visionService = new();
    private readonly HashSet<string> _expandedHelpSections = new(StringComparer.Ordinal);
    private bool _isApiInstructionsExpanded;
    private string _apiKeyStatusText = string.Empty;
    private bool _apiKeyStatusIsError;
    private bool _isTestingApiKey;

    public const string AppShortName = "Comp OWL";
    public const string AppFullName = "Weightlifting Comp Warmup";
    public const string AppVersion = "2.0.46";
    public const string AppBuild = "49";
    public const string AppleAppStoreId = ""; // Populate with numeric App Store Connect ID once created

    public HelpAboutModalPage(
        AppMenuTab initialTab = AppMenuTab.Settings,
        MainViewModel? viewModel = null,
        Action<string>? onSelectTheme = null,
        Func<Task>? onExportBackup = null,
        Func<Task>? onImportBackup = null,
        Func<Task>? onResetAllSettings = null)
    {
        Title = $"{AppShortName} — Settings, Help & About";
        NavigationPage.SetHasNavigationBar(this, false);

        _selectedTab = initialTab;
        _viewModel = viewModel;
        _onSelectTheme = onSelectTheme ?? (viewModel != null ? pref => viewModel.SetThemePreference(pref) : null);
        _onExportBackup = onExportBackup;
        _onImportBackup = onImportBackup;
        _onResetAllSettings = onResetAllSettings;
        _isApiInstructionsExpanded = !_visionService.HasApiKey();

        BuildModalLayout();
    }

    public HelpAboutModalPage(bool startOnAboutTab, Action<string>? onSelectTheme = null)
        : this(startOnAboutTab ? AppMenuTab.About : AppMenuTab.Help, viewModel: null, onSelectTheme: onSelectTheme)
    {
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        AppThemeManager.ThemeChanged += OnThemeChanged;
        if (_selectedTab == AppMenuTab.Settings)
        {
            _tabContentContainer.Content = BuildSettingsTabView();
        }
    }

    protected override void OnDisappearing()
    {
        AppThemeManager.ThemeChanged -= OnThemeChanged;
        base.OnDisappearing();
    }

    private void OnThemeChanged()
    {
        MainThread.BeginInvokeOnMainThread(BuildModalLayout);
    }

    private void ApplySelectedTheme(string preference)
    {
        if (_onSelectTheme != null)
        {
            _onSelectTheme(preference);
        }
        else
        {
            AppThemeManager.SetPreference(preference);
        }
    }

    private void BuildModalLayout()
    {
        BackgroundColor = AppThemeManager.Color("#0B1120");

        var outerGrid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto }, // Top Title + Close bar
                new RowDefinition { Height = GridLength.Auto }, // 3-Tab Switcher (Settings | Help | About)
                new RowDefinition { Height = GridLength.Star }, // Scrollable Tab Content
                new RowDefinition { Height = GridLength.Auto }  // Bottom Action Button
            },
            Padding = new Thickness(14, 12, 14, 12),
            RowSpacing = 10,
            MaximumWidthRequest = 760,
            HorizontalOptions = LayoutOptions.Fill
        };

        // 1. Header Bar
        var headerBorder = new Border
        {
            BackgroundColor = AppThemeManager.Color("#1E293B"),
            Stroke = AppThemeManager.Color("#334155"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            Padding = new Thickness(14, 10)
        };

        var headerGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Auto }
            },
            ColumnSpacing = 10
        };

        var titleStack = new VerticalStackLayout
        {
            Spacing = 2,
            VerticalOptions = LayoutOptions.Center
        };
        titleStack.Add(new Label
        {
            Text = "🦉 Comp OWL — Olympic Weightlifting Warmup",
            TextColor = AppThemeManager.Color("#F8FAFC"),
            FontAttributes = FontAttributes.Bold,
            FontSize = 14.5
        });
        titleStack.Add(new Label
        {
            Text = "Settings, Competition Guide & App Info",
            TextColor = AppThemeManager.Color("#38BDF8"),
            FontSize = 11.5
        });

        var closeBtn = new Button
        {
            Text = "✕",
            BackgroundColor = AppThemeManager.Color("#0F172A"),
            BorderColor = AppThemeManager.Color("#475569"),
            BorderWidth = 1,
            TextColor = AppThemeManager.Color("#E2E8F0"),
            FontAttributes = FontAttributes.Bold,
            FontSize = 14,
            CornerRadius = 8,
            WidthRequest = 38,
            HeightRequest = 38,
            Padding = Thickness.Zero,
            VerticalOptions = LayoutOptions.Center
        };
        closeBtn.Clicked += OnCloseClicked;

        headerGrid.Add(titleStack, 0, 0);
        headerGrid.Add(closeBtn, 1, 0);
        headerBorder.Content = headerGrid;

        // 2. Segmented 3-Tab Bar (SETTINGS | HELP | ABOUT)
        var tabGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Star }
            },
            ColumnSpacing = 6
        };

        _settingsTabBtn = new Button
        {
            Text = "⚙️ SETTINGS",
            FontAttributes = FontAttributes.Bold,
            FontSize = 11.5,
            CornerRadius = 8,
            Padding = new Thickness(4, 0),
            HeightRequest = 38
        };
        _settingsTabBtn.Clicked += (_, _) => SelectTab(AppMenuTab.Settings);

        _helpTabBtn = new Button
        {
            Text = "📖 HELP",
            FontAttributes = FontAttributes.Bold,
            FontSize = 11.5,
            CornerRadius = 8,
            Padding = new Thickness(4, 0),
            HeightRequest = 38
        };
        _helpTabBtn.Clicked += (_, _) => SelectTab(AppMenuTab.Help);

        _aboutTabBtn = new Button
        {
            Text = "🦉 ABOUT",
            FontAttributes = FontAttributes.Bold,
            FontSize = 11.5,
            CornerRadius = 8,
            Padding = new Thickness(4, 0),
            HeightRequest = 38
        };
        _aboutTabBtn.Clicked += (_, _) => SelectTab(AppMenuTab.About);

        tabGrid.Add(_settingsTabBtn, 0, 0);
        tabGrid.Add(_helpTabBtn, 1, 0);
        tabGrid.Add(_aboutTabBtn, 2, 0);

        // 3. Tab Body Container
        _tabContentContainer = new ContentView();

        // 4. Footer "Done" Button
        var footerBtn = new Button
        {
            Text = "DONE — BACK TO WARMUP",
            BackgroundColor = Color.FromArgb("#0284C7"),
            BorderColor = AppThemeManager.Color("#38BDF8"),
            BorderWidth = 1,
            TextColor = Colors.White,
            FontAttributes = FontAttributes.Bold,
            FontSize = 13,
            CornerRadius = 8,
            HeightRequest = 44
        };
        footerBtn.Clicked += OnCloseClicked;

        outerGrid.Add(headerBorder, 0, 0);
        outerGrid.Add(tabGrid, 0, 1);
        outerGrid.Add(_tabContentContainer, 0, 2);
        outerGrid.Add(footerBtn, 0, 3);

        Content = outerGrid;
        SelectTab(_selectedTab);
    }

    private Button CreateThemeSegmentButton(string title, string preferenceValue, bool isSelected)
    {
        var btn = new Button
        {
            Text = title,
            BackgroundColor = isSelected ? Color.FromArgb("#0284C7") : AppThemeManager.Color("#0F172A"),
            BorderColor = isSelected ? AppThemeManager.Color("#38BDF8") : AppThemeManager.Color("#334155"),
            BorderWidth = isSelected ? 1.5 : 1,
            TextColor = isSelected ? Colors.White : AppThemeManager.Color("#94A3B8"),
            FontAttributes = FontAttributes.Bold,
            FontSize = 11.5,
            CornerRadius = 7,
            HeightRequest = 36,
            Padding = new Thickness(4, 0)
        };
        btn.Clicked += (_, _) => ApplySelectedTheme(preferenceValue);
        return btn;
    }

    private void SelectTab(AppMenuTab tab)
    {
        _selectedTab = tab;

        StyleTabButton(_settingsTabBtn, isActive: tab == AppMenuTab.Settings);
        StyleTabButton(_helpTabBtn, isActive: tab == AppMenuTab.Help);
        StyleTabButton(_aboutTabBtn, isActive: tab == AppMenuTab.About);

        _tabContentContainer.Content = tab switch
        {
            AppMenuTab.Settings => BuildSettingsTabView(),
            AppMenuTab.About => BuildAboutTabView(),
            _ => BuildHelpTabView()
        };
    }

    private static void StyleTabButton(Button btn, bool isActive)
    {
        btn.BackgroundColor = isActive ? Color.FromArgb("#0284C7") : AppThemeManager.Color("#0F172A");
        btn.BorderColor = isActive ? AppThemeManager.Color("#38BDF8") : AppThemeManager.Color("#334155");
        btn.BorderWidth = isActive ? 1.5 : 1;
        btn.TextColor = isActive ? Colors.White : AppThemeManager.Color("#94A3B8");
    }

    // =========================================================================
    // SETTINGS TAB
    // =========================================================================
    private View BuildSettingsTabView()
    {
        var stack = new VerticalStackLayout
        {
            Spacing = 12,
            Padding = new Thickness(0, 0, 0, 10)
        };

        // 1. Appearance Theme Card
        var themeCard = CreateSettingsCard("APPEARANCE THEME", "Choose System (Auto), Dark, or Light mode for Comp OWL.");
        var themeGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Star }
            },
            ColumnSpacing = 8
        };
        string currentPref = AppThemeManager.CurrentPreference;
        themeGrid.Add(CreateThemeSegmentButton("🌓 System", ProfileStorageService.ThemeSystem, currentPref == ProfileStorageService.ThemeSystem), 0, 0);
        themeGrid.Add(CreateThemeSegmentButton("🌙 Dark", ProfileStorageService.ThemeDark, currentPref == ProfileStorageService.ThemeDark), 1, 0);
        themeGrid.Add(CreateThemeSegmentButton("☀️ Light", ProfileStorageService.ThemeLight, currentPref == ProfileStorageService.ThemeLight), 2, 0);
        ((VerticalStackLayout)themeCard.Content!).Add(themeGrid);
        stack.Add(themeCard);

        // 2. Audio & Live Alerts Card
        if (_viewModel != null)
        {
            var audioCard = CreateSettingsCard("AUDIO & COMPETITION ALERTS", "Alerts during Live warmup progression.");
            var audioStack = (VerticalStackLayout)audioCard.Content!;

            var beepRow = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Auto }
                },
                ColumnSpacing = 8
            };
            beepRow.Add(new VerticalStackLayout
            {
                Spacing = 1,
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    new Label
                    {
                        Text = "🔔 Beep & Haptic on Next Step",
                        TextColor = AppThemeManager.Color("#F8FAFC"),
                        FontAttributes = FontAttributes.Bold,
                        FontSize = 13
                    },
                    new Label
                    {
                        Text = "Also toggleable anytime from the top header bar",
                        TextColor = AppThemeManager.Color("#94A3B8"),
                        FontSize = 11
                    }
                }
            }, 0, 0);

            var beepSwitch = new Switch
            {
                IsToggled = _viewModel.BeepOnNextLift,
                OnColor = Color.FromArgb("#22C55E"),
                ThumbColor = Colors.White,
                VerticalOptions = LayoutOptions.Center
            };
            beepSwitch.Toggled += (_, e) =>
            {
                if (_viewModel.BeepOnNextLift != e.Value)
                {
                    _viewModel.BeepOnNextLift = e.Value;
                }
            };
            beepRow.Add(beepSwitch, 1, 0);
            audioStack.Add(beepRow);
            stack.Add(audioCard);
        }

        // 3. AI Scoreboard Scanner (Beta) — Gemini API Key & Preferences
        stack.Add(BuildGeminiSettingsCard());

        // 4. Android Floating Mini-Timer Overlay Permission Status
#if ANDROID
        var overlayCard = CreateSettingsCard("FLOATING MINI-TIMER (ANDROID)", "Keep a draggable countdown pill on screen when switching apps during Live mode.");
        var overlayStack = (VerticalStackLayout)overlayCard.Content!;
        bool overlayGranted = DevicePlatformService.CanDrawAndroidOverlays();

        var statusRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Auto }
            },
            ColumnSpacing = 8
        };
        statusRow.Add(new Label
        {
            Text = overlayGranted
                ? "✅ 'Appear on top' permission is enabled"
                : "⚠️ 'Appear on top' permission is not enabled",
            TextColor = overlayGranted ? AppThemeManager.Color("#4ADE80") : AppThemeManager.Color("#FBBF24"),
            FontAttributes = FontAttributes.Bold,
            FontSize = 12,
            VerticalTextAlignment = TextAlignment.Center
        }, 0, 0);

        var openOverlayBtn = new Button
        {
            Text = overlayGranted ? "Manage" : "Enable...",
            BackgroundColor = overlayGranted ? AppThemeManager.Color("#0F172A") : Color.FromArgb("#0284C7"),
            BorderColor = AppThemeManager.Color("#38BDF8"),
            BorderWidth = 1,
            TextColor = overlayGranted ? AppThemeManager.Color("#38BDF8") : Colors.White,
            FontAttributes = FontAttributes.Bold,
            FontSize = 11.5,
            CornerRadius = 6,
            HeightRequest = 32,
            Padding = new Thickness(12, 0)
        };
        openOverlayBtn.Clicked += (_, _) => DevicePlatformService.OpenAndroidOverlaySettings();
        statusRow.Add(openOverlayBtn, 1, 0);
        overlayStack.Add(statusRow);
        stack.Add(overlayCard);
#endif

        // 5. Data, Backups & Reset Card
        if (_onExportBackup != null || _onImportBackup != null || _onResetAllSettings != null)
        {
            var dataCard = CreateSettingsCard("DATA & BACKUPS", "Export or restore all saved lifter profiles and settings (.json). Your Gemini API key is never included in backups.");
            var dataStack = (VerticalStackLayout)dataCard.Content!;

            var backupRow = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Star }
                },
                ColumnSpacing = 8
            };

            if (_onExportBackup != null)
            {
                var exportBtn = new Button
                {
                    Text = "📤 Export Backup",
                    BackgroundColor = AppThemeManager.Color("#0F172A"),
                    BorderColor = AppThemeManager.Color("#38BDF8"),
                    BorderWidth = 1,
                    TextColor = AppThemeManager.Color("#38BDF8"),
                    FontAttributes = FontAttributes.Bold,
                    FontSize = 12,
                    CornerRadius = 8,
                    HeightRequest = 38
                };
                exportBtn.Clicked += async (_, _) => await _onExportBackup();
                backupRow.Add(exportBtn, 0, 0);
            }

            if (_onImportBackup != null)
            {
                var importBtn = new Button
                {
                    Text = "📥 Import Backup",
                    BackgroundColor = AppThemeManager.Color("#0F172A"),
                    BorderColor = AppThemeManager.Color("#38BDF8"),
                    BorderWidth = 1,
                    TextColor = AppThemeManager.Color("#38BDF8"),
                    FontAttributes = FontAttributes.Bold,
                    FontSize = 12,
                    CornerRadius = 8,
                    HeightRequest = 38
                };
                importBtn.Clicked += async (_, _) => await _onImportBackup();
                backupRow.Add(importBtn, 1, 0);
            }

            dataStack.Add(backupRow);

            if (_onResetAllSettings != null)
            {
                var resetBtn = new Button
                {
                    Text = "⚠️ Clear All Settings (Reset to Demo Profile)",
                    BackgroundColor = AppThemeManager.Color("#450A0A"),
                    BorderColor = Color.FromArgb("#DC2626"),
                    BorderWidth = 1,
                    TextColor = AppThemeManager.Color("#FCA5A5"),
                    FontAttributes = FontAttributes.Bold,
                    FontSize = 12,
                    CornerRadius = 8,
                    HeightRequest = 38,
                    Margin = new Thickness(0, 2, 0, 0)
                };
                resetBtn.Clicked += async (_, _) => await _onResetAllSettings();
                dataStack.Add(resetBtn);
            }

            stack.Add(dataCard);
        }

        return new ScrollView { Content = stack };
    }

    private Border BuildGeminiSettingsCard()
    {
        bool hasKey = _visionService.HasApiKey();
        var card = CreateSettingsCard(
            "AI SCOREBOARD SCANNER (BETA)",
            "Encrypted on-device Google Gemini API key (Android Keystore / iOS Keychain) — never exported or backed up.");
        var stack = (VerticalStackLayout)card.Content!;

        // Key Status + Instructions Toggle Row
        var statusRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Auto }
            },
            ColumnSpacing = 8
        };

        var keyStatusHeaderLbl = new Label
        {
            Text = hasKey ? $"🔑 Key Ready ({_visionService.GetMaskedApiKey()})" : "⚠️ No Gemini API Key Configured",
            TextColor = hasKey ? AppThemeManager.Color("#4ADE80") : AppThemeManager.Color("#FBBF24"),
            FontAttributes = FontAttributes.Bold,
            FontSize = 12.5,
            VerticalTextAlignment = TextAlignment.Center
        };
        statusRow.Add(keyStatusHeaderLbl, 0, 0);

        var instructionsBorder = new Border
        {
            BackgroundColor = AppThemeManager.Color("#0F172A"),
            Stroke = AppThemeManager.Color("#1E3A8A"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Padding = new Thickness(10, 8),
            IsVisible = _isApiInstructionsExpanded
        };

        var toggleGuideBtn = new Button
        {
            Text = _isApiInstructionsExpanded ? "▲ Hide Guide" : "📖 How to Get Key",
            BackgroundColor = AppThemeManager.Color("#0F172A"),
            BorderColor = AppThemeManager.Color("#334155"),
            BorderWidth = 1,
            TextColor = AppThemeManager.Color("#38BDF8"),
            FontAttributes = FontAttributes.Bold,
            FontSize = 11,
            CornerRadius = 6,
            HeightRequest = 30,
            Padding = new Thickness(8, 0)
        };
        toggleGuideBtn.Clicked += (_, _) =>
        {
            _isApiInstructionsExpanded = !_isApiInstructionsExpanded;
            instructionsBorder.IsVisible = _isApiInstructionsExpanded;
            toggleGuideBtn.Text = _isApiInstructionsExpanded ? "▲ Hide Guide" : "📖 How to Get Key";
        };
        statusRow.Add(toggleGuideBtn, 1, 0);
        stack.Add(statusRow);

        var instStack = new VerticalStackLayout { Spacing = 5 };
        instStack.Add(new Label
        {
            Text = "GET A FREE GOOGLE GEMINI API KEY (30 SECONDS):",
            FontSize = 11,
            FontAttributes = FontAttributes.Bold,
            TextColor = AppThemeManager.Color("#38BDF8")
        });
        instStack.Add(new Label
        {
            Text = "1. Tap '🌐 Get Free Key at Google AI Studio' below and sign in with your Google account.\n" +
                   "2. Tap 'Create API key' and copy the key (starts with AIza...).\n" +
                   "3. Paste it below, tap '💾 Save', and optionally tap '✓ Test' to verify.",
            FontSize = 11.5,
            LineHeight = 1.2,
            TextColor = AppThemeManager.Color("#CBD5E1")
        });
        var openStudioBtn = new Button
        {
            Text = "🌐 Get Free Key at Google AI Studio",
            BackgroundColor = Color.FromArgb("#0284C7"),
            TextColor = Colors.White,
            FontAttributes = FontAttributes.Bold,
            FontSize = 11.5,
            CornerRadius = 6,
            HeightRequest = 34,
            Margin = new Thickness(0, 3, 0, 0)
        };
        openStudioBtn.Clicked += async (_, _) =>
        {
            try
            {
                await Launcher.Default.OpenAsync(new Uri(GeminiScoreboardVisionService.GoogleAiStudioKeyUrl));
            }
            catch (Exception ex)
            {
                await DisplayAlertAsync("Browser Error", $"Visit {GeminiScoreboardVisionService.GoogleAiStudioKeyUrl}\n({ex.Message})", "OK");
            }
        };
        instStack.Add(openStudioBtn);
        instructionsBorder.Content = instStack;
        stack.Add(instructionsBorder);

        var keyEntry = new Entry
        {
            Placeholder = "Paste Gemini API Key (AIza...)",
            PlaceholderColor = AppThemeManager.Color("#64748B"),
            Text = _visionService.GetApiKey(),
            IsPassword = true,
            FontSize = 13,
            TextColor = AppThemeManager.Color("#F8FAFC"),
            BackgroundColor = AppThemeManager.EntryBackground,
            HeightRequest = 40
        };
        stack.Add(keyEntry);

        var btnGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Auto }
            },
            ColumnSpacing = 6
        };

        var statusBannerLabel = new Label
        {
            Text = _apiKeyStatusText,
            FontSize = 11.5,
            TextColor = AppThemeManager.Color(_apiKeyStatusIsError ? "#FCA5A5" : "#86EFAC")
        };

        var statusBannerBorder = new Border
        {
            BackgroundColor = AppThemeManager.Color(_apiKeyStatusIsError ? "#450A0A" : "#0F291E"),
            Stroke = Color.FromArgb(_apiKeyStatusIsError ? "#DC2626" : "#16A34A"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 6 },
            Padding = new Thickness(8, 6),
            IsVisible = !string.IsNullOrWhiteSpace(_apiKeyStatusText),
            Content = statusBannerLabel
        };

        void UpdateKeyStatusInPlace()
        {
            bool keyPresent = _visionService.HasApiKey();
            keyStatusHeaderLbl.Text = keyPresent
                ? $"🔑 Key Ready ({_visionService.GetMaskedApiKey()})"
                : "⚠️ No Gemini API Key Configured";
            keyStatusHeaderLbl.TextColor = keyPresent
                ? AppThemeManager.Color("#4ADE80")
                : AppThemeManager.Color("#FBBF24");

            instructionsBorder.IsVisible = _isApiInstructionsExpanded;
            toggleGuideBtn.Text = _isApiInstructionsExpanded ? "▲ Hide Guide" : "📖 How to Get Key";

            statusBannerBorder.IsVisible = !string.IsNullOrWhiteSpace(_apiKeyStatusText);
            statusBannerBorder.BackgroundColor = AppThemeManager.Color(_apiKeyStatusIsError ? "#450A0A" : "#0F291E");
            statusBannerBorder.Stroke = Color.FromArgb(_apiKeyStatusIsError ? "#DC2626" : "#16A34A");
            statusBannerLabel.Text = _apiKeyStatusText;
            statusBannerLabel.TextColor = AppThemeManager.Color(_apiKeyStatusIsError ? "#FCA5A5" : "#86EFAC");
        }

        var saveKeyBtn = new Button
        {
            Text = "💾 Save",
            BackgroundColor = Color.FromArgb("#16A34A"),
            TextColor = Colors.White,
            FontAttributes = FontAttributes.Bold,
            FontSize = 12,
            CornerRadius = 6,
            HeightRequest = 36,
            Padding = new Thickness(8, 0)
        };
        saveKeyBtn.Clicked += (_, _) =>
        {
            string entered = keyEntry.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(entered))
            {
                _apiKeyStatusText = "Please paste a valid Gemini API key before saving.";
                _apiKeyStatusIsError = true;
            }
            else
            {
                _visionService.SaveApiKey(entered);
                _isApiInstructionsExpanded = false;
                _apiKeyStatusText = "Saved encrypted Gemini API key on this device.";
                _apiKeyStatusIsError = false;
            }
            UpdateKeyStatusInPlace();
        };

        var testKeyBtn = new Button
        {
            Text = _isTestingApiKey ? "Testing..." : "✓ Test",
            BackgroundColor = Color.FromArgb("#0284C7"),
            TextColor = Colors.White,
            FontAttributes = FontAttributes.Bold,
            FontSize = 11.5,
            CornerRadius = 6,
            HeightRequest = 36,
            Padding = new Thickness(10, 0),
            IsEnabled = !_isTestingApiKey
        };
        testKeyBtn.Clicked += async (_, _) =>
        {
            string candidate = keyEntry.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(candidate))
            {
                _apiKeyStatusText = "Paste or save a Gemini API key to test.";
                _apiKeyStatusIsError = true;
                UpdateKeyStatusInPlace();
                return;
            }

            _isTestingApiKey = true;
            testKeyBtn.IsEnabled = false;
            testKeyBtn.Text = "Testing...";
            _apiKeyStatusText = "Checking key against Google Gemini API...";
            _apiKeyStatusIsError = false;
            UpdateKeyStatusInPlace();

            var (ok, msg) = await _visionService.TestApiKeyAsync(candidate);
            _isTestingApiKey = false;
            testKeyBtn.IsEnabled = true;
            testKeyBtn.Text = "✓ Test";
            _apiKeyStatusText = msg;
            _apiKeyStatusIsError = !ok;
            UpdateKeyStatusInPlace();
        };

        var showHideBtn = new Button
        {
            Text = "👁",
            BackgroundColor = AppThemeManager.NeutralButtonBackground,
            TextColor = AppThemeManager.NeutralButtonText,
            FontSize = 13,
            CornerRadius = 6,
            WidthRequest = 38,
            HeightRequest = 36,
            Padding = Thickness.Zero
        };
        showHideBtn.Clicked += (_, _) =>
        {
            keyEntry.IsPassword = !keyEntry.IsPassword;
            showHideBtn.Text = keyEntry.IsPassword ? "👁" : "🙈";
        };

        var clearKeyBtn = new Button
        {
            Text = "🗑",
            BackgroundColor = Color.FromArgb("#DC2626"),
            TextColor = Colors.White,
            FontSize = 13,
            CornerRadius = 6,
            WidthRequest = 38,
            HeightRequest = 36,
            Padding = Thickness.Zero
        };
        clearKeyBtn.Clicked += (_, _) =>
        {
            _visionService.SaveApiKey(string.Empty);
            keyEntry.Text = string.Empty;
            _isApiInstructionsExpanded = true;
            _apiKeyStatusText = "Cleared saved Gemini API key from this device.";
            _apiKeyStatusIsError = false;
            UpdateKeyStatusInPlace();
        };

        btnGrid.Add(saveKeyBtn, 0, 0);
        btnGrid.Add(testKeyBtn, 1, 0);
        btnGrid.Add(showHideBtn, 2, 0);
        btnGrid.Add(clearKeyBtn, 3, 0);
        stack.Add(btnGrid);
        stack.Add(statusBannerBorder);

        if (_viewModel != null)
        {
            stack.Add(new BoxView
            {
                HeightRequest = 1,
                Color = AppThemeManager.Color("#334155"),
                Margin = new Thickness(0, 4, 0, 2)
            });

            var autoOneRow = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Auto }
                },
                ColumnSpacing = 8
            };
            autoOneRow.Add(new VerticalStackLayout
            {
                Spacing = 1,
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    new Label
                    {
                        Text = "Replace auto +1 kg board placeholders",
                        TextColor = AppThemeManager.Color("#F8FAFC"),
                        FontAttributes = FontAttributes.Bold,
                        FontSize = 12.5
                    },
                    new Label
                    {
                        Text = "Scoreboards auto-fill +1 kg after a make until the coach declares the real jump",
                        TextColor = AppThemeManager.Color("#94A3B8"),
                        FontSize = 10.5
                    }
                }
            }, 0, 0);

            var autoOneSwitch = new Switch
            {
                IsToggled = _viewModel.ReplaceAutoOneKgAfterMake,
                OnColor = Color.FromArgb("#0284C7"),
                ThumbColor = Colors.White,
                VerticalOptions = LayoutOptions.Center
            };
            autoOneSwitch.Toggled += (_, e) =>
            {
                if (_viewModel.ReplaceAutoOneKgAfterMake != e.Value)
                {
                    _viewModel.ReplaceAutoOneKgAfterMake = e.Value;
                }
            };
            autoOneRow.Add(autoOneSwitch, 1, 0);
            stack.Add(autoOneRow);
        }

        return card;
    }

    private static Border CreateSettingsCard(string badgeTitle, string subtitle)
    {
        var stack = new VerticalStackLayout { Spacing = 8 };
        stack.Add(new Label
        {
            Text = badgeTitle,
            TextColor = AppThemeManager.Color("#38BDF8"),
            FontAttributes = FontAttributes.Bold,
            FontSize = 11
        });
        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            stack.Add(new Label
            {
                Text = subtitle,
                TextColor = AppThemeManager.Color("#94A3B8"),
                FontSize = 11.5,
                LineHeight = 1.15
            });
        }

        return new Border
        {
            BackgroundColor = AppThemeManager.Color("#1E293B"),
            Stroke = AppThemeManager.Color("#334155"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            Padding = new Thickness(14, 12),
            Content = stack
        };
    }

    private View BuildHelpTabView()
    {
        var stack = new VerticalStackLayout
        {
            Spacing = 10,
            Padding = new Thickness(0, 0, 0, 8)
        };

        // 1. High-Level Quick Start Card (Always Visible & Brief)
        stack.Add(CreateQuickStartCard());

        // 2. Section Header for Expandable Details & Tips
        stack.Add(new Label
        {
            Text = "FUNCTIONS & COMPETITION TIPS (TAP TO EXPAND)",
            TextColor = AppThemeManager.Color("#38BDF8"),
            FontAttributes = FontAttributes.Bold,
            FontSize = 11,
            Margin = new Thickness(4, 4, 4, 0)
        });

        // 3. Expandable Accordion Cards
        stack.Add(CreateExpandableSectionCard(
            sectionKey: "scanner",
            title: "📷 AI Scoreboard Scanner (Beta) — Lifts Out & Passed",
            subtitle: "Photo scoreboard analysis, IWF calling-order rules, S-curve jumps & Show Your Work",
            accentColorHex: "#38BDF8",
            bullets: new[]
            {
                "Snap or Pick a Scoreboard Photo: Tap '📷 Scan Scoreboard (Beta)' in Setup or '📷 Scan (Beta)' in Live mode to photograph the warmup room monitor or pick a photo from your gallery. Photos are automatically oriented and compressed for fast uploads.",
                "Private Encrypted Gemini API Key: Configure and test your free Google AI Studio key anytime in ⚙️ Settings (☰). Your key is encrypted in device SecureStorage (Android Keystore / iOS Keychain) and is never exported or backed up.",
                "Exact IWF Calling-Order & 2-Min Self-Follow Detection: Calculates Lifts Out using official rising-bar priority (Lowest Weight → Earlier Attempt # → Earlier Previous Attempt Sequence → Lower Lot #) and automatically flags consecutive self-follow 2-minute clocks.",
                "S-Curve Next-Attempt Projections: Undeclared 2nd & 3rd attempts are projected in whole kilograms using a logistic S-curve (+1kg around 20kg, +2kg around 40kg, +3kg around 65kg, +4kg around 90kg, capped at +5kg at 105kg+, and +0kg repeat after a miss).",
                "Show Your Work, Row Exclusion & Overrides: Inspect every athlete's expected lifts and chronological call order, exclude prior-session lifter rows ('🚫 Exclude'), tap any cell to override a weight or status, or tap '✉️ Report Analysis Error' at the bottom to email a diagnostic report."
            }));

        stack.Add(CreateExpandableSectionCard(
            sectionKey: "tips",
            title: "💡 Competition Tips & Common Scenarios",
            subtitle: "Handling 2-minute timers, board changes, early starts & mock meets",
            accentColorHex: "#FBBF24",
            bullets: new[]
            {
                "2-Minute Clock (Following Yourself): Tap the dedicated '+1m' button in Live mode to add 60 seconds to the current stage lift only (without altering Lifts Out). It automatically resets to normal stage lift duration on the next attempt.",
                "Accidental Advance or Edit: Tap '↩️ Undo' in Live mode to revert your last stage advance, Lifts Out edit, +1m extension, or progress bar scrub.",
                "Marshaling Board Changes & Live Opener Tweaks: Adjust Lifts Out (- / + or tap to type), or tap your Opener weight chip directly in Live mode to change your opener on the fly without stopping the timer.",
                "Stage Lifting Starts Early or Late: Tap '⚡ Start Comp Now' (pre-Snatch), 'ADVANCE TO STAGE LIFTING' (when introductions/break finish early), or tap/drag directly on any clock progress bar to scrub the timer.",
                "Solo Training & Mock Meets: Turn on 'Auto-Advance (Training)' at the bottom of Live mode so Lifts Out ticks down automatically each time the stage clock hits 0s — ideal for hands-free training or practice meets."
            }));

        stack.Add(CreateExpandableSectionCard(
            sectionKey: "setup",
            title: "⚙️ Setup Parameters (Opener, Clocks & Buffers)",
            subtitle: "Opener weight, Snatch presentation buffer, C&J break, lifts out & end buffer",
            accentColorHex: "#38BDF8",
            bullets: new[]
            {
                "Opener Weight & Plate Diagram: Use -5/-1/+1/+5 or tap the weight to type directly. The barbell graphic shows exact IWF color-coded plate loading per side (including 2.5 kg competition collars on openers).",
                "Hit Opener in Warmup: Toggle ON to include your full opener weight as your final warmup lift, or OFF to stop one weight jump below your opener.",
                "Competition Start Time (Snatch): Set the scheduled session start time when athlete presentation begins.",
                "Presentation & Clock (Snatch): Total duration of athlete introductions plus the 10-minute clock before the first Snatch attempt (default 14 min).",
                "Snatch Presentation Buffer ('Presentation lift buffer'): Extra time (default +3 min) automatically added to whichever Snatch warmup lift is active at Competition Start Time, giving you time to pause warming up, walk out for athlete presentation, and return without falling behind.",
                "Clean & Jerk — Snatch Remaining & Break: Set 'Snatch attempts remaining' and 'Break after snatch (min)' (default 10 min) so you can start your C&J warmup while Snatches are still finishing on stage and count down seamlessly through the intermission.",
                "Lifts Out & Stage Lift Time: 'Lifts out' is the number of platform attempts before your opener; each adds 'Stage lift time (sec)' (default 55s Snatch / 62s C&J) to your countdown.",
                "Extra Time After Last Warmup (End Buffer): Buffer between finishing your final warmup lift and walking out to the platform for your opener (default 60s Snatch / 75s C&J)."
            }));

        stack.Add(CreateExpandableSectionCard(
            sectionKey: "rules",
            title: "⚖️ Weight Jumps, Lift Times, Extras & Preview",
            subtitle: "Jump rules (with smoothing), rest intervals, mobility steps & schedule table",
            accentColorHex: "#38BDF8",
            bullets: new[]
            {
                "Warmup Extras (Pre-Barbell Prep): Add mobility or prep steps before your first barbell set (e.g., Foam Roll, Mobility, Shoes/Knee Sleeves) with custom durations in seconds. Tap any step's name or time to edit, reorder with ▲ / ▼, or delete with ✕.",
                "Weight Jumps Table: Set how many kilos to jump from each weight threshold (e.g., +20 kg from 1 kg, +10 kg from 40 kg, +5 kg from 70 kg). Tap any rule to edit its threshold or jump size.",
                "Automatic Final-Jump Midpoint Smoothing: Comp OWL automatically evens out awkward tiny jumps right before your opener so your final two warmup transitions stay balanced.",
                "Warmup Rest / Interval Times: Set the duration per warmup set from each weight threshold (e.g., 2m00s light, 2m30s heavy). Tap any existing rule to edit its weight or time.",
                "Calculated Warmup Schedule Table: Live preview in Setup showing every warmup step (#, Step name, Weight, IWF sleeve plate graphic, Duration, Total elapsed time, and '-XmYs before opener' reverse timestamp)."
            }));

        stack.Add(CreateExpandableSectionCard(
            sectionKey: "live",
            title: "🔴 Live Runner & Stage Tracking Controls",
            subtitle: "Telemetry clocks, phase progress bars, scrubbing, Advance, Pause & Reset",
            accentColorHex: "#4ADE80",
            bullets: new[]
            {
                "Live Telemetry Clocks: Top pills display CURRENT TIME, EST. OPEN TIME (projected clock time of your opener), and TIME TO OPENER (total countdown until you lift on stage).",
                "Active Warmup Progression: Your current warmup step is highlighted in yellow ('ACTIVE NOW') with a live progress bar and countdown badge, completed steps dim ('✓ DONE'), upcoming steps show 'in XmYs', and your opener displays a full-barbell 'OPEN NOW!' card.",
                "Tracking Stage Lifts ('⏭️ ADVANCE' & Lifts Out): Tap '⏭️ ADVANCE: NEXT LIFT' (turns red in the final 10s) as each platform attempt finishes, or adjust 'Lifts Out' (- / + or tap to type). Also tracks 'Passed: X' attempts.",
                "Phase Clocks & Scrubbing: Tap or drag anywhere along the Presentation Clock bar, C&J Snatch Remaining bar, C&J Break bar, or green Stage Lift bar (or tap '⏱ XmYs left') to sync with the venue clock. Completed pre-stage phases collapse automatically on phones (tap to expand).",
                "Start Comp Now, Pause/Resume & Reset: Tap '⚡ Start Comp Now' if Snatch starts early, '⏸ Pause / ▶ Resume' to freeze or continue the live timer, or '↺ Reset' to restore your initial Setup countdown."
            }));

        stack.Add(CreateExpandableSectionCard(
            sectionKey: "pip",
            title: "🪟 Floating Mini-Timer (PiP), Audio Beep & Wake Lock",
            subtitle: "Multitasking overlay window, step transition beeps & screen wake lock",
            accentColorHex: "#38BDF8",
            bullets: new[]
            {
                "Floating Mini-Timer (PiP): Minimize Comp OWL while the Live timer is running to keep a floating overlay on screen showing your active weight/step, progress bar, countdown, Lifts Out, and a one-tap '⏭️ Adv' button. Follows your device's system light/dark mode.",
                "Audio Beep Alerts ('🔔 Beep'): Toggle '🔔 ON / 🔕 OFF' in the top bar to play an audio alert whenever a warmup step countdown finishes and transitions to the next lift.",
                "Screen Wake Lock: Comp OWL automatically keeps your screen awake while the Live timer is running so your display never turns off mid-warmup."
            }));

        stack.Add(CreateExpandableSectionCard(
            sectionKey: "profiles",
            title: "👤 Profiles, Barbell Weight, Backups & Theme",
            subtitle: "Managing lifters, 20kg/15kg bar toggle, JSON export/import & Dark/Light mode",
            accentColorHex: "#38BDF8",
            bullets: new[]
            {
                "Lifter Profiles ('👤'): Tap the top-left profile button to switch lifters, create a new profile, duplicate, rename, or delete. Every change saves automatically and atomically to the active profile.",
                "Barbell Weight ('🏋️ 20kg / 15kg'): Tap the bar button in the top bar to enter a 20 kg (Men's), 15 kg (Women's), or 10 kg (Youth) barbell. All warmup steps and IWF plate diagrams update immediately.",
                "Settings, Theme & Backups ('☰'): Tap the top-right '☰' menu button to switch Appearance Theme (System / Dark / Light), configure and test your Gemini API key, check Android Floating Timer permissions, or export/import JSON profile backups."
            }));

        return new ScrollView { Content = stack };
    }

    private View BuildAboutTabView()
    {
        var stack = new VerticalStackLayout
        {
            Spacing = 12,
            Padding = new Thickness(0, 0, 0, 8)
        };

        // Hero Card
        var heroBorder = new Border
        {
            BackgroundColor = AppThemeManager.Color("#1E293B"),
            Stroke = AppThemeManager.Color("#38BDF8"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
            Padding = new Thickness(16, 16)
        };

        var heroStack = new VerticalStackLayout
        {
            Spacing = 6,
            HorizontalOptions = LayoutOptions.Center
        };

        heroStack.Add(new Label
        {
            Text = "🦉🏋️",
            FontSize = 36,
            HorizontalTextAlignment = TextAlignment.Center
        });

        heroStack.Add(new Label
        {
            Text = "Comp OWL",
            TextColor = AppThemeManager.Color("#F8FAFC"),
            FontAttributes = FontAttributes.Bold,
            FontSize = 24,
            HorizontalTextAlignment = TextAlignment.Center
        });

        heroStack.Add(new Label
        {
            Text = "Competition Olympic WeightLifting Warmup",
            TextColor = AppThemeManager.Color("#38BDF8"),
            FontAttributes = FontAttributes.Bold,
            FontSize = 13,
            HorizontalTextAlignment = TextAlignment.Center
        });

        heroStack.Add(new Label
        {
            Text = $"Version {AppVersion} (Build {AppBuild}) • Android & iOS",
            TextColor = AppThemeManager.Color("#94A3B8"),
            FontSize = 12,
            HorizontalTextAlignment = TextAlignment.Center
        });

        var whatsNewBtn = new Button
        {
            Text = "🎉 What's New / Release Notes",
            BackgroundColor = AppThemeManager.Color("#0F172A"),
            BorderColor = AppThemeManager.Color("#334155"),
            BorderWidth = 1,
            TextColor = AppThemeManager.Color("#38BDF8"),
            FontAttributes = FontAttributes.Bold,
            FontSize = 12,
            CornerRadius = 8,
            Padding = new Thickness(12, 6),
            HorizontalOptions = LayoutOptions.Center,
            Margin = new Thickness(0, 4, 0, 0)
        };
        whatsNewBtn.Clicked += async (_, _) =>
        {
            var notes = AppChangelog.GetChangesSince("2.0.0", AppVersion);
            await Navigation.PushModalAsync(
                new WhatsNewModalPage(AppVersion, string.Empty, notes),
                animated: true);
        };
        heroStack.Add(whatsNewBtn);

        heroBorder.Content = heroStack;
        stack.Add(heroBorder);

        // Optional Support / Ko-fi Card
        var supportBorder = new Border
        {
            BackgroundColor = AppThemeManager.Color("#1E293B"),
            Stroke = Color.FromArgb("#F59E0B"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            Padding = new Thickness(14, 12)
        };

        var supportStack = new VerticalStackLayout { Spacing = 6 };
        supportStack.Add(new Label
        {
            Text = "SUPPORT COMP OWL (OPTIONAL)",
            TextColor = AppThemeManager.Color("#FBBF24"),
            FontAttributes = FontAttributes.Bold,
            FontSize = 11
        });
        supportStack.Add(new Label
        {
            Text = "Help Cover Annual License Maintenance",
            TextColor = AppThemeManager.Color("#F8FAFC"),
            FontAttributes = FontAttributes.Bold,
            FontSize = 14
        });
        supportStack.Add(new Label
        {
            Text = "Comp OWL is free, open-source, and built for the weightlifting community — I'm not looking to profit from this app. However, maintaining developer account licenses costs me around $150/year. If Comp OWL helps you or your lifters on competition day, an optional contribution on Ko-fi to help cover annual license maintenance is hugely appreciated!",
            TextColor = AppThemeManager.Color("#CBD5E1"),
            FontSize = 12.5,
            LineHeight = 1.2
        });

        var kofiBtn = new Button
        {
            Text = "☕ Support Comp OWL on Ko-fi",
            BackgroundColor = Color.FromArgb("#0284C7"),
            BorderColor = AppThemeManager.Color("#38BDF8"),
            BorderWidth = 1,
            TextColor = Colors.White,
            FontAttributes = FontAttributes.Bold,
            FontSize = 12.5,
            CornerRadius = 8,
            HeightRequest = 38,
            Margin = new Thickness(0, 4, 0, 0)
        };
        kofiBtn.Clicked += async (_, _) =>
        {
            try
            {
                await Launcher.Default.OpenAsync("https://ko-fi.com/compowlapp");
            }
            catch
            {
                // Ignore if browser launcher unavailable
            }
        };
        supportStack.Add(kofiBtn);

        supportBorder.Content = supportStack;
        stack.Add(supportBorder);

        // Store Rating Card (shown only when installed from Google Play Store or Apple App Store)
        bool isIosEnv = DeviceInfo.Current.Platform == DevicePlatform.iOS ||
                        DeviceInfo.Current.Platform == DevicePlatform.MacCatalyst;
        bool isAndroidEnv = DeviceInfo.Current.Platform == DevicePlatform.Android;

        if ((isIosEnv || isAndroidEnv) && Services.DevicePlatformService.IsInstalledFromAppStore())
        {
            string storeBadgeText = isIosEnv
                ? "RATE ON THE APP STORE"
                : "RATE ON GOOGLE PLAY";

            string storeNameText = isIosEnv
                ? "Apple App Store"
                : "Google Play Store";

            string rateButtonText = isIosEnv
                ? "⭐ Rate Comp OWL on the App Store"
                : "⭐ Rate Comp OWL on Google Play";

            var rateBorder = new Border
            {
                BackgroundColor = AppThemeManager.Color("#1E293B"),
                Stroke = AppThemeManager.Color("#38BDF8"),
                StrokeThickness = 1,
                StrokeShape = new RoundRectangle { CornerRadius = 10 },
                Padding = new Thickness(14, 12)
            };

            var rateStack = new VerticalStackLayout { Spacing = 6 };
            rateStack.Add(new Label
            {
                Text = storeBadgeText,
                TextColor = AppThemeManager.Color("#38BDF8"),
                FontAttributes = FontAttributes.Bold,
                FontSize = 11
            });
            rateStack.Add(new Label
            {
                Text = "Enjoying Comp OWL on Competition Day?",
                TextColor = AppThemeManager.Color("#F8FAFC"),
                FontAttributes = FontAttributes.Bold,
                FontSize = 14
            });
            rateStack.Add(new Label
            {
                Text = $"Leaving a quick rating or review on the {storeNameText} helps other weightlifters, coaches, and handlers discover Comp OWL.",
                TextColor = AppThemeManager.Color("#CBD5E1"),
                FontSize = 12.5,
                LineHeight = 1.2
            });

            var rateBtn = new Button
            {
                Text = rateButtonText,
                BackgroundColor = AppThemeManager.Color("#0F172A"),
                BorderColor = AppThemeManager.Color("#38BDF8"),
                BorderWidth = 1,
                TextColor = AppThemeManager.Color("#FACC15"),
                FontAttributes = FontAttributes.Bold,
                FontSize = 12.5,
                CornerRadius = 8,
                HeightRequest = 38,
                Margin = new Thickness(0, 4, 0, 0)
            };
            rateBtn.Clicked += async (_, _) => await OpenStoreRatingAsync();
            rateStack.Add(rateBtn);

            rateBorder.Content = rateStack;
            stack.Add(rateBorder);
        }

        // Why Comp OWL?
        stack.Add(CreateInfoCard(
            badgeText: "THE NAME",
            badgeColorHex: "#38BDF8",
            title: "Why \"Comp OWL\"?",
            body: "Comp OWL stands for Competition Olympic WeightLifting — a short, competition-ready name designed to fit cleanly in your phone's app drawer while keeping your warmup timing sharp and wise in the back room."));

        // Offline & Privacy
        stack.Add(CreateInfoCard(
            badgeText: "100% OFFLINE & RELIABLE",
            badgeColorHex: "#4ADE80",
            title: "Built for Basement Venues & Warmup Halls",
            body: "Competition warmup areas often have poor or zero cell reception. Comp OWL runs 100% offline on your phone or tablet, saving all lifter profiles, jump tables, and timing rules locally on your device."));

        // Open Source & License Card
        var licenseBorder = new Border
        {
            BackgroundColor = AppThemeManager.Color("#1E293B"),
            Stroke = AppThemeManager.Color("#334155"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            Padding = new Thickness(14, 12)
        };

        var licenseStack = new VerticalStackLayout { Spacing = 8 };
        licenseStack.Add(new Label
        {
            Text = "OPEN SOURCE & LICENSE",
            TextColor = AppThemeManager.Color("#38BDF8"),
            FontAttributes = FontAttributes.Bold,
            FontSize = 11
        });
        var creatorLabel = new Label
        {
            FormattedText = new FormattedString
            {
                Spans =
                {
                    new Span
                    {
                        Text = "Created by Matt Cunningham - ",
                        TextColor = AppThemeManager.Color("#F8FAFC"),
                        FontAttributes = FontAttributes.Bold,
                        FontSize = 13
                    },
                    new Span
                    {
                        Text = "@lifting_while_diabetic",
                        TextColor = AppThemeManager.Color("#38BDF8"),
                        FontAttributes = FontAttributes.Bold,
                        TextDecorations = TextDecorations.Underline,
                        FontSize = 13
                    }
                }
            }
        };
        var instagramTap = new TapGestureRecognizer();
        instagramTap.Tapped += async (_, _) =>
        {
            try
            {
                await Launcher.Default.OpenAsync("https://www.instagram.com/lifting_while_diabetic/");
            }
            catch
            {
                // Ignore if browser launcher unavailable
            }
        };
        creatorLabel.GestureRecognizers.Add(instagramTap);
        licenseStack.Add(creatorLabel);
        licenseStack.Add(new Label
        {
            Text = "GNU General Public License v3.0 (GPL-3.0) • Based on the original Windows desktop Weightlifting_Comp_Warmup application. Free and open-source software distributed under the terms of the GPL-3.0 license.",
            TextColor = AppThemeManager.Color("#CBD5E1"),
            FontSize = 12,
            LineHeight = 1.2
        });

        var linksGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Star }
            },
            ColumnSpacing = 8,
            Margin = new Thickness(0, 4, 0, 0)
        };

        var mobileRepoBtn = new Button
        {
            Text = "🌐 Mobile GitHub Repo",
            BackgroundColor = AppThemeManager.Color("#0F172A"),
            BorderColor = AppThemeManager.Color("#475569"),
            BorderWidth = 1,
            TextColor = AppThemeManager.Color("#38BDF8"),
            FontAttributes = FontAttributes.Bold,
            FontSize = 11.5,
            CornerRadius = 8,
            HeightRequest = 36
        };
        mobileRepoBtn.Clicked += async (_, _) =>
        {
            try
            {
                await Launcher.Default.OpenAsync("https://github.com/dyaballikl/Weightlifting_Comp_Warmup_Mobile");
            }
            catch
            {
                // Ignore if browser launcher unavailable
            }
        };

        var desktopRepoBtn = new Button
        {
            Text = "💻 Desktop GitHub Repo",
            BackgroundColor = AppThemeManager.Color("#0F172A"),
            BorderColor = AppThemeManager.Color("#475569"),
            BorderWidth = 1,
            TextColor = AppThemeManager.Color("#38BDF8"),
            FontAttributes = FontAttributes.Bold,
            FontSize = 11.5,
            CornerRadius = 8,
            HeightRequest = 36
        };
        desktopRepoBtn.Clicked += async (_, _) =>
        {
            try
            {
                await Launcher.Default.OpenAsync("https://github.com/dyaballikl/Weightlifting_Comp_Warmup");
            }
            catch
            {
                // Ignore if browser launcher unavailable
            }
        };

        linksGrid.Add(mobileRepoBtn, 0, 0);
        linksGrid.Add(desktopRepoBtn, 1, 0);
        licenseStack.Add(linksGrid);

        licenseBorder.Content = licenseStack;
        stack.Add(licenseBorder);

        return new ScrollView { Content = stack };
    }

    private static View CreateInfoCard(string badgeText, string badgeColorHex, string title, string body)
    {
        var border = new Border
        {
            BackgroundColor = AppThemeManager.Color("#1E293B"),
            Stroke = AppThemeManager.Color("#334155"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            Padding = new Thickness(14, 12)
        };

        var stack = new VerticalStackLayout { Spacing = 4 };
        stack.Add(new Label
        {
            Text = badgeText,
            TextColor = AppThemeManager.Color(badgeColorHex),
            FontAttributes = FontAttributes.Bold,
            FontSize = 11
        });
        stack.Add(new Label
        {
            Text = title,
            TextColor = AppThemeManager.Color("#F8FAFC"),
            FontAttributes = FontAttributes.Bold,
            FontSize = 14
        });
        stack.Add(new Label
        {
            Text = body,
            TextColor = AppThemeManager.Color("#CBD5E1"),
            FontSize = 12.5,
            LineHeight = 1.2
        });

        border.Content = stack;
        return border;
    }

    private static View CreateQuickStartCard()
    {
        var border = new Border
        {
            BackgroundColor = AppThemeManager.Color("#1E293B"),
            Stroke = AppThemeManager.Color("#38BDF8"),
            StrokeThickness = 1.5,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            Padding = new Thickness(14, 12)
        };

        var stack = new VerticalStackLayout { Spacing = 8 };

        stack.Add(new Label
        {
            Text = "⚡ QUICK START — GET GOING IN 4 STEPS",
            TextColor = AppThemeManager.Color("#38BDF8"),
            FontAttributes = FontAttributes.Bold,
            FontSize = 11.5
        });

        stack.Add(new Label
        {
            Text = "Comp OWL schedules every warmup set backward from your platform opener so you're ready right as your name is called.",
            TextColor = AppThemeManager.Color("#CBD5E1"),
            FontSize = 12,
            LineHeight = 1.15
        });

        var quickSteps = new (string Number, string Text)[]
        {
            ("1", "Profile & Bar: Pick your profile (👤) and bar weight (🏋️ 20 kg / 15 kg) at the top."),
            ("2", "Setup: Set your Opener Weight, Start/Break Clocks & Buffers, Lifts Out, Weight Jumps, Lift Times, and Extras for SNATCH (then switch to CLEAN & JERK for the second half)."),
            ("3", "Start Live: Tap 🔴 LIVE to start the countdown and follow the highlighted ACTIVE NOW row."),
            ("4", "Track the Stage: Not linked to the venue scoreboard — tap ⏭️ ADVANCE as stage lifts finish, or adjust Lifts Out (- / +) when the board changes.")
        };

        foreach (var (number, text) in quickSteps)
        {
            var stepRow = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = GridLength.Star }
                },
                ColumnSpacing = 8
            };

            var badge = new Border
            {
                BackgroundColor = Color.FromArgb("#0284C7"),
                Stroke = AppThemeManager.Color("#38BDF8"),
                StrokeThickness = 1,
                StrokeShape = new RoundRectangle { CornerRadius = 6 },
                WidthRequest = 22,
                HeightRequest = 22,
                VerticalOptions = LayoutOptions.Start,
                Margin = new Thickness(0, 1, 0, 0),
                Content = new Label
                {
                    Text = number,
                    TextColor = Colors.White,
                    FontAttributes = FontAttributes.Bold,
                    FontSize = 11,
                    HorizontalTextAlignment = TextAlignment.Center,
                    VerticalTextAlignment = TextAlignment.Center
                }
            };

            stepRow.Add(badge, 0, 0);
            stepRow.Add(BuildFormattedTextLabel(text, 12.5), 1, 0);
            stack.Add(stepRow);
        }

        border.Content = stack;
        return border;
    }

    private View CreateExpandableSectionCard(
        string sectionKey,
        string title,
        string subtitle,
        string accentColorHex,
        string[] bullets)
    {
        bool isExpanded = _expandedHelpSections.Contains(sectionKey);

        var border = new Border
        {
            BackgroundColor = AppThemeManager.Color("#1E293B"),
            Stroke = isExpanded ? AppThemeManager.Color(accentColorHex) : AppThemeManager.Color("#334155"),
            StrokeThickness = isExpanded ? 1.5 : 1,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            Padding = new Thickness(14, 11)
        };

        var outerStack = new VerticalStackLayout { Spacing = 0 };

        var headerGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Auto }
            },
            ColumnSpacing = 8
        };

        var titleStack = new VerticalStackLayout { Spacing = 2 };
        titleStack.Add(new Label
        {
            Text = title,
            TextColor = AppThemeManager.Color("#F8FAFC"),
            FontAttributes = FontAttributes.Bold,
            FontSize = 13.5
        });
        titleStack.Add(new Label
        {
            Text = subtitle,
            TextColor = AppThemeManager.Color("#94A3B8"),
            FontSize = 11.5
        });

        var chevronLabel = new Label
        {
            Text = isExpanded ? "▾" : "▸",
            TextColor = AppThemeManager.Color(accentColorHex),
            FontAttributes = FontAttributes.Bold,
            FontSize = 16,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center
        };

        var chevronBadge = new Border
        {
            BackgroundColor = AppThemeManager.Color("#0F172A"),
            Stroke = AppThemeManager.Color("#334155"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 6 },
            WidthRequest = 28,
            HeightRequest = 28,
            VerticalOptions = LayoutOptions.Center,
            Content = chevronLabel
        };

        headerGrid.Add(titleStack, 0, 0);
        headerGrid.Add(chevronBadge, 1, 0);

        var bodyStack = new VerticalStackLayout
        {
            Spacing = 7,
            IsVisible = isExpanded,
            Margin = new Thickness(0, 10, 0, 2)
        };

        bodyStack.Add(new BoxView
        {
            HeightRequest = 1,
            Color = AppThemeManager.Color("#334155"),
            Margin = new Thickness(0, 0, 0, 2)
        });

        foreach (string bullet in bullets)
        {
            var bulletGrid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = new GridLength(14) },
                    new ColumnDefinition { Width = GridLength.Star }
                },
                ColumnSpacing = 4
            };

            bulletGrid.Add(new Label
            {
                Text = "•",
                TextColor = AppThemeManager.Color(accentColorHex),
                FontAttributes = FontAttributes.Bold,
                FontSize = 13,
                VerticalTextAlignment = TextAlignment.Start
            }, 0, 0);

            bulletGrid.Add(BuildFormattedTextLabel(bullet, 12), 1, 0);
            bodyStack.Add(bulletGrid);
        }

        var tapGesture = new TapGestureRecognizer();
        tapGesture.Tapped += (_, _) =>
        {
            bool nextExpanded = !bodyStack.IsVisible;
            bodyStack.IsVisible = nextExpanded;
            chevronLabel.Text = nextExpanded ? "▾" : "▸";
            border.Stroke = nextExpanded ? AppThemeManager.Color(accentColorHex) : AppThemeManager.Color("#334155");
            border.StrokeThickness = nextExpanded ? 1.5 : 1;

            if (nextExpanded)
            {
                _expandedHelpSections.Add(sectionKey);
            }
            else
            {
                _expandedHelpSections.Remove(sectionKey);
            }
        };
        headerGrid.GestureRecognizers.Add(tapGesture);

        outerStack.Add(headerGrid);
        outerStack.Add(bodyStack);
        border.Content = outerStack;
        return border;
    }

    private static Label BuildFormattedTextLabel(string text, double fontSize)
    {
        int colonIdx = text.IndexOf(':');
        if (colonIdx > 0 && colonIdx < 70)
        {
            string prefix = text[..(colonIdx + 1)];
            string remainder = text[(colonIdx + 1)..].TrimStart();

            var formatted = new FormattedString();
            formatted.Spans.Add(new Span
            {
                Text = prefix + " ",
                FontAttributes = FontAttributes.Bold,
                TextColor = AppThemeManager.Color("#F8FAFC"),
                FontSize = fontSize
            });
            formatted.Spans.Add(new Span
            {
                Text = remainder,
                TextColor = AppThemeManager.Color("#CBD5E1"),
                FontSize = fontSize
            });

            return new Label
            {
                FormattedText = formatted,
                LineHeight = 1.18,
                LineBreakMode = LineBreakMode.WordWrap
            };
        }

        return new Label
        {
            Text = text,
            TextColor = AppThemeManager.Color("#CBD5E1"),
            FontSize = fontSize,
            LineHeight = 1.18,
            LineBreakMode = LineBreakMode.WordWrap
        };
    }

    private static async Task OpenStoreRatingAsync()
    {
        try
        {
            string packageId = AppInfo.Current.PackageName;
            if (string.IsNullOrWhiteSpace(packageId))
            {
                packageId = "com.weightlifting.compwarmup";
            }

#if ANDROID
            try
            {
                await Launcher.Default.OpenAsync($"market://details?id={packageId}");
                return;
            }
            catch
            {
                await Launcher.Default.OpenAsync($"https://play.google.com/store/apps/details?id={packageId}");
                return;
            }
#elif IOS
            bool requestedNativeReview = false;
            try
            {
                var windowScene = UIKit.UIApplication.SharedApplication.ConnectedScenes
                    .ToArray()
                    .OfType<UIKit.UIWindowScene>()
                    .FirstOrDefault(s => s.ActivationState == UIKit.UISceneActivationState.ForegroundActive)
                    ?? UIKit.UIApplication.SharedApplication.ConnectedScenes
                        .ToArray()
                        .OfType<UIKit.UIWindowScene>()
                        .FirstOrDefault();

                if (windowScene != null)
                {
#pragma warning disable CA1422
                    StoreKit.SKStoreReviewController.RequestReview(windowScene);
#pragma warning restore CA1422
                    requestedNativeReview = true;
                }
            }
            catch
            {
                // Fall back to App Store URL if native review controller fails
            }

            if (!string.IsNullOrWhiteSpace(AppleAppStoreId))
            {
                await Launcher.Default.OpenAsync($"https://apps.apple.com/app/id{AppleAppStoreId}?action=write-review");
                return;
            }

            if (!requestedNativeReview)
            {
                await Launcher.Default.OpenAsync("https://apps.apple.com/");
            }
#else
            if (DeviceInfo.Current.Platform == DevicePlatform.Android)
            {
                await Launcher.Default.OpenAsync($"https://play.google.com/store/apps/details?id={packageId}");
            }
#endif
        }
        catch
        {
            // Ignore if store/browser launcher is unavailable
        }
    }

    private async void OnCloseClicked(object? sender, EventArgs e)
    {
        if (Navigation.ModalStack.Count > 0)
        {
            await Navigation.PopModalAsync(animated: true);
        }
    }
}
