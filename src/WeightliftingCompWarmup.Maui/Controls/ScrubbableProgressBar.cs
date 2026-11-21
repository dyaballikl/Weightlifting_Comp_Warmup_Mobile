using System.Windows.Input;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace WeightliftingCompWarmup.Maui.Controls;

/// <summary>
/// Touch-interactive progress bar that renders a high-visibility progress fill, status text overlay,
/// and supports tap/drag scrubbing (ported from the interactive ProgressBars in <c>form_Main.cs</c>).
/// </summary>
public class ScrubbableProgressBar : GraphicsView
{
    private readonly ScrubbableProgressDrawable _drawable;

    public static readonly BindableProperty ProgressProperty = BindableProperty.Create(
        nameof(Progress),
        typeof(double),
        typeof(ScrubbableProgressBar),
        0.0,
        propertyChanged: OnVisualPropertyChanged);

    public static readonly BindableProperty OverlayTextProperty = BindableProperty.Create(
        nameof(OverlayText),
        typeof(string),
        typeof(ScrubbableProgressBar),
        string.Empty,
        propertyChanged: OnVisualPropertyChanged);

    public static readonly BindableProperty FillColorHexProperty = BindableProperty.Create(
        nameof(FillColorHex),
        typeof(string),
        typeof(ScrubbableProgressBar),
        "#22C55E",
        propertyChanged: OnVisualPropertyChanged);

    public static readonly BindableProperty TrackColorHexProperty = BindableProperty.Create(
        nameof(TrackColorHex),
        typeof(string),
        typeof(ScrubbableProgressBar),
        "#1E293B",
        propertyChanged: OnVisualPropertyChanged);

    public static readonly BindableProperty ScrubCommandProperty = BindableProperty.Create(
        nameof(ScrubCommand),
        typeof(ICommand),
        typeof(ScrubbableProgressBar),
        propertyChanged: OnVisualPropertyChanged);

    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public string OverlayText
    {
        get => (string)GetValue(OverlayTextProperty);
        set => SetValue(OverlayTextProperty, value);
    }

    public string FillColorHex
    {
        get => (string)GetValue(FillColorHexProperty);
        set => SetValue(FillColorHexProperty, value);
    }

    public string TrackColorHex
    {
        get => (string)GetValue(TrackColorHexProperty);
        set => SetValue(TrackColorHexProperty, value);
    }

    public ICommand? ScrubCommand
    {
        get => (ICommand?)GetValue(ScrubCommandProperty);
        set => SetValue(ScrubCommandProperty, value);
    }

    public ScrubbableProgressBar()
    {
        _drawable = new ScrubbableProgressDrawable();
        Drawable = _drawable;
        HeightRequest = 36;

        StartInteraction += OnTouchInteraction;
        DragInteraction += OnTouchInteraction;
        UpdateVisual();
    }

    private void OnTouchInteraction(object? sender, TouchEventArgs e)
    {
        if (Width <= 0 || e.Touches.Length == 0)
        {
            return;
        }

        double touchX = e.Touches[0].X;
        double fraction = Math.Clamp(touchX / Width, 0.0, 1.0);
        if (ScrubCommand?.CanExecute(fraction) == true)
        {
            ScrubCommand.Execute(fraction);
        }
    }

    private static void OnVisualPropertyChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is ScrubbableProgressBar bar)
        {
            bar.UpdateVisual();
        }
    }

    private void UpdateVisual()
    {
        double prog = double.IsNaN(Progress) || double.IsInfinity(Progress) ? 0.0 : Math.Clamp(Progress, 0.0, 1.0);
        _drawable.Progress = prog;
        _drawable.OverlayText = OverlayText ?? string.Empty;
        _drawable.FillColorHex = string.IsNullOrWhiteSpace(FillColorHex) ? "#22C55E" : FillColorHex;
        _drawable.TrackColorHex = string.IsNullOrWhiteSpace(TrackColorHex) ? "#1E293B" : TrackColorHex;
        _drawable.ShowScrubThumb = ScrubCommand != null;

        if (Handler != null)
        {
            try
            {
                Invalidate();
            }
            catch
            {
                // Ignore if native graphics view is detaching
            }
        }
    }
}

public class ScrubbableProgressDrawable : IDrawable
{
    public double Progress { get; set; }
    public string OverlayText { get; set; } = string.Empty;
    public string FillColorHex { get; set; } = "#22C55E";
    public string TrackColorHex { get; set; } = "#1E293B";
    public bool ShowScrubThumb { get; set; }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        if (dirtyRect.Width <= 20f || dirtyRect.Height <= 4f)
        {
            return;
        }

        canvas.SaveState();

        float radius = Math.Min(8f, dirtyRect.Height / 2f);

        // Background track
        canvas.FillColor = Color.FromArgb(string.IsNullOrWhiteSpace(TrackColorHex) ? "#1E293B" : TrackColorHex);
        canvas.StrokeColor = Color.FromArgb("#334155");
        canvas.StrokeSize = 1f;
        canvas.FillRoundedRectangle(dirtyRect.X, dirtyRect.Y, dirtyRect.Width, dirtyRect.Height, radius);
        canvas.DrawRoundedRectangle(dirtyRect.X, dirtyRect.Y, dirtyRect.Width, dirtyRect.Height, radius);

        // Progress fill
        double safeProgress = double.IsNaN(Progress) || double.IsInfinity(Progress) ? 0.0 : Math.Clamp(Progress, 0.0, 1.0);
        float fillWidth = (float)(dirtyRect.Width * safeProgress);
        string effectiveFillHex = string.IsNullOrWhiteSpace(FillColorHex) ? "#22C55E" : FillColorHex;
        if (fillWidth > 1f)
        {
            canvas.FillColor = Color.FromArgb(effectiveFillHex);
            canvas.FillRoundedRectangle(dirtyRect.X, dirtyRect.Y, fillWidth, dirtyRect.Height, radius);
        }

        // Subtle thumb indicator for interactive scrub bars
        if (ShowScrubThumb && dirtyRect.Height >= 18f)
        {
            float thumbW = 4f;
            float thumbH = Math.Max(10f, dirtyRect.Height - 8f);
            float thumbX = Math.Clamp(dirtyRect.X + fillWidth - (thumbW / 2f), dirtyRect.X + 3f, dirtyRect.Right - thumbW - 3f);
            float thumbY = dirtyRect.Y + (dirtyRect.Height - thumbH) / 2f;
            canvas.FillColor = Colors.White.WithAlpha(0.85f);
            canvas.FillRoundedRectangle(thumbX, thumbY, thumbW, thumbH, 2f);
        }

        // Overlay text (draw subtle dark halo on light/yellow fills for crisp legibility across 0%-100%)
        if (!string.IsNullOrEmpty(OverlayText))
        {
            float textWidth = Math.Max(1f, dirtyRect.Width - 16f);
            bool isBrightFill = effectiveFillHex.Equals("#EAB308", StringComparison.OrdinalIgnoreCase)
                             || effectiveFillHex.Equals("#FACC15", StringComparison.OrdinalIgnoreCase);

            canvas.FontSize = 12f;
            if (isBrightFill)
            {
                canvas.FontColor = Color.FromArgb("#0F172A").WithAlpha(0.70f);
                canvas.DrawString(
                    OverlayText,
                    dirtyRect.X + 9f,
                    dirtyRect.Y + 1f,
                    textWidth,
                    dirtyRect.Height,
                    HorizontalAlignment.Center,
                    VerticalAlignment.Center);
            }

            canvas.FontColor = Colors.White;
            canvas.DrawString(
                OverlayText,
                dirtyRect.X + 8f,
                dirtyRect.Y,
                textWidth,
                dirtyRect.Height,
                HorizontalAlignment.Center,
                VerticalAlignment.Center);
        }

        canvas.RestoreState();
    }
}
