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
        typeof(ScrubbableProgressBar));

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
        if (fillWidth > 1f)
        {
            canvas.FillColor = Color.FromArgb(string.IsNullOrWhiteSpace(FillColorHex) ? "#22C55E" : FillColorHex);
            canvas.FillRoundedRectangle(dirtyRect.X, dirtyRect.Y, fillWidth, dirtyRect.Height, radius);
        }

        // Overlay text
        if (!string.IsNullOrEmpty(OverlayText))
        {
            float textWidth = Math.Max(1f, dirtyRect.Width - 16f);
            canvas.FontColor = Colors.White;
            canvas.FontSize = 12f;
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
