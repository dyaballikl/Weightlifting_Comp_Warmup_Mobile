namespace WeightliftingCompWarmup.Core.Services;

/// <summary>
/// IWF standard plate/element color classification.
/// </summary>
public enum PlateColorType
{
    Red,
    Blue,
    Yellow,
    Green,
    White,
    CollarSilver
}

/// <summary>
/// Represents a single plate or competition collar loaded on one side of the barbell sleeve.
/// </summary>
public record LoadedSleeveElement(
    decimal WeightKg,
    bool IsCollar,
    PlateColorType ColorType,
    string HexColor,
    string BorderHexColor,
    string TextHexColor,
    int WidthUnits,
    double HeightRatio,
    string Label);

/// <summary>
/// Represents the complete calculated plate loading for a barbell lift.
/// </summary>
public record BarbellLoadout(
    int LiftWeightKg,
    int BarWeightKg,
    bool IsOpener,
    bool HasCompetitionCollars,
    IReadOnlyList<LoadedSleeveElement> SleeveElements,
    string SummaryText);

/// <summary>
/// Calculates the exact IWF barbell plate and competition collar arrangement,
/// ported from <c>Utilities.WeightBox</c> in the original WinForms application.
/// </summary>
public static class BarbellLoadCalculator
{
    private static readonly decimal[] StandardPlates =
    [
        25.0m, 20.0m, 15.0m, 10.0m, 5.0m, 2.5m, 2.0m, 1.5m, 1.0m, 0.5m
    ];

    /// <summary>
    /// Computes the plate and collar sequence for one sleeve (and full barbell metadata) for a given lift weight.
    /// </summary>
    public static BarbellLoadout Calculate(int liftWeightKg, int barWeightKg, bool isOpener)
    {
        if (liftWeightKg <= 0 || barWeightKg <= 0 || liftWeightKg < barWeightKg)
        {
            return new BarbellLoadout(
                Math.Max(0, liftWeightKg),
                Math.Max(0, barWeightKg),
                isOpener,
                false,
                Array.Empty<LoadedSleeveElement>(),
                liftWeightKg > 0 ? $"{liftWeightKg} kg (Empty Bar)" : "No Bar");
        }

        // Matches original WinForms rule: bool is5KgCollar = isOpener ? (_intWeight_Lift - 15) >= _intWeight_Bar : false;
        bool useCompCollars = isOpener && (liftWeightKg - 15) >= barWeightKg;
        decimal remainingPerSide = (liftWeightKg - barWeightKg - (useCompCollars ? 5 : 0)) / 2m;

        var rawPlates = new List<decimal>();
        foreach (decimal plateWeight in StandardPlates)
        {
            while (remainingPerSide >= plateWeight)
            {
                rawPlates.Add(plateWeight);
                remainingPerSide -= plateWeight;
            }
        }

        decimal maxPlate = rawPlates.Count > 0 ? rawPlates.Max() : 0m;
        var elements = new List<LoadedSleeveElement>();

        // 1. Plates >= 2.5 kg go inside the collar
        foreach (decimal plate in rawPlates.Where(p => p >= 2.5m))
        {
            elements.Add(CreatePlateElement(plate, maxPlate));
        }

        // 2. Competition 2.5 kg collar (5 kg pair) goes outside >= 2.5 kg plates and inside < 2.5 kg change plates
        if (useCompCollars)
        {
            elements.Add(new LoadedSleeveElement(
                WeightKg: 2.5m,
                IsCollar: true,
                ColorType: PlateColorType.CollarSilver,
                HexColor: "#B8C0CC",
                BorderHexColor: "#334155",
                TextHexColor: "#0F172A",
                WidthUnits: 14,
                HeightRatio: 0.30,
                Label: "C"));
        }

        // 3. Fractional change plates (< 2.5 kg) go outside the collar
        foreach (decimal plate in rawPlates.Where(p => p < 2.5m))
        {
            elements.Add(CreatePlateElement(plate, maxPlate));
        }

        string summary = BuildSummaryText(barWeightKg, useCompCollars, rawPlates);

        return new BarbellLoadout(
            LiftWeightKg: liftWeightKg,
            BarWeightKg: barWeightKg,
            IsOpener: isOpener,
            HasCompetitionCollars: useCompCollars,
            SleeveElements: elements,
            SummaryText: summary);
    }

    private static LoadedSleeveElement CreatePlateElement(decimal plateWeight, decimal maxPlateOnBar)
    {
        // Special case from WinForms WeightBox: if 5.0kg is the heaviest plate on the bar, draw full-height white training bumper
        if (plateWeight == 5.0m && maxPlateOnBar == 5.0m)
        {
            return new LoadedSleeveElement(
                WeightKg: 5.0m,
                IsCollar: false,
                ColorType: PlateColorType.White,
                HexColor: "#F8FAFC",
                BorderHexColor: "#334155",
                TextHexColor: "#0F172A",
                WidthUnits: 13,
                HeightRatio: 1.00,
                Label: "5");
        }

        // Special case from WinForms WeightBox: if 2.5kg is the heaviest plate on the bar, draw full-height white technique bumper
        if (plateWeight == 2.5m && maxPlateOnBar == 2.5m)
        {
            return new LoadedSleeveElement(
                WeightKg: 2.5m,
                IsCollar: false,
                ColorType: PlateColorType.White,
                HexColor: "#F8FAFC",
                BorderHexColor: "#334155",
                TextHexColor: "#0F172A",
                WidthUnits: 11,
                HeightRatio: 1.00,
                Label: "2.5");
        }

        return plateWeight switch
        {
            25.0m => new LoadedSleeveElement(25.0m, false, PlateColorType.Red, "#DC2626", "#7F1D1D", "#FFFFFF", 13, 1.00, "25"),
            20.0m => new LoadedSleeveElement(20.0m, false, PlateColorType.Blue, "#2563EB", "#1E3A8A", "#FFFFFF", 11, 1.00, "20"),
            15.0m => new LoadedSleeveElement(15.0m, false, PlateColorType.Yellow, "#EAB308", "#713F12", "#0F172A", 9, 1.00, "15"),
            10.0m => new LoadedSleeveElement(10.0m, false, PlateColorType.Green, "#16A34A", "#14532D", "#FFFFFF", 8, 1.00, "10"),
            5.0m => new LoadedSleeveElement(5.0m, false, PlateColorType.White, "#F8FAFC", "#334155", "#0F172A", 6, 0.56, "5"),
            2.5m => new LoadedSleeveElement(2.5m, false, PlateColorType.Red, "#DC2626", "#7F1D1D", "#FFFFFF", 4, 0.42, "2.5"),
            2.0m => new LoadedSleeveElement(2.0m, false, PlateColorType.Blue, "#2563EB", "#1E3A8A", "#FFFFFF", 4, 0.38, "2"),
            1.5m => new LoadedSleeveElement(1.5m, false, PlateColorType.Yellow, "#EAB308", "#713F12", "#0F172A", 4, 0.34, "1.5"),
            1.0m => new LoadedSleeveElement(1.0m, false, PlateColorType.Green, "#16A34A", "#14532D", "#FFFFFF", 4, 0.30, "1"),
            0.5m => new LoadedSleeveElement(0.5m, false, PlateColorType.White, "#F8FAFC", "#334155", "#0F172A", 4, 0.26, "0.5"),
            _ => new LoadedSleeveElement(plateWeight, false, PlateColorType.White, "#E2E8F0", "#475569", "#0F172A", 4, 0.30, plateWeight.ToString("0.#"))
        };
    }

    private static string BuildSummaryText(int barWeightKg, bool useCompCollars, List<decimal> rawPlates)
    {
        if (rawPlates.Count == 0 && !useCompCollars)
        {
            return $"Bar ({barWeightKg}kg)";
        }

        var grouped = rawPlates
            .GroupBy(p => p)
            .OrderByDescending(g => g.Key)
            .Select(g => g.Count() > 1 ? $"{g.Count()}×{g.Key:0.#}" : $"{g.Key:0.#}")
            .ToList();

        if (useCompCollars)
        {
            grouped.Add("Collar (2.5)");
        }

        return $"Per side: {string.Join(" + ", grouped)}";
    }
}
