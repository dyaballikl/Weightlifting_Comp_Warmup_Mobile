using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Maui.Storage;
using WeightliftingCompWarmup.Core.Models;
using WeightliftingCompWarmup.Core.Services;

namespace WeightliftingCompWarmup.Maui.Services;

/// <summary>
/// On-device client for analyzing competition scoreboard photos using the user's own
/// Google Gemini API key. The API key is stored in encrypted device <see cref="SecureStorage"/>
/// (Android Keystore / iOS Keychain) and is never included in profile exports, cloud backups,
/// or sent anywhere other than Google's official Generative Language API via the
/// <c>x-goog-api-key</c> HTTP header.
/// </summary>
public class GeminiScoreboardVisionService
{
    public const string GoogleAiStudioKeyUrl = "https://aistudio.google.com/app/apikey";
    private const string SecureStorageKeyName = "compowl_gemini_api_key";

    public static string? LastUsedModelId { get; private set; }
    public static string? LastRawModelResponseJson { get; private set; }
    public static string? LastErrorDetail { get; private set; }

    private static readonly HttpClient SharedHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(60)
    };

    private static string? _memoryCachedApiKey;
    private static bool _hasLoadedSecureKey;

    private readonly string _keyFilePath;
    private readonly string _cachedModelFilePath;
    private readonly bool _useCustomFileOnly;

    public GeminiScoreboardVisionService(string? customKeyFilePath = null)
    {
        if (!string.IsNullOrWhiteSpace(customKeyFilePath))
        {
            _useCustomFileOnly = true;
            _keyFilePath = customKeyFilePath;
            string dir = Path.GetDirectoryName(customKeyFilePath) ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _cachedModelFilePath = Path.Combine(dir, "last_working_gemini_model.txt");
        }
        else
        {
            _useCustomFileOnly = false;
            string appDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WeightliftingCompWarmup");
            Directory.CreateDirectory(appDir);
            _keyFilePath = Path.Combine(appDir, "gemini_api_key.txt");
            _cachedModelFilePath = Path.Combine(appDir, "last_working_gemini_model.txt");
        }
    }

    public bool HasApiKey() => !string.IsNullOrWhiteSpace(GetApiKey());

    public string GetApiKey()
    {
        if (_useCustomFileOnly)
        {
            return ReadLegacyKeyFile();
        }

        if (_hasLoadedSecureKey)
        {
            return _memoryCachedApiKey ?? string.Empty;
        }

        string key = string.Empty;
        bool secureStorageAvailable = false;

        try
        {
            string? stored = SecureStorage.Default.GetAsync(SecureStorageKeyName).GetAwaiter().GetResult();
            secureStorageAvailable = true;
            if (!string.IsNullOrWhiteSpace(stored))
            {
                key = stored.Trim();
            }
        }
        catch
        {
            secureStorageAvailable = false;
        }

        // One-time transparent migration from legacy plaintext gemini_api_key.txt into SecureStorage
        if (string.IsNullOrEmpty(key))
        {
            string legacyKey = ReadLegacyKeyFile();
            if (!string.IsNullOrEmpty(legacyKey))
            {
                key = legacyKey;
                if (secureStorageAvailable)
                {
                    try
                    {
                        SecureStorage.Default.SetAsync(SecureStorageKeyName, legacyKey).GetAwaiter().GetResult();
                        DeleteLegacyKeyFile();
                    }
                    catch
                    {
                        // Keep legacy file if SecureStorage write fails on this device
                    }
                }
            }
        }
        else
        {
            // Key is already safely in SecureStorage; ensure any leftover plaintext file is removed
            DeleteLegacyKeyFile();
        }

        _memoryCachedApiKey = key;
        _hasLoadedSecureKey = true;
        return key;
    }

    public void SaveApiKey(string? apiKey)
    {
        SaveCachedWorkingModel(null);
        string trimmed = (apiKey ?? string.Empty).Trim();

        if (_useCustomFileOnly)
        {
            WriteLegacyKeyFile(trimmed);
            return;
        }

        _memoryCachedApiKey = trimmed;
        _hasLoadedSecureKey = true;

        bool savedToSecureStorage = false;
        try
        {
            if (string.IsNullOrEmpty(trimmed))
            {
                SecureStorage.Default.Remove(SecureStorageKeyName);
            }
            else
            {
                SecureStorage.Default.SetAsync(SecureStorageKeyName, trimmed).GetAwaiter().GetResult();
            }
            savedToSecureStorage = true;
        }
        catch
        {
            savedToSecureStorage = false;
        }

        if (savedToSecureStorage)
        {
            DeleteLegacyKeyFile();
        }
        else
        {
            WriteLegacyKeyFile(trimmed);
        }
    }

    /// <summary>
    /// Verifies that the supplied (or currently saved) Gemini API key is accepted by Google's
    /// Generative Language API and reports the top active vision model available on that key.
    /// </summary>
    public async Task<(bool Success, string Message)> TestApiKeyAsync(
        string? candidateApiKey = null,
        CancellationToken cancellationToken = default)
    {
        string apiKey = !string.IsNullOrWhiteSpace(candidateApiKey)
            ? candidateApiKey.Trim()
            : GetApiKey();

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return (false, "Enter or save a Gemini API key first.");
        }

        try
        {
            const string listUrl = "https://generativelanguage.googleapis.com/v1beta/models?pageSize=100";
            using var request = new HttpRequestMessage(HttpMethod.Get, listUrl);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.TryAddWithoutValidation("x-goog-api-key", apiKey);

            using var response = await SharedHttpClient.SendAsync(request, cancellationToken);
            string body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                string err = TryExtractApiErrorMessage(body) ?? $"HTTP {(int)response.StatusCode} ({response.ReasonPhrase})";
                return (false, $"Key check failed: {err}");
            }

            var models = GeminiModelResolver.RankAvailableVisionModelsFromJson(body);
            string topModel = models.FirstOrDefault() ?? "gemini-2.5-flash";
            return (true, $"✅ Key valid! Ready with {topModel} ({models.Count} vision model(s) available).");
        }
        catch (OperationCanceledException)
        {
            return (false, "Key check canceled or timed out.");
        }
        catch (Exception ex)
        {
            return (false, $"Could not reach Google AI Studio (offline?): {ex.Message}");
        }
    }

    private string ReadLegacyKeyFile()
    {
        try
        {
            if (File.Exists(_keyFilePath))
            {
                return File.ReadAllText(_keyFilePath).Trim();
            }
        }
        catch
        {
            // Ignore local file read errors
        }

        return string.Empty;
    }

    private void WriteLegacyKeyFile(string trimmed)
    {
        try
        {
            if (string.IsNullOrEmpty(trimmed))
            {
                DeleteLegacyKeyFile();
                return;
            }

            string? dir = Path.GetDirectoryName(_keyFilePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(_keyFilePath, trimmed);
        }
        catch
        {
            // Ignore local file write errors
        }
    }

    private void DeleteLegacyKeyFile()
    {
        try
        {
            if (File.Exists(_keyFilePath))
            {
                File.Delete(_keyFilePath);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    public string GetMaskedApiKey()
    {
        string key = GetApiKey();
        if (string.IsNullOrWhiteSpace(key))
        {
            return "Not configured";
        }

        if (key.Length <= 8)
        {
            return "••••••••";
        }

        return $"{key[..4]}••••••••{key[^4..]}";
    }

    /// <summary>
    /// Sends the scoreboard photo to Gemini Vision and parses the competition table into
    /// <see cref="ScoreboardLifterRow"/> records with whole-kilogram weights.
    /// Includes perspective-angle row-anchoring instructions and automatic self-healing alignment verification.
    /// </summary>
    public async Task<(bool Success, string? ErrorMessage, List<ScoreboardLifterRow> Lifters)> AnalyzeScoreboardImageAsync(
        byte[] imageBytes,
        string mimeType,
        CancellationToken cancellationToken = default,
        IProgress<ScoreboardScanProgress>? progress = null)
    {
        string apiKey = GetApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return (false, "Please enter and save your Google Gemini API key first.", []);
        }

        if (imageBytes == null || imageBytes.Length == 0)
        {
            return (false, "The selected image is empty.", []);
        }

        int imageKb = Math.Max(1, imageBytes.Length / 1024);
        progress?.Report(new ScoreboardScanProgress(
            1,
            4,
            "Step 1/4: Preparing scoreboard photo...",
            $"Encoding {imageKb} KB image for AI vision analysis",
            0.12));

        string base64Image = Convert.ToBase64String(imageBytes);
        string effectiveMime = string.IsNullOrWhiteSpace(mimeType) ? "image/jpeg" : mimeType;

        string prompt = """
            You are an expert Olympic Weightlifting competition scoreboard reader (IWF, OWLCMS, USAW, AWF, BWL, and club meet displays).
            Analyze this photo of a competition scoreboard and extract EVERY lifter row in top-to-bottom table order (do NOT stop early — extract every single row down to the bottom of the table right above the records/footer bar, whether there are 8, 12, 14, 16, or 18 rows).

            Return ONLY valid JSON matching this exact structure:
            {
              "totalRows": 16,
              "lifters": [
                {
                  "orderNumber": 1,
                  "leftColumnText": "1",
                  "lotNumber": 1,
                  "name": "ENKILEDA Carja",
                  "countryOrClub": "ALB",
                  "snatch": [
                    { "attempt": 1, "weightKg": 98, "status": "Good" },
                    { "attempt": 2, "weightKg": 100, "status": "Good" },
                    { "attempt": 3, "weightKg": 102, "status": "Good" }
                  ],
                  "snatchBestKg": 102,
                  "snatchPlace": 2,
                  "cleanAndJerk": [
                    { "attempt": 1, "weightKg": 117, "status": "Pending" },
                    { "attempt": 2, "weightKg": null, "status": "Empty" },
                    { "attempt": 3, "weightKg": null, "status": "Empty" }
                  ],
                  "cjBestKg": null,
                  "cjPlace": null,
                  "totalKg": null
                }
              ]
            }

            CRITICAL RULES FOR WEIGHTLIFTING SCOREBOARDS:
            1. PERSPECTIVE & ANGLED PHOTO ALIGNMENT (MOST IMPORTANT):
               - Scoreboard TVs are often photographed at an oblique angle where horizontal table rows slope diagonally across the image.
               - NEVER match an athlete's name on the left to weights in the middle using flat horizontal pixel height!
               - Instead, trace each row's sloped horizontal divider line and alternating row background band continuously from left to right across EVERY adjacent column:
                 leftColumnText -> name -> countryOrClub (which sits immediately to the left of Snatch 1st attempt!) -> snatch (1st, 2nd, 3rd) -> snatchBestKg (the BEST column immediately after Snatch 3rd) -> snatchPlace (SPL/Rank column) -> cleanAndJerk (1st, 2nd, 3rd) -> cjBestKg -> cjPlace -> totalKg.
               - Count the total athlete rows on the left ("totalRows") and count the total rows in the Snatch and Clean & Jerk columns. They MUST have the exact same number of rows ("totalRows"), and the last athlete at the bottom of the table ("orderNumber" == "totalRows") MUST pair with the bottom row of Snatch and Clean & Jerk weights!
               - Cross-check every row using "snatchBestKg" (and "cjBestKg"): on every row where "snatchBestKg" is a number, that exact number MUST equal the highest "Good" weight in that same row's "snatch" array. If a row's "snatch" weights do not contain its "snatchBestKg", you have shifted up or down by a row — re-trace that row along its sloped divider line!
            2. Whole kilograms ONLY: "weightKg" must be a positive integer (e.g. 95) or null if blank. If a missed lift is shown as "-95" or "(95)" or strikethrough "95x", set "weightKg": 95 and "status": "Miss".
            3. Attempt Status ("status") MUST be one of:
               - "Good": Lift was completed and successful (on IWF broadcast boards, Good lifts are WHITE boxes with black text; on OWLCMS/club boards, green background or checkmark).
               - "Miss": Lift was attempted and missed / no-lift (RED, PINK, or MAGENTA box, strikethrough, negative number like -95, or X).
               - IMPORTANT FOR REPEATED WEIGHTS & BEST COLUMN:
                 * When a lifter misses a weight and repeats that same weight on their next attempt (e.g. 90 followed by 90, 93 followed by 93, 99 followed by 99, or 94 followed by 94), ONLY the earlier attempt is guaranteed to be "Miss". The second attempt at that weight is "Good" if its box is white/green OR if that weight equals "snatchBestKg" / "cjBestKg"!
                 * Whatever weight is shown in "snatchBestKg" (or "cjBestKg"), the lifter's LAST attempt at that exact weight MUST be marked "Good"!
                 * CRITICAL — DO NOT CONFUSE AN UPCOMING "Pending" ATTEMPT 2 WITH A "Miss":
                   When Attempt 3 is blank/dash ("—") and Attempt 2 has a heavier weight than "snatchBestKg" (for example, Attempt 1 = 85 Good with BEST = 85, Attempt 2 = 89, Attempt 3 = "—"; or Attempt 1 = 86 Good with BEST = 86, Attempt 2 = 87, Attempt 3 = "—") in a normal dark/uncolored box without red fill or strikethrough, Attempt 2 has NOT been taken yet — its status MUST be "Pending" (NEVER "Miss")!
                   Only use "snatchBestKg" / "cjBestKg" to infer "Miss" on a heavier weight when ALL 3 attempts on that row have weights OR when a subsequent attempt on that row already has a weight!
               - "Pass": Lifter passed/forfeited the attempt ("-", "---", "PASS").
               - "Pending": Weight is declared on the board for an upcoming or current on-the-clock attempt that has NOT been completed yet (e.g. dark blue/grey/navy box without green or red fill, or yellow outline box).
               - "Empty": Cell is blank or dash ("—", "-") / no weight declared yet ("weightKg": null).
            4. Sequential & Concluded Snatch Rule:
               - Within a discipline, if Attempt 2 has a weight, Attempt 1 has ALREADY been completed ("Good" or "Miss", never "Pending"). If Attempt 3 has a weight, Attempts 1 and 2 have ALREADY been completed ("Good" or "Miss", never "Pending").
               - If Snatch is fully completed and the scoreboard shows a Clean & Jerk lift up next (or C&J is in progress), mark all completed Snatch cells as "Good", "Miss", or "Pass" (never "Pending").
            5. Lot / Seed Number ("lotNumber") vs Placement ("SPL" / "CJPL"):
               - Columns labeled "SPL", "CJPL", "PL", "RANK", or "PLACE" (located after the "BEST" column) are placement rankings — put those in "snatchPlace" / "cjPlace", NEVER in "lotNumber"!
               - "lotNumber" comes ONLY from a "LOT" / "SEED" column or the leftmost start-order number ("1", "2", "3"...) before the athlete's name. If the leftmost column contains a group letter like "D", "B", or "C" instead of a number, set "lotNumber" equal to the 1-based "orderNumber".
            6. If the scoreboard only displays one discipline (e.g., only Snatch or only Clean & Jerk is visible), populate that discipline's 3 attempts and set the other discipline's 3 attempts to Empty unless visible.
            """;

        string requestJsonWithMime = BuildImagePromptPayload(prompt, effectiveMime, base64Image, useJsonMimeType: true).ToJsonString();
        string requestJsonPlain = BuildImagePromptPayload(prompt, effectiveMime, base64Image, useJsonMimeType: false).ToJsonString();
        string? lastError = null;

        string? cachedModel = GetCachedWorkingModel();
        if (!string.IsNullOrWhiteSpace(cachedModel) &&
            (cachedModel.Contains("preview", StringComparison.OrdinalIgnoreCase) ||
             cachedModel.Contains("lite", StringComparison.OrdinalIgnoreCase)))
        {
            // Clear any previously cached preview or lite model so we always prefer full stable GA Flash models first
            SaveCachedWorkingModel(null);
            cachedModel = null;
        }

        List<string> discoveredModels = [];
        bool hasQueriedLiveModels = false;

        // If we don't have a previously verified stable model cached on device, query GET /v1beta/models first
        // so we immediately pick the best active stable Flash model available on the user's API key.
        if (string.IsNullOrWhiteSpace(cachedModel))
        {
            progress?.Report(new ScoreboardScanProgress(
                2,
                4,
                "Step 2/4: Checking available Gemini Vision models...",
                "Querying Google AI endpoint for active vision models on your API key",
                0.28));
            discoveredModels = await DiscoverAvailableModelsAsync(apiKey, cancellationToken);
            hasQueriedLiveModels = true;
        }
        else
        {
            progress?.Report(new ScoreboardScanProgress(
                2,
                4,
                "Step 2/4: Selecting cached Gemini Vision model...",
                $"Using verified model {cachedModel}",
                0.28));
        }

        var modelQueue = GeminiModelResolver.BuildOrderedModelQueue(cachedModel, discoveredModels);
        var triedModels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? lastFailedModelSummary = null;

        for (int i = 0; i < modelQueue.Count; i++)
        {
            string model = GeminiModelResolver.NormalizeModelId(modelQueue[i]);
            if (string.IsNullOrWhiteSpace(model) || !triedModels.Add(model))
            {
                continue;
            }

            bool isFallbackAttempt = triedModels.Count > 1;
            if (isFallbackAttempt)
            {
                // Brief pause before trying a fallback model so we don't rapid-fire requests or flash the UI
                await Task.Delay(1500, cancellationToken);
            }

            progress?.Report(new ScoreboardScanProgress(
                3,
                4,
                isFallbackAttempt
                    ? $"Step 3/4: Trying fallback AI model ({model})..."
                    : $"Step 3/4: Waiting for AI model ({model}) analysis...",
                isFallbackAttempt
                    ? $"{lastFailedModelSummary ?? "Previous model unavailable"} — waiting for {model} response (attempt {triedModels.Count})"
                    : "Photo sent — waiting for AI model to extract lifter rows, weights & cell colors",
                0.58,
                IsFallbackOrRetry: isFallbackAttempt));

            string url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent";

            try
            {
                bool usedPlainConfig = false;
                var (statusCode, reasonPhrase, responseBody) = await SendGenerateContentRequestAsync(
                    url,
                    apiKey,
                    requestJsonWithMime,
                    cancellationToken);

                // If a future model rejects responseMimeType in generationConfig (HTTP 400), retry once with plain generationConfig
                if (statusCode == 400 &&
                    (responseBody.Contains("responseMimeType", StringComparison.OrdinalIgnoreCase) ||
                     responseBody.Contains("response_mime_type", StringComparison.OrdinalIgnoreCase) ||
                     responseBody.Contains("generationConfig", StringComparison.OrdinalIgnoreCase)))
                {
                    usedPlainConfig = true;
                    (statusCode, reasonPhrase, responseBody) = await SendGenerateContentRequestAsync(
                        url,
                        apiKey,
                        requestJsonPlain,
                        cancellationToken);
                }

                // If the current model returned a transient 503 Overloaded or 429 Rate-Limit (and not a hard limit: 0 tier restriction),
                // do NOT give up on this model right away — wait 3 seconds and retry this exact model once!
                if ((statusCode == 503 || statusCode == 429) &&
                    !responseBody.Contains("limit: 0", StringComparison.OrdinalIgnoreCase))
                {
                    progress?.Report(new ScoreboardScanProgress(
                        3,
                        4,
                        $"Step 3/4: AI model ({model}) is busy — retrying in 3s...",
                        $"Server returned HTTP {statusCode} — giving {model} another chance before switching models",
                        0.58,
                        IsFallbackOrRetry: true));

                    await Task.Delay(3000, cancellationToken);

                    (statusCode, reasonPhrase, responseBody) = await SendGenerateContentRequestAsync(
                        url,
                        apiKey,
                        usedPlainConfig ? requestJsonPlain : requestJsonWithMime,
                        cancellationToken);
                }

                if (statusCode < 200 || statusCode >= 300)
                {
                    string apiErr = TryExtractApiErrorMessage(responseBody) ?? $"HTTP {statusCode} ({reasonPhrase})";
                    lastError = apiErr;
                    lastFailedModelSummary = $"{model} returned HTTP {statusCode}";

                    // Stop immediately if the user's API key itself is invalid
                    if (IsInvalidApiKeyError(statusCode, apiErr, responseBody))
                    {
                        return (false, $"Invalid Gemini API Key: {apiErr}", []);
                    }

                    // If we started with a cached model that failed, clear the cache and run live GET /v1beta/models discovery
                    // to replace the remaining queue with only verified live models.
                    if (!hasQueriedLiveModels)
                    {
                        SaveCachedWorkingModel(null);
                        hasQueriedLiveModels = true;
                        var liveModels = await DiscoverAvailableModelsAsync(apiKey, cancellationToken);
                        var verifiedQueue = GeminiModelResolver.BuildOrderedModelQueue(null, liveModels);
                        modelQueue.RemoveRange(i + 1, modelQueue.Count - (i + 1));
                        foreach (string liveModel in verifiedQueue)
                        {
                            if (!triedModels.Contains(liveModel))
                            {
                                modelQueue.Add(liveModel);
                            }
                        }
                    }

                    // Self-healing: if Google's error message recommends a specific replacement model, insert it next!
                    string? suggestedModel = GeminiModelResolver.ExtractSuggestedModelFromError(apiErr, model);
                    if (!string.IsNullOrWhiteSpace(suggestedModel) && !triedModels.Contains(suggestedModel))
                    {
                        modelQueue.Insert(i + 1, suggestedModel);
                    }

                    continue;
                }

                string? extractedJson = TryExtractCandidateText(responseBody);
                if (string.IsNullOrWhiteSpace(extractedJson))
                {
                    lastError = "Gemini returned an empty response.";
                    continue;
                }

                LastUsedModelId = model;
                LastRawModelResponseJson = extractedJson;
                LastErrorDetail = null;

                progress?.Report(new ScoreboardScanProgress(
                    4,
                    4,
                    "Step 4/4: Verifying row alignment & IWF rules...",
                    "Cross-checking BEST columns, repeated weights, and prior-session rows",
                    0.85));

                var lifters = ParseLiftersJson(extractedJson);
                if (lifters.Count == 0)
                {
                    return (false, "No lifter rows were detected in the image. Make sure the scoreboard table is clearly visible.", []);
                }

                // Self-healing perspective alignment check:
                // If the photo was taken at a steep angle and we detect a row-shift anomaly
                // (e.g., a row's snatchBestKg doesn't match any of its extracted Snatch attempts,
                // or the bottom athlete row was left blank after a block of populated rows because a middle weight row was skipped),
                // run a fast targeted alignment repair pass anchored to the exact numbered athlete list!
                if (HasRowAlignmentAnomaly(extractedJson, lifters))
                {
                    progress?.Report(new ScoreboardScanProgress(
                        4,
                        4,
                        "Step 4/4: Repairing angled-photo row alignment...",
                        "Detected perspective slope shift — re-anchoring weights to exact athlete rows",
                        0.92,
                        IsFallbackOrRetry: true));

                    string repairPrompt = BuildRowAlignmentCorrectionPrompt(lifters);
                    string repairRequestJson = BuildImagePromptPayload(
                        repairPrompt,
                        effectiveMime,
                        base64Image,
                        useJsonMimeType: !usedPlainConfig).ToJsonString();

                    var (repairStatus, _, repairBody) = await SendGenerateContentRequestAsync(
                        url,
                        apiKey,
                        repairRequestJson,
                        cancellationToken);

                    if (repairStatus is >= 200 and < 300)
                    {
                        string? repairedJson = TryExtractCandidateText(repairBody);
                        if (!string.IsNullOrWhiteSpace(repairedJson))
                        {
                            var repairedLifters = ParseLiftersJson(repairedJson);
                            if (repairedLifters.Count == lifters.Count &&
                                !HasRowAlignmentAnomaly(repairedJson, repairedLifters))
                            {
                                lifters = repairedLifters;
                                LastRawModelResponseJson = repairedJson;
                            }
                        }
                    }
                }

                // Cache this working model (unless it was a fallback -lite model) so future scans succeed on the first request
                if (!model.Contains("lite", StringComparison.OrdinalIgnoreCase))
                {
                    SaveCachedWorkingModel(model);
                }
                return (true, null, lifters);
            }
            catch (OperationCanceledException)
            {
                return (false, "Scoreboard scan was canceled or timed out.", []);
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
            }
        }

        LastErrorDetail = lastError;
        return (false, $"Could not analyze scoreboard: {lastError ?? "Unknown error"}", []);
    }

    private static JsonObject BuildImagePromptPayload(
        string prompt,
        string effectiveMime,
        string base64Image,
        bool useJsonMimeType)
    {
        var genConfig = new JsonObject
        {
            ["temperature"] = 0.0
        };

        if (useJsonMimeType)
        {
            genConfig["responseMimeType"] = "application/json";
        }

        return new JsonObject
        {
            ["contents"] = new JsonArray
            {
                new JsonObject
                {
                    ["parts"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["text"] = prompt
                        },
                        new JsonObject
                        {
                            ["inline_data"] = new JsonObject
                            {
                                ["mime_type"] = effectiveMime,
                                ["data"] = base64Image
                            }
                        }
                    }
                }
            },
            ["generationConfig"] = genConfig
        };
    }

    /// <summary>
    /// Detects if an angled scoreboard photo caused the vision model to skip or shift a weight row.
    /// Delegates to <see cref="ScoreboardJsonParser.HasRowAlignmentAnomaly"/> in Core.
    /// </summary>
    public static bool HasRowAlignmentAnomaly(string rawJson, IReadOnlyList<ScoreboardLifterRow> lifters)
        => ScoreboardJsonParser.HasRowAlignmentAnomaly(rawJson, lifters);

    private static string BuildRowAlignmentCorrectionPrompt(IReadOnlyList<ScoreboardLifterRow> initialLifters)
    {
        var sb = new StringBuilder();
        sb.AppendLine("The scoreboard photo was taken at an angle, causing a row-alignment shift between the athlete names on the left and the attempt weights in the middle/right.");
        sb.AppendLine($"There are exactly {initialLifters.Count} athlete rows from top (#1) to bottom (#{initialLifters.Count}):");
        for (int i = 0; i < initialLifters.Count; i++)
        {
            sb.AppendLine($"  Row #{i + 1}: {initialLifters[i].Name}");
        }

        sb.AppendLine();
        sb.AppendLine($$"""
            Carefully re-read the scoreboard table from Row #1 at the top down to Row #{{initialLifters.Count}} at the bottom:
            - Follow each row's sloped horizontal divider line from the athlete's name -> their country/club code -> their Snatch 1st, 2nd, 3rd, and BEST columns -> their Clean & Jerk 1st, 2nd, 3rd, and BEST columns.
            - Make sure NO row of weights is skipped in the middle, so Row #{{initialLifters.Count}} at the bottom gets the bottom row of weights on the scoreboard!
            - Verify on EVERY row that "snatchBestKg" equals one of the 3 Snatch weights on that exact same row.
            - Remember: "SPL" and "CJPL" columns after "BEST" are placement ranks ("snatchPlace" / "cjPlace"), NOT "lotNumber"!
            - If Attempt 2 and Attempt 3 have the same weight (e.g. 94 and 94), Attempt 2 MUST be "Miss". Any completed attempt heavier than "snatchBestKg" / "cjBestKg" on that row MUST be "Miss".

            Return ONLY valid JSON with the exact same schema ("totalRows", "lifters" array with "orderNumber", "leftColumnText", "lotNumber", "name", "countryOrClub", "snatch", "snatchBestKg", "snatchPlace", "cleanAndJerk", "cjBestKg", "cjPlace", "totalKg").
            """);

        return sb.ToString();
    }

    private static async Task<(int StatusCode, string? ReasonPhrase, string ResponseBody)> SendGenerateContentRequestAsync(
        string url,
        string apiKey,
        string requestJson,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(requestJson, Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("x-goog-api-key", apiKey);

        using var response = await SharedHttpClient.SendAsync(request, cancellationToken);
        string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        return ((int)response.StatusCode, response.ReasonPhrase, responseBody);
    }

    /// <summary>
    /// Queries <c>GET https://generativelanguage.googleapis.com/v1beta/models</c> to dynamically discover
    /// all currently active multimodal Gemini models for this API key, ranked newest Flash first.
    /// </summary>
    private static async Task<List<string>> DiscoverAvailableModelsAsync(
        string apiKey,
        CancellationToken cancellationToken)
    {
        try
        {
            const string listUrl = "https://generativelanguage.googleapis.com/v1beta/models?pageSize=100";
            using var request = new HttpRequestMessage(HttpMethod.Get, listUrl);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.TryAddWithoutValidation("x-goog-api-key", apiKey);

            using var response = await SharedHttpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return [];
            }

            string json = await response.Content.ReadAsStringAsync(cancellationToken);
            return GeminiModelResolver.RankAvailableVisionModelsFromJson(json);
        }
        catch
        {
            return [];
        }
    }

    private static bool IsInvalidApiKeyError(int statusCode, string apiErr, string rawBody)
    {
        if (statusCode is 400 or 401 or 403)
        {
            if (apiErr.Contains("API key not valid", StringComparison.OrdinalIgnoreCase) ||
                apiErr.Contains("API_KEY_INVALID", StringComparison.OrdinalIgnoreCase) ||
                apiErr.Contains("API key expired", StringComparison.OrdinalIgnoreCase) ||
                rawBody.Contains("API_KEY_INVALID", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private string? GetCachedWorkingModel()
    {
        try
        {
            if (File.Exists(_cachedModelFilePath))
            {
                string cached = File.ReadAllText(_cachedModelFilePath).Trim();
                return string.IsNullOrWhiteSpace(cached) ? null : GeminiModelResolver.NormalizeModelId(cached);
            }
        }
        catch
        {
            // Ignore local cache read errors
        }

        return null;
    }

    private void SaveCachedWorkingModel(string? modelId)
    {
        try
        {
            string trimmed = (modelId ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                if (File.Exists(_cachedModelFilePath))
                {
                    File.Delete(_cachedModelFilePath);
                }
                return;
            }

            File.WriteAllText(_cachedModelFilePath, GeminiModelResolver.NormalizeModelId(trimmed));
        }
        catch
        {
            // Ignore local cache write errors
        }
    }

    private static string? TryExtractApiErrorMessage(string responseBody)
    {
        try
        {
            var node = JsonNode.Parse(responseBody);
            return node?["error"]?["message"]?.GetValue<string>();
        }
        catch
        {
            return null;
        }
    }

    private static string? TryExtractCandidateText(string responseBody)
    {
        try
        {
            var root = JsonNode.Parse(responseBody);
            var parts = root?["candidates"]?[0]?["content"]?["parts"]?.AsArray();
            if (parts == null)
            {
                return null;
            }

            var sb = new StringBuilder();
            foreach (var part in parts)
            {
                string? text = part?["text"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    sb.AppendLine(text);
                }
            }

            string raw = sb.ToString().Trim();
            if (raw.StartsWith("```", StringComparison.Ordinal))
            {
                int firstNewline = raw.IndexOf('\n');
                if (firstNewline >= 0)
                {
                    raw = raw[(firstNewline + 1)..];
                }
                int lastFence = raw.LastIndexOf("```", StringComparison.Ordinal);
                if (lastFence >= 0)
                {
                    raw = raw[..lastFence];
                }
            }

            return raw.Trim();
        }
        catch
        {
            return null;
        }
    }

    public static List<ScoreboardLifterRow> ParseLiftersJson(string json)
        => ScoreboardJsonParser.ParseLiftersJson(json);
}
