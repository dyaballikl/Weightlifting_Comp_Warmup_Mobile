using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace WeightliftingCompWarmup.Core.Services;

/// <summary>
/// Future-proof model discovery, ranking, and self-healing fallback resolver for Google Gemini API.
/// Dynamically selects the best available multimodal Gemini model from <c>GET /v1beta/models</c>
/// and extracts recommended replacement models from deprecation error messages so the app
/// continues working across future Gemini generations without requiring code updates.
/// </summary>
public static class GeminiModelResolver
{
    /// <summary>
    /// Evergreen aliases and known production model IDs used as fallback seeds only if live
    /// model discovery (<c>GET /v1beta/models</c>) is unreachable.
    /// </summary>
    public static readonly IReadOnlyList<string> FallbackSeedModels =
    [
        "gemini-2.5-flash",
        "gemini-flash-latest",
        "gemini-3-flash-preview",
        "gemini-2.5-pro",
        "gemini-pro-latest",
        "gemini-2.5-flash-lite"
    ];

    private static readonly string[] ExcludedModelSubstrings =
    [
        "embedding",
        "aqa",
        "imagen",
        "veo",
        "tts",
        "audio",
        "native-audio",
        "live",
        "robotics",
        "computer-use",
        "learnlm",
        "gemma",
        "bison",
        "gecko",
        "image",
        "thinking",
        "exp",
        "customtools",
        "tuning",
        "research",
        "preview-0",
        "-001",
        "-002",
        "8b",
        "1.0",
        "1.5-",
        "2.0-flash"
    ];

    private static readonly Regex VersionRegex = new(
        @"^gemini-(\d+)(?:\.(\d+))?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ErrorModelRegex = new(
        @"models/(gemini-[a-zA-Z0-9._-]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Parses the JSON payload from <c>GET https://generativelanguage.googleapis.com/v1beta/models</c>
    /// and returns all multimodal-capable Gemini models ordered from best to worst for scoreboard OCR.
    /// Prefers the newest generation stable Flash model (e.g. 4.0 &gt; 3.5 &gt; 3.0 &gt; 2.5), followed by
    /// preview/latest Flash, Flash-Lite, and Pro models.
    /// </summary>
    public static List<string> RankAvailableVisionModelsFromJson(string? modelsListJson)
    {
        var candidates = new List<(string ModelId, int Score)>();

        if (!string.IsNullOrWhiteSpace(modelsListJson))
        {
            try
            {
                var root = JsonNode.Parse(modelsListJson);
                var modelsArray = root?["models"]?.AsArray();
                if (modelsArray != null)
                {
                    foreach (var item in modelsArray)
                    {
                        if (item is not JsonObject obj)
                        {
                            continue;
                        }

                        string? rawName = obj["name"]?.GetValue<string>();
                        if (string.IsNullOrWhiteSpace(rawName))
                        {
                            continue;
                        }

                        string modelId = NormalizeModelId(rawName);
                        if (!modelId.StartsWith("gemini-", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        if (IsExcludedModel(modelId))
                        {
                            continue;
                        }

                        // Ensure the model supports generateContent if supportedGenerationMethods is present
                        var methods = obj["supportedGenerationMethods"]?.AsArray();
                        if (methods != null && methods.Count > 0)
                        {
                            bool supportsGenerateContent = methods.Any(m =>
                                string.Equals(m?.GetValue<string>(), "generateContent", StringComparison.OrdinalIgnoreCase));
                            if (!supportsGenerateContent)
                            {
                                continue;
                            }
                        }

                        int score = ScoreModelId(modelId);
                        candidates.Add((modelId, score));
                    }
                }
            }
            catch
            {
                // Ignore malformed JSON and fall back to seed models
            }
        }

        return candidates
            .OrderByDescending(c => c.Score)
            .ThenBy(c => c.ModelId, StringComparer.OrdinalIgnoreCase)
            .Select(c => c.ModelId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Builds the complete ordered candidate list combining any cached last-known-good model,
    /// live discovered models from <c>GET /v1beta/models</c>, and evergreen fallback aliases.
    /// </summary>
    public static List<string> BuildOrderedModelQueue(
        string? lastKnownGoodModel,
        IEnumerable<string>? discoveredModels)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddIfValid(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return;
            }

            string normalized = NormalizeModelId(raw);
            if (!normalized.StartsWith("gemini-", StringComparison.OrdinalIgnoreCase) || IsExcludedModel(normalized))
            {
                return;
            }

            if (seen.Add(normalized))
            {
                result.Add(normalized);
            }
        }

        var discoveredList = discoveredModels?
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Select(NormalizeModelId)
            .ToList() ?? [];

        // Only keep lastKnownGoodModel at the front if we haven't queried live models yet
        // OR if live discovery confirmed it still exists on the server and is not a preview model
        // when a stable model is available.
        if (!string.IsNullOrWhiteSpace(lastKnownGoodModel))
        {
            string normCached = NormalizeModelId(lastKnownGoodModel);
            bool isCachedPreviewOrLite = normCached.Contains("preview", StringComparison.OrdinalIgnoreCase) ||
                                         normCached.Contains("lite", StringComparison.OrdinalIgnoreCase);
            bool discoveredCheckPassed = discoveredList.Count == 0 ||
                                         discoveredList.Contains(normCached, StringComparer.OrdinalIgnoreCase);
            if (discoveredCheckPassed && !isCachedPreviewOrLite)
            {
                AddIfValid(normCached);
            }
        }

        if (discoveredList.Count > 0)
        {
            foreach (string m in discoveredList)
            {
                AddIfValid(m);
            }
        }
        else
        {
            foreach (string seed in FallbackSeedModels)
            {
                AddIfValid(seed);
            }
        }

        // Cap the fallback queue to at most 4 distinct models so we never cycle through a long list
        if (result.Count > 4)
        {
            result = result.Take(4).ToList();
        }

        return result;
    }

    /// <summary>
    /// Extracts a recommended replacement model ID from a Google Gemini API error message
    /// (for example: "This model models/gemini-2.0-flash is no longer available. Please update your code to use models/gemini-3.0-flash...").
    /// </summary>
    public static string? ExtractSuggestedModelFromError(string? errorMessage, string? failedModelId = null)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
        {
            return null;
        }

        string? normalizedFailed = string.IsNullOrWhiteSpace(failedModelId)
            ? null
            : NormalizeModelId(failedModelId);

        var matches = ErrorModelRegex.Matches(errorMessage);
        foreach (Match match in matches)
        {
            if (!match.Success || match.Groups.Count < 2)
            {
                continue;
            }

            string candidate = NormalizeModelId(match.Groups[1].Value.TrimEnd('.', ',', ';', ')', '\'', '"'));
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            if (!string.Equals(candidate, normalizedFailed, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return null;
    }

    public static string NormalizeModelId(string rawModelName)
    {
        string trimmed = (rawModelName ?? string.Empty).Trim();
        if (trimmed.StartsWith("models/", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed["models/".Length..];
        }

        return trimmed.Trim();
    }

    public static int ScoreModelId(string modelId)
    {
        string lower = modelId.ToLowerInvariant();

        // Parse major.minor version (e.g. gemini-3.0-flash -> major=3, minor=0; gemini-2.5-flash -> major=2, minor=5)
        int major = 0;
        int minor = 0;
        var verMatch = VersionRegex.Match(lower);
        if (verMatch.Success)
        {
            int.TryParse(verMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out major);
            if (verMatch.Groups[2].Success)
            {
                int.TryParse(verMatch.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out minor);
            }
        }

        int versionScore = (major * 1000) + (minor * 100);

        bool isFlash = lower.Contains("flash", StringComparison.Ordinal);
        bool isLite = lower.Contains("lite", StringComparison.Ordinal) || lower.Contains("8b", StringComparison.Ordinal);
        bool isPro = lower.Contains("pro", StringComparison.Ordinal);
        bool isPreviewOrExp = lower.Contains("preview", StringComparison.Ordinal) ||
                              lower.Contains("exp", StringComparison.Ordinal) ||
                              lower.Contains("thinking", StringComparison.Ordinal);
        bool isLatestAlias = lower.EndsWith("-latest", StringComparison.Ordinal);

        int familyTier;
        if (isFlash && !isLite && !isPreviewOrExp && !isLatestAlias)
        {
            // Stable GA Flash model (e.g. gemini-2.5-flash, gemini-3.8-flash, gemini-4.2-flash) — always preferred first!
            familyTier = 25000;
        }
        else if (string.Equals(lower, "gemini-flash-latest", StringComparison.Ordinal))
        {
            // Evergreen alias for the current production Flash model
            familyTier = 24000;
        }
        else if (isFlash && !isLite)
        {
            // Preview Flash model (e.g. gemini-3-flash-preview) — full Flash capability, ahead of Pro and Lite
            familyTier = 19000;
        }
        else if (isPro)
        {
            // Pro model (high accuracy vision, ahead of Flash-Lite)
            familyTier = isPreviewOrExp ? 11000 : 14000;
        }
        else if (isFlash && isLite)
        {
            // Flash-Lite model (last resort only — smaller model is less accurate on dense 16-row scoreboards)
            familyTier = isPreviewOrExp ? 2000 : 3000;
        }
        else
        {
            familyTier = 1000;
        }

        // Prefer shorter, canonical aliases (e.g. "gemini-3.0-flash" over "gemini-3.0-flash-001")
        int canonicalBonus = (!isLatestAlias && Regex.IsMatch(lower, @"^gemini-\d+(\.\d+)?-(flash|pro)(-preview)?$"))
            ? 5
            : 0;

        return familyTier + versionScore + canonicalBonus;
    }

    private static bool IsExcludedModel(string modelId)
    {
        string lower = modelId.ToLowerInvariant();
        foreach (string excluded in ExcludedModelSubstrings)
        {
            if (lower.Contains(excluded, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
