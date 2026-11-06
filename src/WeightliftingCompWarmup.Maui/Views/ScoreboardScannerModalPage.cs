using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.Communication;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Storage;
using WeightliftingCompWarmup.Core.Models;
using WeightliftingCompWarmup.Core.Services;
using WeightliftingCompWarmup.Maui.Services;
using WeightliftingCompWarmup.Maui.ViewModels;

namespace WeightliftingCompWarmup.Maui.Views;

/// <summary>
/// Unified competition scoreboard scanner modal that analyzes both Snatch and Clean &amp; Jerk
/// in a single view regardless of where it was opened from, preserves its full state across
/// modal closes during the app session, provides a clickable expandable photo preview, and
/// lets the user apply Snatch analysis and Clean &amp; Jerk analysis independently.
/// </summary>
public class ScoreboardScannerModalPage : ContentPage
{
    // In-memory session cache so closing ("X" / "Close") and reopening restores the exact state
    private static byte[]? _cachedPhotoBytes;
    private static string _cachedPhotoMimeType = "image/jpeg";
    private static List<ScoreboardLifterRow> _cachedLifterRows = [];
    private static int _cachedSelectedUserIndex;
    private static int? _cachedSnatchOpenerOverrideKg;
    private static int? _cachedCjOpenerOverrideKg;
    private static bool _cachedOverrideAutoPlusOne = true;
    private static string _cachedStatusMessage = string.Empty;
    private static bool _cachedIsStatusError;
    private static bool _cachedIsSnatchWorkExpanded;
    private static int _cachedSnatchWorkTab;
    private static bool _cachedIsCjWorkExpanded;
    private static int _cachedCjWorkTab;
    private static bool _cachedSnatchApplied;
    private static bool _cachedCjApplied;
    private static bool _cachedIsExcludeListExpanded;
    private static double _cachedScrollY;

    private readonly MainViewModel _viewModel;
    private readonly GeminiScoreboardVisionService _visionService;

    private Grid? _outerGrid;
    private ScrollView? _mainScrollView;
    private VerticalStackLayout? _scrollContent;
    private ContentView? _overlayHost;
    private double _lastScrollY;
    private bool _isRebuildingLayout;

    private byte[]? _photoBytes;
    private string _photoMimeType = "image/jpeg";
    private bool _isPhotoExpanded;
    private double _expandedPhotoZoom = 1.0;

    private List<ScoreboardLifterRow> _lifterRows = [];
    private int _selectedUserIndex;
    private int? _snatchOpenerOverrideKg;
    private int? _cjOpenerOverrideKg;
    private bool _overrideAutoPlusOne = true;
    private bool _isApiKeyExpanded;
    private bool _isExcludeListExpanded;
    private bool _isAnalyzing;
    private string _statusMessage = string.Empty;
    private bool _isStatusError;

    // Live scan progress state & in-place UI references
    private string _scanStageTitle = "Step 1/4: Preparing scoreboard photo...";
    private string _scanDetailMessage = "Initializing scoreboard scanner...";
    private double _scanProgressFraction = 0.10;
    private bool _scanIsRetryOrFallback;
    private readonly System.Diagnostics.Stopwatch _scanStopwatch = new();
    private Label? _progressTitleLabel;
    private Label? _progressDetailLabel;
    private Label? _progressElapsedLabel;
    private ProgressBar? _progressBar;

    private bool _isSnatchWorkExpanded;
    private int _snatchWorkTab; // 0 = Expected Lifts Table, 1 = IWF Call Order Timeline
    private bool _isCjWorkExpanded;
    private int _cjWorkTab;     // 0 = Expected Lifts Table, 1 = IWF Call Order Timeline

    private bool _snatchApplied;
    private bool _cjApplied;

    private CancellationTokenSource? _scanCts;

    private ScoreboardAnalysisResult? _snatchResult;
    private ScoreboardAnalysisResult? _cjResult;

    public ScoreboardScannerModalPage(
        MainViewModel viewModel,
        GeminiScoreboardVisionService? visionService = null)
    {
        _viewModel = viewModel;
        _visionService = visionService ?? new GeminiScoreboardVisionService();

        Title = "AI Scoreboard Scanner (Beta)";
        NavigationPage.SetHasNavigationBar(this, false);

        _isApiKeyExpanded = !_visionService.HasApiKey();

        RestoreStateFromSessionCache();
        if (_lifterRows.Count > 0)
        {
            RecalculateScoreboard();
        }

        BuildLayout();
    }

    private void RestoreStateFromSessionCache()
    {
        _photoBytes = _cachedPhotoBytes;
        _photoMimeType = _cachedPhotoMimeType;
        _lifterRows = _cachedLifterRows;
        _selectedUserIndex = _cachedSelectedUserIndex;
        _snatchOpenerOverrideKg = _cachedSnatchOpenerOverrideKg;
        _cjOpenerOverrideKg = _cachedCjOpenerOverrideKg;
        _overrideAutoPlusOne = _cachedOverrideAutoPlusOne;
        _statusMessage = _cachedStatusMessage;
        _isStatusError = _cachedIsStatusError;
        _isSnatchWorkExpanded = _cachedIsSnatchWorkExpanded;
        _snatchWorkTab = _cachedSnatchWorkTab;
        _isCjWorkExpanded = _cachedIsCjWorkExpanded;
        _cjWorkTab = _cachedCjWorkTab;
        _snatchApplied = _cachedSnatchApplied;
        _cjApplied = _cachedCjApplied;
        _isExcludeListExpanded = _cachedIsExcludeListExpanded;
        _lastScrollY = _cachedScrollY;
    }

    private void SaveStateToSessionCache()
    {
        if (_mainScrollView != null && _mainScrollView.ScrollY >= 0 && !_isRebuildingLayout)
        {
            _lastScrollY = _mainScrollView.ScrollY;
        }

        _cachedPhotoBytes = _photoBytes;
        _cachedPhotoMimeType = _photoMimeType;
        _cachedLifterRows = _lifterRows;
        _cachedSelectedUserIndex = _selectedUserIndex;
        _cachedSnatchOpenerOverrideKg = _snatchOpenerOverrideKg;
        _cachedCjOpenerOverrideKg = _cjOpenerOverrideKg;
        _cachedOverrideAutoPlusOne = _overrideAutoPlusOne;
        _cachedStatusMessage = _statusMessage;
        _cachedIsStatusError = _isStatusError;
        _cachedIsSnatchWorkExpanded = _isSnatchWorkExpanded;
        _cachedSnatchWorkTab = _snatchWorkTab;
        _cachedIsCjWorkExpanded = _isCjWorkExpanded;
        _cachedCjWorkTab = _cjWorkTab;
        _cachedSnatchApplied = _snatchApplied;
        _cachedCjApplied = _cjApplied;
        _cachedIsExcludeListExpanded = _isExcludeListExpanded;
        _cachedScrollY = _lastScrollY;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        AppThemeManager.ThemeChanged += OnThemeChanged;
        RestoreScrollPositionAsync(_lastScrollY);
    }

    protected override void OnDisappearing()
    {
        AppThemeManager.ThemeChanged -= OnThemeChanged;
        _scanCts?.Cancel();
        SaveStateToSessionCache();
        base.OnDisappearing();
    }

    private void OnThemeChanged()
    {
        MainThread.BeginInvokeOnMainThread(BuildLayout);
    }

    private void RestoreScrollPositionAsync(double targetScrollY)
    {
        if (_mainScrollView == null || targetScrollY <= 0)
        {
            _isRebuildingLayout = false;
            return;
        }

        Dispatcher.Dispatch(async () =>
        {
            try
            {
                await Task.Delay(16);
                if (_mainScrollView != null && Math.Abs(_mainScrollView.ScrollY - targetScrollY) > 1.0)
                {
                    await _mainScrollView.ScrollToAsync(0, targetScrollY, animated: false);
                }
            }
            catch
            {
                // Ignore if page was disposed during scroll restore
            }
            finally
            {
                _isRebuildingLayout = false;
            }
        });
    }

    private void UpdateExpandedPhotoOverlayInPlace()
    {
        if (_overlayHost == null)
        {
            return;
        }

        bool showOverlay = _isPhotoExpanded && _photoBytes != null && _photoBytes.Length > 0;
        _overlayHost.IsVisible = showOverlay;
        _overlayHost.InputTransparent = !showOverlay;
        _overlayHost.Content = showOverlay ? BuildExpandedPhotoOverlay() : null;
    }

    private void RecalculateScoreboard()
    {
        if (_lifterRows.Count == 0)
        {
            _snatchResult = null;
            _cjResult = null;
            SaveStateToSessionCache();
            return;
        }

        _selectedUserIndex = Math.Clamp(_selectedUserIndex, 0, _lifterRows.Count - 1);
        if (_lifterRows[_selectedUserIndex].IsExcluded)
        {
            int firstActive = _lifterRows.FindIndex(r => !r.IsExcluded);
            if (firstActive >= 0)
            {
                _selectedUserIndex = firstActive;
            }
            else
            {
                _lifterRows[_selectedUserIndex].IsExcluded = false;
            }
        }

        int effectiveSnatchOpener = _snatchOpenerOverrideKg ?? _viewModel.GetOpenerWeightForDiscipline(LiftType.Snatch);
        _snatchResult = ScoreboardCalculator.AnalyzeScoreboard(
            _lifterRows,
            LiftType.Snatch,
            _selectedUserIndex,
            effectiveSnatchOpener,
            useSetupOpenerForUser: true,
            replaceAutoOneKgAfterMake: _overrideAutoPlusOne);

        int effectiveCjOpener = _cjOpenerOverrideKg ?? _viewModel.GetOpenerWeightForDiscipline(LiftType.CleanAndJerk);
        _cjResult = ScoreboardCalculator.AnalyzeScoreboard(
            _lifterRows,
            LiftType.CleanAndJerk,
            _selectedUserIndex,
            effectiveCjOpener,
            useSetupOpenerForUser: true,
            replaceAutoOneKgAfterMake: _overrideAutoPlusOne);

        SaveStateToSessionCache();
    }

    private void BuildLayout()
    {
        double savedScrollY = _mainScrollView != null && _mainScrollView.ScrollY > 0
            ? _mainScrollView.ScrollY
            : _lastScrollY;
        _lastScrollY = savedScrollY;
        _isRebuildingLayout = true;

        BackgroundColor = AppThemeManager.Color("#0B1120");

        var scrollContent = new VerticalStackLayout
        {
            Spacing = 12,
            Padding = new Thickness(0, 0, 0, 12)
        };

        scrollContent.Children.Add(BuildApiKeyCard());
        scrollContent.Children.Add(BuildCaptureBarCard());

        if (_photoBytes != null && _photoBytes.Length > 0)
        {
            scrollContent.Children.Add(BuildPhotoPreviewCard());
        }

        if (!string.IsNullOrWhiteSpace(_statusMessage))
        {
            scrollContent.Children.Add(BuildStatusBanner());
        }

        if (_snatchResult != null && _cjResult != null && _lifterRows.Count > 0)
        {
            scrollContent.Children.Add(BuildLifterSelectionCard());
            scrollContent.Children.Add(BuildDisciplineAnalysisCard(LiftType.Snatch, _snatchResult));
            scrollContent.Children.Add(BuildDisciplineAnalysisCard(LiftType.CleanAndJerk, _cjResult));
        }
        else if (!_isAnalyzing)
        {
            scrollContent.Children.Add(BuildEmptyStateExplanationCard());
        }

        scrollContent.Children.Add(BuildReportAnalysisErrorCard());

        _scrollContent = scrollContent;

        if (_outerGrid == null || _mainScrollView == null || _overlayHost == null)
        {
            _outerGrid = new Grid
            {
                RowDefinitions =
                {
                    new RowDefinition { Height = GridLength.Auto }, // Header bar
                    new RowDefinition { Height = GridLength.Star }, // Scrollable content
                    new RowDefinition { Height = GridLength.Auto }  // Bottom Close bar
                },
                Padding = new Thickness(12, 10, 12, 10),
                RowSpacing = 10,
                MaximumWidthRequest = 820,
                HorizontalOptions = LayoutOptions.Fill
            };

            _outerGrid.Add(BuildHeaderCard(), 0, 0);

            _mainScrollView = new ScrollView
            {
                Content = _scrollContent
            };
            _mainScrollView.Scrolled += (_, e) =>
            {
                if (!_isRebuildingLayout)
                {
                    _lastScrollY = e.ScrollY;
                    _cachedScrollY = e.ScrollY;
                }
            };
            _outerGrid.Add(_mainScrollView, 0, 1);

            _outerGrid.Add(BuildBottomBar(), 0, 2);

            _overlayHost = new ContentView
            {
                IsVisible = false,
                InputTransparent = true
            };
            Grid.SetRow(_overlayHost, 0);
            Grid.SetRowSpan(_overlayHost, 3);
            _outerGrid.Children.Add(_overlayHost);

            Content = _outerGrid;
        }
        else
        {
            _mainScrollView.Content = _scrollContent;
        }

        UpdateExpandedPhotoOverlayInPlace();
        RestoreScrollPositionAsync(savedScrollY);
    }

    private View BuildHeaderCard()
    {
        var headerBorder = new Border
        {
            BackgroundColor = AppThemeManager.Color("#1E293B"),
            Stroke = AppThemeManager.Color("#334155"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            Padding = new Thickness(12, 10)
        };

        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Auto }
            },
            ColumnSpacing = 8
        };

        var titleStack = new VerticalStackLayout { Spacing = 2 };

        var titleRow = new HorizontalStackLayout
        {
            Spacing = 6,
            VerticalOptions = LayoutOptions.Center
        };
        titleRow.Children.Add(new Label
        {
            Text = "📷 AI Scoreboard Scanner",
            FontSize = 16,
            FontAttributes = FontAttributes.Bold,
            TextColor = AppThemeManager.Color("#F8FAFC"),
            VerticalOptions = LayoutOptions.Center
        });
        titleRow.Children.Add(new Border
        {
            BackgroundColor = Color.FromArgb("#451A03"),
            Stroke = Color.FromArgb("#F59E0B"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 5 },
            Padding = new Thickness(6, 1),
            VerticalOptions = LayoutOptions.Center,
            Content = new Label
            {
                Text = "BETA",
                FontSize = 9.5,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb("#FBBF24"),
                VerticalOptions = LayoutOptions.Center
            }
        });

        titleStack.Children.Add(titleRow);
        titleStack.Children.Add(new Label
        {
            Text = "Beta Feature • Calculates Snatch & Clean & Jerk Lifts Out and Lifts Passed from the scoreboard",
            FontSize = 11,
            TextColor = AppThemeManager.Color("#94A3B8")
        });

        var closeBtn = new Button
        {
            Text = "✕",
            FontSize = 14,
            FontAttributes = FontAttributes.Bold,
            BackgroundColor = AppThemeManager.Color("#334155"),
            TextColor = AppThemeManager.Color("#F8FAFC"),
            CornerRadius = 8,
            WidthRequest = 36,
            HeightRequest = 36,
            Padding = Thickness.Zero,
            VerticalOptions = LayoutOptions.Center
        };
        closeBtn.Clicked += async (_, _) =>
        {
            SaveStateToSessionCache();
            await Navigation.PopModalAsync();
        };

        grid.Add(titleStack, 0, 0);
        grid.Add(closeBtn, 1, 0);
        headerBorder.Content = grid;
        return headerBorder;
    }

    private View BuildApiKeyCard()
    {
        bool hasKey = _visionService.HasApiKey();
        string badgeText = hasKey ? $"🔑 Key Ready ({_visionService.GetMaskedApiKey()})" : "⚠️ Free Gemini API Key Required";
        string badgeColorHex = hasKey ? "#16A34A" : "#D97706";

        var card = new Border
        {
            BackgroundColor = AppThemeManager.Color("#1E293B"),
            Stroke = AppThemeManager.Color(hasKey ? "#334155" : "#D97706"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            Padding = new Thickness(12, 10)
        };

        var stack = new VerticalStackLayout { Spacing = 8 };

        var headerGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Auto }
            },
            ColumnSpacing = 8
        };

        var leftStack = new VerticalStackLayout { Spacing = 2 };
        leftStack.Children.Add(new Label
        {
            Text = badgeText,
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb(badgeColorHex),
            LineBreakMode = LineBreakMode.WordWrap
        });
        leftStack.Children.Add(new Label
        {
            Text = "🔒 Stored strictly on this device only — never exported or shared",
            FontSize = 11,
            TextColor = AppThemeManager.Color("#94A3B8"),
            LineBreakMode = LineBreakMode.WordWrap
        });

        var detailsStack = new VerticalStackLayout
        {
            Spacing = 8,
            IsVisible = _isApiKeyExpanded
        };

        var toggleBtn = new Button
        {
            Text = _isApiKeyExpanded ? "▲ Hide Key Setup" : "⚙️ API Key & Instructions",
            FontSize = 11,
            FontAttributes = FontAttributes.Bold,
            BackgroundColor = AppThemeManager.Color("#0F172A"),
            TextColor = AppThemeManager.Color("#38BDF8"),
            BorderColor = AppThemeManager.Color("#334155"),
            BorderWidth = 1,
            CornerRadius = 6,
            HeightRequest = 30,
            Padding = new Thickness(10, 0),
            VerticalOptions = LayoutOptions.Center
        };
        toggleBtn.Clicked += (_, _) =>
        {
            _isApiKeyExpanded = !_isApiKeyExpanded;
            detailsStack.IsVisible = _isApiKeyExpanded;
            toggleBtn.Text = _isApiKeyExpanded ? "▲ Hide Key Setup" : "⚙️ API Key & Instructions";
        };

        headerGrid.Add(leftStack, 0, 0);
        headerGrid.Add(toggleBtn, 1, 0);
        stack.Children.Add(headerGrid);

        var instructionsBox = new Border
        {
            BackgroundColor = AppThemeManager.Color("#0F172A"),
            Stroke = AppThemeManager.Color("#1E3A8A"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Padding = new Thickness(10, 8)
        };

        var instStack = new VerticalStackLayout { Spacing = 5 };
        instStack.Children.Add(new Label
        {
            Text = "HOW TO GET YOUR FREE GOOGLE GEMINI API KEY (TAKES 30 SECONDS):",
            FontSize = 11,
            FontAttributes = FontAttributes.Bold,
            TextColor = AppThemeManager.Color("#38BDF8")
        });
        instStack.Children.Add(new Label
        {
            Text = "1. Tap '🌐 Get Free Key at Google AI Studio' below and sign in with your Google account.\n" +
                   "2. Tap 'Create API key' and copy the key string (starts with AIza...).\n" +
                   "3. Paste it into the box below and tap 'Save Key'.\n" +
                   "• Privacy Guarantee: Your key stays in local device storage only. It is never included in profile JSON backups and is only sent directly to Google's Gemini endpoint when analyzing a scoreboard image.",
            FontSize = 11.5,
            LineHeight = 1.2,
            TextColor = AppThemeManager.Color("#CBD5E1")
        });

        var openStudioBtn = new Button
        {
            Text = "🌐 Get Free Key at Google AI Studio",
            FontSize = 11.5,
            FontAttributes = FontAttributes.Bold,
            BackgroundColor = AppThemeManager.Color("#0284C7"),
            TextColor = Colors.White,
            CornerRadius = 6,
            HeightRequest = 34,
            Margin = new Thickness(0, 4, 0, 0)
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
        instStack.Children.Add(openStudioBtn);
        instructionsBox.Content = instStack;
        detailsStack.Children.Add(instructionsBox);

        var keyEntry = new Entry
        {
            Placeholder = "Paste Gemini API Key (AIza...)",
            PlaceholderColor = AppThemeManager.Color("#64748B"),
            Text = _visionService.GetApiKey(),
            IsPassword = true,
            FontSize = 13,
            TextColor = AppThemeManager.Color("#F8FAFC"),
            BackgroundColor = AppThemeManager.Color("#0F172A"),
            HeightRequest = 38
        };

        var btnRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Auto }
            },
            ColumnSpacing = 8
        };

        var showHideBtn = new Button
        {
            Text = "👁 Show",
            FontSize = 11,
            BackgroundColor = AppThemeManager.Color("#334155"),
            TextColor = AppThemeManager.Color("#E2E8F0"),
            CornerRadius = 6,
            HeightRequest = 36,
            Padding = new Thickness(10, 0)
        };
        showHideBtn.Clicked += (_, _) =>
        {
            keyEntry.IsPassword = !keyEntry.IsPassword;
            showHideBtn.Text = keyEntry.IsPassword ? "👁 Show" : "🙈 Hide";
        };

        var clearKeyBtn = new Button
        {
            Text = "🗑 Clear",
            FontSize = 11,
            BackgroundColor = AppThemeManager.Color("#7F1D1D"),
            TextColor = Colors.White,
            CornerRadius = 6,
            HeightRequest = 36,
            Padding = new Thickness(10, 0)
        };
        clearKeyBtn.Clicked += (_, _) =>
        {
            _visionService.SaveApiKey(string.Empty);
            keyEntry.Text = string.Empty;
            _statusMessage = "Cleared saved Gemini API key from this device.";
            _isStatusError = false;
            SaveStateToSessionCache();
            BuildLayout();
        };

        var saveKeyBtn = new Button
        {
            Text = "💾 Save Key",
            FontSize = 12,
            FontAttributes = FontAttributes.Bold,
            BackgroundColor = Color.FromArgb("#16A34A"),
            TextColor = Colors.White,
            CornerRadius = 6,
            HeightRequest = 36
        };
        saveKeyBtn.Clicked += (_, _) =>
        {
            string entered = keyEntry.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(entered))
            {
                _statusMessage = "Please paste a valid Gemini API key before saving.";
                _isStatusError = true;
                SaveStateToSessionCache();
                BuildLayout();
                return;
            }

            _visionService.SaveApiKey(entered);
            _isApiKeyExpanded = false;
            _statusMessage = "Gemini API key saved locally on this device.";
            _isStatusError = false;
            SaveStateToSessionCache();
            BuildLayout();
        };

        btnRow.Add(saveKeyBtn, 0, 0);
        btnRow.Add(showHideBtn, 1, 0);
        btnRow.Add(clearKeyBtn, 2, 0);

        detailsStack.Children.Add(keyEntry);
        detailsStack.Children.Add(btnRow);
        stack.Children.Add(detailsStack);

        card.Content = stack;
        return card;
    }

    private void ClearAnalysisResults()
    {
        _scanCts?.Cancel();
        _isAnalyzing = false;
        _scanStopwatch.Stop();

        _photoBytes = null;
        _photoMimeType = "image/jpeg";
        _isPhotoExpanded = false;
        _expandedPhotoZoom = 1.0;

        _lifterRows = [];
        _selectedUserIndex = 0;
        _snatchOpenerOverrideKg = null;
        _cjOpenerOverrideKg = null;
        _snatchResult = null;
        _cjResult = null;
        _snatchApplied = false;
        _cjApplied = false;
        _isSnatchWorkExpanded = false;
        _isCjWorkExpanded = false;
        _isExcludeListExpanded = false;

        _statusMessage = "Cleared scoreboard photo and analysis results.";
        _isStatusError = false;
        _lastScrollY = 0;
        _cachedScrollY = 0;

        SaveStateToSessionCache();
        BuildLayout();
    }

    private View BuildCaptureBarCard()
    {
        var card = new Border
        {
            BackgroundColor = AppThemeManager.Color("#1E293B"),
            Stroke = AppThemeManager.Color("#334155"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            Padding = new Thickness(12, 10)
        };

        var stack = new VerticalStackLayout { Spacing = 8 };

        bool hasExistingData = (_photoBytes != null && _photoBytes.Length > 0) || _lifterRows.Count > 0;
        var btnGrid = new Grid
        {
            ColumnDefinitions = hasExistingData
                ? new ColumnDefinitionCollection
                {
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Auto }
                }
                : new ColumnDefinitionCollection
                {
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Star }
                },
            ColumnSpacing = 8
        };

        var cameraBtn = new Button
        {
            Text = "📷 Take Photo",
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            BackgroundColor = AppThemeManager.Color("#0284C7"),
            TextColor = Colors.White,
            CornerRadius = 8,
            HeightRequest = 42,
            IsEnabled = !_isAnalyzing
        };
        cameraBtn.Clicked += async (_, _) => await CaptureOrPickScoreboardAsync(useCamera: true);

        var pickBtn = new Button
        {
            Text = "🖼️ Pick Photo",
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            BackgroundColor = AppThemeManager.Color("#2563EB"),
            TextColor = Colors.White,
            CornerRadius = 8,
            HeightRequest = 42,
            IsEnabled = !_isAnalyzing
        };
        pickBtn.Clicked += async (_, _) => await CaptureOrPickScoreboardAsync(useCamera: false);

        btnGrid.Add(cameraBtn, 0, 0);
        btnGrid.Add(pickBtn, 1, 0);

        if (hasExistingData)
        {
            var clearResultsBtn = new Button
            {
                Text = "🗑️ Clear",
                FontSize = 12,
                FontAttributes = FontAttributes.Bold,
                BackgroundColor = AppThemeManager.Color("#7F1D1D"),
                TextColor = Colors.White,
                CornerRadius = 8,
                HeightRequest = 42,
                Padding = new Thickness(12, 0),
                IsEnabled = !_isAnalyzing
            };
            clearResultsBtn.Clicked += (_, _) => ClearAnalysisResults();
            btnGrid.Add(clearResultsBtn, 2, 0);
        }

        stack.Children.Add(btnGrid);

        if (_isAnalyzing)
        {
            var progressBorder = new Border
            {
                BackgroundColor = AppThemeManager.Color("#0F172A"),
                Stroke = AppThemeManager.Color(_scanIsRetryOrFallback ? "#F59E0B" : "#0284C7"),
                StrokeThickness = 1,
                StrokeShape = new RoundRectangle { CornerRadius = 8 },
                Padding = new Thickness(10, 8)
            };

            var progressStack = new VerticalStackLayout { Spacing = 6 };

            var topRow = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Auto }
                },
                ColumnSpacing = 8,
                HorizontalOptions = LayoutOptions.Fill
            };

            topRow.Add(new ActivityIndicator
            {
                IsRunning = true,
                Color = AppThemeManager.Color(_scanIsRetryOrFallback ? "#FBBF24" : "#38BDF8"),
                WidthRequest = 18,
                HeightRequest = 18,
                VerticalOptions = LayoutOptions.Center
            }, 0, 0);

            _progressTitleLabel = new Label
            {
                Text = _scanStageTitle,
                FontSize = 12,
                FontAttributes = FontAttributes.Bold,
                TextColor = AppThemeManager.Color(_scanIsRetryOrFallback ? "#FBBF24" : "#38BDF8"),
                VerticalOptions = LayoutOptions.Center,
                LineBreakMode = LineBreakMode.WordWrap
            };
            topRow.Add(_progressTitleLabel, 1, 0);

            _progressElapsedLabel = new Label
            {
                Text = $"{(int)_scanStopwatch.Elapsed.TotalSeconds}s",
                FontSize = 11,
                FontAttributes = FontAttributes.Bold,
                TextColor = AppThemeManager.Color("#94A3B8"),
                VerticalOptions = LayoutOptions.Center
            };
            topRow.Add(_progressElapsedLabel, 2, 0);
            progressStack.Children.Add(topRow);

            _progressBar = new ProgressBar
            {
                Progress = Math.Clamp(_scanProgressFraction, 0.05, 1.0),
                ProgressColor = AppThemeManager.Color(_scanIsRetryOrFallback ? "#F59E0B" : "#38BDF8"),
                BackgroundColor = AppThemeManager.Color("#1E293B"),
                HeightRequest = 6
            };
            progressStack.Children.Add(_progressBar);

            var bottomRow = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Auto }
                },
                ColumnSpacing = 6,
                HorizontalOptions = LayoutOptions.Fill
            };

            _progressDetailLabel = new Label
            {
                Text = _scanDetailMessage,
                FontSize = 11,
                TextColor = AppThemeManager.Color("#CBD5E1"),
                VerticalOptions = LayoutOptions.Center,
                LineBreakMode = LineBreakMode.WordWrap
            };
            bottomRow.Add(_progressDetailLabel, 0, 0);

            var cancelScanBtn = new Button
            {
                Text = "⏹ Cancel",
                FontSize = 10.5,
                FontAttributes = FontAttributes.Bold,
                BackgroundColor = AppThemeManager.Color("#7F1D1D"),
                TextColor = Colors.White,
                CornerRadius = 6,
                HeightRequest = 28,
                Padding = new Thickness(8, 0),
                VerticalOptions = LayoutOptions.Center
            };
            cancelScanBtn.Clicked += (_, _) =>
            {
                _scanCts?.Cancel();
            };
            bottomRow.Add(cancelScanBtn, 1, 0);

            progressStack.Children.Add(bottomRow);
            progressBorder.Content = progressStack;
            stack.Children.Add(progressBorder);
        }

        card.Content = stack;
        return card;
    }

    private View BuildPhotoPreviewCard()
    {
        var card = new Border
        {
            BackgroundColor = AppThemeManager.Color("#1E293B"),
            Stroke = AppThemeManager.Color("#334155"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            Padding = new Thickness(12, 10)
        };

        var stack = new VerticalStackLayout { Spacing = 8 };

        var headerRow = new Grid
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

        headerRow.Add(new Label
        {
            Text = "🖼️ ANALYZED PHOTO",
            FontSize = 11,
            FontAttributes = FontAttributes.Bold,
            TextColor = AppThemeManager.Color("#38BDF8"),
            VerticalOptions = LayoutOptions.Center,
            LineBreakMode = LineBreakMode.WordWrap
        }, 0, 0);

        var rescanBtn = new Button
        {
            Text = "🔄 Re-Scan",
            FontSize = 11,
            FontAttributes = FontAttributes.Bold,
            BackgroundColor = AppThemeManager.Color("#0F172A"),
            TextColor = AppThemeManager.Color("#E2E8F0"),
            BorderColor = AppThemeManager.Color("#334155"),
            BorderWidth = 1,
            CornerRadius = 6,
            HeightRequest = 28,
            Padding = new Thickness(8, 0),
            VerticalOptions = LayoutOptions.Center,
            IsEnabled = !_isAnalyzing
        };
        rescanBtn.Clicked += async (_, _) => await AnalyzeLoadedPhotoBytesAsync();
        headerRow.Add(rescanBtn, 1, 0);

        var expandBtn = new Button
        {
            Text = "🔍 Expand",
            FontSize = 11,
            FontAttributes = FontAttributes.Bold,
            BackgroundColor = AppThemeManager.Color("#0F172A"),
            TextColor = AppThemeManager.Color("#38BDF8"),
            BorderColor = AppThemeManager.Color("#334155"),
            BorderWidth = 1,
            CornerRadius = 6,
            HeightRequest = 28,
            Padding = new Thickness(8, 0),
            VerticalOptions = LayoutOptions.Center
        };
        expandBtn.Clicked += (_, _) =>
        {
            _isPhotoExpanded = true;
            _expandedPhotoZoom = 1.0;
            UpdateExpandedPhotoOverlayInPlace();
        };
        headerRow.Add(expandBtn, 2, 0);

        var clearPhotoBtn = new Button
        {
            Text = "🗑️ Clear",
            FontSize = 11,
            FontAttributes = FontAttributes.Bold,
            BackgroundColor = AppThemeManager.Color("#7F1D1D"),
            TextColor = Colors.White,
            CornerRadius = 6,
            HeightRequest = 28,
            Padding = new Thickness(8, 0),
            VerticalOptions = LayoutOptions.Center,
            IsEnabled = !_isAnalyzing
        };
        clearPhotoBtn.Clicked += (_, _) => ClearAnalysisResults();
        headerRow.Add(clearPhotoBtn, 3, 0);

        stack.Children.Add(headerRow);

        byte[] bytesCopy = _photoBytes!;
        var previewImage = new Image
        {
            Source = ImageSource.FromStream(() => new MemoryStream(bytesCopy)),
            Aspect = Aspect.AspectFit,
            HeightRequest = 140,
            HorizontalOptions = LayoutOptions.Fill
        };

        var imageFrame = new Border
        {
            BackgroundColor = AppThemeManager.Color("#090D16"),
            Stroke = AppThemeManager.Color("#334155"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Padding = new Thickness(4),
            Content = previewImage
        };

        var tapExpand = new TapGestureRecognizer();
        tapExpand.Tapped += (_, _) =>
        {
            _isPhotoExpanded = true;
            _expandedPhotoZoom = 1.0;
            UpdateExpandedPhotoOverlayInPlace();
        };
        imageFrame.GestureRecognizers.Add(tapExpand);
        previewImage.GestureRecognizers.Add(tapExpand);

        stack.Children.Add(imageFrame);
        card.Content = stack;
        return card;
    }

    private View BuildExpandedPhotoOverlay()
    {
        var overlayBorder = new Border
        {
            BackgroundColor = Color.FromArgb("#F2050811"),
            Stroke = AppThemeManager.Color("#334155"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            Padding = new Thickness(10)
        };

        var grid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Star },
                new RowDefinition { Height = GridLength.Auto }
            },
            RowSpacing = 8
        };

        var topBar = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Auto }
            },
            ColumnSpacing = 6
        };

        topBar.Add(new Label
        {
            Text = "🖼️ Scoreboard Photo",
            FontSize = 14,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White,
            VerticalOptions = LayoutOptions.Center
        }, 0, 0);

        Button CreateZoomBtn(string text, double zoomLevel)
        {
            bool isActive = Math.Abs(_expandedPhotoZoom - zoomLevel) < 0.05;
            var btn = new Button
            {
                Text = text,
                FontSize = 11,
                FontAttributes = FontAttributes.Bold,
                BackgroundColor = isActive ? Color.FromArgb("#0284C7") : AppThemeManager.Color("#1E293B"),
                TextColor = Colors.White,
                CornerRadius = 6,
                HeightRequest = 34,
                Padding = new Thickness(10, 0)
            };
            btn.Clicked += (_, _) =>
            {
                _expandedPhotoZoom = zoomLevel;
                UpdateExpandedPhotoOverlayInPlace();
            };
            return btn;
        }

        topBar.Add(CreateZoomBtn("1x Fit", 1.0), 1, 0);
        topBar.Add(CreateZoomBtn("2x", 2.0), 2, 0);
        topBar.Add(CreateZoomBtn("3x", 3.0), 3, 0);

        var closeOverlayBtn = new Button
        {
            Text = "✕ Close",
            FontSize = 12,
            FontAttributes = FontAttributes.Bold,
            BackgroundColor = Color.FromArgb("#DC2626"),
            TextColor = Colors.White,
            CornerRadius = 6,
            HeightRequest = 34,
            Padding = new Thickness(12, 0)
        };
        closeOverlayBtn.Clicked += (_, _) =>
        {
            _isPhotoExpanded = false;
            UpdateExpandedPhotoOverlayInPlace();
        };
        topBar.Add(closeOverlayBtn, 4, 0);
        grid.Add(topBar, 0, 0);

        byte[] bytesCopy = _photoBytes!;
        var fullImage = new Image
        {
            Source = ImageSource.FromStream(() => new MemoryStream(bytesCopy)),
            Aspect = Aspect.AspectFit,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };

        if (_expandedPhotoZoom > 1.01)
        {
            fullImage.WidthRequest = 360 * _expandedPhotoZoom;
            fullImage.HeightRequest = 480 * _expandedPhotoZoom;
        }

        var pinch = new PinchGestureRecognizer();
        pinch.PinchUpdated += (_, e) =>
        {
            if (e.Status == GestureStatus.Running)
            {
                double nextZoom = Math.Clamp(_expandedPhotoZoom * e.Scale, 1.0, 4.0);
                fullImage.WidthRequest = 360 * nextZoom;
                fullImage.HeightRequest = 480 * nextZoom;
                _expandedPhotoZoom = nextZoom;
            }
        };
        fullImage.GestureRecognizers.Add(pinch);

        var imageScroll = new ScrollView
        {
            Orientation = ScrollOrientation.Both,
            Content = fullImage
        };
        grid.Add(imageScroll, 0, 1);

        grid.Add(new Label
        {
            Text = "Use 1x / 2x / 3x or pinch to zoom and scroll around the scoreboard • Tap '✕ Close' to return",
            FontSize = 11,
            TextColor = AppThemeManager.Color("#94A3B8"),
            HorizontalTextAlignment = TextAlignment.Center
        }, 0, 2);

        overlayBorder.Content = grid;
        return overlayBorder;
    }

    private View BuildStatusBanner()
    {
        string bgHex = _isStatusError ? "#450A0A" : "#0F291E";
        string borderHex = _isStatusError ? "#DC2626" : "#16A34A";
        string textHex = _isStatusError ? "#FCA5A5" : "#86EFAC";

        return new Border
        {
            BackgroundColor = Color.FromArgb(bgHex),
            Stroke = Color.FromArgb(borderHex),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Padding = new Thickness(10, 8),
            Content = new Label
            {
                Text = _statusMessage,
                FontSize = 12,
                TextColor = Color.FromArgb(textHex)
            }
        };
    }

    private View BuildLifterSelectionCard()
    {
        var card = new Border
        {
            BackgroundColor = AppThemeManager.Color("#1E293B"),
            Stroke = AppThemeManager.Color("#38BDF8"),
            StrokeThickness = 1.5,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            Padding = new Thickness(12, 10)
        };

        var stack = new VerticalStackLayout { Spacing = 10 };

        var identityStack = new VerticalStackLayout { Spacing = 4 };
        identityStack.Children.Add(new Label
        {
            Text = "🎯 WHO ARE YOU ON THE SCOREBOARD? (SELECT YOUR LIFTER):",
            FontSize = 11,
            FontAttributes = FontAttributes.Bold,
            TextColor = AppThemeManager.Color("#38BDF8")
        });

        var pickerItems = new List<string>();
        for (int i = 0; i < _lifterRows.Count; i++)
        {
            var r = _lifterRows[i];
            string snStr = r.Snatch1.WeightKg.HasValue ? $"Sn:{r.Snatch1.WeightKg}kg" : "Sn:—";
            string cjStr = r.Cj1.WeightKg.HasValue ? $"C&J:{r.Cj1.WeightKg}kg" : "C&J:—";
            string prefix = i == _selectedUserIndex ? "🎯 " : (r.IsExcluded ? "🚫 " : string.Empty);
            string leftLabel = !string.IsNullOrWhiteSpace(r.LeftColumnText) && !r.LeftColumnText.All(char.IsDigit)
                ? $"Grp {r.LeftColumnText}"
                : $"Lot {r.EffectiveSeedNumber}";
            pickerItems.Add($"{prefix}#{r.OrderNumber} ({leftLabel}) — {r.Name} [{snStr} | {cjStr}]");
        }

        var lifterPicker = new Picker
        {
            Title = "Select your lifter row",
            ItemsSource = pickerItems,
            SelectedIndex = Math.Clamp(_selectedUserIndex, 0, _lifterRows.Count - 1),
            TextColor = AppThemeManager.Color("#F8FAFC"),
            BackgroundColor = AppThemeManager.Color("#0F172A"),
            FontSize = 13,
            HeightRequest = 40
        };
        lifterPicker.SelectedIndexChanged += (_, _) =>
        {
            if (lifterPicker.SelectedIndex >= 0 && lifterPicker.SelectedIndex != _selectedUserIndex)
            {
                _selectedUserIndex = lifterPicker.SelectedIndex;
                _lifterRows[_selectedUserIndex].IsExcluded = false;
                _snatchOpenerOverrideKg = null;
                _cjOpenerOverrideKg = null;
                _snatchApplied = false;
                _cjApplied = false;
                RecalculateScoreboard();
                BuildLayout();
            }
        };
        identityStack.Children.Add(lifterPicker);
        stack.Children.Add(identityStack);

        // Exclude / Include Lifters control (for prior-session rows or scratched lifters)
        int excludedCount = _lifterRows.Count(r => r.IsExcluded);
        int includedCount = _lifterRows.Count - excludedCount;

        var excludeSummaryRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Auto }
            },
            ColumnSpacing = 8,
            Padding = new Thickness(2, 2)
        };

        var excludeInfoStack = new VerticalStackLayout { Spacing = 1, VerticalOptions = LayoutOptions.Center };
        excludeInfoStack.Children.Add(new Label
        {
            Text = excludedCount > 0
                ? $"🚫 {excludedCount} Lifter Row(s) Excluded ({includedCount} of {_lifterRows.Count} Active)"
                : $"👥 All {_lifterRows.Count} Lifter Rows Included in Analysis",
            FontSize = 12,
            FontAttributes = FontAttributes.Bold,
            TextColor = excludedCount > 0 ? Color.FromArgb("#FBBF24") : AppThemeManager.Color("#E2E8F0")
        });
        excludeInfoStack.Children.Add(new Label
        {
            Text = "Exclude prior-session leaders (e.g. Group D) or scratched lifters",
            FontSize = 10.5,
            TextColor = AppThemeManager.Color("#94A3B8")
        });

        var excludeListHost = new ContentView
        {
            IsVisible = _isExcludeListExpanded,
            Content = _isExcludeListExpanded ? BuildExcludeLiftersChecklist() : null
        };

        var toggleExcludeBtn = new Button
        {
            Text = _isExcludeListExpanded
                ? "▲ Hide List"
                : (excludedCount > 0 ? $"⚙️ Manage ({excludedCount} Excl.)" : "🚫 Exclude Rows"),
            FontSize = 11,
            FontAttributes = FontAttributes.Bold,
            BackgroundColor = excludedCount > 0 ? Color.FromArgb("#451A03") : AppThemeManager.Color("#0F172A"),
            TextColor = excludedCount > 0 ? Color.FromArgb("#FDE68A") : AppThemeManager.Color("#38BDF8"),
            BorderColor = excludedCount > 0 ? Color.FromArgb("#F59E0B") : AppThemeManager.Color("#334155"),
            BorderWidth = 1,
            CornerRadius = 6,
            HeightRequest = 32,
            Padding = new Thickness(10, 0),
            VerticalOptions = LayoutOptions.Center
        };
        toggleExcludeBtn.Clicked += (_, _) =>
        {
            _isExcludeListExpanded = !_isExcludeListExpanded;
            toggleExcludeBtn.Text = _isExcludeListExpanded
                ? "▲ Hide List"
                : (excludedCount > 0 ? $"⚙️ Manage ({excludedCount} Excl.)" : "🚫 Exclude Rows");
            excludeListHost.IsVisible = _isExcludeListExpanded;
            excludeListHost.Content = _isExcludeListExpanded ? BuildExcludeLiftersChecklist() : null;
            SaveStateToSessionCache();
        };

        excludeSummaryRow.Add(excludeInfoStack, 0, 0);
        excludeSummaryRow.Add(toggleExcludeBtn, 1, 0);
        stack.Children.Add(excludeSummaryRow);
        stack.Children.Add(excludeListHost);

        var toggleRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Auto }
            },
            ColumnSpacing = 8,
            Padding = new Thickness(4, 2)
        };
        var toggleLabels = new VerticalStackLayout { Spacing = 1, VerticalOptions = LayoutOptions.Center };
        toggleLabels.Children.Add(new Label
        {
            Text = "Replace auto +1 kg board placeholders with estimated jumps",
            FontSize = 12,
            FontAttributes = FontAttributes.Bold,
            TextColor = AppThemeManager.Color("#E2E8F0")
        });
        toggleLabels.Children.Add(new Label
        {
            Text = "Scoreboards auto-fill +1 kg after a make until the coach declares the real jump",
            FontSize = 10.5,
            TextColor = AppThemeManager.Color("#94A3B8")
        });

        var autoPlusOneSwitch = new Switch
        {
            IsToggled = _overrideAutoPlusOne,
            OnColor = Color.FromArgb("#0284C7"),
            VerticalOptions = LayoutOptions.Center
        };
        autoPlusOneSwitch.Toggled += (_, e) =>
        {
            if (_overrideAutoPlusOne != e.Value)
            {
                _overrideAutoPlusOne = e.Value;
                _snatchApplied = false;
                _cjApplied = false;
                RecalculateScoreboard();
                BuildLayout();
            }
        };

        toggleRow.Add(toggleLabels, 0, 0);
        toggleRow.Add(autoPlusOneSwitch, 1, 0);
        stack.Children.Add(toggleRow);

        card.Content = stack;
        return card;
    }

    private void ToggleLifterExclusion(int lifterIndex)
    {
        if (lifterIndex < 0 || lifterIndex >= _lifterRows.Count)
        {
            return;
        }

        _lifterRows[lifterIndex].IsExcluded = !_lifterRows[lifterIndex].IsExcluded;
        _snatchApplied = false;
        _cjApplied = false;
        RecalculateScoreboard();
        BuildLayout();
    }

    private View BuildExcludeLiftersChecklist()
    {
        var border = new Border
        {
            BackgroundColor = AppThemeManager.Color("#0F172A"),
            Stroke = AppThemeManager.Color("#334155"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Padding = new Thickness(10, 8)
        };

        var stack = new VerticalStackLayout { Spacing = 6 };

        var quickActionRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Star }
            },
            ColumnSpacing = 8
        };

        var includeAllBtn = new Button
        {
            Text = "↩️ Include All Rows",
            FontSize = 11,
            FontAttributes = FontAttributes.Bold,
            BackgroundColor = AppThemeManager.Color("#1E293B"),
            TextColor = Color.FromArgb("#4ADE80"),
            BorderColor = AppThemeManager.Color("#334155"),
            BorderWidth = 1,
            CornerRadius = 6,
            HeightRequest = 30
        };
        includeAllBtn.Clicked += (_, _) =>
        {
            foreach (var r in _lifterRows)
            {
                r.IsExcluded = false;
            }
            _snatchApplied = false;
            _cjApplied = false;
            RecalculateScoreboard();
            BuildLayout();
        };

        var excludeBottom3Btn = new Button
        {
            Text = "🚫 Exclude Bottom 3",
            FontSize = 11,
            FontAttributes = FontAttributes.Bold,
            BackgroundColor = AppThemeManager.Color("#1E293B"),
            TextColor = Color.FromArgb("#FCA5A5"),
            BorderColor = AppThemeManager.Color("#334155"),
            BorderWidth = 1,
            CornerRadius = 6,
            HeightRequest = 30,
            IsEnabled = _lifterRows.Count > 3
        };
        excludeBottom3Btn.Clicked += (_, _) =>
        {
            int startExclude = Math.Max(1, _lifterRows.Count - 3);
            for (int i = startExclude; i < _lifterRows.Count; i++)
            {
                if (i != _selectedUserIndex)
                {
                    _lifterRows[i].IsExcluded = true;
                }
            }
            _snatchApplied = false;
            _cjApplied = false;
            RecalculateScoreboard();
            BuildLayout();
        };

        quickActionRow.Add(includeAllBtn, 0, 0);
        quickActionRow.Add(excludeBottom3Btn, 1, 0);
        stack.Children.Add(quickActionRow);

        for (int i = 0; i < _lifterRows.Count; i++)
        {
            int idx = i;
            var r = _lifterRows[i];
            bool isUser = i == _selectedUserIndex;
            bool isExcl = r.IsExcluded;

            var rowGrid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Auto }
                },
                ColumnSpacing = 8,
                Padding = new Thickness(4, 2)
            };

            string leftLabel = !string.IsNullOrWhiteSpace(r.LeftColumnText) && !r.LeftColumnText.All(char.IsDigit)
                ? $"Grp {r.LeftColumnText}"
                : $"#{r.EffectiveSeedNumber}";
            string snStr = r.Snatch1.WeightKg.HasValue ? $"Sn:{r.Snatch1.WeightKg}" : "Sn:—";
            string cjStr = r.Cj1.WeightKg.HasValue ? $"CJ:{r.Cj1.WeightKg}" : "CJ:—";

            rowGrid.Add(new Label
            {
                Text = $"{(isUser ? "🎯 " : string.Empty)}{leftLabel} {r.Name} ({snStr} | {cjStr})",
                FontSize = 11.5,
                FontAttributes = isUser ? FontAttributes.Bold : FontAttributes.None,
                TextColor = isExcl
                    ? Color.FromArgb("#94A3B8")
                    : (isUser ? Color.FromArgb("#38BDF8") : AppThemeManager.Color("#F8FAFC")),
                TextDecorations = isExcl ? TextDecorations.Strikethrough : TextDecorations.None,
                VerticalOptions = LayoutOptions.Center,
                LineBreakMode = LineBreakMode.TailTruncation
            }, 0, 0);

            var rowToggleBtn = new Button
            {
                Text = isExcl ? "🚫 Excluded (Tap=Include)" : (isUser ? "🎯 YOU" : "✅ Included"),
                FontSize = 10.5,
                FontAttributes = FontAttributes.Bold,
                BackgroundColor = isExcl
                    ? Color.FromArgb("#450A0A")
                    : (isUser ? Color.FromArgb("#0369A1") : Color.FromArgb("#064E3B")),
                TextColor = isExcl ? Color.FromArgb("#FCA5A5") : Colors.White,
                CornerRadius = 6,
                HeightRequest = 28,
                Padding = new Thickness(8, 0),
                IsEnabled = !isUser
            };
            rowToggleBtn.Clicked += (_, _) => ToggleLifterExclusion(idx);
            rowGrid.Add(rowToggleBtn, 1, 0);

            stack.Children.Add(rowGrid);
        }

        border.Content = stack;
        return border;
    }

    private View BuildDisciplineAnalysisCard(LiftType discipline, ScoreboardAnalysisResult result)
    {
        bool isSnatch = discipline == LiftType.Snatch;
        string disciplineTitle = isSnatch ? "SNATCH ANALYSIS" : "CLEAN & JERK ANALYSIS";
        string disciplineShort = isSnatch ? "Snatch" : "Clean & Jerk";
        string accentHex = isSnatch ? "#0284C7" : "#7C3AED";
        string headerBadgeHex = isSnatch ? "#0369A1" : "#6D28D9";

        var card = new Border
        {
            BackgroundColor = AppThemeManager.Color("#1E293B"),
            Stroke = Color.FromArgb(accentHex),
            StrokeThickness = 1.5,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            Padding = new Thickness(12, 10)
        };

        var stack = new VerticalStackLayout { Spacing = 10 };

        // Block Header Row
        var headerRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Auto }
            }
        };

        var badgeBorder = new Border
        {
            BackgroundColor = Color.FromArgb(headerBadgeHex),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 6 },
            Padding = new Thickness(8, 3),
            HorizontalOptions = LayoutOptions.Start,
            Content = new Label
            {
                Text = $"🏋️ {disciplineTitle}",
                FontSize = 11.5,
                FontAttributes = FontAttributes.Bold,
                TextColor = Colors.White
            }
        };
        headerRow.Add(badgeBorder, 0, 0);

        bool wasApplied = isSnatch ? _snatchApplied : _cjApplied;
        if (wasApplied)
        {
            headerRow.Add(new Label
            {
                Text = "✓ Applied to Warmup",
                FontSize = 11,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb("#4ADE80"),
                VerticalOptions = LayoutOptions.Center
            }, 1, 0);
        }

        stack.Children.Add(headerRow);

        int existingLiftsOut = _viewModel.GetLiftsOutForDiscipline(discipline);

        // State 1: Discipline has concluded (e.g., Snatch is finished and C&J is up next)
        if (result.IsDisciplineConcluded)
        {
            stack.Children.Add(new Border
            {
                BackgroundColor = AppThemeManager.Color("#0F172A"),
                Stroke = Color.FromArgb("#64748B"),
                StrokeThickness = 1,
                StrokeShape = new RoundRectangle { CornerRadius = 8 },
                Padding = new Thickness(12, 10),
                Content = new VerticalStackLayout
                {
                    Spacing = 4,
                    Children =
                    {
                        new Label
                        {
                            Text = $"🏁 {result.DisciplineConcludedMessage ?? $"{disciplineShort} has concluded."}",
                            FontSize = 13,
                            FontAttributes = FontAttributes.Bold,
                            TextColor = AppThemeManager.Color("#F8FAFC")
                        },
                        new Label
                        {
                            Text = $"All {disciplineShort} attempts on this board are finished ({result.LiftsPassed} completed lifts). No {disciplineShort} warmup calculation is needed.",
                            FontSize = 11.5,
                            TextColor = AppThemeManager.Color("#94A3B8")
                        }
                    }
                }
            });
        }
        // State 2: User has already lifted their opener on stage
        else if (result.IsUserOpenerCompleted)
        {
            stack.Children.Add(new Border
            {
                BackgroundColor = Color.FromArgb("#064E3B"),
                Stroke = Color.FromArgb("#10B981"),
                StrokeThickness = 1,
                StrokeShape = new RoundRectangle { CornerRadius = 8 },
                Padding = new Thickness(12, 10),
                Content = new VerticalStackLayout
                {
                    Spacing = 4,
                    Children =
                    {
                        new Label
                        {
                            Text = $"✅ {result.UserOpenerCompletedMessage ?? $"You have already lifted your {disciplineShort} opener on stage — the app is done with {disciplineShort}."}",
                            FontSize = 13,
                            FontAttributes = FontAttributes.Bold,
                            TextColor = Color.FromArgb("#A7F3D0")
                        },
                        new Label
                        {
                            Text = $"Once your opener is lifted on stage, Lifts Out is no longer calculated for {disciplineShort}.",
                            FontSize = 11.5,
                            TextColor = Color.FromArgb("#D1FAE5")
                        }
                    }
                }
            });
        }
        // State 3: No weights on the board for this discipline yet
        else if (!result.HasDisciplineWeightsOnBoard)
        {
            var noWeightsStack = new VerticalStackLayout { Spacing = 8 };
            noWeightsStack.Children.Add(new Label
            {
                Text = $"ℹ️ No {disciplineShort} weights are listed on this scoreboard yet.",
                FontSize = 12,
                TextColor = AppThemeManager.Color("#94A3B8")
            });

            var changeOpenerBtn = new Button
            {
                Text = $"✏️ Your {disciplineShort} Opener: {result.UserOpenerWeightKg} kg (Tap to change)",
                FontSize = 12,
                FontAttributes = FontAttributes.Bold,
                BackgroundColor = AppThemeManager.Color("#1E293B"),
                TextColor = Color.FromArgb("#FBBF24"),
                BorderColor = Color.FromArgb("#F59E0B"),
                BorderWidth = 1,
                CornerRadius = 7,
                HeightRequest = 36
            };
            changeOpenerBtn.Clicked += async (_, _) => await PromptOverrideUserOpenerAsync(discipline, result.UserOpenerWeightKg);
            noWeightsStack.Children.Add(changeOpenerBtn);

            stack.Children.Add(new Border
            {
                BackgroundColor = AppThemeManager.Color("#0F172A"),
                Stroke = AppThemeManager.Color("#334155"),
                StrokeThickness = 1,
                StrokeShape = new RoundRectangle { CornerRadius = 8 },
                Padding = new Thickness(12, 10),
                Content = noWeightsStack
            });
        }
        // State 4: Active discipline where user's opener has not yet been lifted
        else
        {
            if (result.IsBarPastUserOpener)
            {
                stack.Children.Add(new Border
                {
                    BackgroundColor = Color.FromArgb("#451A03"),
                    Stroke = Color.FromArgb("#F59E0B"),
                    StrokeThickness = 1.5,
                    StrokeShape = new RoundRectangle { CornerRadius = 8 },
                    Padding = new Thickness(10, 8),
                    Content = new Label
                    {
                        Text = $"⚠️ {result.BarPastUserOpenerMessage ?? $"Cannot determine Lifts Out because {result.MaxCompletedWeightKg} kg has already been lifted on the scoreboard, which is heavier than your {result.UserOpenerWeightKg} kg Opener in the app. Lifts Out will be left as-is."} (Current {disciplineShort} Lifts Out: {existingLiftsOut})",
                        FontSize = 11.5,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = Color.FromArgb("#FDE68A")
                    }
                });
            }

            bool showSnatchLeft = !isSnatch && (result.RemainingSnatchLifts ?? 0) > 0;
            var metricsGrid = new Grid
            {
                ColumnDefinitions = showSnatchLeft
                    ? new ColumnDefinitionCollection
                    {
                        new ColumnDefinition { Width = GridLength.Star },
                        new ColumnDefinition { Width = GridLength.Star },
                        new ColumnDefinition { Width = GridLength.Star },
                        new ColumnDefinition { Width = GridLength.Star }
                    }
                    : new ColumnDefinitionCollection
                    {
                        new ColumnDefinition { Width = GridLength.Star },
                        new ColumnDefinition { Width = GridLength.Star },
                        new ColumnDefinition { Width = GridLength.Star }
                    },
                ColumnSpacing = 8
            };

            string liftsOutDisplay = result.IsBarPastUserOpener ? "N/A" : result.LiftsOut.ToString();
            string liftsOutSubtext = result.IsBarPastUserOpener
                ? $"Kept as-is ({existingLiftsOut})"
                : "Before your opener";
            string liftsOutColorHex = result.IsBarPastUserOpener ? "#F59E0B" : "#38BDF8";

            metricsGrid.Add(BuildMetricTile(
                "LIFTS OUT",
                liftsOutDisplay,
                liftsOutSubtext,
                liftsOutColorHex), 0, 0);

            metricsGrid.Add(BuildMetricTile(
                "LIFTS PASSED",
                result.LiftsPassed.ToString(),
                "Completed on stage",
                "#4ADE80"), 1, 0);

            string openerSubtext = result.BoardDeclaredUserOpenerKg is > 0 && result.BoardDeclaredUserOpenerKg.Value != result.UserOpenerWeightKg
                ? $"Tap to edit (Board: {result.BoardDeclaredUserOpenerKg}kg)"
                : "Tap to change (Setup & Live)";

            var openerTile = BuildMetricTile(
                $"✏️ YOUR {(isSnatch ? "SNATCH" : "C&J")} OPENER",
                $"{result.UserOpenerWeightKg} kg",
                openerSubtext,
                "#FBBF24",
                borderHex: "#F59E0B");
            var openerTap = new TapGestureRecognizer();
            openerTap.Tapped += async (_, _) => await PromptOverrideUserOpenerAsync(discipline, result.UserOpenerWeightKg);
            openerTile.GestureRecognizers.Add(openerTap);
            metricsGrid.Add(openerTile, 2, 0);

            if (showSnatchLeft)
            {
                metricsGrid.Add(BuildMetricTile(
                    "SNATCH LEFT",
                    (result.RemainingSnatchLifts ?? 0).ToString(),
                    "Still in Snatch",
                    "#C084FC"), 3, 0);
            }

            stack.Children.Add(metricsGrid);

            string applyBtnText;
            if (result.IsBarPastUserOpener)
            {
                applyBtnText = wasApplied
                    ? $"✓ {disciplineShort} Applied ({result.LiftsPassed} Passed • Keep Lifts Out)"
                    : $"✅ Apply {disciplineShort} Analysis ({result.LiftsPassed} Passed • Keep Lifts Out)";
            }
            else
            {
                string extraSnatch = showSnatchLeft ? $", {result.RemainingSnatchLifts} Snatch Left" : string.Empty;
                applyBtnText = wasApplied
                    ? $"✓ {disciplineShort} Applied ({result.LiftsOut} Out, {result.LiftsPassed} Passed{extraSnatch})"
                    : $"✅ Apply {disciplineShort} Analysis ({result.LiftsOut} Out, {result.LiftsPassed} Passed{extraSnatch})";
            }

            var applyDisciplineBorder = new Border
            {
                BackgroundColor = wasApplied ? Color.FromArgb("#15803D") : Color.FromArgb(accentHex),
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 8 },
                Padding = new Thickness(12, 10),
                MinimumHeightRequest = 42,
                Content = new Label
                {
                    Text = applyBtnText,
                    FontSize = 12.5,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Colors.White,
                    HorizontalTextAlignment = TextAlignment.Center,
                    VerticalTextAlignment = TextAlignment.Center,
                    LineBreakMode = LineBreakMode.WordWrap
                }
            };
            var applyTap = new TapGestureRecognizer();
            applyTap.Tapped += (_, _) =>
            {
                int? remainingSnatch = !isSnatch ? result.RemainingSnatchLifts : null;
                int? liftsOutToApply = result.IsBarPastUserOpener ? null : result.LiftsOut;
                int? openerOverride = isSnatch ? _snatchOpenerOverrideKg : _cjOpenerOverrideKg;

                _viewModel.ApplyScoreboardScanResult(
                    liftsOut: liftsOutToApply,
                    liftsPassed: result.LiftsPassed,
                    remainingSnatchLifts: remainingSnatch,
                    updatedOpenerWeightKg: openerOverride,
                    targetDiscipline: discipline,
                    switchActiveDiscipline: false);

                if (isSnatch)
                {
                    _snatchApplied = true;
                }
                else
                {
                    _cjApplied = true;
                }

                _statusMessage = $"Applied {disciplineShort} analysis to your {disciplineShort} warmup setup.";
                _isStatusError = false;
                SaveStateToSessionCache();
                BuildLayout();
            };
            applyDisciplineBorder.GestureRecognizers.Add(applyTap);

            stack.Children.Add(applyDisciplineBorder);
        }

        // Collapsible "Show Work" toggle for this discipline (updates in-place to hold scroll position)
        bool isWorkExpanded = isSnatch ? _isSnatchWorkExpanded : _isCjWorkExpanded;
        var workSectionHost = new ContentView
        {
            IsVisible = isWorkExpanded,
            Content = isWorkExpanded ? BuildDisciplineWorkSection(discipline, result) : null
        };

        var toggleWorkLabel = new Label
        {
            Text = isWorkExpanded
                ? $"▲ Hide {disciplineShort} Work & Call Order"
                : $"🔍 Show {disciplineShort} Work & Call Order ({result.LifterSummaries.Count} Lifters)",
            FontSize = 11.5,
            FontAttributes = FontAttributes.Bold,
            TextColor = AppThemeManager.Color("#38BDF8"),
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.WordWrap
        };

        var toggleWorkBorder = new Border
        {
            BackgroundColor = AppThemeManager.Color("#0F172A"),
            Stroke = AppThemeManager.Color("#334155"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 7 },
            Padding = new Thickness(10, 8),
            MinimumHeightRequest = 36,
            Content = toggleWorkLabel
        };
        var toggleWorkTap = new TapGestureRecognizer();
        toggleWorkTap.Tapped += (_, _) =>
        {
            bool nowExpanded;
            if (isSnatch)
            {
                _isSnatchWorkExpanded = !_isSnatchWorkExpanded;
                nowExpanded = _isSnatchWorkExpanded;
            }
            else
            {
                _isCjWorkExpanded = !_isCjWorkExpanded;
                nowExpanded = _isCjWorkExpanded;
            }

            toggleWorkLabel.Text = nowExpanded
                ? $"▲ Hide {disciplineShort} Work & Call Order"
                : $"🔍 Show {disciplineShort} Work & Call Order ({result.LifterSummaries.Count} Lifters)";
            workSectionHost.IsVisible = nowExpanded;
            workSectionHost.Content = nowExpanded ? BuildDisciplineWorkSection(discipline, result) : null;
            SaveStateToSessionCache();
        };
        toggleWorkBorder.GestureRecognizers.Add(toggleWorkTap);
        stack.Children.Add(toggleWorkBorder);
        stack.Children.Add(workSectionHost);

        card.Content = stack;
        return card;
    }

    private View BuildMetricTile(string label, string value, string subtext, string accentHex, string? borderHex = null)
    {
        return new Border
        {
            BackgroundColor = AppThemeManager.Color("#0F172A"),
            Stroke = borderHex != null ? Color.FromArgb(borderHex) : AppThemeManager.Color("#334155"),
            StrokeThickness = borderHex != null ? 1.5 : 1,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Padding = new Thickness(6, 7),
            Content = new VerticalStackLayout
            {
                Spacing = 2,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    new Label
                    {
                        Text = label,
                        FontSize = 9.5,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = borderHex != null ? Color.FromArgb("#FDE68A") : AppThemeManager.Color("#94A3B8"),
                        HorizontalTextAlignment = TextAlignment.Center,
                        LineBreakMode = LineBreakMode.WordWrap
                    },
                    new Label
                    {
                        Text = value,
                        FontSize = 18,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = Color.FromArgb(accentHex),
                        HorizontalTextAlignment = TextAlignment.Center,
                        LineBreakMode = LineBreakMode.NoWrap
                    },
                    new Label
                    {
                        Text = subtext,
                        FontSize = 9,
                        TextColor = borderHex != null ? Color.FromArgb("#FBBF24") : AppThemeManager.Color("#64748B"),
                        HorizontalTextAlignment = TextAlignment.Center,
                        LineBreakMode = LineBreakMode.WordWrap
                    }
                }
            }
        };
    }

    private View BuildDisciplineWorkSection(LiftType discipline, ScoreboardAnalysisResult result)
    {
        bool isSnatch = discipline == LiftType.Snatch;
        int activeTab = isSnatch ? _snatchWorkTab : _cjWorkTab;

        var stack = new VerticalStackLayout
        {
            Spacing = 10,
            Padding = new Thickness(0, 4, 0, 0)
        };

        var tabGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Star }
            },
            ColumnSpacing = 8
        };

        var tabContentHost = new ContentView
        {
            Content = activeTab == 0
                ? BuildExpectedLiftsTable(discipline, result)
                : BuildCallOrderTimeline(discipline, result)
        };

        var expectedTabBtn = new Button
        {
            Text = $"📋 Expected Lifts ({result.LifterSummaries.Count})",
            FontSize = 12,
            FontAttributes = FontAttributes.Bold,
            BackgroundColor = activeTab == 0 ? AppThemeManager.Color("#0284C7") : AppThemeManager.Color("#0F172A"),
            TextColor = activeTab == 0 ? Colors.White : AppThemeManager.Color("#94A3B8"),
            CornerRadius = 7,
            HeightRequest = 36
        };

        string callOrderTabLabel = result.IsDisciplineConcluded
            ? "🔢 IWF Call Order (Concluded)"
            : result.IsUserOpenerCompleted
                ? "🔢 IWF Call Order (Opener Done)"
                : result.IsBarPastUserOpener
                    ? "🔢 IWF Call Order (N/A Out)"
                    : $"🔢 IWF Call Order ({result.LiftsOut} Out)";

        var callOrderTabBtn = new Button
        {
            Text = callOrderTabLabel,
            FontSize = 12,
            FontAttributes = FontAttributes.Bold,
            BackgroundColor = activeTab == 1 ? AppThemeManager.Color("#0284C7") : AppThemeManager.Color("#0F172A"),
            TextColor = activeTab == 1 ? Colors.White : AppThemeManager.Color("#94A3B8"),
            CornerRadius = 7,
            HeightRequest = 36
        };

        void SwitchWorkTabInPlace(int newTab)
        {
            if (isSnatch)
            {
                if (_snatchWorkTab == newTab) return;
                _snatchWorkTab = newTab;
            }
            else
            {
                if (_cjWorkTab == newTab) return;
                _cjWorkTab = newTab;
            }

            expectedTabBtn.BackgroundColor = newTab == 0 ? AppThemeManager.Color("#0284C7") : AppThemeManager.Color("#0F172A");
            expectedTabBtn.TextColor = newTab == 0 ? Colors.White : AppThemeManager.Color("#94A3B8");
            callOrderTabBtn.BackgroundColor = newTab == 1 ? AppThemeManager.Color("#0284C7") : AppThemeManager.Color("#0F172A");
            callOrderTabBtn.TextColor = newTab == 1 ? Colors.White : AppThemeManager.Color("#94A3B8");
            tabContentHost.Content = newTab == 0
                ? BuildExpectedLiftsTable(discipline, result)
                : BuildCallOrderTimeline(discipline, result);
            SaveStateToSessionCache();
        }

        expectedTabBtn.Clicked += (_, _) => SwitchWorkTabInPlace(0);
        callOrderTabBtn.Clicked += (_, _) => SwitchWorkTabInPlace(1);

        tabGrid.Add(expectedTabBtn, 0, 0);
        tabGrid.Add(callOrderTabBtn, 1, 0);
        stack.Children.Add(tabGrid);
        stack.Children.Add(tabContentHost);

        return stack;
    }

    private View BuildExpectedLiftsTable(LiftType discipline, ScoreboardAnalysisResult result)
    {
        var stack = new VerticalStackLayout { Spacing = 6 };

        var legendBox = new Border
        {
            BackgroundColor = AppThemeManager.Color("#0F172A"),
            Stroke = AppThemeManager.Color("#334155"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 7 },
            Padding = new Thickness(9, 7),
            Content = new Label
            {
                Text = "💡 Tap any lifter name to set as YOU, tap '🚫 Exclude' to ignore a row (e.g. prior session), or tap any attempt cell to edit.\n" +
                       "• Estimated Jumps (after Good lift): ~20kg→+1kg | ~40kg→+2kg | ~65kg→+3kg | ~90kg→+4kg | 105kg+→+5kg max.\n" +
                       "• 2nd→3rd Attempt: Occasionally 1kg smaller when 1st jump > 3kg. Repeat (+0kg) after a Miss.",
                FontSize = 10.5,
                LineHeight = 1.2,
                TextColor = AppThemeManager.Color("#94A3B8")
            }
        };
        stack.Children.Add(legendBox);

        foreach (var lifterSummary in result.LifterSummaries)
        {
            bool isUser = lifterSummary.IsUser;
            bool isExcluded = lifterSummary.IsExcluded;
            int lifterIdx = lifterSummary.LifterIndex;
            var rawRow = lifterIdx >= 0 && lifterIdx < _lifterRows.Count ? _lifterRows[lifterIdx] : null;

            var rowBorder = new Border
            {
                BackgroundColor = isExcluded
                    ? Color.FromArgb("#1A1118")
                    : (isUser ? AppThemeManager.Color("#0C2D48") : AppThemeManager.Color("#0F172A")),
                Stroke = isExcluded
                    ? Color.FromArgb("#7F1D1D")
                    : (isUser ? Color.FromArgb("#38BDF8") : AppThemeManager.Color("#334155")),
                StrokeThickness = isUser ? 1.5 : 1,
                StrokeShape = new RoundRectangle { CornerRadius = 8 },
                Padding = new Thickness(8, 7),
                Opacity = isExcluded ? 0.78 : 1.0
            };

            var rowGrid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) },
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Star }
                },
                ColumnSpacing = 6
            };

            var leftCellStack = new VerticalStackLayout
            {
                Spacing = 3,
                VerticalOptions = LayoutOptions.Center
            };

            var nameStack = new VerticalStackLayout
            {
                Spacing = 1
            };
            string prefix = isExcluded ? "🚫 " : (isUser ? "🎯 " : string.Empty);
            nameStack.Children.Add(new Label
            {
                Text = $"{prefix}{lifterSummary.Name}",
                FontSize = 11.5,
                FontAttributes = FontAttributes.Bold,
                TextColor = isExcluded
                    ? Color.FromArgb("#FCA5A5")
                    : (isUser ? Color.FromArgb("#38BDF8") : AppThemeManager.Color("#F8FAFC")),
                TextDecorations = isExcluded ? TextDecorations.Strikethrough : TextDecorations.None,
                LineBreakMode = LineBreakMode.WordWrap
            });

            string lotOrGroup = rawRow != null && !string.IsNullOrWhiteSpace(rawRow.LeftColumnText) && !rawRow.LeftColumnText.All(char.IsDigit)
                ? $"Group {rawRow.LeftColumnText}"
                : $"Lot #{lifterSummary.SeedNumber}";

            nameStack.Children.Add(new Label
            {
                Text = isExcluded
                    ? $"{lotOrGroup} • Excluded"
                    : (isUser ? $"{lotOrGroup} • YOU" : $"{lotOrGroup} (Tap=Me)"),
                FontSize = 10,
                TextColor = isExcluded
                    ? Color.FromArgb("#F87171")
                    : (isUser ? Color.FromArgb("#7DD3FC") : AppThemeManager.Color("#94A3B8"))
            });

            var selectMeTap = new TapGestureRecognizer();
            selectMeTap.Tapped += (_, _) =>
            {
                if (_selectedUserIndex != lifterIdx)
                {
                    _selectedUserIndex = lifterIdx;
                    if (_lifterRows[_selectedUserIndex].IsExcluded)
                    {
                        _lifterRows[_selectedUserIndex].IsExcluded = false;
                    }
                    _snatchOpenerOverrideKg = null;
                    _cjOpenerOverrideKg = null;
                    _snatchApplied = false;
                    _cjApplied = false;
                    RecalculateScoreboard();
                    BuildLayout();
                }
            };
            nameStack.GestureRecognizers.Add(selectMeTap);
            leftCellStack.Children.Add(nameStack);

            if (!isUser)
            {
                var excludeBtn = new Button
                {
                    Text = isExcluded ? "↩️ Include" : "🚫 Exclude",
                    FontSize = 9.5,
                    FontAttributes = FontAttributes.Bold,
                    BackgroundColor = isExcluded ? Color.FromArgb("#064E3B") : AppThemeManager.Color("#1E293B"),
                    TextColor = isExcluded ? Color.FromArgb("#A7F3D0") : Color.FromArgb("#FCA5A5"),
                    BorderColor = isExcluded ? Color.FromArgb("#10B981") : AppThemeManager.Color("#475569"),
                    BorderWidth = 1,
                    CornerRadius = 5,
                    HeightRequest = 22,
                    Padding = new Thickness(6, 0),
                    HorizontalOptions = LayoutOptions.Start
                };
                excludeBtn.Clicked += (_, _) => ToggleLifterExclusion(lifterIdx);
                leftCellStack.Children.Add(excludeBtn);
            }

            rowGrid.Add(leftCellStack, 0, 0);

            if (isExcluded)
            {
                var excludedBanner = new Border
                {
                    BackgroundColor = Color.FromArgb("#2A1215"),
                    Stroke = Color.FromArgb("#7F1D1D"),
                    StrokeThickness = 1,
                    StrokeShape = new RoundRectangle { CornerRadius = 6 },
                    Padding = new Thickness(8, 6),
                    VerticalOptions = LayoutOptions.Center,
                    Content = new Label
                    {
                        Text = "🚫 Excluded from analysis\nTap '↩️ Include' to restore",
                        FontSize = 10,
                        TextColor = Color.FromArgb("#FCA5A5"),
                        HorizontalTextAlignment = TextAlignment.Center
                    }
                };
                var restoreTap = new TapGestureRecognizer();
                restoreTap.Tapped += (_, _) => ToggleLifterExclusion(lifterIdx);
                excludedBanner.GestureRecognizers.Add(restoreTap);

                Grid.SetColumn(excludedBanner, 1);
                Grid.SetColumnSpan(excludedBanner, 3);
                rowGrid.Children.Add(excludedBanner);
            }
            else
            {
                for (int a = 0; a < lifterSummary.Attempts.Count && a < 3; a++)
                {
                    var attempt = lifterSummary.Attempts[a];
                    var cellView = BuildAttemptCellBadge(discipline, lifterIdx, attempt, isUser);
                    rowGrid.Add(cellView, a + 1, 0);
                }
            }

            rowBorder.Content = rowGrid;
            stack.Children.Add(rowBorder);
        }

        return stack;
    }

    private View BuildAttemptCellBadge(LiftType discipline, int lifterIndex, ResolvedLifterAttempt attempt, bool isUser)
    {
        string bgHex;
        string borderHex;
        string mainText;
        string subText;
        string textColorHex = "#F8FAFC";

        if (attempt.IsPass || attempt.Origin == ScoreboardAttemptOrigin.CompletedPass)
        {
            bgHex = "#1E293B";
            borderHex = "#475569";
            mainText = "PASS";
            subText = $"Att {attempt.AttemptNumber}";
            textColorHex = "#94A3B8";
        }
        else if (attempt.IsCompleted)
        {
            if (attempt.Origin == ScoreboardAttemptOrigin.CompletedGood)
            {
                bgHex = "#064E3B";
                borderHex = "#10B981";
                mainText = $"{attempt.WeightKg}✓";
                subText = $"Att {attempt.AttemptNumber} Good";
                textColorHex = "#A7F3D0";
            }
            else
            {
                bgHex = "#450A0A";
                borderHex = "#EF4444";
                mainText = $"{attempt.WeightKg}✗";
                subText = $"Att {attempt.AttemptNumber} Miss";
                textColorHex = "#FCA5A5";
            }
        }
        else if (attempt.Origin == ScoreboardAttemptOrigin.DeclaredOnBoard || attempt.Origin == ScoreboardAttemptOrigin.UserOverride)
        {
            bgHex = isUser && attempt.AttemptNumber == 1 ? "#0369A1" : "#1E293B";
            borderHex = isUser && attempt.AttemptNumber == 1 ? "#38BDF8" : "#475569";
            mainText = $"{attempt.WeightKg}kg";
            subText = isUser && attempt.AttemptNumber == 1 ? "🎯 APP OPENER" : $"Att {attempt.AttemptNumber} Board";
        }
        else if (attempt.JumpFromPreviousKg == 0)
        {
            bgHex = "#2E1065";
            borderHex = "#8B5CF6";
            mainText = $"~{attempt.WeightKg}kg";
            subText = $"Att {attempt.AttemptNumber} (+0 Rpt)";
            textColorHex = "#DDD6FE";
        }
        else
        {
            bgHex = "#2E1065";
            borderHex = "#A855F7";
            mainText = $"~{attempt.WeightKg}kg";
            subText = $"Est. +{attempt.JumpFromPreviousKg ?? 0}kg";
            textColorHex = "#E9D5FF";
        }

        var badge = new Border
        {
            BackgroundColor = Color.FromArgb(bgHex),
            Stroke = Color.FromArgb(borderHex),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 6 },
            Padding = new Thickness(4, 5),
            Content = new VerticalStackLayout
            {
                Spacing = 1,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    new Label
                    {
                        Text = mainText,
                        FontSize = 12,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = Color.FromArgb(textColorHex),
                        HorizontalTextAlignment = TextAlignment.Center
                    },
                    new Label
                    {
                        Text = subText,
                        FontSize = 9,
                        TextColor = Color.FromArgb(textColorHex),
                        Opacity = 0.85,
                        HorizontalTextAlignment = TextAlignment.Center,
                        LineBreakMode = LineBreakMode.TailTruncation
                    }
                }
            }
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await PromptEditAttemptCellAsync(discipline, lifterIndex, attempt.AttemptNumber, attempt);
        badge.GestureRecognizers.Add(tap);

        return badge;
    }

    private View BuildCallOrderTimeline(LiftType discipline, ScoreboardAnalysisResult result)
    {
        var stack = new VerticalStackLayout { Spacing = 8 };

        stack.Children.Add(new Border
        {
            BackgroundColor = AppThemeManager.Color("#0F172A"),
            Stroke = AppThemeManager.Color("#334155"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 7 },
            Padding = new Thickness(9, 7),
            Content = new Label
            {
                Text = "⚖️ IWF Calling Order Rules Applied:\n" +
                       "1. Lowest Barbell Weight first (rising bar).\n" +
                       "2. Earlier Attempt # first (1st attempt ALWAYS goes before 2nd or 3rd attempt at the same weight, regardless of Lot/Seed #).\n" +
                       "3. Same Attempt # (2nd vs 2nd, or 3rd vs 3rd): Whoever lifted earlier on their previous attempt goes first (replayed retroactively from Lift #1).\n" +
                       "4. Same 1st Attempt Opener weight: Lower Lot/Seed # goes first.",
                FontSize = 10.5,
                LineHeight = 1.2,
                TextColor = AppThemeManager.Color("#94A3B8")
            }
        });

        // Section A: Completed Lifts (Lifts Passed)
        stack.Children.Add(new Label
        {
            Text = $"✅ COMPLETED LIFTS ON BOARD (LIFTS PASSED: {result.CompletedCalls.Count})",
            FontSize = 11,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#4ADE80"),
            Margin = new Thickness(0, 4, 0, 0)
        });

        if (result.CompletedCalls.Count == 0)
        {
            stack.Children.Add(new Label
            {
                Text = "No completed lifts recorded yet for this discipline (0 lifts passed).",
                FontSize = 11.5,
                TextColor = AppThemeManager.Color("#94A3B8")
            });
        }
        else
        {
            foreach (var call in result.CompletedCalls)
            {
                stack.Children.Add(BuildCallTimelineRow(call, isBeforeUser: false));
            }
        }

        if (result.IsDisciplineConcluded || result.IsUserOpenerCompleted)
        {
            return stack;
        }

        // Section B: Upcoming Lifts Before User's Opener (Lifts Out)
        stack.Children.Add(new Label
        {
            Text = result.IsBarPastUserOpener
                ? "⏳ UPCOMING LIFTS BEFORE YOUR OPENER (LIFTS OUT: N/A — KEPT AS-IS)"
                : $"⏳ UPCOMING LIFTS BEFORE YOUR OPENER (LIFTS OUT: {result.LiftsOut})",
            FontSize = 11,
            FontAttributes = FontAttributes.Bold,
            TextColor = result.IsBarPastUserOpener ? Color.FromArgb("#F59E0B") : Color.FromArgb("#38BDF8"),
            Margin = new Thickness(0, 6, 0, 0)
        });

        if (result.IsBarPastUserOpener)
        {
            stack.Children.Add(new Border
            {
                BackgroundColor = Color.FromArgb("#451A03"),
                Stroke = Color.FromArgb("#F59E0B"),
                StrokeThickness = 1,
                StrokeShape = new RoundRectangle { CornerRadius = 7 },
                Padding = new Thickness(10, 8),
                Content = new Label
                {
                    Text = result.BarPastUserOpenerMessage ??
                           $"Cannot determine Lifts Out because {result.MaxCompletedWeightKg} kg has already been lifted on the scoreboard, which is heavier than your {result.UserOpenerWeightKg} kg Opener in the app. Lifts Out will be left as-is.",
                    FontSize = 11.5,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb("#FDE68A")
                }
            });
        }
        else if (result.UpcomingCallsBeforeUser.Count == 0)
        {
            stack.Children.Add(new Border
            {
                BackgroundColor = AppThemeManager.Color("#0F172A"),
                Stroke = Color.FromArgb("#38BDF8"),
                StrokeThickness = 1,
                StrokeShape = new RoundRectangle { CornerRadius = 7 },
                Padding = new Thickness(10, 8),
                Content = new Label
                {
                    Text = "0 Lifts Out — You are first up on the platform!",
                    FontSize = 12,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb("#38BDF8")
                }
            });
        }
        else
        {
            foreach (var call in result.UpcomingCallsBeforeUser)
            {
                stack.Children.Add(BuildCallTimelineRow(call, isBeforeUser: true));
            }
        }

        // Section C: Highlighted User Opener Card
        if (result.UserOpenerCall != null)
        {
            var u = result.UserOpenerCall;
            string tieNote = !string.IsNullOrWhiteSpace(u.SequenceReasonNote)
                ? $"\n⚖️ {u.SequenceReasonNote}"
                : string.Empty;

            string subLineText = result.IsBarPastUserOpener
                ? $"Lifts Out kept as-is ({_viewModel.GetLiftsOutForDiscipline(discipline)}){tieNote}"
                : $"Platform Call #{u.GlobalCallNumber} overall • {result.LiftsOut} lift(s) before you{tieNote}";

            var userOpenerCard = new Border
            {
                BackgroundColor = result.IsBarPastUserOpener ? Color.FromArgb("#451A03") : Color.FromArgb("#0C4A6E"),
                Stroke = result.IsBarPastUserOpener ? Color.FromArgb("#F59E0B") : Color.FromArgb("#38BDF8"),
                StrokeThickness = 2,
                StrokeShape = new RoundRectangle { CornerRadius = 8 },
                Padding = new Thickness(10, 9),
                Margin = new Thickness(0, 4, 0, 4),
                Content = new VerticalStackLayout
                {
                    Spacing = 2,
                    Children =
                    {
                        new Label
                        {
                            Text = $"🎯 YOUR OPENER — {u.WeightKg} kg (1st Attempt) • {u.LifterName} (Lot #{u.SeedNumber}) ✏️",
                            FontSize = 13,
                            FontAttributes = FontAttributes.Bold,
                            TextColor = Colors.White
                        },
                        new Label
                        {
                            Text = $"{subLineText}\n✏️ Tap to change your opener weight (updates Setup & Live)",
                            FontSize = 11,
                            TextColor = result.IsBarPastUserOpener ? Color.FromArgb("#FDE68A") : Color.FromArgb("#BAE6FD")
                        }
                    }
                }
            };
            var cardTap = new TapGestureRecognizer();
            cardTap.Tapped += async (_, _) => await PromptOverrideUserOpenerAsync(discipline, result.UserOpenerWeightKg);
            userOpenerCard.GestureRecognizers.Add(cardTap);
            stack.Children.Add(userOpenerCard);
        }

        // Section D: Lifts After User's Opener
        if (result.UpcomingCallsAfterUser.Count > 0)
        {
            stack.Children.Add(new Label
            {
                Text = $"⏭️ EXPECTED LIFTS AFTER YOUR OPENER ({result.UpcomingCallsAfterUser.Count})",
                FontSize = 11,
                FontAttributes = FontAttributes.Bold,
                TextColor = AppThemeManager.Color("#94A3B8"),
                Margin = new Thickness(0, 6, 0, 0)
            });

            foreach (var call in result.UpcomingCallsAfterUser)
            {
                stack.Children.Add(BuildCallTimelineRow(call, isBeforeUser: false));
            }
        }

        return stack;
    }

    private View BuildCallTimelineRow(ScoreboardCallEntry call, bool isBeforeUser)
    {
        string badgeText;
        string badgeBgHex;

        if (call.IsCompleted)
        {
            badgeText = $"Passed #{call.PhaseCallNumber}";
            badgeBgHex = call.Origin == ScoreboardAttemptOrigin.CompletedGood ? "#16A34A" : "#DC2626";
        }
        else if (isBeforeUser)
        {
            badgeText = $"Out #{call.PhaseCallNumber}";
            badgeBgHex = "#0284C7";
        }
        else
        {
            badgeText = $"After #{call.GlobalCallNumber}";
            badgeBgHex = "#475569";
        }

        string originBadge = call.IsCompleted
            ? (call.Origin == ScoreboardAttemptOrigin.CompletedGood ? "✓ Good Lift" : "✗ Missed Lift")
            : call.Origin switch
            {
                ScoreboardAttemptOrigin.DeclaredOnBoard => "Declared on Board",
                ScoreboardAttemptOrigin.UserOverride => "Manual Override",
                ScoreboardAttemptOrigin.ProjectedFromAutoOneKg => "Est. Jump (from +1kg Board)",
                _ => "Estimated Jump"
            };

        var rowStack = new VerticalStackLayout { Spacing = 2 };

        var topLine = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Auto }
            },
            ColumnSpacing = 8
        };

        topLine.Add(new Border
        {
            BackgroundColor = Color.FromArgb(badgeBgHex),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 5 },
            Padding = new Thickness(6, 2),
            VerticalOptions = LayoutOptions.Center,
            Content = new Label
            {
                Text = badgeText,
                FontSize = 10,
                FontAttributes = FontAttributes.Bold,
                TextColor = Colors.White
            }
        }, 0, 0);

        topLine.Add(new Label
        {
            Text = $"{call.WeightKg} kg",
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            TextColor = AppThemeManager.Color("#F8FAFC"),
            VerticalOptions = LayoutOptions.Center
        }, 1, 0);

        topLine.Add(new Label
        {
            Text = $"{call.LifterName} (Lot #{call.SeedNumber}) • Att {call.AttemptNumber}",
            FontSize = 12,
            TextColor = AppThemeManager.Color("#E2E8F0"),
            VerticalOptions = LayoutOptions.Center,
            LineBreakMode = LineBreakMode.TailTruncation
        }, 2, 0);

        topLine.Add(new Label
        {
            Text = originBadge,
            FontSize = 10,
            TextColor = call.Origin is ScoreboardAttemptOrigin.ProjectedSCurve or ScoreboardAttemptOrigin.ProjectedFromAutoOneKg
                ? Color.FromArgb("#C084FC")
                : AppThemeManager.Color("#94A3B8"),
            VerticalOptions = LayoutOptions.Center
        }, 3, 0);

        rowStack.Children.Add(topLine);

        if (!string.IsNullOrWhiteSpace(call.SequenceReasonNote))
        {
            rowStack.Children.Add(new Label
            {
                Text = $"⚖️ {call.SequenceReasonNote}",
                FontSize = 10.5,
                TextColor = Color.FromArgb("#FBBF24"),
                Margin = new Thickness(4, 0, 0, 0)
            });
        }

        return new Border
        {
            BackgroundColor = AppThemeManager.Color("#0F172A"),
            Stroke = isBeforeUser ? AppThemeManager.Color("#1E3A8A") : AppThemeManager.Color("#334155"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 7 },
            Padding = new Thickness(8, 6),
            Content = rowStack
        };
    }

    private View BuildEmptyStateExplanationCard()
    {
        return new Border
        {
            BackgroundColor = AppThemeManager.Color("#1E293B"),
            Stroke = AppThemeManager.Color("#334155"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            Padding = new Thickness(14, 12),
            Content = new VerticalStackLayout
            {
                Spacing = 8,
                Children =
                {
                    new Label
                    {
                        Text = "HOW THE AI SCOREBOARD SCANNER WORKS",
                        FontSize = 12,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = AppThemeManager.Color("#38BDF8")
                    },
                    new Label
                    {
                        Text = "1. Take or pick a photo of the warmup room scoreboard TV/monitor.\n" +
                               "2. Gemini Vision extracts every lifter's Lot #, completed lifts (Good / Miss), and declared upcoming weights for both Snatch and Clean & Jerk.\n" +
                               "3. For undeclared 2nd & 3rd attempts, Comp OWL estimates realistic whole-kg jumps based on the weight on the bar (+1kg at ~20kg, +2kg at ~40kg, +3kg at ~65kg, +4kg at ~90kg, up to +5kg max at 105kg+, and +0kg repeat after a miss).\n" +
                               "4. The engine replays completed lifts from Lift #1 to establish 'Lifts Passed' and exact previous-attempt order, then simulates the rising bar using IWF rules (Weight → Attempt # → Previous Attempt Sequence → Lot/Seed #) up to your Opener.\n" +
                               "5. Separate Snatch and Clean & Jerk analysis blocks let you review and apply each discipline independently.",
                        FontSize = 12,
                        LineHeight = 1.25,
                        TextColor = AppThemeManager.Color("#CBD5E1")
                    }
                }
            }
        };
    }

    private View BuildBottomBar()
    {
        var closeBtn = new Button
        {
            Text = "Close",
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            BackgroundColor = AppThemeManager.Color("#334155"),
            TextColor = AppThemeManager.Color("#F8FAFC"),
            CornerRadius = 8,
            HeightRequest = 42,
            HorizontalOptions = LayoutOptions.Fill
        };
        closeBtn.Clicked += async (_, _) =>
        {
            SaveStateToSessionCache();
            await Navigation.PopModalAsync();
        };

        return closeBtn;
    }

    private View BuildReportAnalysisErrorCard()
    {
        var card = new Border
        {
            BackgroundColor = AppThemeManager.Color("#1E293B"),
            Stroke = AppThemeManager.Color("#334155"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            Padding = new Thickness(12, 10)
        };

        var stack = new VerticalStackLayout { Spacing = 8 };

        stack.Children.Add(new Label
        {
            Text = "🐞 REPORT AN ANALYSIS ERROR",
            FontSize = 11.5,
            FontAttributes = FontAttributes.Bold,
            TextColor = AppThemeManager.Color("#F59E0B")
        });

        stack.Children.Add(new Label
        {
            Text = "Spotted a misread weight, status, or call order? Tap below to compose an email to compowl.app@gmail.com with the full diagnostic report and scoreboard photo attached.",
            FontSize = 11,
            TextColor = AppThemeManager.Color("#94A3B8")
        });

        var reportBtn = new Button
        {
            Text = "✉️ Report Analysis Error",
            FontSize = 12.5,
            FontAttributes = FontAttributes.Bold,
            BackgroundColor = Color.FromArgb("#451A03"),
            BorderColor = Color.FromArgb("#F59E0B"),
            BorderWidth = 1,
            TextColor = Color.FromArgb("#FDE68A"),
            CornerRadius = 8,
            MinimumHeightRequest = 42,
            Padding = new Thickness(12, 6),
            HorizontalOptions = LayoutOptions.Fill
        };
        reportBtn.Clicked += async (_, _) => await SendAnalysisErrorReportEmailAsync();
        stack.Children.Add(reportBtn);

        card.Content = stack;
        return card;
    }

    private async Task SendAnalysisErrorReportEmailAsync()
    {
        try
        {
            const string recipientEmail = "compowl.app@gmail.com";
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string subject = $"Comp OWL Scoreboard Analysis Error Report ({DateTime.Now:yyyy-MM-dd HH:mm})";

            string reportText = BuildDetailedDiagnosticReportText();
            string cacheDir = FileSystem.CacheDirectory;
            Directory.CreateDirectory(cacheDir);

            var attachmentPaths = new List<string>();
            string reportFilePath = System.IO.Path.Combine(cacheDir, $"Scoreboard_Analysis_Report_{timestamp}.txt");
            await File.WriteAllTextAsync(reportFilePath, reportText, Encoding.UTF8);
            attachmentPaths.Add(reportFilePath);

            if (_photoBytes != null && _photoBytes.Length > 0)
            {
                string ext = _photoMimeType.Contains("png", StringComparison.OrdinalIgnoreCase) ? "png" : "jpg";
                string photoFilePath = System.IO.Path.Combine(cacheDir, $"Scoreboard_Photo_{timestamp}.{ext}");
                await File.WriteAllBytesAsync(photoFilePath, _photoBytes);
                attachmentPaths.Add(photoFilePath);
            }

            string matchedLifter = (_selectedUserIndex >= 0 && _selectedUserIndex < _lifterRows.Count)
                ? _lifterRows[_selectedUserIndex].Name
                : "None";

            var bodyBuilder = new StringBuilder();
            bodyBuilder.AppendLine("What was wrong with the scoreboard analysis? (Optional notes):");
            bodyBuilder.AppendLine();
            bodyBuilder.AppendLine("------------------------------------------------------------");
            bodyBuilder.AppendLine("Quick Summary (Full report & photo attached):");
            bodyBuilder.AppendLine($"• App Version: {AppInfo.Current.VersionString} (Build {AppInfo.Current.BuildString})");
            bodyBuilder.AppendLine($"• AI Model: {GeminiScoreboardVisionService.LastUsedModelId ?? "N/A"}");
            bodyBuilder.AppendLine($"• Extracted Lifters: {_lifterRows.Count} ({_lifterRows.Count(r => r.IsExcluded)} excluded)");
            bodyBuilder.AppendLine($"• Selected Lifter: {matchedLifter}");
            if (_snatchResult != null)
            {
                bodyBuilder.AppendLine($"• Snatch: {_snatchResult.LiftsOut} Out, {_snatchResult.LiftsPassed} Passed (Opener {_snatchResult.UserOpenerWeightKg} kg)");
            }
            if (_cjResult != null)
            {
                bodyBuilder.AppendLine($"• Clean & Jerk: {_cjResult.LiftsOut} Out, {_cjResult.LiftsPassed} Passed, {_cjResult.RemainingSnatchLifts ?? 0} Snatch Left (Opener {_cjResult.UserOpenerWeightKg} kg)");
            }
            if (!string.IsNullOrWhiteSpace(_statusMessage))
            {
                bodyBuilder.AppendLine($"• Status Banner: {_statusMessage}");
            }
            bodyBuilder.AppendLine();
            bodyBuilder.AppendLine(reportText);

            string emailBody = bodyBuilder.ToString();

            var message = new EmailMessage
            {
                Subject = subject,
                Body = emailBody,
                BodyFormat = EmailBodyFormat.PlainText,
                To = [recipientEmail]
            };

            message.Attachments ??= [];
            foreach (string path in attachmentPaths)
            {
                string contentType = path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                    ? "image/png"
                    : path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                        ? "image/jpeg"
                        : "text/plain";
                message.Attachments.Add(new EmailAttachment(path, contentType));
            }

            bool launched = false;
            if (Email.Default.IsComposeSupported)
            {
                try
                {
                    await Email.Default.ComposeAsync(message);
                    launched = true;
                }
                catch
                {
                    launched = false;
                }
            }

#if ANDROID
            if (!launched)
            {
                launched = TryLaunchAndroidEmailIntentWithAttachments(
                    recipientEmail,
                    subject,
                    emailBody,
                    attachmentPaths);
            }
#endif

            if (!launched)
            {
                var shareFiles = attachmentPaths.Select(p => new ShareFile(p)).ToList();
                await Share.Default.RequestAsync(new ShareMultipleFilesRequest
                {
                    Title = $"Send to {recipientEmail}",
                    Files = shareFiles
                });
            }
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync(
                "Report Error",
                $"Could not open email app: {ex.Message}\n\nYou can email compowl.app@gmail.com directly.",
                "OK");
        }
    }

#if ANDROID
    private static bool TryLaunchAndroidEmailIntentWithAttachments(
        string recipientEmail,
        string subject,
        string body,
        IReadOnlyList<string> attachmentPaths)
    {
        try
        {
            var context = Android.App.Application.Context;
            var uris = new List<Android.Net.Uri>();
            Android.Content.ClipData? clipData = null;

            foreach (string filePath in attachmentPaths)
            {
                var javaFile = new Java.IO.File(filePath);
                var uri = Microsoft.Maui.Storage.FileProvider.GetUriForFile(
                    context,
                    context.PackageName + ".fileProvider",
                    javaFile);
                if (uri != null)
                {
                    uris.Add(uri);
                    if (clipData == null)
                    {
                        clipData = Android.Content.ClipData.NewRawUri("Attachment", uri);
                    }
                    else
                    {
                        clipData.AddItem(new Android.Content.ClipData.Item(uri));
                    }
                }
            }

            var intent = new Android.Content.Intent(
                uris.Count > 1
                    ? Android.Content.Intent.ActionSendMultiple
                    : Android.Content.Intent.ActionSend);
            intent.SetType("message/rfc822");
            intent.PutExtra(Android.Content.Intent.ExtraEmail, new[] { recipientEmail });
            intent.PutExtra(Android.Content.Intent.ExtraSubject, subject);
            intent.PutExtra(Android.Content.Intent.ExtraText, body);

            if (uris.Count == 1)
            {
                intent.PutExtra(Android.Content.Intent.ExtraStream, uris[0]);
            }
            else if (uris.Count > 1)
            {
                var parcelables = new List<Android.OS.IParcelable>(uris);
                intent.PutParcelableArrayListExtra(Android.Content.Intent.ExtraStream, parcelables);
            }

            if (clipData != null)
            {
                intent.ClipData = clipData;
            }

            intent.AddFlags(Android.Content.ActivityFlags.GrantReadUriPermission);

            var chooser = Android.Content.Intent.CreateChooser(intent, $"Email Report to {recipientEmail}");
            if (chooser != null)
            {
                chooser.AddFlags(Android.Content.ActivityFlags.NewTask | Android.Content.ActivityFlags.GrantReadUriPermission);
                context.StartActivity(chooser);
                return true;
            }
        }
        catch
        {
            // Fall through to Share.Default fallback
        }

        return false;
    }
#endif

    private string BuildDetailedDiagnosticReportText()
    {
        var sb = new StringBuilder();
        sb.AppendLine("================================================================================");
        sb.AppendLine("COMP OWL — AI SCOREBOARD SCANNER DIAGNOSTIC REPORT");
        sb.AppendLine("================================================================================");
        sb.AppendLine($"Generated (Local):  {DateTime.Now:yyyy-MM-dd HH:mm:ss zzz}");
        sb.AppendLine($"Generated (UTC):    {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"App Version:        {AppInfo.Current.VersionString} (Build {AppInfo.Current.BuildString})");
        sb.AppendLine($"Package Name:       {AppInfo.Current.PackageName}");
        sb.AppendLine($"Device:             {DeviceInfo.Current.Manufacturer} {DeviceInfo.Current.Model}");
        sb.AppendLine($"OS Platform:        {DeviceInfo.Current.Platform} {DeviceInfo.Current.VersionString}");
        sb.AppendLine();

        sb.AppendLine("--- USER & APP SETTINGS ---");
        sb.AppendLine($"Active Profile:     {_viewModel.ActiveProfileName}");
        sb.AppendLine($"Selected Discipline:{(_viewModel.IsSnatchSelected ? "Snatch" : "Clean & Jerk")}");
        sb.AppendLine($"Live Mode Active:   {_viewModel.IsLiveMode}");
        sb.AppendLine($"Snatch Opener (App):{_viewModel.GetOpenerWeightForDiscipline(LiftType.Snatch)} kg (Scanner Override: {_snatchOpenerOverrideKg?.ToString() ?? "None"})");
        sb.AppendLine($"C&J Opener (App):   {_viewModel.GetOpenerWeightForDiscipline(LiftType.CleanAndJerk)} kg (Scanner Override: {_cjOpenerOverrideKg?.ToString() ?? "None"})");
        sb.AppendLine($"Auto +1kg TieBreak: {_overrideAutoPlusOne}");
        sb.AppendLine($"Selected User Row:  Index {_selectedUserIndex} ({((_selectedUserIndex >= 0 && _selectedUserIndex < _lifterRows.Count) ? _lifterRows[_selectedUserIndex].Name : "None")})");
        sb.AppendLine($"Status Banner:      {_statusMessage}");
        sb.AppendLine($"Status Is Error:    {_isStatusError}");
        sb.AppendLine($"Photo Attached:     {(_photoBytes != null ? $"Yes ({_photoBytes.Length} bytes, {_photoMimeType})" : "No")}");
        sb.AppendLine($"Last AI Model Used: {GeminiScoreboardVisionService.LastUsedModelId ?? "None"}");
        if (!string.IsNullOrWhiteSpace(GeminiScoreboardVisionService.LastErrorDetail))
        {
            sb.AppendLine($"Last AI API Error:  {GeminiScoreboardVisionService.LastErrorDetail}");
        }
        sb.AppendLine();

        sb.AppendLine($"--- EXTRACTED LIFTER ROWS ({_lifterRows.Count} TOTAL, {_lifterRows.Count(r => r.IsExcluded)} EXCLUDED) ---");
        for (int i = 0; i < _lifterRows.Count; i++)
        {
            var r = _lifterRows[i];
            string userTag = i == _selectedUserIndex ? " [YOU]" : string.Empty;
            string exclTag = r.IsExcluded ? " [EXCLUDED]" : string.Empty;
            sb.AppendLine(
                $"Row #{i + 1:00} (Ord={r.OrderNumber}, Left='{r.LeftColumnText}', Lot=#{r.LotNumber}): {r.Name}{userTag}{exclTag} | " +
                $"SN: [{FormatCellForReport(r.Snatch1)}, {FormatCellForReport(r.Snatch2)}, {FormatCellForReport(r.Snatch3)}] | " +
                $"CJ: [{FormatCellForReport(r.Cj1)}, {FormatCellForReport(r.Cj2)}, {FormatCellForReport(r.Cj3)}]");
        }
        sb.AppendLine();

        AppendDisciplineResultToReport(sb, "SNATCH ANALYSIS RESULT", _snatchResult);
        AppendDisciplineResultToReport(sb, "CLEAN & JERK ANALYSIS RESULT", _cjResult);

        sb.AppendLine("--- RAW GEMINI VISION MODEL JSON OUTPUT ---");
        sb.AppendLine(string.IsNullOrWhiteSpace(GeminiScoreboardVisionService.LastRawModelResponseJson)
            ? "(No raw model JSON recorded in this session)"
            : GeminiScoreboardVisionService.LastRawModelResponseJson);
        sb.AppendLine();

        sb.AppendLine("--- SERIALIZED LIFTER ROWS STATE (JSON) ---");
        try
        {
            sb.AppendLine(JsonSerializer.Serialize(_lifterRows, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            sb.AppendLine($"(JSON serialize error: {ex.Message})");
        }

        return sb.ToString();
    }

    private static string FormatCellForReport(ScoreboardAttemptCell cell)
    {
        if (cell.Status == ScoreboardAttemptStatus.Empty && !cell.WeightKg.HasValue)
        {
            return "—";
        }

        return $"{cell.WeightKg?.ToString() ?? "null"}kg ({cell.Status})";
    }

    private static void AppendDisciplineResultToReport(
        StringBuilder sb,
        string sectionTitle,
        ScoreboardAnalysisResult? res)
    {
        sb.AppendLine($"--- {sectionTitle} ---");
        if (res == null)
        {
            sb.AppendLine("(Not calculated)");
            sb.AppendLine();
            return;
        }

        sb.AppendLine($"Discipline:               {res.Discipline}");
        sb.AppendLine($"Lifts Out:                {res.LiftsOut}");
        sb.AppendLine($"Lifts Passed:             {res.LiftsPassed}");
        sb.AppendLine($"Remaining Snatch Lifts:   {res.RemainingSnatchLifts?.ToString() ?? "N/A"}");
        sb.AppendLine($"User Opener Weight:       {res.UserOpenerWeightKg} kg (Board Declared: {res.BoardDeclaredUserOpenerKg?.ToString() ?? "N/A"} kg)");
        sb.AppendLine($"Matched User Lifter:      {res.UserLifterName} (Index {res.UserLifterIndex})");
        sb.AppendLine($"IsDisciplineConcluded:    {res.IsDisciplineConcluded} ({res.DisciplineConcludedMessage})");
        sb.AppendLine($"IsUserOpenerCompleted:    {res.IsUserOpenerCompleted} ({res.UserOpenerCompletedMessage})");
        sb.AppendLine($"IsBarPastUserOpener:      {res.IsBarPastUserOpener} (MaxCompleted={res.MaxCompletedWeightKg?.ToString() ?? "N/A"} kg)");
        sb.AppendLine();

        sb.AppendLine($"Lifter Summaries ({res.LifterSummaries.Count}):");
        foreach (var l in res.LifterSummaries)
        {
            string flags = $"{(l.IsUser ? " [YOU]" : "")}{(l.IsExcluded ? " [EXCLUDED]" : "")}";
            string a1 = l.Attempts.Count > 0 ? $"{l.Attempts[0].WeightKg}kg ({l.Attempts[0].Origin})" : "—";
            string a2 = l.Attempts.Count > 1 ? $"{l.Attempts[1].WeightKg}kg ({l.Attempts[1].Origin})" : "—";
            string a3 = l.Attempts.Count > 2 ? $"{l.Attempts[2].WeightKg}kg ({l.Attempts[2].Origin})" : "—";
            sb.AppendLine($"  • Lot #{l.SeedNumber} {l.Name}{flags}: Att1=[{a1}] | Att2=[{a2}] | Att3=[{a3}]");
        }
        sb.AppendLine();

        sb.AppendLine($"Completed Calls ({res.CompletedCalls.Count}):");
        foreach (var c in res.CompletedCalls)
        {
            sb.AppendLine($"  #{c.GlobalCallNumber:00}: {c.WeightKg} kg — {c.LifterName} (Att {c.AttemptNumber}, Lot #{c.SeedNumber}, {c.Origin}, {c.SequenceReasonNote})");
        }
        sb.AppendLine();

        sb.AppendLine($"Upcoming Calls Before User Opener ({res.UpcomingCallsBeforeUser.Count}):");
        foreach (var c in res.UpcomingCallsBeforeUser)
        {
            sb.AppendLine($"  #{c.GlobalCallNumber:00} (Out #{c.PhaseCallNumber}): {c.WeightKg} kg — {c.LifterName} (Att {c.AttemptNumber}, Lot #{c.SeedNumber}, {c.Origin}, {c.SequenceReasonNote})");
        }
        if (res.UserOpenerCall != null)
        {
            var u = res.UserOpenerCall;
            sb.AppendLine($"  #{u.GlobalCallNumber:00} [YOUR OPENER]: {u.WeightKg} kg — {u.LifterName} (Att {u.AttemptNumber}, Lot #{u.SeedNumber}, {u.Origin}, {u.SequenceReasonNote})");
        }
        sb.AppendLine();

        sb.AppendLine($"Upcoming Calls After User Opener ({res.UpcomingCallsAfterUser.Count}):");
        foreach (var c in res.UpcomingCallsAfterUser)
        {
            sb.AppendLine($"  #{c.GlobalCallNumber:00}: {c.WeightKg} kg — {c.LifterName} (Att {c.AttemptNumber}, Lot #{c.SeedNumber}, {c.Origin}, {c.SequenceReasonNote})");
        }
        sb.AppendLine();
    }

    private void UpdateProgressUiInPlace(ScoreboardScanProgress update)
    {
        _scanStageTitle = update.StageTitle;
        _scanDetailMessage = update.DetailMessage;
        _scanProgressFraction = Math.Clamp(update.ProgressFraction, 0.05, 1.0);
        _scanIsRetryOrFallback = update.IsFallbackOrRetry;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (!_isAnalyzing)
            {
                return;
            }

            if (_progressTitleLabel != null)
            {
                _progressTitleLabel.Text = _scanStageTitle;
                _progressTitleLabel.TextColor = AppThemeManager.Color(_scanIsRetryOrFallback ? "#FBBF24" : "#38BDF8");
            }

            if (_progressDetailLabel != null)
            {
                _progressDetailLabel.Text = _scanDetailMessage;
            }

            if (_progressBar != null)
            {
                _progressBar.Progress = _scanProgressFraction;
                _progressBar.ProgressColor = AppThemeManager.Color(_scanIsRetryOrFallback ? "#F59E0B" : "#38BDF8");
            }

            if (_progressElapsedLabel != null)
            {
                _progressElapsedLabel.Text = $"{(int)_scanStopwatch.Elapsed.TotalSeconds}s";
            }
        });
    }

    private void StartElapsedTimerLoop(CancellationToken token)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                while (!token.IsCancellationRequested && _isAnalyzing)
                {
                    await Task.Delay(500, token);
                    if (token.IsCancellationRequested || !_isAnalyzing)
                    {
                        break;
                    }

                    int elapsedSec = (int)_scanStopwatch.Elapsed.TotalSeconds;
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        if (_isAnalyzing && _progressElapsedLabel != null)
                        {
                            _progressElapsedLabel.Text = $"{elapsedSec}s";
                        }

                        if (_isAnalyzing &&
                            elapsedSec >= 6 &&
                            _progressDetailLabel != null &&
                            !_scanDetailMessage.Contains("AI model is still analyzing", StringComparison.OrdinalIgnoreCase))
                        {
                            _progressDetailLabel.Text = $"{_scanDetailMessage} — AI model is still analyzing the table ({elapsedSec}s)...";
                        }
                        else if (_isAnalyzing &&
                                 elapsedSec >= 6 &&
                                 _progressDetailLabel != null)
                        {
                            _progressDetailLabel.Text = $"{_scanDetailMessage} — AI model is still analyzing the table ({elapsedSec}s)...";
                        }
                    });
                }
            }
            catch (OperationCanceledException)
            {
                // Normal termination when scan finishes or is cancelled
            }
        }, token);
    }

    private async Task CaptureOrPickScoreboardAsync(bool useCamera)
    {
        if (!_visionService.HasApiKey())
        {
            _isApiKeyExpanded = true;
            _statusMessage = "Please save your free Gemini API key first before scanning a scoreboard.";
            _isStatusError = true;
            SaveStateToSessionCache();
            BuildLayout();
            return;
        }

        try
        {
            FileResult? photo = null;
            if (useCamera && MediaPicker.Default.IsCaptureSupported)
            {
                var camPerm = await Permissions.CheckStatusAsync<Permissions.Camera>();
                if (camPerm != PermissionStatus.Granted)
                {
                    camPerm = await Permissions.RequestAsync<Permissions.Camera>();
                }

                if (camPerm != PermissionStatus.Granted)
                {
                    _statusMessage = "Camera permission is required to take a scoreboard photo.";
                    _isStatusError = true;
                    SaveStateToSessionCache();
                    BuildLayout();
                    return;
                }

                photo = await MediaPicker.Default.CapturePhotoAsync(new MediaPickerOptions
                {
                    Title = "Take a photo of the competition scoreboard"
                });
            }
            else
            {
                try
                {
                    var picked = await MediaPicker.Default.PickPhotosAsync(new MediaPickerOptions
                    {
                        Title = "Select a scoreboard photo or screenshot"
                    });
                    photo = picked?.FirstOrDefault();
                }
                catch
                {
                    photo = await FilePicker.Default.PickAsync(new PickOptions
                    {
                        PickerTitle = "Select a scoreboard photo or screenshot",
                        FileTypes = FilePickerFileType.Images
                    });
                }
            }

            if (photo == null)
            {
                return;
            }

            _isAnalyzing = true;
            _scanStageTitle = "Step 1/4: Loading selected photo...";
            _scanDetailMessage = "Reading image file from device storage";
            _scanProgressFraction = 0.08;
            _scanIsRetryOrFallback = false;
            _scanStopwatch.Restart();
            _statusMessage = string.Empty;
            BuildLayout();

            byte[] imageBytes;
            await using (var stream = await photo.OpenReadAsync())
            using (var ms = new MemoryStream())
            {
                await stream.CopyToAsync(ms);
                imageBytes = ms.ToArray();
            }

            string mimeType = !string.IsNullOrWhiteSpace(photo.ContentType)
                ? photo.ContentType
                : "image/jpeg";

            _photoBytes = imageBytes;
            _photoMimeType = mimeType;
            _isPhotoExpanded = false;
            SaveStateToSessionCache();

            await AnalyzeLoadedPhotoBytesAsync();
        }
        catch (Exception ex)
        {
            _isAnalyzing = false;
            _scanStopwatch.Stop();
            _statusMessage = $"Camera/Photo error: {ex.Message}";
            _isStatusError = true;
            SaveStateToSessionCache();
            BuildLayout();
        }
    }

    private async Task AnalyzeLoadedPhotoBytesAsync()
    {
        if (_photoBytes == null || _photoBytes.Length == 0)
        {
            return;
        }

        if (!_visionService.HasApiKey())
        {
            _isApiKeyExpanded = true;
            _statusMessage = "Please save your free Gemini API key first before scanning a scoreboard.";
            _isStatusError = true;
            SaveStateToSessionCache();
            BuildLayout();
            return;
        }

        try
        {
            _scanCts?.Cancel();
            _scanCts = new CancellationTokenSource();
            var token = _scanCts.Token;

            _isAnalyzing = true;
            _scanStageTitle = "Step 1/4: Preparing scoreboard photo for AI Vision...";
            _scanDetailMessage = "Connecting to Google Gemini Vision";
            _scanProgressFraction = 0.10;
            _scanIsRetryOrFallback = false;
            _scanStopwatch.Restart();
            _statusMessage = string.Empty;
            BuildLayout();

            StartElapsedTimerLoop(token);
            var progressReporter = new Progress<ScoreboardScanProgress>(UpdateProgressUiInPlace);

            var (success, errorMsg, lifters) = await _visionService.AnalyzeScoreboardImageAsync(
                _photoBytes,
                _photoMimeType,
                token,
                progressReporter);

            _isAnalyzing = false;
            _scanStopwatch.Stop();

            if (!success)
            {
                _statusMessage = errorMsg ?? "Failed to analyze scoreboard photo.";
                _isStatusError = true;
                SaveStateToSessionCache();
                BuildLayout();
                return;
            }

            _lifterRows = lifters;
            int autoExcluded = _lifterRows.Count(r => r.IsExcluded);
            _selectedUserIndex = ScoreboardCalculator.AutoMatchUserLifterIndex(
                _lifterRows,
                _viewModel.ActiveProfileName,
                _viewModel.GetOpenerWeightForDiscipline(LiftType.Snatch),
                LiftType.Snatch,
                fallbackCjOpenerWeightKg: _viewModel.GetOpenerWeightForDiscipline(LiftType.CleanAndJerk));
            _snatchOpenerOverrideKg = null;
            _cjOpenerOverrideKg = null;
            _snatchApplied = false;
            _cjApplied = false;
            RecalculateScoreboard();

            string excludedNote = autoExcluded > 0
                ? $" ({autoExcluded} prior-session row(s) auto-excluded)"
                : string.Empty;
            int elapsedSec = Math.Max(1, (int)Math.Round(_scanStopwatch.Elapsed.TotalSeconds));
            _statusMessage = $"Extracted {_lifterRows.Count} lifter rows{excludedNote} in {elapsedSec}s. Review Snatch & Clean & Jerk below!";
            _isStatusError = false;
            SaveStateToSessionCache();
            BuildLayout();
        }
        catch (Exception ex)
        {
            _isAnalyzing = false;
            _scanStopwatch.Stop();
            _statusMessage = $"Analysis error: {ex.Message}";
            _isStatusError = true;
            SaveStateToSessionCache();
            BuildLayout();
        }
    }

    private void ApplyUpdatedOpenerWeightAndSync(LiftType discipline, int newOpenerKg)
    {
        if (discipline == LiftType.Snatch)
        {
            _snatchOpenerOverrideKg = newOpenerKg;
        }
        else
        {
            _cjOpenerOverrideKg = newOpenerKg;
        }

        RecalculateScoreboard();

        ScoreboardAnalysisResult? updatedResult = discipline == LiftType.Snatch ? _snatchResult : _cjResult;
        bool wasApplied = discipline == LiftType.Snatch ? _snatchApplied : _cjApplied;
        int? updatedLiftsOut = null;

        if ((wasApplied || (_viewModel.IsLiveMode && _viewModel.SelectedLiftType == discipline)) &&
            updatedResult != null &&
            updatedResult.HasDisciplineWeightsOnBoard &&
            !updatedResult.IsDisciplineConcluded &&
            !updatedResult.IsUserOpenerCompleted &&
            !updatedResult.IsBarPastUserOpener)
        {
            updatedLiftsOut = updatedResult.LiftsOut;
        }

        _viewModel.SetOpenerWeightForDiscipline(discipline, newOpenerKg, updatedLiftsOut);

        string disciplineName = discipline == LiftType.Snatch ? "Snatch" : "Clean & Jerk";
        _statusMessage = $"Updated your {disciplineName} Opener to {newOpenerKg} kg (updated in Setup & Live).";
        _isStatusError = false;
        SaveStateToSessionCache();
    }

    private async Task PromptOverrideUserOpenerAsync(LiftType discipline, int currentOpenerKg)
    {
        string disciplineName = discipline == LiftType.Snatch ? "Snatch" : "Clean & Jerk";
        string? input = await DisplayPromptAsync(
            $"Change Your {disciplineName} Opener",
            $"Enter your 1st attempt {disciplineName} opener weight in whole kilograms (kg). This updates the scoreboard analysis, Setup settings, and Live mode:",
            accept: "Update",
            cancel: "Cancel",
            initialValue: currentOpenerKg.ToString(),
            keyboard: Keyboard.Numeric);

        if (string.IsNullOrWhiteSpace(input))
        {
            return;
        }

        if (int.TryParse(input.Trim(), out int newOpener) && newOpener >= 15 && newOpener <= 300)
        {
            ApplyUpdatedOpenerWeightAndSync(discipline, newOpener);
            BuildLayout();
        }
        else
        {
            await DisplayAlertAsync("Invalid Weight", "Please enter a whole kilogram weight between 15 and 300 kg.", "OK");
        }
    }

    private async Task PromptEditAttemptCellAsync(LiftType discipline, int lifterIndex, int attemptNumber, ResolvedLifterAttempt currentAttempt)
    {
        if (lifterIndex < 0 || lifterIndex >= _lifterRows.Count)
        {
            return;
        }

        var row = _lifterRows[lifterIndex];
        string disciplineLabel = discipline == LiftType.Snatch ? "Snatch" : "Clean & Jerk";
        int existingKg = currentAttempt.WeightKg;

        string action = await DisplayActionSheetAsync(
            $"{row.Name} — {disciplineLabel} Att {attemptNumber} ({existingKg} kg)",
            "Cancel",
            null,
            $"⏳ Mark as Not Taken Yet (Pending at {existingKg} kg)",
            $"✅ Mark as Good Lift (Completed at {existingKg} kg)",
            $"❌ Mark as Missed Lift (Completed at {existingKg} kg)",
            $"✏️ Change Weight ({existingKg} kg)...",
            "🔄 Not Declared Yet (Clear & Estimate Jump)",
            "⏭️ Mark as Pass / Forfeit",
            "🚫 Exclude This Lifter Row from Analysis");

        if (string.IsNullOrWhiteSpace(action) || action == "Cancel")
        {
            return;
        }

        if (action.StartsWith("🚫", StringComparison.Ordinal))
        {
            ToggleLifterExclusion(lifterIndex);
            return;
        }

        ScoreboardAttemptCell updatedCell;
        int? updatedUserOpenerKg = null;

        if (action.StartsWith("🔄", StringComparison.Ordinal))
        {
            updatedCell = new ScoreboardAttemptCell(null, ScoreboardAttemptStatus.Empty);
            if (lifterIndex == _selectedUserIndex && attemptNumber == 1)
            {
                if (discipline == LiftType.Snatch) _snatchOpenerOverrideKg = null;
                else _cjOpenerOverrideKg = null;
            }
        }
        else if (action.StartsWith("⏭️", StringComparison.Ordinal))
        {
            updatedCell = new ScoreboardAttemptCell(null, ScoreboardAttemptStatus.Pass);
        }
        else if (action.StartsWith("⏳", StringComparison.Ordinal))
        {
            updatedCell = new ScoreboardAttemptCell(existingKg, ScoreboardAttemptStatus.Pending);
        }
        else if (action.StartsWith("✅", StringComparison.Ordinal))
        {
            updatedCell = new ScoreboardAttemptCell(existingKg, ScoreboardAttemptStatus.Good);
        }
        else if (action.StartsWith("❌", StringComparison.Ordinal))
        {
            updatedCell = new ScoreboardAttemptCell(existingKg, ScoreboardAttemptStatus.Miss);
        }
        else
        {
            string? weightStr = await DisplayPromptAsync(
                $"{row.Name} — {disciplineLabel} Attempt {attemptNumber}",
                "Enter barbell weight in whole kilograms (kg):",
                accept: "Save",
                cancel: "Cancel",
                initialValue: existingKg.ToString(),
                keyboard: Keyboard.Numeric);

            if (string.IsNullOrWhiteSpace(weightStr) || !int.TryParse(weightStr.Trim(), out int newKg) || newKg < 15 || newKg > 350)
            {
                return;
            }

            // Preserve Good/Miss status if already marked completed, otherwise set to Pending on board
            ScoreboardAttemptStatus newStatus = currentAttempt.Origin == ScoreboardAttemptOrigin.CompletedGood
                ? ScoreboardAttemptStatus.Good
                : currentAttempt.Origin == ScoreboardAttemptOrigin.CompletedMiss
                    ? ScoreboardAttemptStatus.Miss
                    : ScoreboardAttemptStatus.Pending;

            updatedCell = new ScoreboardAttemptCell(newKg, newStatus);
            if (lifterIndex == _selectedUserIndex && attemptNumber == 1 && newStatus == ScoreboardAttemptStatus.Pending)
            {
                updatedUserOpenerKg = newKg;
            }
        }

        if (discipline == LiftType.Snatch)
        {
            if (attemptNumber == 1) row.Snatch1 = updatedCell;
            else if (attemptNumber == 2) row.Snatch2 = updatedCell;
            else row.Snatch3 = updatedCell;
            if (!updatedUserOpenerKg.HasValue) _snatchApplied = false;
        }
        else
        {
            if (attemptNumber == 1) row.Cj1 = updatedCell;
            else if (attemptNumber == 2) row.Cj2 = updatedCell;
            else row.Cj3 = updatedCell;
            if (!updatedUserOpenerKg.HasValue) _cjApplied = false;
        }

        if (updatedUserOpenerKg.HasValue)
        {
            ApplyUpdatedOpenerWeightAndSync(discipline, updatedUserOpenerKg.Value);
        }
        else
        {
            RecalculateScoreboard();
        }

        BuildLayout();
    }
}
