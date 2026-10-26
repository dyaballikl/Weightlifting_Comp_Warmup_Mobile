using Microsoft.Maui;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;

namespace WeightliftingCompWarmup.Maui.Views;

/// <summary>
/// Modal page providing a tabbed "How to Use (Help)" guide and "About Comp OWL" screen.
/// Automatically shown on first launch and accessible anytime via the '?' header button.
/// </summary>
public class HelpAboutModalPage : ContentPage
{
    private readonly ContentView _tabContentContainer;
    private readonly Button _helpTabBtn;
    private readonly Button _aboutTabBtn;
    private bool _isAboutTabSelected;

    public const string AppShortName = "Comp OWL";
    public const string AppFullName = "Weightlifting Comp Warmup";
    public const string AppVersion = "2.0.11";
    public const string AppBuild = "13";

    public HelpAboutModalPage(bool startOnAboutTab = false)
    {
        Title = $"{AppShortName} — Help & About";
        BackgroundColor = Color.FromArgb("#0B1120");
        NavigationPage.SetHasNavigationBar(this, false);

        _isAboutTabSelected = startOnAboutTab;

        var outerGrid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto }, // Top Title + Close bar
                new RowDefinition { Height = GridLength.Auto }, // Segmented Tab Switcher (Help vs About)
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
            BackgroundColor = Color.FromArgb("#1E293B"),
            Stroke = Color.FromArgb("#334155"),
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
            TextColor = Color.FromArgb("#F8FAFC"),
            FontAttributes = FontAttributes.Bold,
            FontSize = 15
        });
        titleStack.Add(new Label
        {
            Text = "Competition & Training Warmup Timing Assistant",
            TextColor = Color.FromArgb("#38BDF8"),
            FontSize = 11.5
        });

        var closeBtn = new Button
        {
            Text = "✕",
            BackgroundColor = Color.FromArgb("#0F172A"),
            BorderColor = Color.FromArgb("#475569"),
            BorderWidth = 1,
            TextColor = Color.FromArgb("#E2E8F0"),
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

        // 2. Segmented Tab Bar (HOW TO USE vs ABOUT)
        var tabGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Star }
            },
            ColumnSpacing = 8
        };

        _helpTabBtn = new Button
        {
            Text = "📖 HOW TO USE (HELP)",
            FontAttributes = FontAttributes.Bold,
            FontSize = 12,
            CornerRadius = 8,
            HeightRequest = 40
        };
        _helpTabBtn.Clicked += (_, _) => SelectTab(aboutTab: false);

        _aboutTabBtn = new Button
        {
            Text = "🦉 ABOUT COMP OWL",
            FontAttributes = FontAttributes.Bold,
            FontSize = 12,
            CornerRadius = 8,
            HeightRequest = 40
        };
        _aboutTabBtn.Clicked += (_, _) => SelectTab(aboutTab: true);

        tabGrid.Add(_helpTabBtn, 0, 0);
        tabGrid.Add(_aboutTabBtn, 1, 0);

        // 3. Tab Body Container
        _tabContentContainer = new ContentView();

        // 4. Footer "Got It" Button
        var footerBtn = new Button
        {
            Text = "GOT IT — BACK TO WARMUP",
            BackgroundColor = Color.FromArgb("#0284C7"),
            BorderColor = Color.FromArgb("#38BDF8"),
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
        SelectTab(_isAboutTabSelected);
    }

    private void SelectTab(bool aboutTab)
    {
        _isAboutTabSelected = aboutTab;

        StyleTabButton(_helpTabBtn, isActive: !aboutTab);
        StyleTabButton(_aboutTabBtn, isActive: aboutTab);

        _tabContentContainer.Content = aboutTab
            ? BuildAboutTabView()
            : BuildHelpTabView();
    }

    private static void StyleTabButton(Button btn, bool isActive)
    {
        btn.BackgroundColor = Color.FromArgb(isActive ? "#0284C7" : "#0F172A");
        btn.BorderColor = Color.FromArgb(isActive ? "#38BDF8" : "#334155");
        btn.BorderWidth = isActive ? 1.5 : 1;
        btn.TextColor = Color.FromArgb(isActive ? "#FFFFFF" : "#94A3B8");
    }

    private View BuildHelpTabView()
    {
        var stack = new VerticalStackLayout
        {
            Spacing = 10,
            Padding = new Thickness(0, 0, 0, 8)
        };

        // Card 1: Purpose Overview
        stack.Add(CreateInfoCard(
            badgeText: "WHAT IS COMP OWL FOR?",
            badgeColorHex: "#38BDF8",
            title: "Know Exactly When to Lift What in the Warmup Room",
            body: "Comp OWL (Competition Olympic WeightLifting) helps athletes managing their own warmups — or coaches and handlers managing warmups for another lifter — time every warmup attempt so the athlete is ready at the exact moment they are called to the competition platform for their opener."));

        // Card 2: Step 1 — Profiles & Bar Setup
        stack.Add(CreateStepCard(
            stepNumber: "1",
            stepTitle: "Choose a Lifter Profile & Bar Weight",
            bullets: new[]
            {
                "Tap the 👤 Profile button (top-left) to switch lifters, create a new blank profile, duplicate an existing profile, or rename/delete profiles.",
                "Tap the 🏋️ Bar button (top-right) to set the barbell weight (20 kg Men's bar, 15 kg Women's bar, or custom youth/technique bar).",
                "Tap 🔔 Beep to toggle audio alerts when it is time for your next warmup lift."
            }));

        // Card 3: Step 2 — Configure Snatch or Clean & Jerk Setup
        stack.Add(CreateStepCard(
            stepNumber: "2",
            stepTitle: "Set Opener, Lifts Out & Warmup Rules (⚙️ SETUP)",
            bullets: new[]
            {
                "Select SNATCH or CLEAN & JERK in the top bar while in ⚙️ SETUP mode.",
                "Opener Weight: Your planned first competition attempt on the platform. The barbell diagram shows exact IWF plate loading (including 2.5 kg competition collars on openers, and full-size plastic 5 kg white / 2.5 kg red plates below the 10 kg green plate minimum).",
                "Lifts Out & Stage Lift Time: How many platform attempts are scheduled before your opener. Every attempt ahead of you adds 'Stage lift time' (defaults to 55s for Snatch and 62s for Clean & Jerk) to your available warmup time.",
                "Extra Time After Last Warmup: Buffer between your final warmup lift and walking out for your platform opener (defaults to 60s; 75s in the Clean & Jerk Demo Profile).",
                "Rules & Extras: Customize weight jump thresholds (e.g., larger jumps on light weights, smaller jumps near your opener), rest time between warmup sets, and pre-lifting steps (like foam rolling or general mobility). Comp OWL automatically smooths your final 3 warmup jumps."
            }));

        // Card 4: Step 3 — Pre-Stage Timers (Presentation & Break)
        stack.Add(CreateStepCard(
            stepNumber: "3",
            stepTitle: "Presentation Clock & Mid-Meet Break",
            bullets: new[]
            {
                "Snatch — Presentation & Clock (min): Accounts for athlete line-up, walk-out, introduction, and the standard 10-minute clock after presentation (defaults to 14 min total). Set to 0 if stage lifting is already underway.",
                "Clean & Jerk — Snatch Remaining & Break: Tracks the remaining Snatch attempts on stage plus the intermission break (usually 10 min) between Snatch and Clean & Jerk so you can start warming up for C&J during the break or late in the Snatch session.",
                "On phone screens, once 'Snatch Left' and 'Break' are completed in C&J Live, they automatically collapse upward to keep your active C&J warmup schedule front and center (tap a collapsed bar anytime to expand it)."
            }));

        // Card 5: Step 4 — Running a Live Session
        stack.Add(CreateStepCard(
            stepNumber: "4",
            stepTitle: "Run the Warmup in 🔴 LIVE Mode",
            bullets: new[]
            {
                "Tap 🔴 LIVE to start the session clock. Your device screen will stay awake automatically while the live timer is running.",
                "Before platform lifting begins, tap 'ADVANCE TO STAGE LIFTING' as soon as the first lifter is called to the bar.",
                "During platform lifting, tap '⏭️ ADVANCE: NEXT STAGE LIFT' each time a platform attempt finishes, or tap the Lifts Out badge directly to type a new count if the marshaling board changes.",
                "Watch the highlighted row in your Warmup Schedule: 'ACTIVE NOW' shows the bar weight you should be lifting right now, and 'UP NEXT' counts down the exact time until your next warmup set."
            }));

        // Card 6 (Bottom): Important Callout — Manual Stage Tracking vs Training Mode
        var scoreboardCard = new Border
        {
            BackgroundColor = Color.FromArgb("#1E1B4B"),
            Stroke = Color.FromArgb("#F59E0B"),
            StrokeThickness = 1.5,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            Padding = new Thickness(12, 10)
        };
        var scoreboardStack = new VerticalStackLayout { Spacing = 6 };
        scoreboardStack.Add(new Label
        {
            Text = "⚠️ NO AUTOMATIC SCOREBOARD LINK",
            TextColor = Color.FromArgb("#FBBF24"),
            FontAttributes = FontAttributes.Bold,
            FontSize = 12
        });
        scoreboardStack.Add(new Label
        {
            Text = "Comp OWL is NOT linked to the competition venue scoreboard or marshaling system. During a live competition, you must manually advance the stage lifts as attempts happen on stage, or update your 'Lifts Out' count when lifters ahead of you change weights or pass.",
            TextColor = Color.FromArgb("#F8FAFC"),
            FontSize = 12.5,
            LineHeight = 1.2
        });
        scoreboardStack.Add(new BoxView
        {
            HeightRequest = 1,
            Color = Color.FromArgb("#312E81"),
            Margin = new Thickness(0, 2)
        });
        scoreboardStack.Add(new Label
        {
            Text = "⏱️ TRAINING / PRACTICE COMPETITION MODE (AUTO-ADVANCE)",
            TextColor = Color.FromArgb("#4ADE80"),
            FontAttributes = FontAttributes.Bold,
            FontSize = 11.5
        });
        scoreboardStack.Add(new Label
        {
            Text = "In the Live screen, you can check 'Auto-advance when stage lift timer hits 0s'. This automatically ticks off a stage lift each time the stage clock expires — perfect for solo training sessions or running a realistic practice competition 1–2 weeks out from meet day!",
            TextColor = Color.FromArgb("#E2E8F0"),
            FontSize = 12,
            LineHeight = 1.2
        });
        scoreboardCard.Content = scoreboardStack;
        stack.Add(scoreboardCard);

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
            BackgroundColor = Color.FromArgb("#1E293B"),
            Stroke = Color.FromArgb("#38BDF8"),
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
            TextColor = Color.FromArgb("#F8FAFC"),
            FontAttributes = FontAttributes.Bold,
            FontSize = 24,
            HorizontalTextAlignment = TextAlignment.Center
        });

        heroStack.Add(new Label
        {
            Text = "Competition Olympic WeightLifting Warmup",
            TextColor = Color.FromArgb("#38BDF8"),
            FontAttributes = FontAttributes.Bold,
            FontSize = 13,
            HorizontalTextAlignment = TextAlignment.Center
        });

        heroStack.Add(new Label
        {
            Text = $"Version {AppVersion} (Build {AppBuild}) • Android & iOS",
            TextColor = Color.FromArgb("#94A3B8"),
            FontSize = 12,
            HorizontalTextAlignment = TextAlignment.Center
        });

        heroBorder.Content = heroStack;
        stack.Add(heroBorder);

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
            BackgroundColor = Color.FromArgb("#1E293B"),
            Stroke = Color.FromArgb("#334155"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            Padding = new Thickness(14, 12)
        };

        var licenseStack = new VerticalStackLayout { Spacing = 8 };
        licenseStack.Add(new Label
        {
            Text = "OPEN SOURCE & LICENSE",
            TextColor = Color.FromArgb("#38BDF8"),
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
                        TextColor = Color.FromArgb("#F8FAFC"),
                        FontAttributes = FontAttributes.Bold,
                        FontSize = 13
                    },
                    new Span
                    {
                        Text = "@lifting_while_diabetic",
                        TextColor = Color.FromArgb("#38BDF8"),
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
            TextColor = Color.FromArgb("#CBD5E1"),
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
            BackgroundColor = Color.FromArgb("#0F172A"),
            BorderColor = Color.FromArgb("#475569"),
            BorderWidth = 1,
            TextColor = Color.FromArgb("#38BDF8"),
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
            BackgroundColor = Color.FromArgb("#0F172A"),
            BorderColor = Color.FromArgb("#475569"),
            BorderWidth = 1,
            TextColor = Color.FromArgb("#38BDF8"),
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

        // Optional Support / Ko-fi Card
        var supportBorder = new Border
        {
            BackgroundColor = Color.FromArgb("#1E293B"),
            Stroke = Color.FromArgb("#F59E0B"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            Padding = new Thickness(14, 12)
        };

        var supportStack = new VerticalStackLayout { Spacing = 6 };
        supportStack.Add(new Label
        {
            Text = "SUPPORT COMP OWL (OPTIONAL)",
            TextColor = Color.FromArgb("#FBBF24"),
            FontAttributes = FontAttributes.Bold,
            FontSize = 11
        });
        supportStack.Add(new Label
        {
            Text = "Help Cover Annual License Maintenance",
            TextColor = Color.FromArgb("#F8FAFC"),
            FontAttributes = FontAttributes.Bold,
            FontSize = 14
        });
        supportStack.Add(new Label
        {
            Text = "Comp OWL is free, open-source, and built for the weightlifting community — I'm not looking to profit from this app. However, maintaining developer account licenses costs me around $150/year. If Comp OWL helps you or your lifters on competition day, an optional contribution on Ko-fi to help cover annual license maintenance is hugely appreciated!",
            TextColor = Color.FromArgb("#CBD5E1"),
            FontSize = 12.5,
            LineHeight = 1.2
        });

        var kofiBtn = new Button
        {
            Text = "☕ Support Comp OWL on Ko-fi",
            BackgroundColor = Color.FromArgb("#0284C7"),
            BorderColor = Color.FromArgb("#38BDF8"),
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

        return new ScrollView { Content = stack };
    }

    private static View CreateInfoCard(string badgeText, string badgeColorHex, string title, string body)
    {
        var border = new Border
        {
            BackgroundColor = Color.FromArgb("#1E293B"),
            Stroke = Color.FromArgb("#334155"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            Padding = new Thickness(14, 12)
        };

        var stack = new VerticalStackLayout { Spacing = 4 };
        stack.Add(new Label
        {
            Text = badgeText,
            TextColor = Color.FromArgb(badgeColorHex),
            FontAttributes = FontAttributes.Bold,
            FontSize = 11
        });
        stack.Add(new Label
        {
            Text = title,
            TextColor = Color.FromArgb("#F8FAFC"),
            FontAttributes = FontAttributes.Bold,
            FontSize = 14
        });
        stack.Add(new Label
        {
            Text = body,
            TextColor = Color.FromArgb("#CBD5E1"),
            FontSize = 12.5,
            LineHeight = 1.2
        });

        border.Content = stack;
        return border;
    }

    private static View CreateStepCard(string stepNumber, string stepTitle, string[] bullets)
    {
        var border = new Border
        {
            BackgroundColor = Color.FromArgb("#1E293B"),
            Stroke = Color.FromArgb("#334155"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            Padding = new Thickness(14, 12)
        };

        var stack = new VerticalStackLayout { Spacing = 6 };

        var headerRow = new Grid
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
            Stroke = Color.FromArgb("#38BDF8"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 6 },
            WidthRequest = 24,
            HeightRequest = 24,
            VerticalOptions = LayoutOptions.Center,
            Content = new Label
            {
                Text = stepNumber,
                TextColor = Colors.White,
                FontAttributes = FontAttributes.Bold,
                FontSize = 12,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center
            }
        };

        var titleLbl = new Label
        {
            Text = stepTitle,
            TextColor = Color.FromArgb("#F8FAFC"),
            FontAttributes = FontAttributes.Bold,
            FontSize = 13.5,
            VerticalTextAlignment = TextAlignment.Center
        };

        headerRow.Add(badge, 0, 0);
        headerRow.Add(titleLbl, 1, 0);
        stack.Add(headerRow);

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
                TextColor = Color.FromArgb("#38BDF8"),
                FontAttributes = FontAttributes.Bold,
                FontSize = 13,
                VerticalTextAlignment = TextAlignment.Start
            }, 0, 0);

            bulletGrid.Add(new Label
            {
                Text = bullet,
                TextColor = Color.FromArgb("#CBD5E1"),
                FontSize = 12,
                LineHeight = 1.2
            }, 1, 0);

            stack.Add(bulletGrid);
        }

        border.Content = stack;
        return border;
    }

    private async void OnCloseClicked(object? sender, EventArgs e)
    {
        if (Navigation.ModalStack.Count > 0)
        {
            await Navigation.PopModalAsync(animated: true);
        }
    }
}
