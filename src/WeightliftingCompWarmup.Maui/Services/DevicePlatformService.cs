using System.Text.Json;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices;
using WeightliftingCompWarmup.Maui.Views;

namespace WeightliftingCompWarmup.Maui.Services;

/// <summary>
/// Provides cross-platform mobile hardware integration for Android (Phone &amp; Tablet) and iOS (iPhone &amp; iPad):
/// - Keeping the screen awake during a live competition warmup (<c>DeviceDisplay.KeepScreenOn</c>)
/// - Playing the 750Hz 600ms step transition beep &amp; haptic feedback
/// - Checking for newer app versions on open and opening the platform store page
/// </summary>
public class DevicePlatformService
{
    private const string VersionManifestUrl = "https://raw.githubusercontent.com/dyaballikl/Weightlifting_Comp_Warmup_Mobile/main/version.json";
    private const string DefaultPackageId = "com.weightlifting.compwarmup";
    private const string DefaultPlayStoreUrl = "https://play.google.com/store/apps/details?id=com.weightlifting.compwarmup";

    private static readonly HttpClient UpdateHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(4)
    };

    private string? _discoveredStoreUrlAndroid;
    private string? _discoveredStoreUrlIos;

    public static bool IsLiveMiniTimerActive { get; private set; }
    public static bool IsInPipMode { get; private set; }
    public static event Action<bool>? PipModeChanged;

    /// <summary>
    /// Enables or disables the automatic floating mini-timer (Picture-in-Picture overlay)
    /// when the user navigates to the home screen while a Live warmup timer is running.
    /// </summary>
    public void SetLiveMiniTimerEnabled(bool enabled)
    {
        IsLiveMiniTimerActive = enabled;
#if ANDROID
        try
        {
            MainThread.BeginInvokeOnMainThread(() => UpdateAndroidPipParams(enabled));
        }
        catch
        {
            // Ignore when MainThread is unavailable in unit tests
        }
#endif
    }

#if ANDROID
    public static Android.App.PictureInPictureParams? BuildPipParams(bool autoEnterEnabled)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            return null;
        }

        var builder = new Android.App.PictureInPictureParams.Builder();
        builder.SetAspectRatio(new Android.Util.Rational(19, 8));

        if (OperatingSystem.IsAndroidVersionAtLeast(31))
        {
            builder.SetAutoEnterEnabled(autoEnterEnabled);
            builder.SetSeamlessResizeEnabled(true);
        }

        return builder.Build();
    }

    private static void UpdateAndroidPipParams(bool enabled)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            return;
        }

        try
        {
            var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
            if (activity != null)
            {
                var pipParams = BuildPipParams(enabled);
                if (pipParams != null)
                {
                    activity.SetPictureInPictureParams(pipParams);
                }
            }
        }
        catch
        {
            // Ignore if Picture-in-Picture is unsupported on this device
        }
    }

    public static void TryEnterMiniTimerPip(Android.App.Activity activity)
    {
        if (!IsLiveMiniTimerActive || !OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            return;
        }

        try
        {
            if (!activity.IsInPictureInPictureMode)
            {
                var pipParams = BuildPipParams(true);
                if (pipParams != null)
                {
                    activity.EnterPictureInPictureMode(pipParams);
                }
            }
        }
        catch
        {
            // Ignore if Picture-in-Picture is unsupported or unavailable
        }
    }
#endif

    public static void NotifyPipModeChanged(bool isInPipMode)
    {
        if (IsInPipMode == isInPipMode)
        {
            return;
        }

        IsInPipMode = isInPipMode;
        try
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                PipModeChanged?.Invoke(isInPipMode);
            });
        }
        catch
        {
            PipModeChanged?.Invoke(isInPipMode);
        }
    }

    /// <summary>
    /// Prevents or allows the device screen from dimming/sleeping during live warmup sessions.
    /// Equivalent to <c>Utilities.PreventMonitorPowerdown</c> / <c>AllowMonitorPowerdown</c>.
    /// </summary>
    public void SetKeepScreenOn(bool keepOn)
    {
        try
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    DeviceDisplay.Current.KeepScreenOn = keepOn;
                }
                catch
                {
                    // Ignore when running in unit test / headless environment
                }
            });
        }
        catch
        {
            // Ignore when MainThread is unavailable in unit tests
        }
    }

    /// <summary>
    /// Plays a 750Hz, 600ms alert tone (matching <c>Console.Beep(750, 600)</c> in WinForms)
    /// along with mobile haptic feedback when a warmup step transitions.
    /// </summary>
    public void PlayStepTransitionBeep()
    {
        try
        {
            if (HapticFeedback.Default.IsSupported)
            {
                HapticFeedback.Default.Perform(HapticFeedbackType.LongPress);
            }
        }
        catch
        {
            // Ignore if haptics are unsupported on tablet hardware
        }

#if ANDROID
        _ = Task.Run(async () =>
        {
            try
            {
                using var toneGen = new Android.Media.ToneGenerator(Android.Media.Stream.Alarm, 100);
                toneGen.StartTone(Android.Media.Tone.PropBeep, 600);
                await Task.Delay(650);
            }
            catch
            {
                // Ignore audio playback errors if muted
            }
        });
#elif IOS
        _ = Task.Run(async () =>
        {
            try
            {
                byte[] wavBytes = GenerateSineWaveWav(frequencyHz: 750, durationMs: 600);
                using var data = Foundation.NSData.FromArray(wavBytes);
                using var player = AVFoundation.AVAudioPlayer.FromData(data);
                player?.PrepareToPlay();
                player?.Play();
                await Task.Delay(650);
            }
            catch
            {
                try
                {
                    AudioToolbox.SystemSound.Vibrate.PlayAlertSound();
                }
                catch
                {
                }
            }
        });
#elif WINDOWS
        _ = Task.Run(() =>
        {
            try
            {
                Console.Beep(750, 600);
            }
            catch
            {
                // Ignore if no audio output device is connected
            }
        });
#endif
    }

    /// <summary>
    /// Synthesizes a 16-bit mono PCM WAV buffer in memory for the 750Hz, 600ms step transition tone.
    /// </summary>
    public static byte[] GenerateSineWaveWav(int frequencyHz = 750, int durationMs = 600, int sampleRate = 22050)
    {
        int numSamples = (sampleRate * durationMs) / 1000;
        int dataChunkSize = numSamples * sizeof(short);
        using var ms = new MemoryStream(44 + dataChunkSize);
        using var writer = new BinaryWriter(ms);

        // RIFF WAVE header
        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + dataChunkSize);
        writer.Write("WAVE"u8.ToArray());
        writer.Write("fmt "u8.ToArray());
        writer.Write(16); // PCM chunk size
        writer.Write((short)1); // AudioFormat = 1 (PCM)
        writer.Write((short)1); // NumChannels = 1 (Mono)
        writer.Write(sampleRate);
        writer.Write(sampleRate * sizeof(short)); // ByteRate
        writer.Write((short)sizeof(short)); // BlockAlign
        writer.Write((short)16); // BitsPerSample
        writer.Write("data"u8.ToArray());
        writer.Write(dataChunkSize);

        double angularFreq = 2.0 * Math.PI * frequencyHz / sampleRate;
        int fadeSamples = Math.Min(numSamples / 10, sampleRate / 50); // Smooth attack/release envelope

        for (int i = 0; i < numSamples; i++)
        {
            double envelope = 1.0;
            if (i < fadeSamples)
            {
                envelope = (double)i / fadeSamples;
            }
            else if (i > numSamples - fadeSamples)
            {
                envelope = (double)(numSamples - i) / fadeSamples;
            }

            short sample = (short)(Math.Sin(i * angularFreq) * short.MaxValue * 0.65 * envelope);
            writer.Write(sample);
        }

        return ms.ToArray();
    }

    /// <summary>
    /// Asynchronously checks if a newer version of the app is available.
    /// Fails silently and quickly (4-second timeout) when the device is offline in a competition warmup venue.
    /// Returns the newer version string (e.g. "2.0.17") if an update is available, or <c>null</c> otherwise.
    /// </summary>
    public async Task<string?> CheckForUpdateAsync(string currentVersion)
    {
        if (string.IsNullOrWhiteSpace(currentVersion))
        {
            return null;
        }

        bool isIos = false;
        bool isAndroid = false;
        try
        {
            var platform = DeviceInfo.Current.Platform;
            isIos = platform == DevicePlatform.iOS || platform == DevicePlatform.MacCatalyst;
            isAndroid = platform == DevicePlatform.Android;
        }
        catch
        {
            // Default to general version field in headless/unit test environments
        }

        // 1. Check lightweight GitHub version.json manifest (works across Android & iOS)
        try
        {
            string url = $"{VersionManifestUrl}?t={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
            using var response = await UpdateHttpClient.GetAsync(url).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (TryExtractLatestVersionFromManifest(
                    json,
                    isAndroid,
                    isIos,
                    out string? remoteVersion,
                    out string? androidStoreUrl,
                    out string? iosStoreUrl))
                {
                    if (!string.IsNullOrWhiteSpace(androidStoreUrl))
                    {
                        _discoveredStoreUrlAndroid = androidStoreUrl;
                    }

                    if (!string.IsNullOrWhiteSpace(iosStoreUrl))
                    {
                        _discoveredStoreUrlIos = iosStoreUrl;
                    }

                    if (IsNewerVersion(currentVersion, remoteVersion))
                    {
                        return remoteVersion!.Trim().TrimStart('v', 'V');
                    }
                }
            }
        }
        catch
        {
            // Ignore network/offline errors
        }

        // 2. On iOS, also check Apple's public iTunes Lookup API by bundleId as a fallback
        if (isIos)
        {
            try
            {
                string packageId = DefaultPackageId;
                try
                {
                    if (!string.IsNullOrWhiteSpace(AppInfo.Current.PackageName))
                    {
                        packageId = AppInfo.Current.PackageName;
                    }
                }
                catch
                {
                    // Use default packageId
                }

                string lookupUrl = $"https://itunes.apple.com/lookup?bundleId={Uri.EscapeDataString(packageId)}&country=au&t={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
                using var response = await UpdateHttpClient.GetAsync(lookupUrl).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("results", out var results) &&
                        results.ValueKind == JsonValueKind.Array &&
                        results.GetArrayLength() > 0)
                    {
                        var first = results[0];
                        if (first.TryGetProperty("trackViewUrl", out var trackUrlProp) &&
                            trackUrlProp.ValueKind == JsonValueKind.String)
                        {
                            _discoveredStoreUrlIos = trackUrlProp.GetString();
                        }

                        if (first.TryGetProperty("version", out var verProp) &&
                            verProp.ValueKind == JsonValueKind.String)
                        {
                            string? storeVersion = verProp.GetString();
                            if (IsNewerVersion(currentVersion, storeVersion))
                            {
                                return storeVersion!.Trim().TrimStart('v', 'V');
                            }
                        }
                    }
                }
            }
            catch
            {
                // Ignore network/offline errors
            }
        }

        return null;
    }

    /// <summary>
    /// Opens the platform-specific app store listing so the user can update the app.
    /// </summary>
    public async Task OpenStoreUpdateAsync()
    {
        string packageId = DefaultPackageId;
        try
        {
            if (!string.IsNullOrWhiteSpace(AppInfo.Current.PackageName))
            {
                packageId = AppInfo.Current.PackageName;
            }
        }
        catch
        {
            // Fallback to default package ID
        }

        try
        {
#if ANDROID
            try
            {
                await Launcher.Default.OpenAsync($"market://details?id={packageId}");
                return;
            }
            catch
            {
                string fallbackUrl = !string.IsNullOrWhiteSpace(_discoveredStoreUrlAndroid)
                    ? _discoveredStoreUrlAndroid!
                    : $"https://play.google.com/store/apps/details?id={packageId}";
                await Launcher.Default.OpenAsync(fallbackUrl);
            }
#elif IOS || MACCATALYST
            if (!string.IsNullOrWhiteSpace(_discoveredStoreUrlIos))
            {
                await Launcher.Default.OpenAsync(_discoveredStoreUrlIos!);
                return;
            }

            if (!string.IsNullOrWhiteSpace(HelpAboutModalPage.AppleAppStoreId))
            {
                await Launcher.Default.OpenAsync($"https://apps.apple.com/app/id{HelpAboutModalPage.AppleAppStoreId}");
                return;
            }

            await Launcher.Default.OpenAsync("https://apps.apple.com/");
#else
            if (DeviceInfo.Current.Platform == DevicePlatform.Android)
            {
                await Launcher.Default.OpenAsync(!string.IsNullOrWhiteSpace(_discoveredStoreUrlAndroid)
                    ? _discoveredStoreUrlAndroid!
                    : DefaultPlayStoreUrl);
            }
            else
            {
                await Launcher.Default.OpenAsync("https://github.com/dyaballikl/Weightlifting_Comp_Warmup_Mobile/releases");
            }
#endif
        }
        catch
        {
            // Ignore if store/browser launcher is unavailable
        }
    }

    /// <summary>
    /// Parses the <c>version.json</c> manifest payload and extracts the target platform's latest version and store URLs.
    /// </summary>
    public static bool TryExtractLatestVersionFromManifest(
        string? json,
        bool isAndroid,
        bool isIos,
        out string? remoteVersion,
        out string? androidStoreUrl,
        out string? iosStoreUrl)
    {
        remoteVersion = null;
        androidStoreUrl = null;
        iosStoreUrl = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            string? generalLatest = GetOptionalString(root, "latestVersion");
            string? androidLatest = GetOptionalString(root, "androidLatestVersion");
            string? iosLatest = GetOptionalString(root, "iosLatestVersion");

            androidStoreUrl = GetOptionalString(root, "storeUrlAndroid");
            iosStoreUrl = GetOptionalString(root, "storeUrlIos");

            if (isAndroid && !string.IsNullOrWhiteSpace(androidLatest))
            {
                remoteVersion = androidLatest;
            }
            else if (isIos && !string.IsNullOrWhiteSpace(iosLatest))
            {
                remoteVersion = iosLatest;
            }
            else
            {
                remoteVersion = generalLatest ?? androidLatest ?? iosLatest;
            }

            return !string.IsNullOrWhiteSpace(remoteVersion);
        }
        catch
        {
            return false;
        }
    }

    private static string? GetOptionalString(JsonElement root, string propertyName)
    {
        if (root.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            return prop.GetString();
        }

        return null;
    }

    /// <summary>
    /// Compares two semantic version strings (e.g. "2.0.15" vs "2.0.16") and returns true
    /// only when <paramref name="remoteVersion"/> is strictly newer than <paramref name="currentVersion"/>.
    /// </summary>
    public static bool IsNewerVersion(string? currentVersion, string? remoteVersion)
    {
        if (string.IsNullOrWhiteSpace(currentVersion) || string.IsNullOrWhiteSpace(remoteVersion))
        {
            return false;
        }

        string cleanCurrent = currentVersion.Trim().TrimStart('v', 'V').Split('-', '+')[0];
        string cleanRemote = remoteVersion.Trim().TrimStart('v', 'V').Split('-', '+')[0];

        if (Version.TryParse(NormalizeVersionComponents(cleanCurrent), out var currentVer) &&
            Version.TryParse(NormalizeVersionComponents(cleanRemote), out var remoteVer))
        {
            return remoteVer > currentVer;
        }

        return false;
    }

    private static string NormalizeVersionComponents(string versionStr)
    {
        var parts = versionStr.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            1 => $"{parts[0]}.0.0",
            2 => $"{parts[0]}.{parts[1]}.0",
            _ => versionStr
        };
    }
}
