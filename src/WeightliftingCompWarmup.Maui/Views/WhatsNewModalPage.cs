using System;
using System.Collections.Generic;
using Microsoft.Maui;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;
using WeightliftingCompWarmup.Core.Services;
using WeightliftingCompWarmup.Maui.Services;

namespace WeightliftingCompWarmup.Maui.Views;

/// <summary>
/// Modal splash screen displayed on app startup when the app has updated since the version
/// the user last opened, showing a summary of each change since their previous version.
/// </summary>
public class WhatsNewModalPage : ContentPage
{
    private readonly string _currentVersion;
    private readonly string _previousVersion;
    private readonly IReadOnlyList<AppReleaseNote> _notes;

    public WhatsNewModalPage(
        string currentVersion,
        string previousVersion,
        IReadOnlyList<AppReleaseNote> notes)
    {
        _currentVersion = currentVersion;
        _previousVersion = previousVersion;
        _notes = notes;

        Title = $"What's New in {HelpAboutModalPage.AppShortName}";
        NavigationPage.SetHasNavigationBar(this, false);

        BuildLayout();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        AppThemeManager.ThemeChanged += OnThemeChanged;
    }

    protected override void OnDisappearing()
    {
        AppThemeManager.ThemeChanged -= OnThemeChanged;
        base.OnDisappearing();
    }

    private void OnThemeChanged()
    {
        MainThread.BeginInvokeOnMainThread(BuildLayout);
    }

    private void BuildLayout()
    {
        BackgroundColor = AppThemeManager.Color("#0B1120");

        var outerGrid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto }, // Header bar
                new RowDefinition { Height = GridLength.Star }, // Scrollable release notes
                new RowDefinition { Height = GridLength.Auto }  // Bottom confirmation button
            },
            Padding = new Thickness(14, 14, 14, 14),
            RowSpacing = 12,
            MaximumWidthRequest = 720,
            HorizontalOptions = LayoutOptions.Fill
        };

        // 1. Header Banner
        var headerBorder = new Border
        {
            BackgroundColor = AppThemeManager.Color("#1E293B"),
            Stroke = Color.FromArgb("#0284C7"),
            StrokeThickness = 1.5,
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
            Padding = new Thickness(14, 12)
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

        string subtitleText = !string.IsNullOrWhiteSpace(_previousVersion) &&
                              !string.Equals(_previousVersion, _currentVersion, StringComparison.OrdinalIgnoreCase)
            ? $"Updated from v{_previousVersion} to v{_currentVersion} • Summary of changes since your last open"
            : $"Version v{_currentVersion} • Latest updates & improvements";

        var titleStack = new VerticalStackLayout
        {
            Spacing = 3,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label
                {
                    Text = $"🎉 WHAT'S NEW IN {HelpAboutModalPage.AppShortName.ToUpperInvariant()}",
                    FontAttributes = FontAttributes.Bold,
                    FontSize = 17,
                    TextColor = AppThemeManager.Color("#38BDF8")
                },
                new Label
                {
                    Text = subtitleText,
                    FontSize = 12,
                    TextColor = AppThemeManager.Color("#94A3B8"),
                    LineBreakMode = LineBreakMode.WordWrap
                }
            }
        };

        var closeBtn = new Button
        {
            Text = "✕",
            BackgroundColor = AppThemeManager.Color("#334155"),
            TextColor = AppThemeManager.Color("#F8FAFC"),
            FontAttributes = FontAttributes.Bold,
            FontSize = 14,
            CornerRadius = 8,
            Padding = new Thickness(12, 6),
            MinimumHeightRequest = 36,
            MinimumWidthRequest = 40,
            VerticalOptions = LayoutOptions.Start
        };
        closeBtn.Clicked += async (_, _) => await CloseModalAsync();

        headerGrid.Add(titleStack, 0, 0);
        headerGrid.Add(closeBtn, 1, 0);
        headerBorder.Content = headerGrid;

        // 2. Scrollable Release Notes Stack
        var notesStack = new VerticalStackLayout
        {
            Spacing = 12,
            Padding = new Thickness(0, 2, 0, 12)
        };

        for (int i = 0; i < _notes.Count; i++)
        {
            notesStack.Add(BuildReleaseCard(_notes[i], isLatest: i == 0));
        }

        var scrollView = new ScrollView
        {
            Content = notesStack
        };

        // 3. Bottom Action Button
        var gotItBtn = new Button
        {
            Text = "Got It — Let's Lift!",
            BackgroundColor = Color.FromArgb("#16A34A"),
            TextColor = Colors.White,
            FontAttributes = FontAttributes.Bold,
            FontSize = 15,
            CornerRadius = 10,
            Padding = new Thickness(18, 12),
            HorizontalOptions = LayoutOptions.Fill
        };
        gotItBtn.Clicked += async (_, _) => await CloseModalAsync();

        outerGrid.Add(headerBorder, 0, 0);
        outerGrid.Add(scrollView, 0, 1);
        outerGrid.Add(gotItBtn, 0, 2);

        Content = outerGrid;
    }

    private View BuildReleaseCard(AppReleaseNote note, bool isLatest)
    {
        var cardStack = new VerticalStackLayout
        {
            Spacing = 10
        };

        // Version pill + Title header
        var versionBadge = new Border
        {
            BackgroundColor = isLatest ? Color.FromArgb("#0284C7") : AppThemeManager.Color("#334155"),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 6 },
            Padding = new Thickness(8, 3),
            VerticalOptions = LayoutOptions.Center,
            Content = new Label
            {
                Text = isLatest ? $"v{note.Version} • NEW" : $"v{note.Version}",
                FontAttributes = FontAttributes.Bold,
                FontSize = 11,
                TextColor = Colors.White
            }
        };

        var headerRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Star }
            },
            ColumnSpacing = 8
        };
        headerRow.Add(versionBadge, 0, 0);
        headerRow.Add(new Label
        {
            Text = note.Title,
            FontAttributes = FontAttributes.Bold,
            FontSize = 14,
            TextColor = AppThemeManager.Color("#F8FAFC"),
            VerticalOptions = LayoutOptions.Center,
            LineBreakMode = LineBreakMode.WordWrap
        }, 1, 0);

        cardStack.Add(headerRow);

        cardStack.Add(new BoxView
        {
            HeightRequest = 1,
            Color = AppThemeManager.Color("#334155"),
            HorizontalOptions = LayoutOptions.Fill
        });

        var itemsStack = new VerticalStackLayout
        {
            Spacing = 8
        };

        foreach (string change in note.Changes)
        {
            itemsStack.Add(BuildChangeRow(change));
        }

        cardStack.Add(itemsStack);

        return new Border
        {
            BackgroundColor = AppThemeManager.Color("#1E293B"),
            Stroke = isLatest ? Color.FromArgb("#0284C7") : AppThemeManager.Color("#334155"),
            StrokeThickness = isLatest ? 1.5 : 1,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            Padding = new Thickness(14, 12),
            Content = cardStack
        };
    }

    private static View BuildChangeRow(string change)
    {
        var rowGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Star }
            },
            ColumnSpacing = 8
        };

        rowGrid.Add(new Label
        {
            Text = "✦",
            FontAttributes = FontAttributes.Bold,
            FontSize = 12,
            TextColor = AppThemeManager.Color("#FACC15"),
            VerticalOptions = LayoutOptions.Start,
            Margin = new Thickness(0, 1, 0, 0)
        }, 0, 0);

        int colonIdx = change.IndexOf(':');
        if (colonIdx > 0 && colonIdx < 60)
        {
            string prefix = change[..(colonIdx + 1)];
            string body = change[(colonIdx + 1)..].TrimStart();

            var formatted = new FormattedString();
            formatted.Spans.Add(new Span
            {
                Text = prefix + " ",
                FontAttributes = FontAttributes.Bold,
                TextColor = AppThemeManager.Color("#F8FAFC"),
                FontSize = 13
            });
            formatted.Spans.Add(new Span
            {
                Text = body,
                TextColor = AppThemeManager.Color("#CBD5E1"),
                FontSize = 13
            });

            rowGrid.Add(new Label
            {
                FormattedText = formatted,
                LineBreakMode = LineBreakMode.WordWrap
            }, 1, 0);
        }
        else
        {
            rowGrid.Add(new Label
            {
                Text = change,
                FontSize = 13,
                TextColor = AppThemeManager.Color("#CBD5E1"),
                LineBreakMode = LineBreakMode.WordWrap
            }, 1, 0);
        }

        return rowGrid;
    }

    private async System.Threading.Tasks.Task CloseModalAsync()
    {
        if (Navigation.ModalStack.Count > 0)
        {
            await Navigation.PopModalAsync(animated: true);
        }
    }
}
