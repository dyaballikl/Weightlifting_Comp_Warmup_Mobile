using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Graphics;
using WeightliftingCompWarmup.Core.Models;
using WeightliftingCompWarmup.Core.Services;
using WeightliftingCompWarmup.Maui.Controls;
using WeightliftingCompWarmup.Maui.ViewModels;

namespace WeightliftingCompWarmup.Maui.Views;

/// <summary>
/// Adaptive cross-platform Main Page for Android (Phone &amp; Tablet) and iOS (iPhone &amp; iPad).
/// Automatically switches between a compact tabbed Phone layout and a multi-column Tablet/iPad dashboard.
/// </summary>
public class MainPage : ContentPage
{
    private readonly MainViewModel _vm;
    private IDispatcherTimer? _timer;
    private bool _isWideLayout;
    private bool _layoutInitialized;

    private readonly Grid _rootGrid;
    private readonly ContentView _headerContainer;
    private readonly ContentView _bodyContainer;
    private readonly List<Layout> _activeBindableLayouts = new();
    private ScrollView? _liveStepsScrollView;
    private VerticalStackLayout? _liveStepsStackLayout;
    private int _scrollRequestVersion;

    private Button? _beepHeaderBtn;
    private Button? _snatchHeaderBtn;
    private Button? _cjHeaderBtn;
    private Button? _setupHeaderBtn;
    private Button? _liveHeaderBtn;

    public MainPage(MainViewModel viewModel)
    {
        _vm = viewModel;
        BindingContext = _vm;

        Title = "Weightlifting Comp Warmup";
        BackgroundColor = Color.FromArgb("#0B1120");
        NavigationPage.SetHasNavigationBar(this, false);

        _rootGrid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto }, // Top App Header + Profile + Discipline + Mode Bar
                new RowDefinition { Height = GridLength.Auto }, // Optional Toast/Status Banner
                new RowDefinition { Height = GridLength.Star }  // Responsive Body (Phone or Tablet)
            },
            Padding = new Thickness(12, 10, 12, 10),
            RowSpacing = 8
        };

        _headerContainer = new ContentView();
        _bodyContainer = new ContentView();

        _headerContainer.Content = BuildTopNavigationHeader();
        _rootGrid.Add(_headerContainer, 0, 0);
        _rootGrid.Add(BuildStatusBanner(), 0, 1);
        _rootGrid.Add(_bodyContainer, 0, 2);

        Content = _rootGrid;

        _vm.PropertyChanged += OnViewModelPropertyChanged;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        if (_timer == null)
        {
            _timer = Dispatcher.CreateTimer();
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick += (_, _) => _vm.OnTimerTick();
            _timer.Start();
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _vm.StopLiveTimer();
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);

        if (width <= 0 || height <= 0)
        {
            return;
        }

        bool shouldBeWide = width >= 760 ||
                           (DeviceInfo.Current.Idiom == DeviceIdiom.Tablet && width >= 680);

        if (!_layoutInitialized || shouldBeWide != _isWideLayout)
        {
            _layoutInitialized = true;
            _isWideLayout = shouldBeWide;
            _rootGrid.Padding = _isWideLayout ? new Thickness(12, 10, 12, 10) : new Thickness(8, 6, 8, 6);
            _rootGrid.RowSpacing = _isWideLayout ? 8 : 6;
            _headerContainer.Content = BuildTopNavigationHeader();
            RebuildBodyLayout();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.BeepOnNextLift)
            or nameof(MainViewModel.SelectedLiftType)
            or nameof(MainViewModel.IsLiveMode))
        {
            UpdateHeaderVisualState();
        }

        if (e.PropertyName is nameof(MainViewModel.IsLiveMode)
            or nameof(MainViewModel.SelectedLiftType)
            or nameof(MainViewModel.SetupMobileSubTab))
        {
            RebuildBodyLayout();
        }
        else if (_vm.IsLiveMode && (
            (e.PropertyName == nameof(MainViewModel.IsLiveTimerRunning) && _vm.IsLiveTimerRunning) ||
            e.PropertyName == nameof(MainViewModel.ActiveStepOrder) ||
            e.PropertyName == nameof(MainViewModel.LiveSteps)))
        {
            ScrollActiveStepIntoViewIfNeeded(animated: e.PropertyName != nameof(MainViewModel.LiveSteps));
        }
    }

    private void RebuildBodyLayout()
    {
        _scrollRequestVersion++;

        foreach (var layout in _activeBindableLayouts)
        {
            try
            {
                layout.RemoveBinding(BindableLayout.ItemsSourceProperty);
                BindableLayout.SetItemsSource(layout, null);
            }
            catch
            {
                // Ignore if already detached
            }
        }
        _activeBindableLayouts.Clear();

        if (_vm.IsLiveMode)
        {
            _bodyContainer.Content = _isWideLayout
                ? BuildTabletLiveLayout()
                : BuildPhoneLiveLayout();

            ScrollActiveStepIntoViewIfNeeded(animated: false);
        }
        else
        {
            _liveStepsScrollView = null;
            _liveStepsStackLayout = null;
            _bodyContainer.Content = _isWideLayout
                ? BuildTabletSetupLayout()
                : BuildPhoneSetupLayout();
        }
    }

    private void ScrollActiveStepIntoViewIfNeeded(bool animated)
    {
        int requestVersion = ++_scrollRequestVersion;

        Dispatcher.Dispatch(async () =>
        {
            try
            {
                await Task.Delay(60);
                if (requestVersion != _scrollRequestVersion || !_vm.IsLiveMode)
                {
                    return;
                }

                var scroll = _liveStepsScrollView;
                var stack = _liveStepsStackLayout;
                if (scroll == null || stack == null || scroll.Handler == null || stack.Handler == null)
                {
                    return;
                }

                VisualElement? activeElement = null;
                foreach (var child in stack.Children)
                {
                    if (child is VisualElement ve && ve.BindingContext is LiveStepItemViewModel item && item.IsActive)
                    {
                        activeElement = ve;
                        break;
                    }
                }

                if (activeElement == null || activeElement.Handler == null)
                {
                    return;
                }

                if (activeElement.Height <= 0 || scroll.Height <= 0)
                {
                    await Task.Delay(80);
                    if (requestVersion != _scrollRequestVersion || !_vm.IsLiveMode)
                    {
                        return;
                    }
                }

                if (scroll.Handler == null || activeElement.Handler == null)
                {
                    return;
                }

                double scrollY = scroll.ScrollY;
                double viewportHeight = scroll.Height;
                double itemTop = activeElement.Y;
                double itemBottom = itemTop + activeElement.Height;

                bool isFullyInView = viewportHeight > 0 &&
                                     activeElement.Height > 0 &&
                                     itemTop >= scrollY &&
                                     itemBottom <= (scrollY + viewportHeight);

                if (!isFullyInView)
                {
                    await scroll.ScrollToAsync(activeElement, ScrollToPosition.Center, animated);
                }
            }
            catch
            {
                // Ignore if view was swapped or detached during async scroll
            }
        });
    }

    // =========================================================================
    // TOP HEADER BAR (Profile Picker, Global Bar/Beep Settings, Snatch/C&J, Setup/LIVE)
    // =========================================================================
    private View BuildTopNavigationHeader()
    {
        bool compact = !_isWideLayout;
        var headerCard = CreateCardBorder(padding: compact ? new Thickness(8, 6) : new Thickness(12, 10));
        var stack = new VerticalStackLayout { Spacing = compact ? 6 : 8 };

        // Row 1: Active Profile Button + Global Settings (Bar Weight + Beep)
        var topRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Auto }
            },
            ColumnSpacing = compact ? 5 : 8
        };

        var profileBtn = new Button
        {
            BackgroundColor = Color.FromArgb("#1E293B"),
            BorderColor = Color.FromArgb("#38BDF8"),
            BorderWidth = 1,
            TextColor = Color.FromArgb("#F8FAFC"),
            FontAttributes = FontAttributes.Bold,
            FontSize = compact ? 12 : 13,
            CornerRadius = 8,
            Padding = compact ? new Thickness(8, 2) : new Thickness(12, 6),
            HeightRequest = compact ? 34 : 40,
            HorizontalOptions = compact ? LayoutOptions.Fill : LayoutOptions.Start
        };
        profileBtn.SetBinding(Button.TextProperty, new Binding(nameof(MainViewModel.ActiveProfileName), stringFormat: "👤 {0} ▾"));
        profileBtn.Clicked += OnProfileMenuClicked;

        var barBtn = new Button
        {
            BackgroundColor = Color.FromArgb("#0F172A"),
            BorderColor = Color.FromArgb("#475569"),
            BorderWidth = 1,
            TextColor = Color.FromArgb("#E2E8F0"),
            FontSize = compact ? 11.5 : 12,
            FontAttributes = FontAttributes.Bold,
            CornerRadius = 8,
            Padding = compact ? new Thickness(7, 2) : new Thickness(10, 6),
            HeightRequest = compact ? 34 : 40
        };
        barBtn.SetBinding(Button.TextProperty, new Binding(nameof(MainViewModel.BarbellWeight), stringFormat: compact ? "🏋️ {0}kg" : "🏋️ Bar: {0}kg"));
        barBtn.Clicked += OnEditBarbellWeightClicked;

        var beepBtn = new Button
        {
            BackgroundColor = Color.FromArgb("#0F172A"),
            BorderColor = Color.FromArgb("#475569"),
            BorderWidth = 1,
            FontSize = compact ? 11.5 : 12,
            CornerRadius = 8,
            Padding = compact ? new Thickness(7, 2) : new Thickness(10, 6),
            HeightRequest = compact ? 34 : 40
        };
        beepBtn.Clicked += (_, _) => _vm.BeepOnNextLift = !_vm.BeepOnNextLift;
        _beepHeaderBtn = beepBtn;

        topRow.Add(profileBtn, 0, 0);
        topRow.Add(barBtn, 1, 0);
        topRow.Add(beepBtn, 2, 0);

        // Row 2: Discipline Selector (SNATCH vs CLEAN & JERK) + Mode Selector (SETUP vs LIVE)
        var navRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = new GridLength(compact ? 4 : 12) },
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Star }
            },
            ColumnSpacing = compact ? 4 : 6
        };

        var snatchTabBtn = CreateSegmentButton("SNATCH", _vm.SelectSnatchCommand);
        var cjTabBtn = CreateSegmentButton(compact ? "C&J" : "CLEAN & JERK", _vm.SelectCleanAndJerkCommand);
        var setupModeBtn = CreateSegmentButton("⚙️ SETUP", _vm.SwitchToSetupCommand);
        var liveModeBtn = CreateSegmentButton(compact ? "🔴 LIVE" : "🔴 LIVE RUNNER", _vm.StartLiveCommand);

        if (compact)
        {
            foreach (var b in new[] { snatchTabBtn, cjTabBtn, setupModeBtn, liveModeBtn })
            {
                b.FontSize = 11.5;
                b.Padding = new Thickness(4, 2);
                b.HeightRequest = 34;
            }
        }

        _snatchHeaderBtn = snatchTabBtn;
        _cjHeaderBtn = cjTabBtn;
        _setupHeaderBtn = setupModeBtn;
        _liveHeaderBtn = liveModeBtn;

        UpdateHeaderVisualState();

        navRow.Add(snatchTabBtn, 0, 0);
        navRow.Add(cjTabBtn, 1, 0);
        navRow.Add(setupModeBtn, 3, 0);
        navRow.Add(liveModeBtn, 4, 0);

        stack.Add(topRow);
        stack.Add(navRow);
        headerCard.Content = stack;
        return headerCard;
    }

    private void UpdateHeaderVisualState()
    {
        bool compact = !_isWideLayout;
        if (_beepHeaderBtn != null)
        {
            _beepHeaderBtn.Text = compact
                ? (_vm.BeepOnNextLift ? "🔔 ON" : "🔕 OFF")
                : (_vm.BeepOnNextLift ? "🔔 Beep: ON" : "🔕 Beep: OFF");
            _beepHeaderBtn.TextColor = _vm.BeepOnNextLift ? Color.FromArgb("#4ADE80") : Color.FromArgb("#94A3B8");
            _beepHeaderBtn.BorderColor = _vm.BeepOnNextLift ? Color.FromArgb("#22C55E") : Color.FromArgb("#475569");
        }

        if (_snatchHeaderBtn != null)
        {
            StyleSegmentButton(_snatchHeaderBtn, _vm.IsSnatchSelected, "#0284C7", "#38BDF8");
        }
        if (_cjHeaderBtn != null)
        {
            StyleSegmentButton(_cjHeaderBtn, _vm.IsCjSelected, "#0284C7", "#38BDF8");
        }
        if (_setupHeaderBtn != null)
        {
            StyleSegmentButton(_setupHeaderBtn, _vm.IsSetupMode, "#334155", "#94A3B8");
        }
        if (_liveHeaderBtn != null)
        {
            StyleSegmentButton(_liveHeaderBtn, _vm.IsLiveMode, "#16A34A", "#4ADE80");
        }
    }

    private View BuildStatusBanner()
    {
        var border = new Border
        {
            BackgroundColor = Color.FromArgb("#0C4A6E"),
            Stroke = Color.FromArgb("#38BDF8"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Padding = new Thickness(12, 6)
        };
        border.SetBinding(IsVisibleProperty, nameof(MainViewModel.HasStatusBanner));

        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Auto }
            }
        };

        var label = new Label
        {
            TextColor = Color.FromArgb("#E0F2FE"),
            FontSize = 12,
            VerticalTextAlignment = TextAlignment.Center
        };
        label.SetBinding(Label.TextProperty, nameof(MainViewModel.StatusBannerText));

        var dismissBtn = new Button
        {
            Text = "✕",
            BackgroundColor = Colors.Transparent,
            TextColor = Color.FromArgb("#7DD3FC"),
            FontSize = 12,
            Padding = new Thickness(6, 0),
            HeightRequest = 26
        };
        dismissBtn.Clicked += (_, _) => _vm.StatusBannerText = string.Empty;

        grid.Add(label, 0, 0);
        grid.Add(dismissBtn, 1, 0);
        border.Content = grid;
        return border;
    }

    // =========================================================================
    // SETUP VIEWS (ADAPTIVE TABLET 2-COLUMN DASHBOARD vs PHONE SUB-TABBED VIEW)
    // =========================================================================
    private View BuildTabletSetupLayout()
    {
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) },
                new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) }
            },
            ColumnSpacing = 12
        };

        // Left Column: Parameters + Opener Barbell + Calculated Warmup Steps
        var leftScroll = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Spacing = 10,
                Children =
                {
                    BuildLiftParametersCard(),
                    BuildOpenerBarbellCard(),
                    BuildPreviewStepsCard()
                }
            }
        };

        // Right Column: Extras Editor + Weight Jumps Editor + Warmup Times Editor
        var rightScroll = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Spacing = 10,
                Children =
                {
                    BuildExtrasEditorCard(),
                    BuildWeightJumpsEditorCard(),
                    BuildWarmupTimesEditorCard()
                }
            }
        };

        grid.Add(leftScroll, 0, 0);
        grid.Add(rightScroll, 1, 0);
        return grid;
    }

    private View BuildPhoneSetupLayout()
    {
        var outerGrid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Star }
            },
            RowSpacing = 8
        };

        // Sub-tab bar for Phone: [ 📋 Plan & Warmup Steps ] | [ ⚖️ Jumps, Times & Extras ]
        var subTabGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Star }
            },
            ColumnSpacing = 6
        };

        var overviewBtn = CreateSegmentButton("📋 Plan & Steps", _vm.SelectMobileSubTabCommand, "0");
        var rulesBtn = CreateSegmentButton("⚖️ Jumps, Times & Extras", _vm.SelectMobileSubTabCommand, "1");
        overviewBtn.FontSize = 11.5;
        overviewBtn.Padding = new Thickness(4, 2);
        overviewBtn.HeightRequest = 34;
        rulesBtn.FontSize = 11.5;
        rulesBtn.Padding = new Thickness(4, 2);
        rulesBtn.HeightRequest = 34;

        StyleSegmentButton(overviewBtn, _vm.IsMobileOverviewTab, "#0369A1", "#38BDF8");
        StyleSegmentButton(rulesBtn, _vm.IsMobileRulesTab, "#0369A1", "#38BDF8");

        subTabGrid.Add(overviewBtn, 0, 0);
        subTabGrid.Add(rulesBtn, 1, 0);

        var scroll = new ScrollView();
        if (_vm.IsMobileOverviewTab)
        {
            scroll.Content = new VerticalStackLayout
            {
                Spacing = 8,
                Children =
                {
                    BuildLiftParametersCard(),
                    BuildOpenerBarbellCard(),
                    BuildPreviewStepsCard()
                }
            };
        }
        else
        {
            scroll.Content = new VerticalStackLayout
            {
                Spacing = 8,
                Children =
                {
                    BuildExtrasEditorCard(),
                    BuildWeightJumpsEditorCard(),
                    BuildWarmupTimesEditorCard()
                }
            };
        }

        outerGrid.Add(subTabGrid, 0, 0);
        outerGrid.Add(scroll, 0, 1);
        return outerGrid;
    }

    private View BuildLiftParametersCard()
    {
        bool compact = !_isWideLayout;
        var card = CreateCardBorder(padding: compact ? new Thickness(10, 8) : new Thickness(12, 10));
        var stack = new VerticalStackLayout { Spacing = compact ? 8 : 10 };

        // Header + Quick Start LIVE CTA
        var headerRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Auto }
            },
            ColumnSpacing = 6
        };

        var titleLbl = new Label
        {
            Text = $"{_vm.CurrentLiftTitle} SETUP PARAMETERS",
            TextColor = Color.FromArgb("#38BDF8"),
            FontAttributes = FontAttributes.Bold,
            FontSize = compact ? 12.5 : 14,
            VerticalTextAlignment = TextAlignment.Center
        };

        var launchLiveBtn = new Button
        {
            Text = compact ? "▶ START LIVE" : $"▶ START {_vm.CurrentLiftTitle} LIVE",
            Command = _vm.StartLiveCommand,
            BackgroundColor = Color.FromArgb("#16A34A"),
            TextColor = Colors.White,
            FontAttributes = FontAttributes.Bold,
            FontSize = compact ? 11.5 : 12,
            CornerRadius = 8,
            Padding = compact ? new Thickness(10, 4) : new Thickness(12, 6),
            HeightRequest = compact ? 32 : 36
        };

        headerRow.Add(titleLbl, 0, 0);
        headerRow.Add(launchLiveBtn, 1, 0);
        stack.Add(headerRow);

        // Opener Weight Prominent Control (-5, -1, [Weight], +1, +5)
        if (compact)
        {
            var openerBlock = new VerticalStackLayout { Spacing = 4 };
            var openerLabelRow = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Auto }
                }
            };
            openerLabelRow.Add(new Label
            {
                Text = "Opener Weight (kg)",
                TextColor = Color.FromArgb("#F8FAFC"),
                FontAttributes = FontAttributes.Bold,
                FontSize = 13,
                VerticalTextAlignment = TextAlignment.Center
            }, 0, 0);
            openerLabelRow.Add(new Label
            {
                Text = "Tap weight to type directly",
                TextColor = Color.FromArgb("#64748B"),
                FontSize = 10.5,
                VerticalTextAlignment = TextAlignment.Center
            }, 1, 0);

            var openerButtonsGrid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) },
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Star }
                },
                ColumnSpacing = 6
            };

            Button CreatePhoneOpenerStepBtn(string text, string param)
            {
                return new Button
                {
                    Text = text,
                    Command = _vm.AdjustOpenerWeightCommand,
                    CommandParameter = param,
                    BackgroundColor = Color.FromArgb("#334155"),
                    TextColor = Colors.White,
                    FontAttributes = FontAttributes.Bold,
                    FontSize = 13,
                    CornerRadius = 6,
                    HeightRequest = 36,
                    HorizontalOptions = LayoutOptions.Fill,
                    Padding = Thickness.Zero
                };
            }

            var openerValueBtn = new Button
            {
                BackgroundColor = Color.FromArgb("#0F172A"),
                BorderColor = Color.FromArgb("#38BDF8"),
                BorderWidth = 1.5,
                TextColor = Color.FromArgb("#FACC15"),
                FontAttributes = FontAttributes.Bold,
                FontSize = 15,
                CornerRadius = 8,
                HeightRequest = 36,
                HorizontalOptions = LayoutOptions.Fill,
                Padding = Thickness.Zero
            };
            openerValueBtn.SetBinding(Button.TextProperty, new Binding(nameof(MainViewModel.OpenerWeight), stringFormat: "{0} kg"));
            openerValueBtn.Clicked += OnEditOpenerWeightClicked;

            openerButtonsGrid.Add(CreatePhoneOpenerStepBtn("-5", "-5"), 0, 0);
            openerButtonsGrid.Add(CreatePhoneOpenerStepBtn("-1", "-1"), 1, 0);
            openerButtonsGrid.Add(openerValueBtn, 2, 0);
            openerButtonsGrid.Add(CreatePhoneOpenerStepBtn("+1", "1"), 3, 0);
            openerButtonsGrid.Add(CreatePhoneOpenerStepBtn("+5", "5"), 4, 0);

            openerBlock.Add(openerLabelRow);
            openerBlock.Add(openerButtonsGrid);
            stack.Add(openerBlock);
        }
        else
        {
            var openerRow = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = GridLength.Auto }
                },
                ColumnSpacing = 6
            };

            openerRow.Add(CreateFieldLabel("Opener Weight (kg)", "Tap weight to type directly"), 0, 0);
            openerRow.Add(CreateStepButton("-5", _vm.AdjustOpenerWeightCommand, "-5"), 1, 0);
            openerRow.Add(CreateStepButton("-1", _vm.AdjustOpenerWeightCommand, "-1"), 2, 0);

            var openerValueBtn = new Button
            {
                BackgroundColor = Color.FromArgb("#0F172A"),
                BorderColor = Color.FromArgb("#38BDF8"),
                BorderWidth = 1.5,
                TextColor = Color.FromArgb("#FACC15"),
                FontAttributes = FontAttributes.Bold,
                FontSize = 16,
                CornerRadius = 8,
                WidthRequest = 86,
                HeightRequest = 40
            };
            openerValueBtn.SetBinding(Button.TextProperty, new Binding(nameof(MainViewModel.OpenerWeight), stringFormat: "{0} kg"));
            openerValueBtn.Clicked += OnEditOpenerWeightClicked;
            openerRow.Add(openerValueBtn, 3, 0);

            openerRow.Add(CreateStepButton("+1", _vm.AdjustOpenerWeightCommand, "1"), 4, 0);
            openerRow.Add(CreateStepButton("+5", _vm.AdjustOpenerWeightCommand, "5"), 5, 0);
            stack.Add(openerRow);
        }

        // Hit Opener in Warmup Switch
        var hitOpenerRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Auto }
            }
        };
        hitOpenerRow.Add(CreateFieldLabel("Hit opener in warmup", "Include opener weight as final warmup lift"), 0, 0);
        var openerSwitch = new Switch
        {
            OnColor = Color.FromArgb("#22C55E"),
            ThumbColor = Colors.White
        };
        openerSwitch.SetBinding(Switch.IsToggledProperty, nameof(MainViewModel.OpenerInWarmup), BindingMode.TwoWay);
        hitOpenerRow.Add(openerSwitch, 1, 0);
        stack.Add(hitOpenerRow);

        // Snatch specific: Competition Start Time
        if (_vm.IsSnatchSelected)
        {
            var timePicker = new TimePicker
            {
                TextColor = Color.FromArgb("#F8FAFC"),
                BackgroundColor = Color.FromArgb("#0F172A"),
                FontAttributes = FontAttributes.Bold,
                FontSize = compact ? 14 : 15,
                HorizontalOptions = compact ? LayoutOptions.Fill : LayoutOptions.End,
                VerticalOptions = LayoutOptions.Center
            };
            timePicker.SetBinding(TimePicker.TimeProperty, nameof(MainViewModel.CompetitionStartTime), BindingMode.TwoWay);

            if (compact)
            {
                var startTimeBlock = new VerticalStackLayout { Spacing = 4 };
                var startLabelRow = new Grid
                {
                    ColumnDefinitions =
                    {
                        new ColumnDefinition { Width = GridLength.Star },
                        new ColumnDefinition { Width = GridLength.Auto }
                    }
                };
                startLabelRow.Add(new Label
                {
                    Text = "Competition Start Time",
                    TextColor = Color.FromArgb("#F8FAFC"),
                    FontAttributes = FontAttributes.Bold,
                    FontSize = 13,
                    VerticalTextAlignment = TextAlignment.Center
                }, 0, 0);
                startLabelRow.Add(new Label
                {
                    Text = "Scheduled session start",
                    TextColor = Color.FromArgb("#64748B"),
                    FontSize = 10.5,
                    VerticalTextAlignment = TextAlignment.Center
                }, 1, 0);

                startTimeBlock.Add(startLabelRow);
                startTimeBlock.Add(timePicker);
                stack.Add(startTimeBlock);
            }
            else
            {
                var startTimeRow = new Grid
                {
                    ColumnDefinitions =
                    {
                        new ColumnDefinition { Width = GridLength.Star },
                        new ColumnDefinition { Width = GridLength.Auto }
                    }
                };
                startTimeRow.Add(CreateFieldLabel("Competition Start Time", "Scheduled session start (presentation)"), 0, 0);
                startTimeRow.Add(timePicker, 1, 0);
                stack.Add(startTimeRow);
            }

            stack.Add(BuildStepperParamRow(
                title: "Presentation & clock (min)",
                subtitle: "Presentation + 10m clock before snatch",
                bindingPath: nameof(MainViewModel.SnatchBreakMinutes),
                unitSuffix: "min",
                adjustCommand: _vm.AdjustSnatchBreakMinutesCommand,
                onTapEdit: OnEditSnatchBreakMinutesClicked));
        }

        // Clean & Jerk specific: Break After Snatch (min) & Snatch Attempts Remaining
        if (_vm.IsCjSelected)
        {
            stack.Add(BuildStepperParamRow(
                title: "Break after snatch (min)",
                subtitle: "Competition break between Snatch and C&J",
                bindingPath: nameof(MainViewModel.CjBreakMinutes),
                unitSuffix: "min",
                adjustCommand: _vm.AdjustCjBreakMinutesCommand,
                onTapEdit: OnEditCjBreakMinutesClicked));

            stack.Add(BuildStepperParamRow(
                title: "Snatch attempts remaining",
                subtitle: "Snatch lifts left before break starts",
                bindingPath: nameof(MainViewModel.CjSnatchAttemptsRemaining),
                unitSuffix: "lifts",
                adjustCommand: _vm.AdjustCjSnatchRemainingCommand,
                onTapEdit: OnEditCjSnatchRemainingClicked));
        }

        // Shared: Lifts Out, Stage Lift Time, Extra Time After Last Warmup
        stack.Add(BuildStepperParamRow(
            title: "Lifts out",
            subtitle: "Attempts on platform before your opener",
            bindingPath: nameof(MainViewModel.SetupLiftsOut),
            unitSuffix: "lifts",
            adjustCommand: _vm.AdjustSetupLiftsOutCommand,
            onTapEdit: OnEditSetupLiftsOutClicked));

        stack.Add(BuildStepperParamRow(
            title: "Stage lift time (sec)",
            subtitle: "Average duration per competition attempt",
            bindingPath: nameof(MainViewModel.StageSeconds),
            unitSuffix: "sec",
            adjustCommand: _vm.AdjustStageSecondsCommand,
            onTapEdit: OnEditStageSecondsClicked));

        stack.Add(BuildStepperParamRow(
            title: "Extra time after last warmup (sec)",
            subtitle: "Buffer between final warmup and platform opener",
            bindingPath: nameof(MainViewModel.ExtraSecondsEnd),
            unitSuffix: "sec",
            adjustCommand: _vm.AdjustExtraSecondsEndCommand,
            onTapEdit: OnEditExtraSecondsEndClicked,
            smallStep: "-5",
            largeStep: "5"));

        card.Content = stack;
        return card;
    }

    private View BuildOpenerBarbellCard()
    {
        bool compact = !_isWideLayout;
        var card = CreateCardBorder(padding: compact ? new Thickness(10, 6) : new Thickness(12, 8));

        var barbellView = new BarbellPlateView
        {
            IsOpener = true,
            IsFullBarbell = true,
            HeightRequest = compact ? 76 : 92,
            HorizontalOptions = LayoutOptions.Fill
        };
        barbellView.SetBinding(BarbellPlateView.LiftWeightProperty, nameof(MainViewModel.OpenerWeight));
        barbellView.SetBinding(BarbellPlateView.BarWeightProperty, nameof(MainViewModel.BarbellWeight));

        card.Content = barbellView;
        return card;
    }

    private View BuildPreviewStepsCard()
    {
        bool compact = !_isWideLayout;
        var card = CreateCardBorder(padding: compact ? new Thickness(8, 8) : new Thickness(12, 10));
        var stack = new VerticalStackLayout { Spacing = compact ? 6 : 8 };

        var headerGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Auto }
            }
        };

        headerGrid.Add(new Label
        {
            Text = "CALCULATED WARMUP SCHEDULE",
            TextColor = Color.FromArgb("#38BDF8"),
            FontAttributes = FontAttributes.Bold,
            FontSize = compact ? 12 : 13
        }, 0, 0);

        var countLbl = new Label
        {
            TextColor = Color.FromArgb("#E2E8F0"),
            FontAttributes = FontAttributes.Bold,
            FontSize = compact ? 11 : 12
        };
        countLbl.SetBinding(Label.TextProperty, nameof(MainViewModel.StepsSummaryHeader));
        headerGrid.Add(countLbl, 1, 0);
        stack.Add(headerGrid);

        // Column headers (matching exact column widths, spacing, and padding of row cards below)
        var colHeader = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(compact ? 18 : 28) },
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = new GridLength(compact ? 44 : 64) },
                new ColumnDefinition { Width = new GridLength(compact ? 58 : 92) },
                new ColumnDefinition { Width = new GridLength(compact ? 40 : 62) },
                new ColumnDefinition { Width = new GridLength(compact ? 44 : 68) }
            },
            ColumnSpacing = compact ? 4 : 6,
            Padding = compact ? new Thickness(7, 4) : new Thickness(9, 5),
            BackgroundColor = Color.FromArgb("#0F172A")
        };
        colHeader.Add(CreateTableHeaderLabel("#"), 0, 0);
        colHeader.Add(CreateTableHeaderLabel("STEP"), 1, 0);
        colHeader.Add(CreateTableHeaderLabel(compact ? "WT" : "WEIGHT"), 2, 0);
        colHeader.Add(CreateTableHeaderLabel("PLATES"), 3, 0);
        colHeader.Add(CreateTableHeaderLabel(compact ? "DUR" : "DURATION"), 4, 0);
        colHeader.Add(CreateTableHeaderLabel("TOTAL"), 5, 0);
        stack.Add(colHeader);

        var listLayout = new VerticalStackLayout { Spacing = 4 };
        listLayout.SetBinding(BindableLayout.ItemsSourceProperty, nameof(MainViewModel.PreviewSteps));
        _activeBindableLayouts.Add(listLayout);
        BindableLayout.SetItemTemplate(listLayout, new DataTemplate(() =>
        {
            var rowBorder = new Border
            {
                BackgroundColor = Color.FromArgb("#0F172A"),
                Stroke = Color.FromArgb("#1E293B"),
                StrokeThickness = 1,
                StrokeShape = new RoundRectangle { CornerRadius = 6 },
                Padding = compact ? new Thickness(6, 4) : new Thickness(8, 5)
            };

            var rowGrid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = new GridLength(compact ? 18 : 28) },
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = new GridLength(compact ? 44 : 64) },
                    new ColumnDefinition { Width = new GridLength(compact ? 58 : 92) },
                    new ColumnDefinition { Width = new GridLength(compact ? 40 : 62) },
                    new ColumnDefinition { Width = new GridLength(compact ? 44 : 68) }
                },
                ColumnSpacing = compact ? 4 : 6
            };

            var orderLbl = new Label
            {
                TextColor = Color.FromArgb("#64748B"),
                FontSize = compact ? 11 : 12,
                VerticalTextAlignment = TextAlignment.Center
            };
            orderLbl.SetBinding(Label.TextProperty, nameof(SetupStepItemViewModel.Order));

            var actionStack = new VerticalStackLayout { Spacing = 1, VerticalOptions = LayoutOptions.Center };
            var actionLbl = new Label
            {
                TextColor = Color.FromArgb("#F8FAFC"),
                FontAttributes = FontAttributes.Bold,
                FontSize = compact ? 12 : 13,
                LineBreakMode = LineBreakMode.TailTruncation,
                MaxLines = 1
            };
            actionLbl.SetBinding(Label.TextProperty, nameof(SetupStepItemViewModel.Action));

            var reverseLbl = new Label
            {
                TextColor = Color.FromArgb("#64748B"),
                FontSize = compact ? 9.5 : 10,
                LineBreakMode = LineBreakMode.TailTruncation,
                MaxLines = 1
            };
            reverseLbl.SetBinding(Label.TextProperty, nameof(SetupStepItemViewModel.FormattedTotalLengthReverse));
            actionStack.Add(actionLbl);
            actionStack.Add(reverseLbl);

            var weightLbl = new Label
            {
                TextColor = Color.FromArgb("#FACC15"),
                FontAttributes = FontAttributes.Bold,
                FontSize = compact ? 11.5 : 13,
                VerticalTextAlignment = TextAlignment.Center
            };
            weightLbl.SetBinding(Label.TextProperty, nameof(SetupStepItemViewModel.WeightDisplay));

            var sleeveGraphic = new BarbellPlateView
            {
                IsOpener = false,
                IsFullBarbell = false,
                HeightRequest = compact ? 26 : 34,
                WidthRequest = compact ? 56 : 88,
                HorizontalOptions = LayoutOptions.Start,
                VerticalOptions = LayoutOptions.Center
            };
            sleeveGraphic.SetBinding(IsVisibleProperty, nameof(SetupStepItemViewModel.IsLift));
            sleeveGraphic.SetBinding(BarbellPlateView.LiftWeightProperty, nameof(SetupStepItemViewModel.Weight));
            sleeveGraphic.SetBinding(BarbellPlateView.BarWeightProperty, nameof(SetupStepItemViewModel.BarbellWeight));

            var lengthLbl = new Label
            {
                TextColor = Color.FromArgb("#38BDF8"),
                FontSize = compact ? 11 : 12,
                VerticalTextAlignment = TextAlignment.Center
            };
            lengthLbl.SetBinding(Label.TextProperty, nameof(SetupStepItemViewModel.FormattedLength));

            var totalLbl = new Label
            {
                TextColor = Color.FromArgb("#94A3B8"),
                FontSize = compact ? 11 : 12,
                VerticalTextAlignment = TextAlignment.Center
            };
            totalLbl.SetBinding(Label.TextProperty, nameof(SetupStepItemViewModel.FormattedTotalLength));

            rowGrid.Add(orderLbl, 0, 0);
            rowGrid.Add(actionStack, 1, 0);
            rowGrid.Add(weightLbl, 2, 0);
            rowGrid.Add(sleeveGraphic, 3, 0);
            rowGrid.Add(lengthLbl, 4, 0);
            rowGrid.Add(totalLbl, 5, 0);

            rowBorder.Content = rowGrid;
            return rowBorder;
        }));

        stack.Add(listLayout);
        card.Content = stack;
        return card;
    }

    // =========================================================================
    // RULE EDITORS (EXTRAS, WEIGHT JUMPS, WARMUP TIMES)
    // Shared 3-Column Grid for 1:1 Vertical Alignment (Responsive Phone vs Tablet)
    // =========================================================================
    private static Grid CreateAlignedRuleGrid(bool compact, Thickness? padding = null)
    {
        return new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = new GridLength(compact ? 96 : 148) },
                new ColumnDefinition { Width = new GridLength(compact ? 92 : 108) }
            },
            ColumnSpacing = compact ? 6 : 8,
            Padding = padding ?? Thickness.Zero
        };
    }

    private View BuildExtrasEditorCard()
    {
        bool compact = !_isWideLayout;
        var card = CreateCardBorder(padding: compact ? new Thickness(10, 8) : new Thickness(12, 10));
        var stack = new VerticalStackLayout { Spacing = 8 };

        stack.Add(new Label
        {
            Text = "WARMUP EXTRAS (PRE-BARBELL MOBILITY / PREP)",
            TextColor = Color.FromArgb("#38BDF8"),
            FontAttributes = FontAttributes.Bold,
            FontSize = compact ? 12 : 13
        });

        // Column Header Bar
        var headerBar = CreateAlignedRuleGrid(compact, padding: new Thickness(compact ? 6 : 10, 5));
        headerBar.BackgroundColor = Color.FromArgb("#0F172A");
        headerBar.Add(CreateTableHeaderLabel("STEP / ACTION"), 0, 0);
        headerBar.Add(CreateTableHeaderLabel(compact ? "DURATION" : "DURATION (SEC)"), 1, 0);
        var actHdr = CreateTableHeaderLabel("ACTIONS");
        actHdr.HorizontalTextAlignment = TextAlignment.End;
        headerBar.Add(actHdr, 2, 0);
        stack.Add(headerBar);

        // Add New Extra Row (aligned to exact same 3 columns)
        var addRow = CreateAlignedRuleGrid(compact);

        var actionEntry = new Entry
        {
            Placeholder = compact ? "Action (e.g. Roll)" : "Action (e.g. Foam Roll)",
            PlaceholderColor = Color.FromArgb("#64748B"),
            TextColor = Colors.White,
            BackgroundColor = Color.FromArgb("#0F172A"),
            FontSize = compact ? 12 : 13,
            HeightRequest = 38,
            VerticalOptions = LayoutOptions.Center
        };
        actionEntry.SetBinding(Entry.TextProperty, nameof(MainViewModel.NewExtraAction), BindingMode.TwoWay);

        // Duration column: Entry + formatted preview side-by-side
        var durationInputGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = new GridLength(compact ? 36 : 52) }
            },
            ColumnSpacing = compact ? 4 : 6,
            VerticalOptions = LayoutOptions.Center
        };

        var secEntry = new Entry
        {
            Placeholder = "Sec",
            PlaceholderColor = Color.FromArgb("#64748B"),
            Keyboard = Keyboard.Numeric,
            TextColor = Colors.White,
            BackgroundColor = Color.FromArgb("#0F172A"),
            FontSize = compact ? 12 : 13,
            HeightRequest = 38,
            VerticalOptions = LayoutOptions.Center
        };
        secEntry.SetBinding(Entry.TextProperty, nameof(MainViewModel.NewExtraSecondsText), BindingMode.TwoWay);

        var previewLbl = new Label
        {
            TextColor = Color.FromArgb("#38BDF8"),
            FontAttributes = FontAttributes.Bold,
            FontSize = compact ? 11 : 12,
            HorizontalTextAlignment = TextAlignment.End,
            VerticalTextAlignment = TextAlignment.Center,
            MaxLines = 1
        };
        previewLbl.SetBinding(Label.TextProperty, nameof(MainViewModel.NewExtraFormattedPreview));

        durationInputGrid.Add(secEntry, 0, 0);
        durationInputGrid.Add(previewLbl, 1, 0);

        var addBtn = new Button
        {
            Text = compact ? "+ Add" : "+ Add Step",
            Command = _vm.AddExtraCommand,
            BackgroundColor = Color.FromArgb("#0284C7"),
            TextColor = Colors.White,
            FontAttributes = FontAttributes.Bold,
            FontSize = 12,
            CornerRadius = 6,
            Padding = Thickness.Zero,
            HeightRequest = 38,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Center
        };

        addRow.Add(actionEntry, 0, 0);
        addRow.Add(durationInputGrid, 1, 0);
        addRow.Add(addBtn, 2, 0);
        stack.Add(addRow);

        // Existing Extras List with Reorder Up (▲), Down (▼), Delete (✕)
        var extrasListLayout = new VerticalStackLayout { Spacing = 4 };
        extrasListLayout.SetBinding(BindableLayout.ItemsSourceProperty, nameof(MainViewModel.ExtrasList));
        _activeBindableLayouts.Add(extrasListLayout);
        BindableLayout.SetItemTemplate(extrasListLayout, new DataTemplate(() =>
        {
            var rowBorder = new Border
            {
                BackgroundColor = Color.FromArgb("#0F172A"),
                Stroke = Color.FromArgb("#1E293B"),
                StrokeThickness = 1,
                StrokeShape = new RoundRectangle { CornerRadius = 6 },
                Padding = new Thickness(compact ? 6 : 10, 4),
                MinimumHeightRequest = 42
            };

            var row = CreateAlignedRuleGrid(compact);

            var leftNameGrid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = new GridLength(compact ? 20 : 24) },
                    new ColumnDefinition { Width = GridLength.Star }
                },
                ColumnSpacing = 4,
                VerticalOptions = LayoutOptions.Center
            };

            var orderLbl = new Label
            {
                TextColor = Color.FromArgb("#64748B"),
                FontAttributes = FontAttributes.Bold,
                FontSize = 12,
                VerticalTextAlignment = TextAlignment.Center
            };
            orderLbl.SetBinding(Label.TextProperty, new Binding(nameof(ExtraItem.Order), stringFormat: "{0}."));

            var nameLbl = new Label
            {
                TextColor = Color.FromArgb("#F8FAFC"),
                FontAttributes = FontAttributes.Bold,
                FontSize = compact ? 12 : 13,
                VerticalTextAlignment = TextAlignment.Center,
                LineBreakMode = LineBreakMode.TailTruncation,
                MaxLines = 1
            };
            nameLbl.SetBinding(Label.TextProperty, nameof(ExtraItem.Action));

            leftNameGrid.Add(orderLbl, 0, 0);
            leftNameGrid.Add(nameLbl, 1, 0);

            var timeLbl = new Label
            {
                TextColor = Color.FromArgb("#38BDF8"),
                FontAttributes = FontAttributes.Bold,
                FontSize = compact ? 12 : 13,
                VerticalTextAlignment = TextAlignment.Center
            };
            timeLbl.SetBinding(Label.TextProperty, nameof(ExtraItem.FormattedLength));

            double btnWidth = compact ? 27 : 32;
            var actionsGrid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = new GridLength(btnWidth) },
                    new ColumnDefinition { Width = new GridLength(btnWidth) },
                    new ColumnDefinition { Width = new GridLength(btnWidth) }
                },
                ColumnSpacing = compact ? 4 : 6,
                HorizontalOptions = LayoutOptions.End,
                VerticalOptions = LayoutOptions.Center
            };

            var upBtn = CreateIconActionButton("▲", "#334155", _vm.MoveExtraUpCommand, btnWidth);
            var downBtn = CreateIconActionButton("▼", "#334155", _vm.MoveExtraDownCommand, btnWidth);
            var delBtn = CreateIconActionButton("✕", "#7F1D1D", _vm.DeleteExtraCommand, btnWidth);

            actionsGrid.Add(upBtn, 0, 0);
            actionsGrid.Add(downBtn, 1, 0);
            actionsGrid.Add(delBtn, 2, 0);

            row.Add(leftNameGrid, 0, 0);
            row.Add(timeLbl, 1, 0);
            row.Add(actionsGrid, 2, 0);

            rowBorder.Content = row;
            return rowBorder;
        }));

        stack.Add(extrasListLayout);
        card.Content = stack;
        return card;
    }

    private View BuildWeightJumpsEditorCard()
    {
        bool compact = !_isWideLayout;
        var card = CreateCardBorder(padding: compact ? new Thickness(10, 8) : new Thickness(12, 10));
        var stack = new VerticalStackLayout { Spacing = 8 };

        stack.Add(new Label
        {
            Text = "WEIGHT JUMPS TABLE (FROM WEIGHT → +JUMP KG)",
            TextColor = Color.FromArgb("#38BDF8"),
            FontAttributes = FontAttributes.Bold,
            FontSize = compact ? 12 : 13
        });

        // Column Header Bar
        var headerBar = CreateAlignedRuleGrid(compact, padding: new Thickness(compact ? 6 : 10, 5));
        headerBar.BackgroundColor = Color.FromArgb("#0F172A");
        headerBar.Add(CreateTableHeaderLabel("FROM WEIGHT"), 0, 0);
        headerBar.Add(CreateTableHeaderLabel("WEIGHT JUMP"), 1, 0);
        var actHdr = CreateTableHeaderLabel("ACTION");
        actHdr.HorizontalTextAlignment = TextAlignment.End;
        headerBar.Add(actHdr, 2, 0);
        stack.Add(headerBar);

        // Add Rule Row (aligned to exact same 3 columns)
        var addRow = CreateAlignedRuleGrid(compact);

        var fromEntry = new Entry
        {
            Placeholder = compact ? $"From (1–{Defaults.MaxOpenerWeight}kg)" : $"From Weight (1–{Defaults.MaxOpenerWeight} kg)",
            PlaceholderColor = Color.FromArgb("#64748B"),
            Keyboard = Keyboard.Numeric,
            TextColor = Colors.White,
            BackgroundColor = Color.FromArgb("#0F172A"),
            FontSize = compact ? 12 : 13,
            HeightRequest = 38,
            VerticalOptions = LayoutOptions.Center
        };
        fromEntry.SetBinding(Entry.TextProperty, nameof(MainViewModel.NewJumpFromWeightText), BindingMode.TwoWay);

        var jumpEntry = new Entry
        {
            Placeholder = "Jump (+kg)",
            PlaceholderColor = Color.FromArgb("#64748B"),
            Keyboard = Keyboard.Numeric,
            TextColor = Colors.White,
            BackgroundColor = Color.FromArgb("#0F172A"),
            FontSize = compact ? 12 : 13,
            HeightRequest = 38,
            VerticalOptions = LayoutOptions.Center
        };
        jumpEntry.SetBinding(Entry.TextProperty, nameof(MainViewModel.NewJumpKgText), BindingMode.TwoWay);

        var addBtn = new Button
        {
            Text = compact ? "+ Save" : "+ Save Rule",
            Command = _vm.AddWeightJumpCommand,
            BackgroundColor = Color.FromArgb("#0284C7"),
            TextColor = Colors.White,
            FontAttributes = FontAttributes.Bold,
            FontSize = 12,
            CornerRadius = 6,
            Padding = Thickness.Zero,
            HeightRequest = 38,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Center
        };

        addRow.Add(fromEntry, 0, 0);
        addRow.Add(jumpEntry, 1, 0);
        addRow.Add(addBtn, 2, 0);
        stack.Add(addRow);

        var listLayout = new VerticalStackLayout { Spacing = 4 };
        listLayout.SetBinding(BindableLayout.ItemsSourceProperty, nameof(MainViewModel.WeightJumpsList));
        _activeBindableLayouts.Add(listLayout);
        BindableLayout.SetItemTemplate(listLayout, new DataTemplate(() =>
        {
            var rowBorder = new Border
            {
                BackgroundColor = Color.FromArgb("#0F172A"),
                Stroke = Color.FromArgb("#1E293B"),
                StrokeThickness = 1,
                StrokeShape = new RoundRectangle { CornerRadius = 6 },
                Padding = new Thickness(compact ? 6 : 10, 4),
                MinimumHeightRequest = 42
            };

            var row = CreateAlignedRuleGrid(compact);

            var fromLbl = new Label
            {
                TextColor = Color.FromArgb("#F8FAFC"),
                FontAttributes = FontAttributes.Bold,
                FontSize = compact ? 12 : 13,
                VerticalTextAlignment = TextAlignment.Center
            };
            fromLbl.SetBinding(Label.TextProperty, new Binding(nameof(WeightJumpRule.FromWeight), stringFormat: "From {0} kg"));

            var jumpLbl = new Label
            {
                TextColor = Color.FromArgb("#FACC15"),
                FontAttributes = FontAttributes.Bold,
                FontSize = compact ? 12 : 13,
                VerticalTextAlignment = TextAlignment.Center
            };
            jumpLbl.SetBinding(Label.TextProperty, new Binding(nameof(WeightJumpRule.Jump), stringFormat: "+{0} kg jump"));

            var actionCell = new Grid
            {
                HorizontalOptions = LayoutOptions.End,
                VerticalOptions = LayoutOptions.Center,
                MinimumHeightRequest = 30
            };

            var baseBadge = new Label
            {
                Text = "BASE",
                TextColor = Color.FromArgb("#64748B"),
                FontAttributes = FontAttributes.Bold,
                FontSize = 10,
                VerticalTextAlignment = TextAlignment.Center,
                HorizontalTextAlignment = TextAlignment.End
            };

            var delBtn = CreateIconActionButton("✕", "#7F1D1D", _vm.DeleteWeightJumpCommand);
            delBtn.SetBinding(IsVisibleProperty, nameof(WeightJumpRule.CanDelete));

            rowBorder.BindingContextChanged += (_, _) =>
            {
                if (rowBorder.BindingContext is WeightJumpRule rule)
                {
                    baseBadge.IsVisible = !rule.CanDelete;
                }
            };

            actionCell.Add(baseBadge);
            actionCell.Add(delBtn);

            row.Add(fromLbl, 0, 0);
            row.Add(jumpLbl, 1, 0);
            row.Add(actionCell, 2, 0);

            rowBorder.Content = row;
            return rowBorder;
        }));

        stack.Add(listLayout);
        card.Content = stack;
        return card;
    }

    private View BuildWarmupTimesEditorCard()
    {
        bool compact = !_isWideLayout;
        var card = CreateCardBorder(padding: compact ? new Thickness(10, 8) : new Thickness(12, 10));
        var stack = new VerticalStackLayout { Spacing = 8 };

        stack.Add(new Label
        {
            Text = "WARMUP REST / INTERVAL TIMES (FROM WEIGHT → SEC)",
            TextColor = Color.FromArgb("#38BDF8"),
            FontAttributes = FontAttributes.Bold,
            FontSize = compact ? 12 : 13
        });

        // Column Header Bar
        var headerBar = CreateAlignedRuleGrid(compact, padding: new Thickness(compact ? 6 : 10, 5));
        headerBar.BackgroundColor = Color.FromArgb("#0F172A");
        headerBar.Add(CreateTableHeaderLabel("FROM WEIGHT"), 0, 0);
        headerBar.Add(CreateTableHeaderLabel("REST INTERVAL"), 1, 0);
        var actHdr = CreateTableHeaderLabel("ACTION");
        actHdr.HorizontalTextAlignment = TextAlignment.End;
        headerBar.Add(actHdr, 2, 0);
        stack.Add(headerBar);

        // Add Rule Row (aligned to exact same 3 columns)
        var addRow = CreateAlignedRuleGrid(compact);

        var fromEntry = new Entry
        {
            Placeholder = compact ? $"From (1–{Defaults.MaxOpenerWeight}kg)" : $"From Weight (1–{Defaults.MaxOpenerWeight} kg)",
            PlaceholderColor = Color.FromArgb("#64748B"),
            Keyboard = Keyboard.Numeric,
            TextColor = Colors.White,
            BackgroundColor = Color.FromArgb("#0F172A"),
            FontSize = compact ? 12 : 13,
            HeightRequest = 38,
            VerticalOptions = LayoutOptions.Center
        };
        fromEntry.SetBinding(Entry.TextProperty, nameof(MainViewModel.NewTimeFromWeightText), BindingMode.TwoWay);

        // Interval column: Entry + formatted preview side-by-side
        var intervalInputGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = new GridLength(compact ? 36 : 52) }
            },
            ColumnSpacing = compact ? 4 : 6,
            VerticalOptions = LayoutOptions.Center
        };

        var secEntry = new Entry
        {
            Placeholder = "Sec",
            PlaceholderColor = Color.FromArgb("#64748B"),
            Keyboard = Keyboard.Numeric,
            TextColor = Colors.White,
            BackgroundColor = Color.FromArgb("#0F172A"),
            FontSize = compact ? 12 : 13,
            HeightRequest = 38,
            VerticalOptions = LayoutOptions.Center
        };
        secEntry.SetBinding(Entry.TextProperty, nameof(MainViewModel.NewTimeSecondsText), BindingMode.TwoWay);

        var previewLbl = new Label
        {
            TextColor = Color.FromArgb("#38BDF8"),
            FontAttributes = FontAttributes.Bold,
            FontSize = compact ? 11 : 12,
            HorizontalTextAlignment = TextAlignment.End,
            VerticalTextAlignment = TextAlignment.Center,
            MaxLines = 1
        };
        previewLbl.SetBinding(Label.TextProperty, nameof(MainViewModel.NewTimeFormattedPreview));

        intervalInputGrid.Add(secEntry, 0, 0);
        intervalInputGrid.Add(previewLbl, 1, 0);

        var addBtn = new Button
        {
            Text = compact ? "+ Save" : "+ Save Rule",
            Command = _vm.AddWarmupTimeCommand,
            BackgroundColor = Color.FromArgb("#0284C7"),
            TextColor = Colors.White,
            FontAttributes = FontAttributes.Bold,
            FontSize = 12,
            CornerRadius = 6,
            Padding = Thickness.Zero,
            HeightRequest = 38,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Center
        };

        addRow.Add(fromEntry, 0, 0);
        addRow.Add(intervalInputGrid, 1, 0);
        addRow.Add(addBtn, 2, 0);
        stack.Add(addRow);

        var listLayout = new VerticalStackLayout { Spacing = 4 };
        listLayout.SetBinding(BindableLayout.ItemsSourceProperty, nameof(MainViewModel.WarmupTimesList));
        _activeBindableLayouts.Add(listLayout);
        BindableLayout.SetItemTemplate(listLayout, new DataTemplate(() =>
        {
            var rowBorder = new Border
            {
                BackgroundColor = Color.FromArgb("#0F172A"),
                Stroke = Color.FromArgb("#1E293B"),
                StrokeThickness = 1,
                StrokeShape = new RoundRectangle { CornerRadius = 6 },
                Padding = new Thickness(compact ? 6 : 10, 4),
                MinimumHeightRequest = 42
            };

            var row = CreateAlignedRuleGrid(compact);

            var fromLbl = new Label
            {
                TextColor = Color.FromArgb("#F8FAFC"),
                FontAttributes = FontAttributes.Bold,
                FontSize = compact ? 12 : 13,
                VerticalTextAlignment = TextAlignment.Center
            };
            fromLbl.SetBinding(Label.TextProperty, new Binding(nameof(WarmupTimeRule.FromWeight), stringFormat: "From {0} kg"));

            var timeLbl = new Label
            {
                TextColor = Color.FromArgb("#38BDF8"),
                FontAttributes = FontAttributes.Bold,
                FontSize = compact ? 12 : 13,
                VerticalTextAlignment = TextAlignment.Center
            };
            timeLbl.SetBinding(Label.TextProperty, new Binding(nameof(WarmupTimeRule.FormattedTime), stringFormat: compact ? "{0} rest" : "{0} interval"));

            var actionCell = new Grid
            {
                HorizontalOptions = LayoutOptions.End,
                VerticalOptions = LayoutOptions.Center,
                MinimumHeightRequest = 30
            };

            var baseBadge = new Label
            {
                Text = "BASE",
                TextColor = Color.FromArgb("#64748B"),
                FontAttributes = FontAttributes.Bold,
                FontSize = 10,
                VerticalTextAlignment = TextAlignment.Center,
                HorizontalTextAlignment = TextAlignment.End
            };

            var delBtn = CreateIconActionButton("✕", "#7F1D1D", _vm.DeleteWarmupTimeCommand);
            delBtn.SetBinding(IsVisibleProperty, nameof(WarmupTimeRule.CanDelete));

            rowBorder.BindingContextChanged += (_, _) =>
            {
                if (rowBorder.BindingContext is WarmupTimeRule rule)
                {
                    baseBadge.IsVisible = !rule.CanDelete;
                }
            };

            actionCell.Add(baseBadge);
            actionCell.Add(delBtn);

            row.Add(fromLbl, 0, 0);
            row.Add(timeLbl, 1, 0);
            row.Add(actionCell, 2, 0);

            rowBorder.Content = row;
            return rowBorder;
        }));

        stack.Add(listLayout);
        card.Content = stack;
        return card;
    }

    // =========================================================================
    // LIVE WARMUP RUNNER VIEWS (TABLET SPLIT VIEW vs PHONE COMPACT DECK)
    // =========================================================================
    private View BuildTabletLiveLayout()
    {
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) },
                new ColumnDefinition { Width = new GridLength(1.25, GridUnitType.Star) }
            },
            ColumnSpacing = 12
        };

        var leftScroll = new ScrollView
        {
            Content = BuildLiveControlDeckCard()
        };

        var rightPanel = BuildLiveStepsListCard();

        grid.Add(leftScroll, 0, 0);
        grid.Add(rightPanel, 1, 0);
        return grid;
    }

    private View BuildPhoneLiveLayout()
    {
        var grid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Star }
            },
            RowSpacing = 6
        };

        grid.Add(BuildLiveControlDeckCard(), 0, 0);
        grid.Add(BuildLiveStepsListCard(), 0, 1);
        return grid;
    }

    private View BuildLiveControlDeckCard()
    {
        bool compact = !_isWideLayout;
        var card = CreateCardBorder(padding: compact ? new Thickness(8, 6) : new Thickness(12, 10));
        var stack = new VerticalStackLayout { Spacing = compact ? 6 : 8 };

        // Row 1: Telemetry Clocks (Current Time | Open Time | Time Till Opener)
        var clocksGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Star }
            },
            ColumnSpacing = compact ? 4 : 6
        };

        clocksGrid.Add(BuildTelemetryPill("CURRENT TIME", nameof(MainViewModel.LiveCurrentTime), "#E2E8F0", compact), 0, 0);
        clocksGrid.Add(BuildTelemetryPill("EST. OPEN TIME", nameof(MainViewModel.LiveOpenTime), "#38BDF8", compact), 1, 0);
        clocksGrid.Add(BuildTelemetryPill("TIME TO OPENER", nameof(MainViewModel.LiveTimeToOpen), "#FACC15", compact), 2, 0);
        stack.Add(clocksGrid);

        // Row 2 (Snatch Only): Competition Start Countdown + Force Start Now Button
        if (_vm.IsSnatchSelected)
        {
            var startRow = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Auto }
                },
                ColumnSpacing = compact ? 6 : 8
            };

            startRow.Add(new Label
            {
                Text = "Time till comp start:",
                TextColor = Color.FromArgb("#94A3B8"),
                FontSize = compact ? 11 : 12,
                VerticalTextAlignment = TextAlignment.Center
            }, 0, 0);

            var startValLbl = new Label
            {
                TextColor = Color.FromArgb("#38BDF8"),
                FontAttributes = FontAttributes.Bold,
                FontSize = compact ? 12 : 13,
                VerticalTextAlignment = TextAlignment.Center
            };
            startValLbl.SetBinding(Label.TextProperty, nameof(MainViewModel.LiveTimeTillStart));
            startRow.Add(startValLbl, 1, 0);

            var startNowBtn = new Button
            {
                Text = compact ? "⚡ Start Now" : "⚡ Start Comp Now",
                Command = _vm.ForceStartStageNowCommand,
                BackgroundColor = Color.FromArgb("#0284C7"),
                TextColor = Colors.White,
                FontSize = 11,
                FontAttributes = FontAttributes.Bold,
                CornerRadius = 6,
                Padding = new Thickness(compact ? 8 : 10, 2),
                HeightRequest = compact ? 28 : 30
            };
            startNowBtn.SetBinding(IsVisibleProperty, nameof(MainViewModel.CanForceStartSnatchStage));
            startRow.Add(startNowBtn, 2, 0);

            stack.Add(startRow);

            // Pre-Snatch Presentation & Clock Timer
            var snBreakBorder = new Border
            {
                BackgroundColor = Color.FromArgb("#0F172A"),
                Stroke = Color.FromArgb("#334155"),
                StrokeThickness = 1,
                StrokeShape = new RoundRectangle { CornerRadius = 8 },
                Padding = compact ? new Thickness(8, 3) : new Thickness(8, 6)
            };
            var snBreakStack = new VerticalStackLayout { Spacing = compact ? 4 : 6 };

            var snBreakHeaderRow = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Auto }
                },
                ColumnSpacing = 6
            };
            var snBreakTitleLbl = new Label
            {
                Text = "Presentation & Pre-Snatch Clock (tap bar or time to adjust):",
                TextColor = Color.FromArgb("#94A3B8"),
                FontSize = compact ? 11 : 12,
                VerticalTextAlignment = TextAlignment.Center,
                MaxLines = 1,
                LineBreakMode = LineBreakMode.TailTruncation
            };
            if (compact)
            {
                snBreakTitleLbl.SetBinding(Label.TextProperty, nameof(MainViewModel.PhoneSnatchBreakHeaderText));
                snBreakTitleLbl.GestureRecognizers.Add(new TapGestureRecognizer { Command = _vm.ToggleCjBreakExpandedCommand });
            }
            snBreakHeaderRow.Add(snBreakTitleLbl, 0, 0);

            var snBreakTimeBtn = new Button
            {
                BackgroundColor = Color.FromArgb("#1E293B"),
                BorderColor = Color.FromArgb("#EAB308"),
                BorderWidth = 1,
                TextColor = Color.FromArgb("#FACC15"),
                FontAttributes = FontAttributes.Bold,
                FontSize = compact ? 11 : 12,
                CornerRadius = 6,
                Padding = new Thickness(8, 2),
                HeightRequest = compact ? 28 : 30
            };
            snBreakTimeBtn.SetBinding(Button.TextProperty, new Binding(nameof(MainViewModel.LiveCjBreakRemainingDisplay), stringFormat: "⏱ {0} left"));
            snBreakTimeBtn.Clicked += OnEditLiveBreakRemainingClicked;
            snBreakHeaderRow.Add(snBreakTimeBtn, 1, 0);

            var snBreakProgress = new ScrubbableProgressBar
            {
                HeightRequest = compact ? 22 : 28,
                FillColorHex = "#EAB308",
                ScrubCommand = _vm.ScrubCjBreakProgressCommand
            };
            snBreakProgress.SetBinding(ScrubbableProgressBar.ProgressProperty, nameof(MainViewModel.LiveCjBreakProgress));
            snBreakProgress.SetBinding(ScrubbableProgressBar.OverlayTextProperty, new Binding(nameof(MainViewModel.LiveCjBreakRemainingDisplay), stringFormat: "Clock Remaining: {0}"));

            snBreakStack.Add(snBreakHeaderRow);
            snBreakStack.Add(snBreakProgress);

            if (compact)
            {
                snBreakStack.SetBinding(IsVisibleProperty, nameof(MainViewModel.IsCjBreakExpanded));

                var snBreakCollapsedRow = new Grid
                {
                    ColumnDefinitions =
                    {
                        new ColumnDefinition { Width = GridLength.Star },
                        new ColumnDefinition { Width = GridLength.Auto }
                    },
                    MinimumHeightRequest = 20
                };
                snBreakCollapsedRow.SetBinding(IsVisibleProperty, nameof(MainViewModel.IsCjBreakCollapsed));
                snBreakCollapsedRow.GestureRecognizers.Add(new TapGestureRecognizer { Command = _vm.ToggleCjBreakExpandedCommand });

                snBreakCollapsedRow.Add(new Label
                {
                    Text = "✓ Presentation & Clock: Complete",
                    TextColor = Color.FromArgb("#94A3B8"),
                    FontSize = 11,
                    VerticalTextAlignment = TextAlignment.Center
                }, 0, 0);
                snBreakCollapsedRow.Add(new Label
                {
                    Text = "▾ Expand",
                    TextColor = Color.FromArgb("#38BDF8"),
                    FontAttributes = FontAttributes.Bold,
                    FontSize = 11,
                    VerticalTextAlignment = TextAlignment.Center
                }, 1, 0);

                var snBreakWrapper = new VerticalStackLayout { Spacing = 0 };
                snBreakWrapper.Add(snBreakCollapsedRow);
                snBreakWrapper.Add(snBreakStack);
                snBreakBorder.Content = snBreakWrapper;
            }
            else
            {
                snBreakBorder.Content = snBreakStack;
            }
            stack.Add(snBreakBorder);
        }

        // Row 2b (Clean & Jerk Only): Phase 1 (Snatch Attempts Remaining) & Phase 2 (Break After Snatch)
        if (_vm.IsCjSelected)
        {
            // Phase 1: Snatch Attempts Remaining
            var snatchPhaseBorder = new Border
            {
                BackgroundColor = Color.FromArgb("#0F172A"),
                Stroke = Color.FromArgb("#334155"),
                StrokeThickness = 1,
                StrokeShape = new RoundRectangle { CornerRadius = 8 },
                Padding = compact ? new Thickness(8, 3) : new Thickness(8, 6)
            };
            var snatchPhaseStack = new VerticalStackLayout { Spacing = compact ? 4 : 6 };

            var snatchHeaderRow = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = GridLength.Auto }
                },
                ColumnSpacing = compact ? 4 : 6
            };

            var snatchTitleLbl = new Label
            {
                Text = "1. Snatch Attempts Left:",
                TextColor = Color.FromArgb("#94A3B8"),
                FontSize = compact ? 11 : 12,
                VerticalTextAlignment = TextAlignment.Center,
                MaxLines = 1,
                LineBreakMode = LineBreakMode.TailTruncation
            };
            if (compact)
            {
                snatchTitleLbl.SetBinding(Label.TextProperty, nameof(MainViewModel.PhoneCjSnatchHeaderText));
                snatchTitleLbl.GestureRecognizers.Add(new TapGestureRecognizer { Command = _vm.ToggleCjSnatchExpandedCommand });
            }
            snatchHeaderRow.Add(snatchTitleLbl, 0, 0);

            snatchHeaderRow.Add(CreateStepButton("-", _vm.AdjustLiveCjSnatchLiftsOutCommand, "-1", compact ? 32 : 40, compact ? 30 : 36), 1, 0);

            var snCountLbl = new Label
            {
                TextColor = Color.FromArgb("#F8FAFC"),
                FontAttributes = FontAttributes.Bold,
                FontSize = compact ? 13 : 14,
                WidthRequest = compact ? 24 : 30,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center
            };
            snCountLbl.SetBinding(Label.TextProperty, nameof(MainViewModel.LiveCjSnatchLiftsOut));
            snatchHeaderRow.Add(snCountLbl, 2, 0);

            snatchHeaderRow.Add(CreateStepButton("+", _vm.AdjustLiveCjSnatchLiftsOutCommand, "1", compact ? 32 : 40, compact ? 30 : 36), 3, 0);

            var advSnatchBtn = new Button
            {
                Text = compact ? "Next Snatch" : "Advance: Next Snatch",
                Command = _vm.AdvanceCjSnatchLiftCommand,
                TextColor = Colors.White,
                FontAttributes = FontAttributes.Bold,
                FontSize = 11,
                CornerRadius = 6,
                Padding = new Thickness(compact ? 8 : 10, 2),
                HeightRequest = compact ? 30 : 34
            };
            advSnatchBtn.SetBinding(Button.BackgroundColorProperty, nameof(MainViewModel.NextSnatchButtonColor));
            snatchHeaderRow.Add(advSnatchBtn, 4, 0);

            var snatchProgress = new ScrubbableProgressBar
            {
                HeightRequest = compact ? 22 : 28,
                FillColorHex = "#0284C7",
                ScrubCommand = _vm.ScrubCjSnatchProgressCommand
            };
            snatchProgress.SetBinding(ScrubbableProgressBar.ProgressProperty, nameof(MainViewModel.LiveCjSnatchProgress));
            snatchProgress.SetBinding(ScrubbableProgressBar.OverlayTextProperty, nameof(MainViewModel.LiveCjSnatchStatusText));

            snatchPhaseStack.Add(snatchHeaderRow);
            snatchPhaseStack.Add(snatchProgress);

            if (compact)
            {
                snatchPhaseStack.SetBinding(IsVisibleProperty, nameof(MainViewModel.IsCjSnatchExpanded));

                var snatchCollapsedRow = new Grid
                {
                    ColumnDefinitions =
                    {
                        new ColumnDefinition { Width = GridLength.Star },
                        new ColumnDefinition { Width = GridLength.Auto }
                    },
                    MinimumHeightRequest = 20
                };
                snatchCollapsedRow.SetBinding(IsVisibleProperty, nameof(MainViewModel.IsCjSnatchCollapsed));
                snatchCollapsedRow.GestureRecognizers.Add(new TapGestureRecognizer { Command = _vm.ToggleCjSnatchExpandedCommand });

                snatchCollapsedRow.Add(new Label
                {
                    Text = "✓ 1. Snatch Left: Complete",
                    TextColor = Color.FromArgb("#94A3B8"),
                    FontSize = 11,
                    VerticalTextAlignment = TextAlignment.Center
                }, 0, 0);
                snatchCollapsedRow.Add(new Label
                {
                    Text = "▾ Expand",
                    TextColor = Color.FromArgb("#38BDF8"),
                    FontAttributes = FontAttributes.Bold,
                    FontSize = 11,
                    VerticalTextAlignment = TextAlignment.Center
                }, 1, 0);

                var snatchWrapper = new VerticalStackLayout { Spacing = 0 };
                snatchWrapper.Add(snatchCollapsedRow);
                snatchWrapper.Add(snatchPhaseStack);
                snatchPhaseBorder.Content = snatchWrapper;
            }
            else
            {
                snatchPhaseBorder.Content = snatchPhaseStack;
            }
            stack.Add(snatchPhaseBorder);

            // Phase 2: Break After Snatch
            var breakPhaseBorder = new Border
            {
                BackgroundColor = Color.FromArgb("#0F172A"),
                Stroke = Color.FromArgb("#334155"),
                StrokeThickness = 1,
                StrokeShape = new RoundRectangle { CornerRadius = 8 },
                Padding = compact ? new Thickness(8, 3) : new Thickness(8, 6)
            };
            var breakStack = new VerticalStackLayout { Spacing = compact ? 4 : 6 };

            var breakHeaderRow = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Auto }
                },
                ColumnSpacing = 6
            };
            var breakTitleLbl = new Label
            {
                Text = "2. Break After Snatch (tap bar or time to adjust):",
                TextColor = Color.FromArgb("#94A3B8"),
                FontSize = compact ? 11 : 12,
                VerticalTextAlignment = TextAlignment.Center,
                MaxLines = 1,
                LineBreakMode = LineBreakMode.TailTruncation
            };
            if (compact)
            {
                breakTitleLbl.SetBinding(Label.TextProperty, nameof(MainViewModel.PhoneCjBreakHeaderText));
                breakTitleLbl.GestureRecognizers.Add(new TapGestureRecognizer { Command = _vm.ToggleCjBreakExpandedCommand });
            }
            breakHeaderRow.Add(breakTitleLbl, 0, 0);

            var breakTimeBtn = new Button
            {
                BackgroundColor = Color.FromArgb("#1E293B"),
                BorderColor = Color.FromArgb("#EAB308"),
                BorderWidth = 1,
                TextColor = Color.FromArgb("#FACC15"),
                FontAttributes = FontAttributes.Bold,
                FontSize = compact ? 11 : 12,
                CornerRadius = 6,
                Padding = new Thickness(8, 2),
                HeightRequest = compact ? 28 : 30
            };
            breakTimeBtn.SetBinding(Button.TextProperty, new Binding(nameof(MainViewModel.LiveCjBreakRemainingDisplay), stringFormat: "⏱ {0} left"));
            breakTimeBtn.Clicked += OnEditLiveBreakRemainingClicked;
            breakHeaderRow.Add(breakTimeBtn, 1, 0);

            var breakProgress = new ScrubbableProgressBar
            {
                HeightRequest = compact ? 22 : 28,
                FillColorHex = "#EAB308",
                ScrubCommand = _vm.ScrubCjBreakProgressCommand
            };
            breakProgress.SetBinding(ScrubbableProgressBar.ProgressProperty, nameof(MainViewModel.LiveCjBreakProgress));
            breakProgress.SetBinding(ScrubbableProgressBar.OverlayTextProperty, new Binding(nameof(MainViewModel.LiveCjBreakRemainingDisplay), stringFormat: "Break Remaining: {0}"));

            breakStack.Add(breakHeaderRow);
            breakStack.Add(breakProgress);

            if (compact)
            {
                breakStack.SetBinding(IsVisibleProperty, nameof(MainViewModel.IsCjBreakExpanded));

                var breakCollapsedRow = new Grid
                {
                    ColumnDefinitions =
                    {
                        new ColumnDefinition { Width = GridLength.Star },
                        new ColumnDefinition { Width = GridLength.Auto }
                    },
                    MinimumHeightRequest = 20
                };
                breakCollapsedRow.SetBinding(IsVisibleProperty, nameof(MainViewModel.IsCjBreakCollapsed));
                breakCollapsedRow.GestureRecognizers.Add(new TapGestureRecognizer { Command = _vm.ToggleCjBreakExpandedCommand });

                breakCollapsedRow.Add(new Label
                {
                    Text = "✓ 2. Break: Complete",
                    TextColor = Color.FromArgb("#94A3B8"),
                    FontSize = 11,
                    VerticalTextAlignment = TextAlignment.Center
                }, 0, 0);
                breakCollapsedRow.Add(new Label
                {
                    Text = "▾ Expand",
                    TextColor = Color.FromArgb("#38BDF8"),
                    FontAttributes = FontAttributes.Bold,
                    FontSize = 11,
                    VerticalTextAlignment = TextAlignment.Center
                }, 1, 0);

                var breakWrapper = new VerticalStackLayout { Spacing = 0 };
                breakWrapper.Add(breakCollapsedRow);
                breakWrapper.Add(breakStack);
                breakPhaseBorder.Content = breakWrapper;
            }
            else
            {
                breakPhaseBorder.Content = breakStack;
            }
            stack.Add(breakPhaseBorder);
        }

        // Row 3: Primary Stage Lift Progress Bar + Lifts Out / Lifts Passed + Big ADVANCE: NEXT LIFT Button
        var stageBorder = new Border
        {
            BackgroundColor = Color.FromArgb("#0F172A"),
            Stroke = Color.FromArgb("#475569"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Padding = compact ? new Thickness(8, 6) : new Thickness(10, 8)
        };
        var stageStack = new VerticalStackLayout { Spacing = compact ? 6 : 8 };

        var liftsOutRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Auto }
            },
            ColumnSpacing = compact ? 5 : 6
        };

        liftsOutRow.Add(new Label
        {
            Text = "Lifts Out:",
            TextColor = Color.FromArgb("#E2E8F0"),
            FontAttributes = FontAttributes.Bold,
            FontSize = compact ? 12 : 13,
            VerticalTextAlignment = TextAlignment.Center
        }, 0, 0);

        liftsOutRow.Add(CreateStepButton("-", _vm.AdjustLiveLiftsOutCommand, "-1", compact ? 34 : 40, compact ? 32 : 36), 1, 0);

        var liftsOutValueBtn = new Button
        {
            BackgroundColor = Color.FromArgb("#1E293B"),
            BorderColor = Color.FromArgb("#38BDF8"),
            BorderWidth = 1,
            TextColor = Color.FromArgb("#FACC15"),
            FontAttributes = FontAttributes.Bold,
            FontSize = compact ? 14 : 15,
            CornerRadius = 6,
            WidthRequest = compact ? 46 : 52,
            HeightRequest = compact ? 32 : 36,
            Padding = Thickness.Zero
        };
        liftsOutValueBtn.SetBinding(Button.TextProperty, nameof(MainViewModel.LiveLiftsOut));
        liftsOutValueBtn.Clicked += OnEditLiveLiftsOutClicked;
        liftsOutRow.Add(liftsOutValueBtn, 2, 0);

        liftsOutRow.Add(CreateStepButton("+", _vm.AdjustLiveLiftsOutCommand, "1", compact ? 34 : 40, compact ? 32 : 36), 3, 0);

        var passedLbl = new Label
        {
            TextColor = Color.FromArgb("#94A3B8"),
            FontSize = compact ? 11 : 12,
            HorizontalTextAlignment = TextAlignment.End,
            VerticalTextAlignment = TextAlignment.Center
        };
        passedLbl.SetBinding(Label.TextProperty, new Binding(nameof(MainViewModel.LiveLiftsPassed), stringFormat: "Passed: {0}"));
        liftsOutRow.Add(passedLbl, 5, 0);

        var stageProgressBar = new ScrubbableProgressBar
        {
            HeightRequest = compact ? 28 : 36,
            FillColorHex = "#22C55E",
            ScrubCommand = _vm.ScrubStageProgressCommand
        };
        stageProgressBar.SetBinding(ScrubbableProgressBar.ProgressProperty, nameof(MainViewModel.LiveStageProgress));
        stageProgressBar.SetBinding(ScrubbableProgressBar.OverlayTextProperty, nameof(MainViewModel.LiveStageStatusText));

        var advanceBtn = new Button
        {
            Command = _vm.AdvanceStageLiftCommand,
            TextColor = Colors.White,
            FontAttributes = FontAttributes.Bold,
            FontSize = compact ? 13 : 15,
            CornerRadius = 8,
            Padding = new Thickness(8, 0),
            HeightRequest = compact ? 40 : 48
        };
        advanceBtn.SetBinding(
            Button.TextProperty,
            compact ? nameof(MainViewModel.AdvanceStageLiftButtonTextPhone) : nameof(MainViewModel.AdvanceStageLiftButtonTextTablet));
        advanceBtn.SetBinding(Button.BackgroundColorProperty, nameof(MainViewModel.NextLiftButtonColor));

        stageStack.Add(liftsOutRow);
        stageStack.Add(stageProgressBar);
        stageStack.Add(advanceBtn);
        stageBorder.Content = stageStack;
        stack.Add(stageBorder);

        // Row 4: Timer Pause/Resume, Reset Session, and Auto-Advance (Training Only) Toggle
        var footerRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Auto }
            },
            ColumnSpacing = compact ? 6 : 8
        };

        var pauseResumeBtn = new Button
        {
            Command = _vm.ToggleLiveTimerCommand,
            TextColor = Colors.White,
            FontAttributes = FontAttributes.Bold,
            FontSize = compact ? 11 : 12,
            CornerRadius = 6,
            Padding = new Thickness(compact ? 10 : 12, 0),
            HeightRequest = compact ? 32 : 36
        };
        pauseResumeBtn.SetBinding(Button.TextProperty, nameof(MainViewModel.LiveTimerButtonText));
        pauseResumeBtn.SetBinding(Button.BackgroundColorProperty, nameof(MainViewModel.LiveTimerButtonColor));

        var resetLiveBtn = new Button
        {
            Text = "↺ Reset",
            Command = _vm.ResetLiveSessionCommand,
            BackgroundColor = Color.FromArgb("#334155"),
            TextColor = Color.FromArgb("#E2E8F0"),
            FontSize = compact ? 11 : 12,
            CornerRadius = 6,
            Padding = new Thickness(compact ? 8 : 10, 0),
            HeightRequest = compact ? 32 : 36
        };

        var autoAdvLabel = new Label
        {
            Text = compact ? "Auto-Adv" : "Auto-Advance (Training)",
            TextColor = Color.FromArgb("#94A3B8"),
            FontSize = 11,
            HorizontalTextAlignment = TextAlignment.End,
            VerticalTextAlignment = TextAlignment.Center,
            MaxLines = 1,
            LineBreakMode = LineBreakMode.TailTruncation
        };

        var autoAdvSwitch = new Switch
        {
            OnColor = Color.FromArgb("#22C55E"),
            VerticalOptions = LayoutOptions.Center
        };
        autoAdvSwitch.SetBinding(Switch.IsToggledProperty, nameof(MainViewModel.LiveAutoAdvance), BindingMode.TwoWay);

        footerRow.Add(pauseResumeBtn, 0, 0);
        footerRow.Add(resetLiveBtn, 1, 0);
        footerRow.Add(autoAdvLabel, 2, 0);
        footerRow.Add(autoAdvSwitch, 3, 0);
        stack.Add(footerRow);

        card.Content = stack;
        return card;
    }

    private View BuildLiveStepsListCard()
    {
        bool compact = !_isWideLayout;
        var card = CreateCardBorder(padding: compact ? new Thickness(8, 6) : new Thickness(10, 8));
        var outerGrid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Star }
            },
            RowSpacing = compact ? 4 : 6
        };

        var header = new Label
        {
            Text = compact
                ? $"LIVE {_vm.CurrentLiftTitle} PROGRESSION"
                : $"LIVE {_vm.CurrentLiftTitle} WARMUP PROGRESSION (ACTIVE STEP HIGHLIGHTED IN YELLOW)",
            TextColor = Color.FromArgb("#38BDF8"),
            FontAttributes = FontAttributes.Bold,
            FontSize = compact ? 11 : 12,
            MaxLines = 1,
            LineBreakMode = LineBreakMode.TailTruncation
        };
        outerGrid.Add(header, 0, 0);

        var stepsStack = new VerticalStackLayout
        {
            Spacing = compact ? 5 : 6,
            VerticalOptions = LayoutOptions.Start
        };
        stepsStack.SetBinding(BindableLayout.ItemsSourceProperty, nameof(MainViewModel.LiveSteps));
        _activeBindableLayouts.Add(stepsStack);
        BindableLayout.SetItemTemplate(stepsStack, new DataTemplate(() =>
        {
            var stepBorder = new Border
            {
                StrokeThickness = 1.5,
                StrokeShape = new RoundRectangle { CornerRadius = 8 },
                Padding = compact ? new Thickness(8, 5) : new Thickness(10, 7),
                VerticalOptions = LayoutOptions.Start
            };

            var stepStack = new VerticalStackLayout
            {
                Spacing = compact ? 4 : 6,
                VerticalOptions = LayoutOptions.Start
            };

            var rowGrid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = new GridLength(compact ? 50 : 76) },
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = new GridLength(compact ? 58 : 86) },
                    new ColumnDefinition { Width = new GridLength(compact ? 68 : 98) }
                },
                ColumnSpacing = compact ? 5 : 8
            };

            var weightLbl = new Label
            {
                FontAttributes = FontAttributes.Bold,
                FontSize = compact ? 13 : 15,
                VerticalTextAlignment = TextAlignment.Center,
                MaxLines = 1,
                LineBreakMode = LineBreakMode.TailTruncation
            };
            weightLbl.SetBinding(Label.TextProperty, nameof(LiveStepItemViewModel.WeightDisplay));

            var titleStack = new VerticalStackLayout { Spacing = 1, VerticalOptions = LayoutOptions.Center };
            var actionTitleLbl = new Label
            {
                FontAttributes = FontAttributes.Bold,
                FontSize = compact ? 12 : 14,
                MaxLines = 1,
                LineBreakMode = LineBreakMode.TailTruncation
            };
            actionTitleLbl.SetBinding(Label.TextProperty, nameof(LiveStepItemViewModel.ActionTitle));

            var actionDetailLbl = new Label
            {
                FontSize = compact ? 10 : 11,
                MaxLines = 1,
                LineBreakMode = LineBreakMode.TailTruncation
            };
            actionDetailLbl.SetBinding(Label.TextProperty, nameof(LiveStepItemViewModel.ActionDetail));
            titleStack.Add(actionTitleLbl);
            titleStack.Add(actionDetailLbl);

            var barbellView = new BarbellPlateView
            {
                HeightRequest = compact ? 28 : 36,
                WidthRequest = compact ? 58 : 86,
                IsFullBarbell = false,
                HorizontalOptions = LayoutOptions.Start,
                VerticalOptions = LayoutOptions.Center
            };
            barbellView.SetBinding(IsVisibleProperty, nameof(LiveStepItemViewModel.IsLift));
            barbellView.SetBinding(BarbellPlateView.LiftWeightProperty, nameof(LiveStepItemViewModel.WeightKg));
            barbellView.SetBinding(BarbellPlateView.BarWeightProperty, nameof(LiveStepItemViewModel.BarbellWeight));
            barbellView.SetBinding(BarbellPlateView.IsOpenerProperty, nameof(LiveStepItemViewModel.IsOpener));

            var badgeBorder = new Border
            {
                StrokeShape = new RoundRectangle { CornerRadius = 6 },
                StrokeThickness = 0,
                Padding = compact ? new Thickness(4, 3) : new Thickness(6, 4),
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.Center
            };
            var badgeLbl = new Label
            {
                FontAttributes = FontAttributes.Bold,
                FontSize = compact ? 10 : 12,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center,
                MaxLines = 1,
                LineBreakMode = LineBreakMode.TailTruncation
            };
            badgeLbl.SetBinding(Label.TextProperty, nameof(LiveStepItemViewModel.CountdownBadgeText));
            badgeBorder.Content = badgeLbl;

            var activeLoadingBar = new ScrubbableProgressBar
            {
                HeightRequest = compact ? 20 : 24,
                TrackColorHex = "#0F172A",
                FillColorHex = "#16A34A",
                HorizontalOptions = LayoutOptions.Fill
            };
            activeLoadingBar.SetBinding(IsVisibleProperty, nameof(LiveStepItemViewModel.IsActive));
            activeLoadingBar.SetBinding(ScrubbableProgressBar.ProgressProperty, nameof(LiveStepItemViewModel.ProgressFraction));
            activeLoadingBar.SetBinding(ScrubbableProgressBar.OverlayTextProperty, nameof(LiveStepItemViewModel.ProgressSummaryText));

            void ApplyRowColors(LiveStepItemViewModel? item)
            {
                if (item == null)
                {
                    return;
                }

                stepBorder.BackgroundColor = Color.FromArgb(item.RowBackgroundHex);
                stepBorder.Stroke = Color.FromArgb(item.RowBorderHex);
                weightLbl.TextColor = Color.FromArgb(item.PrimaryTextHex);
                actionTitleLbl.TextColor = Color.FromArgb(item.PrimaryTextHex);
                actionDetailLbl.TextColor = Color.FromArgb(item.SecondaryTextHex);
                badgeBorder.BackgroundColor = Color.FromArgb(item.BadgeBackgroundHex);
                badgeLbl.TextColor = Color.FromArgb(item.BadgeTextHex);
                activeLoadingBar.FillColorHex = item.ProgressColorHex;
            }

            LiveStepItemViewModel? subscribedVmItem = null;
            System.ComponentModel.PropertyChangedEventHandler? vmItemHandler = null;
            vmItemHandler = (_, e) =>
            {
                if (e.PropertyName == nameof(LiveStepItemViewModel.Status))
                {
                    ApplyRowColors(subscribedVmItem);
                }
            };

            stepBorder.BindingContextChanged += (_, _) =>
            {
                if (subscribedVmItem != null)
                {
                    subscribedVmItem.PropertyChanged -= vmItemHandler;
                    subscribedVmItem = null;
                }

                if (stepBorder.BindingContext is LiveStepItemViewModel vmItem)
                {
                    subscribedVmItem = vmItem;
                    ApplyRowColors(vmItem);
                    vmItem.PropertyChanged += vmItemHandler;
                }
            };

            rowGrid.Add(weightLbl, 0, 0);
            rowGrid.Add(titleStack, 1, 0);
            rowGrid.Add(barbellView, 2, 0);
            rowGrid.Add(badgeBorder, 3, 0);

            stepStack.Add(rowGrid);
            stepStack.Add(activeLoadingBar);

            stepBorder.Content = stepStack;
            return stepBorder;
        }));

        var scroll = new ScrollView { Content = stepsStack };
        _liveStepsStackLayout = stepsStack;
        _liveStepsScrollView = scroll;
        outerGrid.Add(scroll, 0, 1);
        card.Content = outerGrid;
        return card;
    }

    // =========================================================================
    // INTERACTIVE DIALOG HANDLERS (PROFILES & DIRECT NUMERIC INPUT)
    // =========================================================================
    private async void OnProfileMenuClicked(object? sender, EventArgs e)
    {
        string action = await DisplayActionSheetAsync(
            $"Profile: {_vm.ActiveProfileName}",
            "Cancel",
            null,
            "📂 Switch / Load Profile...",
            "➕ Add New Profile...",
            "📋 Duplicate Current Profile...",
            "✏️ Rename Current Profile...",
            "💾 Save Profile Settings",
            "🗑️ Delete Current Profile",
            "⚠️ Clear All Settings (Reset to Demo)");

        if (string.IsNullOrEmpty(action) || action == "Cancel")
        {
            return;
        }

        if (action.StartsWith("📂"))
        {
            var names = _vm.ProfileNames.ToArray();
            string selected = await DisplayActionSheetAsync("Select Profile to Load", "Cancel", null, names);
            if (!string.IsNullOrEmpty(selected) && selected != "Cancel")
            {
                _vm.ActiveProfileName = selected;
            }
        }
        else if (action.StartsWith("➕"))
        {
            string? newName = await DisplayPromptAsync("New Profile", "Enter a name for the new profile:", "Create", "Cancel", "Meet Day 89kg");
            if (!string.IsNullOrWhiteSpace(newName))
            {
                var (ok, err) = _vm.CreateNewProfile(newName);
                if (!ok && err != null)
                {
                    await DisplayAlertAsync("Cannot Create Profile", err, "OK");
                }
            }
        }
        else if (action.StartsWith("📋"))
        {
            string? dupName = await DisplayPromptAsync(
                "Duplicate Profile",
                $"Enter a name for the copy of '{_vm.ActiveProfileName}':",
                "Duplicate",
                "Cancel",
                initialValue: $"{_vm.ActiveProfileName} Copy");

            if (!string.IsNullOrWhiteSpace(dupName))
            {
                var (ok, err) = _vm.DuplicateCurrentProfile(dupName);
                if (!ok && err != null)
                {
                    await DisplayAlertAsync("Cannot Duplicate Profile", err, "OK");
                }
            }
        }
        else if (action.StartsWith("✏️"))
        {
            string? renamed = await DisplayPromptAsync(
                "Rename Profile",
                "Enter new profile name:",
                "Rename",
                "Cancel",
                initialValue: _vm.ActiveProfileName);

            if (!string.IsNullOrWhiteSpace(renamed))
            {
                var (ok, err) = _vm.RenameCurrentProfile(renamed);
                if (!ok && err != null)
                {
                    await DisplayAlertAsync("Cannot Rename Profile", err, "OK");
                }
            }
        }
        else if (action.StartsWith("💾"))
        {
            _vm.SaveCurrentProfile();
        }
        else if (action.StartsWith("🗑️"))
        {
            bool confirm = await DisplayAlertAsync(
                "Delete Profile",
                $"Are you sure you want to delete '{_vm.ActiveProfileName}'?",
                "Delete",
                "Cancel");
            if (confirm)
            {
                _vm.DeleteCurrentProfile();
            }
        }
        else if (action.StartsWith("⚠️"))
        {
            bool confirm = await DisplayAlertAsync(
                "Clear All Settings",
                "This will delete all saved profiles and restore the initial 'Demo Profile'. Continue?",
                "Reset All",
                "Cancel");
            if (confirm)
            {
                _vm.ResetAllSettingsToDemo();
            }
        }
    }

    private async void OnEditBarbellWeightClicked(object? sender, EventArgs e)
    {
        string? res = await DisplayPromptAsync(
            "Barbell Weight (kg)",
            "Enter barbell weight (20kg Men's, 15kg Women's, 10kg Youth):",
            "Save",
            "Cancel",
            initialValue: _vm.BarbellWeight.ToString(),
            keyboard: Keyboard.Numeric);

        if (int.TryParse(res, out int val))
        {
            _vm.BarbellWeight = val;
        }
    }

    private async void OnEditOpenerWeightClicked(object? sender, EventArgs e)
    {
        string? res = await DisplayPromptAsync(
            $"{_vm.CurrentLiftTitle} Opener Weight",
            $"Enter opener weight in kg ({_vm.BarbellWeight}–{Defaults.MaxOpenerWeight}):",
            "Save",
            "Cancel",
            initialValue: _vm.OpenerWeight.ToString(),
            keyboard: Keyboard.Numeric);

        if (int.TryParse(res, out int val))
        {
            _vm.OpenerWeight = val;
        }
    }

    private async void OnEditSetupLiftsOutClicked(object? sender, EventArgs e)
    {
        string? res = await DisplayPromptAsync(
            "Lifts Out",
            "Enter number of attempts before your opener (0–99):",
            "Save",
            "Cancel",
            initialValue: _vm.SetupLiftsOut.ToString(),
            keyboard: Keyboard.Numeric);

        if (int.TryParse(res, out int val))
        {
            _vm.SetupLiftsOut = val;
        }
    }

    private async void OnEditStageSecondsClicked(object? sender, EventArgs e)
    {
        string? res = await DisplayPromptAsync(
            "Stage Lift Duration (sec)",
            "Enter seconds per stage attempt (1–200):",
            "Save",
            "Cancel",
            initialValue: _vm.StageSeconds.ToString(),
            keyboard: Keyboard.Numeric);

        if (int.TryParse(res, out int val))
        {
            _vm.StageSeconds = val;
        }
    }

    private async void OnEditExtraSecondsEndClicked(object? sender, EventArgs e)
    {
        string? res = await DisplayPromptAsync(
            "Extra Time After Last Warmup",
            "Enter extra buffer time in seconds (0–999):",
            "Save",
            "Cancel",
            initialValue: _vm.ExtraSecondsEnd.ToString(),
            keyboard: Keyboard.Numeric);

        if (int.TryParse(res, out int val))
        {
            _vm.ExtraSecondsEnd = val;
        }
    }

    private async void OnEditSnatchBreakMinutesClicked(object? sender, EventArgs e)
    {
        string? res = await DisplayPromptAsync(
            "Presentation & Clock (min)",
            "Enter presentation + pre-snatch clock duration in minutes (0–200):",
            "Save",
            "Cancel",
            initialValue: _vm.SnatchBreakMinutes.ToString(),
            keyboard: Keyboard.Numeric);

        if (int.TryParse(res, out int val))
        {
            _vm.SnatchBreakMinutes = val;
        }
    }

    private async void OnEditCjBreakMinutesClicked(object? sender, EventArgs e)
    {
        string? res = await DisplayPromptAsync(
            "Break After Snatch (min)",
            "Enter competition break duration in minutes (1–200):",
            "Save",
            "Cancel",
            initialValue: _vm.CjBreakMinutes.ToString(),
            keyboard: Keyboard.Numeric);

        if (int.TryParse(res, out int val))
        {
            _vm.CjBreakMinutes = val;
        }
    }

    private async void OnEditCjSnatchRemainingClicked(object? sender, EventArgs e)
    {
        string? res = await DisplayPromptAsync(
            "Snatch Attempts Remaining",
            "Enter remaining snatch attempts before the break (0–99):",
            "Save",
            "Cancel",
            initialValue: _vm.CjSnatchAttemptsRemaining.ToString(),
            keyboard: Keyboard.Numeric);

        if (int.TryParse(res, out int val))
        {
            _vm.CjSnatchAttemptsRemaining = val;
        }
    }

    private async void OnEditLiveLiftsOutClicked(object? sender, EventArgs e)
    {
        string? res = await DisplayPromptAsync(
            "Live Lifts Out",
            "Enter updated number of lifts out (0–99):",
            "Update",
            "Cancel",
            initialValue: _vm.LiveLiftsOut.ToString(),
            keyboard: Keyboard.Numeric);

        if (int.TryParse(res, out int val))
        {
            _vm.SetCustomLiveLiftsOut(val);
        }
    }

    private async void OnEditLiveBreakRemainingClicked(object? sender, EventArgs e)
    {
        string title = _vm.IsSnatchSelected ? "Set Presentation/Clock Remaining" : "Set Break Time Remaining";
        string prompt = _vm.IsSnatchSelected
            ? "Enter remaining presentation/clock time in seconds (e.g., 600 for 10m0s):"
            : "Enter remaining break time in seconds (e.g., 420 for 7m0s):";
        string? res = await DisplayPromptAsync(
            title,
            prompt,
            "Update",
            "Cancel",
            keyboard: Keyboard.Numeric);

        if (int.TryParse(res, out int sec))
        {
            _vm.SetCustomLiveBreakRemainingSeconds(sec);
        }
    }

    // =========================================================================
    // UI COMPONENT FACTORY HELPERS
    // =========================================================================
    private static Border CreateCardBorder(Thickness? padding = null)
    {
        return new Border
        {
            BackgroundColor = Color.FromArgb("#1E293B"),
            Stroke = Color.FromArgb("#334155"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            Padding = padding ?? new Thickness(12, 10)
        };
    }

    private static Button CreateSegmentButton(string text, System.Windows.Input.ICommand command, object? parameter = null)
    {
        return new Button
        {
            Text = text,
            Command = command,
            CommandParameter = parameter,
            FontAttributes = FontAttributes.Bold,
            FontSize = 12,
            CornerRadius = 8,
            Padding = new Thickness(8, 4),
            HeightRequest = 38
        };
    }

    private static void StyleSegmentButton(Button btn, bool isActive, string activeBgHex, string activeBorderHex)
    {
        btn.BackgroundColor = Color.FromArgb(isActive ? activeBgHex : "#0F172A");
        btn.BorderColor = Color.FromArgb(isActive ? activeBorderHex : "#334155");
        btn.BorderWidth = isActive ? 1.5 : 1;
        btn.TextColor = Color.FromArgb(isActive ? "#FFFFFF" : "#94A3B8");
    }

    private static View CreateFieldLabel(string title, string subtitle)
    {
        return new VerticalStackLayout
        {
            Spacing = 1,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label
                {
                    Text = title,
                    TextColor = Color.FromArgb("#F8FAFC"),
                    FontAttributes = FontAttributes.Bold,
                    FontSize = 13
                },
                new Label
                {
                    Text = subtitle,
                    TextColor = Color.FromArgb("#64748B"),
                    FontSize = 11
                }
            }
        };
    }

    private static Button CreateStepButton(string label, System.Windows.Input.ICommand command, string deltaParam, double width = 40, double height = 36)
    {
        return new Button
        {
            Text = label,
            Command = command,
            CommandParameter = deltaParam,
            BackgroundColor = Color.FromArgb("#334155"),
            TextColor = Colors.White,
            FontAttributes = FontAttributes.Bold,
            FontSize = 13,
            CornerRadius = 6,
            WidthRequest = width,
            HeightRequest = height,
            Padding = Thickness.Zero
        };
    }

    private View BuildStepperParamRow(
        string title,
        string subtitle,
        string bindingPath,
        string unitSuffix,
        System.Windows.Input.ICommand adjustCommand,
        EventHandler onTapEdit,
        string smallStep = "-1",
        string largeStep = "1")
    {
        bool compact = !_isWideLayout;
        double stepBtnWidth = compact ? 34 : 40;
        double valBtnWidth = compact ? 72 : 82;

        var row = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Auto }
            },
            ColumnSpacing = compact ? 4 : 6
        };

        row.Add(CreateFieldLabel(title, subtitle), 0, 0);
        row.Add(CreateStepButton(smallStep, adjustCommand, smallStep, stepBtnWidth), 1, 0);

        var valBtn = new Button
        {
            BackgroundColor = Color.FromArgb("#0F172A"),
            BorderColor = Color.FromArgb("#475569"),
            BorderWidth = 1,
            TextColor = Color.FromArgb("#38BDF8"),
            FontAttributes = FontAttributes.Bold,
            FontSize = compact ? 12 : 13,
            CornerRadius = 6,
            WidthRequest = valBtnWidth,
            HeightRequest = 36,
            Padding = new Thickness(4, 0)
        };
        valBtn.SetBinding(Button.TextProperty, new Binding(bindingPath, stringFormat: $"{{0}} {unitSuffix}"));
        valBtn.Clicked += onTapEdit;
        row.Add(valBtn, 2, 0);

        row.Add(CreateStepButton($"+{largeStep.TrimStart('+')}", adjustCommand, largeStep, stepBtnWidth), 3, 0);
        return row;
    }

    private static Label CreateTableHeaderLabel(string text)
    {
        return new Label
        {
            Text = text,
            TextColor = Color.FromArgb("#94A3B8"),
            FontAttributes = FontAttributes.Bold,
            FontSize = 11
        };
    }

    private static Button CreateIconActionButton(string symbol, string bgHex, System.Windows.Input.ICommand command, double width = 32)
    {
        var btn = new Button
        {
            Text = symbol,
            Command = command,
            BackgroundColor = Color.FromArgb(bgHex),
            TextColor = Colors.White,
            FontSize = 11,
            CornerRadius = 6,
            WidthRequest = width,
            HeightRequest = 30,
            Padding = Thickness.Zero
        };
        btn.SetBinding(Button.CommandParameterProperty, new Binding("."));
        return btn;
    }

    private static View BuildTelemetryPill(string labelText, string valueBindingPath, string valueColorHex, bool compact = false)
    {
        var border = new Border
        {
            BackgroundColor = Color.FromArgb("#0F172A"),
            Stroke = Color.FromArgb("#334155"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Padding = compact ? new Thickness(4, 4) : new Thickness(8, 6)
        };

        var stack = new VerticalStackLayout
        {
            Spacing = compact ? 1 : 2,
            HorizontalOptions = LayoutOptions.Center
        };

        stack.Add(new Label
        {
            Text = labelText,
            TextColor = Color.FromArgb("#94A3B8"),
            FontAttributes = FontAttributes.Bold,
            FontSize = compact ? 9 : 10,
            HorizontalTextAlignment = TextAlignment.Center,
            MaxLines = 1,
            LineBreakMode = LineBreakMode.TailTruncation
        });

        var valLbl = new Label
        {
            TextColor = Color.FromArgb(valueColorHex),
            FontAttributes = FontAttributes.Bold,
            FontSize = compact ? 12 : 15,
            HorizontalTextAlignment = TextAlignment.Center,
            MaxLines = 1,
            LineBreakMode = LineBreakMode.TailTruncation
        };
        valLbl.SetBinding(Label.TextProperty, valueBindingPath);
        stack.Add(valLbl);

        border.Content = stack;
        return border;
    }
}
