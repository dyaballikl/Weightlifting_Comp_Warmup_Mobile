using System.Globalization;
using System.Text.Json.Nodes;

namespace WeightliftingCompWarmup.Core.Services;

/// <summary>
/// Pure-C# JSON parser and perspective-alignment anomaly detector for Gemini Vision
/// competition scoreboard responses. Lives in Core so it is fully covered by unit tests.
/// </summary>
public static class ScoreboardJsonParser
{
    /// <summary>
    /// Detects if an angled scoreboard photo caused the vision model to skip or shift a weight row
    /// relative to the athlete names (e.g. when <c>snatchBestKg</c> does not match any extracted Snatch attempt
    /// on that row, or when the final athlete row at the bottom has empty Snatch cells after a block of populated rows).
    /// </summary>
    public static bool HasRowAlignmentAnomaly(string rawJson, IReadOnlyList<ScoreboardLifterRow> lifters)
    {
        if (lifters == null || lifters.Count < 4)
        {
            return false;
        }

        // Check 1: Did 4+ rows have Snatch weights, yet the very last athlete row at the bottom has 0 Snatch weights
        // while the second-to-last row has all 3 Snatch attempts populated? (Classic sign of 1 weight row skipped in the middle)
        int rowsWithSnatch = lifters.Count(r => r.Snatch1.WeightKg.HasValue || r.Snatch2.WeightKg.HasValue || r.Snatch3.WeightKg.HasValue);
        var lastRow = lifters[^1];
        var prevRow = lifters[^2];
        bool lastRowSnatchEmpty = !lastRow.Snatch1.WeightKg.HasValue && !lastRow.Snatch2.WeightKg.HasValue && !lastRow.Snatch3.WeightKg.HasValue;
        bool prevRowSnatchFull = prevRow.Snatch1.WeightKg.HasValue && prevRow.Snatch2.WeightKg.HasValue && prevRow.Snatch3.WeightKg.HasValue;
        if (rowsWithSnatch >= 4 && lastRowSnatchEmpty && prevRowSnatchFull)
        {
            return true;
        }

        // Check 2: Compare each row's reported snatchBestKg / cjBestKg against its extracted attempt weights
        try
        {
            var root = JsonNode.Parse(rawJson);
            JsonArray? liftersArray = root as JsonArray ?? root?["lifters"]?.AsArray();
            if (liftersArray == null)
            {
                return false;
            }

            foreach (var item in liftersArray)
            {
                if (item is not JsonObject obj)
                {
                    continue;
                }

                int? snBest = ParsePositiveInt(obj["snatchBestKg"]);
                if (snBest is > 0 && obj["snatch"] is JsonArray snArr)
                {
                    var weights = snArr
                        .OfType<JsonObject>()
                        .Select(c => ParsePositiveInt(c["weightKg"]))
                        .Where(w => w is > 0)
                        .Select(w => w!.Value)
                        .ToList();

                    if (weights.Count > 0 && !weights.Contains(snBest.Value))
                    {
                        return true;
                    }
                }

                int? cjBest = ParsePositiveInt(obj["cjBestKg"]);
                if (cjBest is > 0 && obj["cleanAndJerk"] is JsonArray cjArr)
                {
                    var weights = cjArr
                        .OfType<JsonObject>()
                        .Select(c => ParsePositiveInt(c["weightKg"]))
                        .Where(w => w is > 0)
                        .Select(w => w!.Value)
                        .ToList();

                    if (weights.Count > 0 && !weights.Contains(cjBest.Value))
                    {
                        return true;
                    }
                }
            }
        }
        catch
        {
            // Ignore JSON inspection errors
        }

        return false;
    }

    public static List<ScoreboardLifterRow> ParseLiftersJson(string json)
    {
        var result = new List<ScoreboardLifterRow>();

        try
        {
            var root = JsonNode.Parse(json);
            JsonArray? liftersArray = root as JsonArray ?? root?["lifters"]?.AsArray();
            if (liftersArray == null)
            {
                return result;
            }

            int rowIdx = 1;
            foreach (var item in liftersArray)
            {
                if (item is not JsonObject obj)
                {
                    continue;
                }

                int orderNum = ParsePositiveInt(obj["orderNumber"]) ?? rowIdx;
                string? leftColRaw = obj["leftColumnText"]?.ToString()?.Trim();
                int? leftColInt = ParsePositiveInt(obj["leftColumnText"]);
                int? rawLot = ParsePositiveInt(obj["lotNumber"]);
                int? snPlace = ParsePositiveInt(obj["snatchPlace"]);
                int? cjPlace = ParsePositiveInt(obj["cjPlace"]);

                // Guard against the model accidentally copying SPL / CJPL (Placement) into lotNumber
                int lotNum;
                if (rawLot.HasValue && (rawLot == snPlace || rawLot == cjPlace) && leftColInt.HasValue && leftColInt != rawLot)
                {
                    lotNum = leftColInt.Value;
                }
                else
                {
                    lotNum = rawLot ?? leftColInt ?? orderNum;
                }

                string name = obj["name"]?.GetValue<string>()?.Trim() ?? $"Lifter {orderNum}";
                if (string.IsNullOrWhiteSpace(name))
                {
                    name = $"Lifter {orderNum}";
                }

                int? snBestKg = ParsePositiveInt(obj["snatchBestKg"]);
                int? cjBestKg = ParsePositiveInt(obj["cjBestKg"]);

                var snCells = ParseDisciplineAttempts(obj["snatch"]?.AsArray(), snBestKg);
                var cjCells = ParseDisciplineAttempts(obj["cleanAndJerk"]?.AsArray(), cjBestKg);

                result.Add(new ScoreboardLifterRow
                {
                    OrderNumber = orderNum,
                    LotNumber = lotNum,
                    LeftColumnText = leftColRaw,
                    Name = name,
                    Snatch1 = snCells[0],
                    Snatch2 = snCells[1],
                    Snatch3 = snCells[2],
                    Cj1 = cjCells[0],
                    Cj2 = cjCells[1],
                    Cj3 = cjCells[2]
                });

                rowIdx++;
            }

            ScoreboardCalculator.AutoFlagPriorSessionRows(result);
        }
        catch
        {
            // Return whatever was parsed
        }

        return result;
    }

    public static ScoreboardAttemptCell[] ParseDisciplineAttempts(JsonArray? arr, int? bestKg = null)
    {
        var cells = new ScoreboardAttemptCell[3]
        {
            new(null, ScoreboardAttemptStatus.Empty),
            new(null, ScoreboardAttemptStatus.Empty),
            new(null, ScoreboardAttemptStatus.Empty)
        };

        if (arr != null)
        {
            for (int i = 0; i < arr.Count; i++)
            {
                if (arr[i] is not JsonObject cellObj)
                {
                    continue;
                }

                // Honor explicit 1-based "attempt" property when present so sparse arrays map to the right slot
                int? explicitAttempt = ParsePositiveInt(cellObj["attempt"]);
                int slot = explicitAttempt is >= 1 and <= 3 ? explicitAttempt.Value - 1 : i;
                if (slot < 0 || slot >= 3)
                {
                    continue;
                }

                var (weightKg, impliedMiss) = ParseWeightKg(cellObj["weightKg"]);
                var status = ParseStatus(cellObj["status"]?.GetValue<string>(), weightKg, impliedMiss);

                if (status == ScoreboardAttemptStatus.Empty || status == ScoreboardAttemptStatus.Pass)
                {
                    weightKg = null;
                }

                cells[slot] = new ScoreboardAttemptCell(weightKg, status);
            }
        }

        // 0. Un-taken Attempt 1 / Attempt 2 rule:
        // In weightlifting, completing Attempt 1 immediately populates Attempt 2 (with the next weight or Pass),
        // and completing Attempt 2 immediately populates Attempt 3 (with the next weight or Pass).
        // Therefore, if Attempt 1 has a weight and Attempt 2 & 3 are both Empty, Attempt 1 has NOT been completed yet (Pending).
        // And if Attempt 2 has a weight and Attempt 3 is Empty, Attempt 2 has NOT been completed yet (Pending).
        bool att2Empty = !cells[1].WeightKg.HasValue && cells[1].Status == ScoreboardAttemptStatus.Empty;
        bool att3Empty = !cells[2].WeightKg.HasValue && cells[2].Status == ScoreboardAttemptStatus.Empty;

        if (cells[0].WeightKg.HasValue && att2Empty && att3Empty)
        {
            cells[0] = cells[0] with { Status = ScoreboardAttemptStatus.Pending };
        }

        if (cells[1].WeightKg.HasValue && att3Empty)
        {
            cells[1] = cells[1] with { Status = ScoreboardAttemptStatus.Pending };
        }

        // 1. Sequential completion rule: if a later attempt is populated or completed, earlier attempts with weights are completed.
        for (int i = 0; i < 2; i++)
        {
            bool laterPopulated = false;
            for (int j = i + 1; j < 3; j++)
            {
                if (cells[j].WeightKg.HasValue ||
                    cells[j].Status is ScoreboardAttemptStatus.Good or ScoreboardAttemptStatus.Miss or ScoreboardAttemptStatus.Pass)
                {
                    laterPopulated = true;
                    break;
                }
            }

            if (laterPopulated && cells[i].Status == ScoreboardAttemptStatus.Pending && cells[i].WeightKg.HasValue)
            {
                bool repeatedNext = cells[i + 1].WeightKg.HasValue && cells[i + 1].WeightKg <= cells[i].WeightKg;
                cells[i] = cells[i] with
                {
                    Status = repeatedNext ? ScoreboardAttemptStatus.Miss : ScoreboardAttemptStatus.Good
                };
            }
        }

        // 2. Repeated-weight rule: in Olympic weightlifting, the bar MUST increase by at least 1 kg after a Good lift.
        // If Attempt[i+1] has the exact same weight (or lower) as Attempt[i], Attempt[i] MUST be a Miss (even if glare hid the red fill).
        for (int i = 0; i < 2; i++)
        {
            if (cells[i].WeightKg.HasValue &&
                cells[i + 1].WeightKg.HasValue &&
                cells[i + 1].WeightKg!.Value <= cells[i].WeightKg!.Value)
            {
                cells[i] = cells[i] with { Status = ScoreboardAttemptStatus.Miss };
            }
        }

        // If Attempt 1 and Attempt 2 had the same weight (so Attempt 1 was a Miss) and Attempt 3 increased in weight (e.g. 90, 90, 92),
        // then Attempt 2 was a Good lift!
        if (cells[0].WeightKg is int w0 &&
            cells[1].WeightKg is int w1 &&
            cells[2].WeightKg is int w2 &&
            w0 == w1 &&
            w2 > w1)
        {
            cells[1] = cells[1] with { Status = ScoreboardAttemptStatus.Good };
        }

        // 3. BEST-column cross-check rule:
        // If the row's BEST column has a positive weight:
        // - The LAST completed (or matching) attempt on that row whose weight equals bestKg is a Good lift
        //   (e.g. 90, 90, 92 with BEST=90 -> Att 2 is Good; 96, 99, 99 with BEST=99 -> Att 3 is Good).
        // - Any earlier attempt at bestKg before that last matching attempt is a Miss.
        // - Any completed attempt strictly smaller than bestKg that was followed by a weight increase is a Good lift.
        // - Any COMPLETED attempt strictly heavier than bestKg MUST be a Miss.
        //   IMPORTANT: Do NOT overwrite an upcoming Pending Attempt 3 to Miss just because all 3 cells have weights!
        if (bestKg is > 0)
        {
            int lastMatchingBestIdx = -1;
            for (int i = 0; i < 3; i++)
            {
                if (cells[i].WeightKg == bestKg.Value)
                {
                    lastMatchingBestIdx = i;
                }
            }

            if (lastMatchingBestIdx >= 0)
            {
                cells[lastMatchingBestIdx] = cells[lastMatchingBestIdx] with { Status = ScoreboardAttemptStatus.Good };
                for (int i = 0; i < lastMatchingBestIdx; i++)
                {
                    if (cells[i].WeightKg == bestKg.Value)
                    {
                        cells[i] = cells[i] with { Status = ScoreboardAttemptStatus.Miss };
                    }
                    else if (cells[i].WeightKg.HasValue &&
                             cells[i].WeightKg!.Value < bestKg.Value &&
                             cells[i + 1].WeightKg.HasValue &&
                             cells[i + 1].WeightKg!.Value > cells[i].WeightKg!.Value)
                    {
                        cells[i] = cells[i] with { Status = ScoreboardAttemptStatus.Good };
                    }
                }
            }

            for (int i = 0; i < 3; i++)
            {
                if (!cells[i].WeightKg.HasValue)
                {
                    continue;
                }

                // Only infer Miss from weight > bestKg when the attempt is genuinely completed
                // (already marked Good/Miss, or a subsequent attempt slot is populated/passed).
                // Never clobber an un-taken Pending Attempt 3 waiting to be lifted!
                bool hasSubsequentSlot = i < 2 &&
                    (cells[i + 1].WeightKg.HasValue || cells[i + 1].Status != ScoreboardAttemptStatus.Empty);
                bool isCompletedCell = cells[i].Status is ScoreboardAttemptStatus.Good or ScoreboardAttemptStatus.Miss
                                       || hasSubsequentSlot;

                if (isCompletedCell && cells[i].WeightKg!.Value > bestKg.Value)
                {
                    cells[i] = cells[i] with { Status = ScoreboardAttemptStatus.Miss };
                }
            }
        }

        // 4. Re-enforce Un-taken Attempt 1 / Attempt 2 rule at the very end so even if bestKg was hallucinated on an
        // un-taken attempt, an Attempt 1 with an Empty Attempt 2 (or an Attempt 2 with an Empty Attempt 3) is ALWAYS Pending.
        if (cells[0].WeightKg.HasValue && att2Empty && att3Empty)
        {
            cells[0] = cells[0] with { Status = ScoreboardAttemptStatus.Pending };
        }

        if (cells[1].WeightKg.HasValue && att3Empty)
        {
            cells[1] = cells[1] with { Status = ScoreboardAttemptStatus.Pending };
        }

        return cells;
    }

    public static (int? WeightKg, bool ImpliedMiss) ParseWeightKg(JsonNode? node)
    {
        if (node == null)
        {
            return (null, false);
        }

        try
        {
            if (node is JsonValue val)
            {
                if (val.TryGetValue<int>(out int intVal))
                {
                    if (intVal == 0)
                    {
                        return (null, false);
                    }
                    return (Math.Abs(intVal), intVal < 0);
                }

                if (val.TryGetValue<double>(out double dblVal))
                {
                    int rounded = (int)Math.Round(Math.Abs(dblVal), MidpointRounding.AwayFromZero);
                    if (rounded == 0)
                    {
                        return (null, false);
                    }
                    return (rounded, dblVal < 0);
                }

                if (val.TryGetValue<string>(out string? strVal) && !string.IsNullOrWhiteSpace(strVal))
                {
                    string cleaned = strVal.Trim();
                    bool negative = cleaned.StartsWith('-') || cleaned.Contains('x', StringComparison.OrdinalIgnoreCase);
                    var digits = new string(cleaned.Where(c => char.IsDigit(c) || c == '.').ToArray());
                    if (double.TryParse(digits, CultureInfo.InvariantCulture, out double parsedDbl))
                    {
                        int rounded = (int)Math.Round(parsedDbl, MidpointRounding.AwayFromZero);
                        return rounded > 0 ? (rounded, negative) : (null, false);
                    }
                }
            }
        }
        catch
        {
            // Ignore malformed weight
        }

        return (null, false);
    }

    public static ScoreboardAttemptStatus ParseStatus(string? rawStatus, int? weightKg, bool impliedMiss)
    {
        if (impliedMiss && weightKg.HasValue)
        {
            return ScoreboardAttemptStatus.Miss;
        }

        string normalized = (rawStatus ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized is "good" or "make" or "made" or "success" or "valid" or "green")
        {
            return weightKg.HasValue ? ScoreboardAttemptStatus.Good : ScoreboardAttemptStatus.Empty;
        }

        if (normalized is "miss" or "missed" or "fail" or "failed" or "nolift" or "no_lift" or "no lift" or "red")
        {
            return weightKg.HasValue ? ScoreboardAttemptStatus.Miss : ScoreboardAttemptStatus.Empty;
        }

        if (normalized is "pass" or "passed" or "forfeit" or "withdrawn")
        {
            return ScoreboardAttemptStatus.Pass;
        }

        if (normalized is "pending" or "declared" or "upcoming" or "next" or "current")
        {
            return weightKg.HasValue ? ScoreboardAttemptStatus.Pending : ScoreboardAttemptStatus.Empty;
        }

        return weightKg.HasValue ? ScoreboardAttemptStatus.Pending : ScoreboardAttemptStatus.Empty;
    }

    public static int? ParsePositiveInt(JsonNode? node)
    {
        var (val, _) = ParseWeightKg(node);
        return val is > 0 ? val : null;
    }
}
