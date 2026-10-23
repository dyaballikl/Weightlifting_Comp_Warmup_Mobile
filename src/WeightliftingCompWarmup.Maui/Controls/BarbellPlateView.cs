using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using WeightliftingCompWarmup.Core.Services;

namespace WeightliftingCompWarmup.Maui.Controls;

/// <summary>
/// Custom bindable <see cref="GraphicsView"/> that renders IWF color-coded barbell plates and collars
/// in either Full Two-Sided Barbell mode (Opener preview) or Compact Single-Sleeve mode (Lift rows).
/// </summary>
public class BarbellPlateView : GraphicsView
{
    private readonly BarbellDrawable _drawable;

    public static readonly BindableProperty LiftWeightProperty = BindableProperty.Create(
        nameof(LiftWeight),
        typeof(int),
        typeof(BarbellPlateView),
        20,
        propertyChanged: OnBarbellParamsChanged);

    public static readonly BindableProperty BarWeightProperty = BindableProperty.Create(
        nameof(BarWeight),
        typeof(int),
        typeof(BarbellPlateView),
        20,
        propertyChanged: OnBarbellParamsChanged);

    public static readonly BindableProperty IsOpenerProperty = BindableProperty.Create(
        nameof(IsOpener),
        typeof(bool),
        typeof(BarbellPlateView),
        false,
        propertyChanged: OnBarbellParamsChanged);

    public static readonly BindableProperty IsFullBarbellProperty = BindableProperty.Create(
        nameof(IsFullBarbell),
        typeof(bool),
        typeof(BarbellPlateView),
        false,
        propertyChanged: OnBarbellParamsChanged);

    public int LiftWeight
    {
        get => (int)GetValue(LiftWeightProperty);
        set => SetValue(LiftWeightProperty, value);
    }

    public int BarWeight
    {
        get => (int)GetValue(BarWeightProperty);
        set => SetValue(BarWeightProperty, value);
    }

    public bool IsOpener
    {
        get => (bool)GetValue(IsOpenerProperty);
        set => SetValue(IsOpenerProperty, value);
    }

    public bool IsFullBarbell
    {
        get => (bool)GetValue(IsFullBarbellProperty);
        set => SetValue(IsFullBarbellProperty, value);
    }

    public BarbellPlateView()
    {
        _drawable = new BarbellDrawable();
        Drawable = _drawable;
        UpdateDrawable();
    }

    private static void OnBarbellParamsChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is BarbellPlateView view)
        {
            view.UpdateDrawable();
        }
    }

    private void UpdateDrawable()
    {
        _drawable.Loadout = BarbellLoadCalculator.Calculate(LiftWeight, BarWeight, IsOpener);
        _drawable.IsFullBarbell = IsFullBarbell;

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

/// <summary>
/// Vector <see cref="IDrawable"/> implementation for <see cref="BarbellPlateView"/>.
/// </summary>
public class BarbellDrawable : IDrawable
{
    public BarbellLoadout? Loadout { get; set; }
    public bool IsFullBarbell { get; set; }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        if (dirtyRect.Width <= 4 || dirtyRect.Height <= 4)
        {
            return;
        }

        canvas.SaveState();

        var loadout = Loadout;
        if (loadout == null || loadout.LiftWeightKg <= 0)
        {
            canvas.RestoreState();
            return;
        }

        if (IsFullBarbell)
        {
            DrawFullTwoSidedBarbell(canvas, dirtyRect, loadout);
        }
        else
        {
            DrawCompactSingleSleeve(canvas, dirtyRect, loadout);
        }

        canvas.RestoreState();
    }

    private static void DrawFullTwoSidedBarbell(ICanvas canvas, RectF rect, BarbellLoadout loadout)
    {
        float centerY = rect.Center.Y;
        float centerX = rect.Center.X;
        float maxPlateHeight = Math.Max(24f, rect.Height - 10f);
        float shaftHeight = Math.Max(5f, maxPlateHeight * 0.085f);
        float sleeveHeight = Math.Max(8f, maxPlateHeight * 0.145f);
        float oldFlangeHeight = Math.Max(14f, maxPlateHeight * 0.28f);
        float flangeHeight = sleeveHeight + (oldFlangeHeight - sleeveHeight) * 0.5f;

        // Take up the whole width (with standard 8px margin per side) up until the point
        // where total width matches actual IWF barbell proportions (2200mm bar : 450mm plate = 4.89 : 1)
        float availableWidth = Math.Max(80f, rect.Width - 16f);
        float proportionalBarWidth = maxPlateHeight * (2200f / 450f);
        float totalBarWidth = Math.Min(availableWidth, proportionalBarWidth);

        // True IWF barbell geometry: 1310mm inner shaft (59.5%), two 30mm flanges, two 415mm loadable sleeves (18.9% each)
        float flangeWidth = Math.Clamp(totalBarWidth * (30f / 2200f), 4.5f, 6.5f);
        float sleeveLength = totalBarWidth * (415f / 2200f);
        float centerShaftWidth = Math.Max(40f, totalBarWidth - 2f * (flangeWidth + sleeveLength));

        int totalUnits = loadout.SleeveElements.Sum(e => e.WidthUnits);
        float idealUnitScale = sleeveLength * 0.0115f;
        float maxFitUnitScale = totalUnits > 0
            ? Math.Max(0.35f, (sleeveLength - 4.5f) / totalUnits)
            : idealUnitScale;
        float unitScale = Math.Min(idealUnitScale, maxFitUnitScale);

        // 1. Draw center shaft, flanges, and proportional left/right sleeves
        float leftFlangeX = centerX - (centerShaftWidth / 2f) - flangeWidth;
        float rightFlangeX = centerX + (centerShaftWidth / 2f);
        float barLeft = leftFlangeX - sleeveLength;
        float rightSleeveStart = rightFlangeX + flangeWidth;

        // Uniform medium grey fill and dark slate outline across the entire barbell
        var barbellFill = Color.FromArgb("#94A3B8");
        var barbellStroke = Color.FromArgb("#334155");
        canvas.FillColor = barbellFill;
        canvas.StrokeColor = barbellStroke;
        canvas.StrokeSize = 1f;

        // Left & right sleeves
        canvas.FillRoundedRectangle(barLeft, centerY - sleeveHeight / 2f, sleeveLength, sleeveHeight, 2f);
        canvas.DrawRoundedRectangle(barLeft, centerY - sleeveHeight / 2f, sleeveLength, sleeveHeight, 2f);

        canvas.FillRoundedRectangle(rightSleeveStart, centerY - sleeveHeight / 2f, sleeveLength, sleeveHeight, 2f);
        canvas.DrawRoundedRectangle(rightSleeveStart, centerY - sleeveHeight / 2f, sleeveLength, sleeveHeight, 2f);

        // Center shaft
        canvas.FillRectangle(leftFlangeX + flangeWidth, centerY - shaftHeight / 2f, centerShaftWidth, shaftHeight);
        canvas.DrawRectangle(leftFlangeX + flangeWidth, centerY - shaftHeight / 2f, centerShaftWidth, shaftHeight);

        // Flanges
        canvas.FillRoundedRectangle(leftFlangeX, centerY - flangeHeight / 2f, flangeWidth, flangeHeight, 1.5f);
        canvas.DrawRoundedRectangle(leftFlangeX, centerY - flangeHeight / 2f, flangeWidth, flangeHeight, 1.5f);
        canvas.FillRoundedRectangle(rightFlangeX, centerY - flangeHeight / 2f, flangeWidth, flangeHeight, 1.5f);
        canvas.DrawRoundedRectangle(rightFlangeX, centerY - flangeHeight / 2f, flangeWidth, flangeHeight, 1.5f);

        // 2. Draw right sleeve plates flush against the flange and each other (inner to outer -> left to right)
        float cursorRight = rightSleeveStart;
        foreach (var item in loadout.SleeveElements)
        {
            float minW = item.IsCollar ? 5.5f : 3.2f;
            float w = Math.Max(minW, item.WidthUnits * unitScale);
            float h = Math.Max(sleeveHeight + 4f, (float)(maxPlateHeight * item.HeightRatio));
            DrawPlateElement(canvas, cursorRight, centerY - h / 2f, w, h, item);
            cursorRight += w;
        }

        // 3. Draw left sleeve plates flush against the flange and each other (mirrored: inner to outer -> right to left)
        float cursorLeft = leftFlangeX;
        foreach (var item in loadout.SleeveElements)
        {
            float minW = item.IsCollar ? 5.5f : 3.2f;
            float w = Math.Max(minW, item.WidthUnits * unitScale);
            float h = Math.Max(sleeveHeight + 4f, (float)(maxPlateHeight * item.HeightRatio));
            cursorLeft -= w;
            DrawPlateElement(canvas, cursorLeft, centerY - h / 2f, w, h, item);
        }
    }

    private static void DrawCompactSingleSleeve(ICanvas canvas, RectF rect, BarbellLoadout loadout)
    {
        float centerY = rect.Center.Y;
        float maxPlateHeight = Math.Max(16f, rect.Height - 6f);
        float shaftStubWidth = 8f;
        float shaftHeight = Math.Max(4f, maxPlateHeight * 0.11f);
        float flangeWidth = 3.5f;
        float sleeveHeight = Math.Max(6f, maxPlateHeight * 0.16f);
        float oldFlangeHeight = Math.Max(12f, maxPlateHeight * 0.30f);
        float flangeHeight = sleeveHeight + (oldFlangeHeight - sleeveHeight) * 0.5f;

        float startX = rect.Left + 4f;
        float sleeveStartX = startX + shaftStubWidth + flangeWidth;
        float maxSleeveWidth = Math.Max(18f, rect.Width - (sleeveStartX - startX) - 6f);

        // Real single-sleeve proportion relative to 450mm plate diameter (415mm / 450mm = 0.922)
        float proportionalSleeveWidth = Math.Min(maxSleeveWidth, maxPlateHeight * 1.05f);

        int totalUnits = loadout.SleeveElements.Sum(e => e.WidthUnits);
        float availableForPlates = maxSleeveWidth - 5f;
        float unitScale = totalUnits > 0
            ? Math.Min(0.60f, Math.Max(0.36f, availableForPlates / totalUnits))
            : 0.55f;

        float platesTotalWidth = 0f;
        foreach (var item in loadout.SleeveElements)
        {
            float minW = item.IsCollar ? 5.0f : 3.0f;
            float w = Math.Max(minW, item.WidthUnits * unitScale);
            platesTotalWidth += w;
        }

        float sleeveWidth = Math.Min(maxSleeveWidth, Math.Max(proportionalSleeveWidth, platesTotalWidth + 4.5f));

        // Uniform medium grey fill and dark slate outline across shaft stub, flange, and sleeve
        canvas.FillColor = Color.FromArgb("#94A3B8");
        canvas.StrokeColor = Color.FromArgb("#334155");
        canvas.StrokeSize = 1f;

        // Shaft stub
        canvas.FillRectangle(startX, centerY - shaftHeight / 2f, shaftStubWidth, shaftHeight);
        canvas.DrawRectangle(startX, centerY - shaftHeight / 2f, shaftStubWidth, shaftHeight);

        // Inner flange
        canvas.FillRoundedRectangle(startX + shaftStubWidth, centerY - flangeHeight / 2f, flangeWidth, flangeHeight, 1f);
        canvas.DrawRoundedRectangle(startX + shaftStubWidth, centerY - flangeHeight / 2f, flangeWidth, flangeHeight, 1f);

        // Sleeve bar
        canvas.FillRoundedRectangle(sleeveStartX, centerY - sleeveHeight / 2f, sleeveWidth, sleeveHeight, 2f);
        canvas.DrawRoundedRectangle(sleeveStartX, centerY - sleeveHeight / 2f, sleeveWidth, sleeveHeight, 2f);

        float cursorX = sleeveStartX;
        foreach (var item in loadout.SleeveElements)
        {
            float minW = item.IsCollar ? 5.0f : 3.0f;
            float w = Math.Max(minW, item.WidthUnits * unitScale);
            float h = Math.Max(sleeveHeight + 3f, (float)(maxPlateHeight * item.HeightRatio));
            DrawPlateElement(canvas, cursorX, centerY - h / 2f, w, h, item);
            cursorX += w;
        }
    }

    private static void DrawPlateElement(
        ICanvas canvas,
        float x,
        float y,
        float width,
        float height,
        LoadedSleeveElement item)
    {
        float cornerRadius = item.IsCollar ? 1.5f : Math.Min(3f, width * 0.28f);

        canvas.FillColor = Color.FromArgb(item.HexColor);
        canvas.StrokeColor = Color.FromArgb(item.BorderHexColor);
        canvas.StrokeSize = 1f;

        canvas.FillRoundedRectangle(x, y, width, height, cornerRadius);
        canvas.DrawRoundedRectangle(x, y, width, height, cornerRadius);

        if (item.IsCollar)
        {
            // Draw competition collar adjustment handle sticking out the top like a real comp collar
            float handleWidth = Math.Clamp(width * 0.34f, 2.5f, 4.5f);
            float handleStickOut = Math.Max(4.5f, height * 0.38f);
            float handleInnerOverlap = Math.Max(3.0f, height * 0.32f);
            float handleX = x + (width - handleWidth) / 2f;
            float handleY = y - handleStickOut;
            float handleHeight = handleStickOut + handleInnerOverlap;

            canvas.FillColor = Color.FromArgb(item.HexColor);
            canvas.StrokeColor = Color.FromArgb(item.BorderHexColor);
            canvas.FillRoundedRectangle(handleX, handleY, handleWidth, handleHeight, 1f);
            canvas.DrawRoundedRectangle(handleX, handleY, handleWidth, handleHeight, 1f);
        }
    }
}
