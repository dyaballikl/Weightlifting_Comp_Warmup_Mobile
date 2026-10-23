using Microsoft.Maui.Devices;

namespace WeightliftingCompWarmup.Maui.Services;

/// <summary>
/// Provides cross-platform mobile hardware integration for Android (Phone &amp; Tablet) and iOS (iPhone &amp; iPad):
/// - Keeping the screen awake during a live competition warmup (<c>DeviceDisplay.KeepScreenOn</c>)
/// - Playing the 750Hz 600ms step transition beep &amp; haptic feedback
/// </summary>
public class DevicePlatformService
{
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
}
