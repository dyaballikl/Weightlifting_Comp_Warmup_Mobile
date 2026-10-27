#if IOS
using System.Runtime.InteropServices;
using AVFoundation;
using AVKit;
using CoreFoundation;
using CoreGraphics;
using CoreMedia;
using CoreVideo;
using Foundation;
using UIKit;
using WeightliftingCompWarmup.Core.Services;
using WeightliftingCompWarmup.Maui.Services;

namespace WeightliftingCompWarmup.Maui.Platforms.iOS;

/// <summary>
/// Manages the iOS Picture-in-Picture (PiP) floating mini-timer using <see cref="AVPictureInPictureController"/>
/// and an <see cref="AVSampleBufferDisplayLayer"/> that renders the active warmup step description,
/// countdown ("xhxmxs"), and progress bar once per second.
/// </summary>
public sealed class IosPipMiniTimerManager : NSObject, IAVPictureInPictureControllerDelegate, IAVPictureInPictureSampleBufferPlaybackDelegate
{
    private const int FrameWidth = 380;
    private const int FrameHeight = 160;

    public static IosPipMiniTimerManager Shared { get; } = new();

    private UIView? _hostView;
    private AVSampleBufferDisplayLayer? _sampleBufferLayer;
    private AVPictureInPictureController? _pipController;
    private AVAudioPlayer? _silentKeepAlivePlayer;
    private bool _isEnabled;
    private string _lastStepDescription = "Wait to start";
    private string _lastRemainingText = "0s";
    private double _lastProgress;
    private int _lastLiftWeightKg;
    private int _lastBarWeightKg = 20;

    private IosPipMiniTimerManager()
    {
    }

    public void SetEnabled(bool enabled)
    {
        if (!OperatingSystem.IsIOSVersionAtLeast(15, 0) || !AVPictureInPictureController.IsPictureInPictureSupported)
        {
            return;
        }

        _isEnabled = enabled;

        if (enabled)
        {
            StartAudioSessionAndKeepAlive();
            EnsurePipControllerInitialized();
            if (_pipController != null)
            {
                if (OperatingSystem.IsIOSVersionAtLeast(14, 2))
                {
                    _pipController.CanStartPictureInPictureAutomaticallyFromInline = true;
                }
            }

            RenderAndEnqueueFrame(_lastStepDescription, _lastRemainingText, _lastProgress);
        }
        else
        {
            if (_pipController != null)
            {
                if (OperatingSystem.IsIOSVersionAtLeast(14, 2))
                {
                    _pipController.CanStartPictureInPictureAutomaticallyFromInline = false;
                }

                if (_pipController.PictureInPictureActive)
                {
                    _pipController.StopPictureInPicture();
                }
            }

            StopKeepAliveAudio();
            DevicePlatformService.NotifyPipModeChanged(false);
        }
    }

    public void UpdateFrame(string stepDescription, string remainingText, double progress, int liftWeightKg = 0, int barWeightKg = 20)
    {
        _lastStepDescription = string.IsNullOrWhiteSpace(stepDescription) ? "Wait to start" : stepDescription;
        _lastRemainingText = string.IsNullOrWhiteSpace(remainingText) ? "0s" : remainingText;
        _lastProgress = Math.Clamp(progress, 0.0, 1.0);
        _lastLiftWeightKg = Math.Max(0, liftWeightKg);
        _lastBarWeightKg = barWeightKg > 0 ? barWeightKg : 20;

        if (!_isEnabled)
        {
            return;
        }

        try
        {
            UIApplication.SharedApplication.InvokeOnMainThread(() =>
            {
                if (_isEnabled)
                {
                    EnsurePipControllerInitialized();
                    RenderAndEnqueueFrame(_lastStepDescription, _lastRemainingText, _lastProgress);
                }
            });
        }
        catch
        {
            // Ignore if main thread is unavailable
        }
    }

    private void StartAudioSessionAndKeepAlive()
    {
        try
        {
            var session = AVAudioSession.SharedInstance();
            session.SetCategory(AVAudioSessionCategory.Playback, AVAudioSessionCategoryOptions.MixWithOthers, out _);
            session.SetActive(true, out _);

            if (_silentKeepAlivePlayer == null)
            {
                // 0Hz generates a completely silent PCM WAV buffer so the iOS runloop stays active while in PiP
                byte[] silentWav = DevicePlatformService.GenerateSineWaveWav(frequencyHz: 0, durationMs: 1000);
                var data = NSData.FromArray(silentWav);
                _silentKeepAlivePlayer = AVAudioPlayer.FromData(data, out _);
                if (_silentKeepAlivePlayer != null)
                {
                    _silentKeepAlivePlayer.NumberOfLoops = -1;
                    _silentKeepAlivePlayer.Volume = 0.0f;
                    _silentKeepAlivePlayer.PrepareToPlay();
                }
            }

            if (_silentKeepAlivePlayer != null && !_silentKeepAlivePlayer.Playing)
            {
                _silentKeepAlivePlayer.Play();
            }
        }
        catch
        {
            // Ignore audio session errors on simulator
        }
    }

    private void StopKeepAliveAudio()
    {
        try
        {
            if (_silentKeepAlivePlayer != null && _silentKeepAlivePlayer.Playing)
            {
                _silentKeepAlivePlayer.Stop();
            }
        }
        catch
        {
            // Ignore
        }
    }

    private void EnsurePipControllerInitialized()
    {
        if (_pipController != null && _sampleBufferLayer != null && _hostView?.Window != null)
        {
            return;
        }

        var keyWindow = GetActiveKeyWindow();
        if (keyWindow == null)
        {
            return;
        }

        if (_hostView == null)
        {
            _hostView = new UIView(new CGRect(0, 0, 1, 1))
            {
                Alpha = 0.01f,
                UserInteractionEnabled = false,
                ClipsToBounds = true
            };
        }

        if (_hostView.Window == null)
        {
            keyWindow.AddSubview(_hostView);
        }

        if (_sampleBufferLayer == null)
        {
            _sampleBufferLayer = new AVSampleBufferDisplayLayer
            {
                Frame = new CGRect(0, 0, FrameWidth, FrameHeight),
                VideoGravity = "AVLayerVideoGravityResizeAspect"
            };
            _hostView.Layer.AddSublayer(_sampleBufferLayer);
        }

        RenderAndEnqueueFrame(_lastStepDescription, _lastRemainingText, _lastProgress);

        if (_pipController == null && OperatingSystem.IsIOSVersionAtLeast(15, 0))
        {
            var contentSource = new AVPictureInPictureControllerContentSource(_sampleBufferLayer, this);
            _pipController = new AVPictureInPictureController(contentSource)
            {
                Delegate = this,
                RequiresLinearPlayback = true
            };

            if (OperatingSystem.IsIOSVersionAtLeast(14, 2))
            {
                _pipController.CanStartPictureInPictureAutomaticallyFromInline = _isEnabled;
            }
        }
    }

    private static UIWindow? GetActiveKeyWindow()
    {
        try
        {
            foreach (var scene in UIApplication.SharedApplication.ConnectedScenes)
            {
                if (scene is UIWindowScene windowScene)
                {
                    foreach (var window in windowScene.Windows)
                    {
                        if (window.IsKeyWindow)
                        {
                            return window;
                        }
                    }

                    if (windowScene.Windows.Length > 0)
                    {
                        return windowScene.Windows[0];
                    }
                }
            }
        }
        catch
        {
            // Ignore
        }

        return null;
    }

    private void RenderAndEnqueueFrame(string stepDescription, string remainingText, double progress)
    {
        if (_sampleBufferLayer == null)
        {
            return;
        }

        try
        {
#pragma warning disable CA1422
            if (_sampleBufferLayer.Status == AVQueuedSampleBufferRenderingStatus.Failed)
            {
                _sampleBufferLayer.Flush();
            }
#pragma warning restore CA1422

            var attrs = new CVPixelBufferAttributes
            {
                AllocateWithIOSurface = true,
                CGImageCompatibility = true,
                CGBitmapContextCompatibility = true
            };

            using var pixelBuffer = new CVPixelBuffer(
                FrameWidth,
                FrameHeight,
                CVPixelFormatType.CV32BGRA,
                attrs);

            pixelBuffer.Lock(CVPixelBufferLock.None);
            try
            {
                IntPtr baseAddress = pixelBuffer.BaseAddress;
                int bytesPerRow = (int)pixelBuffer.BytesPerRow;
                using var colorSpace = CGColorSpace.CreateDeviceRGB();
                using var context = new CGBitmapContext(
                    baseAddress,
                    FrameWidth,
                    FrameHeight,
                    8,
                    bytesPerRow,
                    colorSpace,
                    CGBitmapFlags.ByteOrder32Little | CGBitmapFlags.PremultipliedFirst);

                // Flip coordinate system to top-left origin for UIKit text drawing
                context.TranslateCTM(0, FrameHeight);
                context.ScaleCTM(1.0f, -1.0f);

                UIGraphics.PushContext(context);
                try
                {
                    // 1. Dark slate background (#0F172A)
                    UIColor.FromRGB(15, 23, 42).SetFill();
                    context.FillRect(new CGRect(0, 0, FrameWidth, FrameHeight));

                    // 2. Bottom progress bar track (#1E293B) and active green fill (#16A34A), inset from corners
                    nfloat barInsetX = 24f;
                    nfloat barHeight = 6f;
                    nfloat barY = FrameHeight - barHeight - 6f;
                    nfloat barTrackWidth = FrameWidth - (barInsetX * 2f);
                    UIColor.FromRGB(30, 41, 59).SetFill();
                    context.FillRect(new CGRect(barInsetX, barY, barTrackWidth, barHeight));

                    nfloat fillWidth = (nfloat)(barTrackWidth * Math.Clamp(progress, 0.0, 1.0));
                    if (fillWidth > 0)
                    {
                        UIColor.FromRGB(22, 163, 74).SetFill();
                        context.FillRect(new CGRect(barInsetX, barY, fillWidth, barHeight));
                    }

                    // 3. Simplified plate rectangles on the right when a lift step has loaded plates
                    nfloat platesTotalWidth = 0f;
                    var loadout = (_lastLiftWeightKg > _lastBarWeightKg)
                        ? BarbellLoadCalculator.Calculate(_lastLiftWeightKg, _lastBarWeightKg, isOpener: false)
                        : null;
                    var sleeveElements = loadout?.SleeveElements.Where(e => !e.IsCollar).ToList();
                    if (sleeveElements != null && sleeveElements.Count > 0)
                    {
                        foreach (var plate in sleeveElements)
                        {
                            bool isBigPlate = plate.WeightKg >= 10.0m || plate.HeightRatio >= 0.99;
                            platesTotalWidth += (isBigPlate ? 16f : 8f) + 3f;
                        }
                    }

                    nfloat textAreaWidth = platesTotalWidth > 0
                        ? FrameWidth - platesTotalWidth - 36f
                        : FrameWidth - 28f;

                    // 4. Step Description (Top line, bold white #F8FAFC)
                    var paragraphStyle = new NSMutableParagraphStyle
                    {
                        Alignment = platesTotalWidth > 0 ? UITextAlignment.Left : UITextAlignment.Center,
                        LineBreakMode = UILineBreakMode.TailTruncation
                    };

                    var stepAttrs = new UIStringAttributes
                    {
                        Font = UIFont.BoldSystemFontOfSize(26f),
                        ForegroundColor = UIColor.FromRGB(248, 250, 252),
                        ParagraphStyle = paragraphStyle
                    };

                    using var nsStep = new NSString(stepDescription);
                    nsStep.DrawString(new CGRect(18, 24, textAreaWidth, 36), stepAttrs);

                    // 5. Remaining Time ("xhxmxs", large bold gold #FACC15)
                    var timeAttrs = new UIStringAttributes
                    {
                        Font = UIFont.BoldSystemFontOfSize(50f),
                        ForegroundColor = UIColor.FromRGB(250, 204, 21),
                        ParagraphStyle = paragraphStyle
                    };

                    using var nsTime = new NSString(remainingText);
                    nsTime.DrawString(new CGRect(18, 66, textAreaWidth, 66), timeAttrs);

                    // Draw plate stack to the right of the text
                    if (platesTotalWidth > 0 && sleeveElements != null)
                    {
                        nfloat plateX = FrameWidth - platesTotalWidth - 16f;
                        nfloat centerY = 76f;
                        foreach (var plate in sleeveElements)
                        {
                            bool isBigPlate = plate.WeightKg >= 10.0m || plate.HeightRatio >= 0.99;
                            nfloat w = isBigPlate ? 16f : 8f;
                            nfloat h = isBigPlate ? 56f : 38f;
                            nfloat y = centerY - (h / 2f);

                            ParseHexColor(plate.HexColor).SetFill();
                            var rect = new CGRect(plateX, y, w, h);
                            var path = UIBezierPath.FromRoundedRect(rect, 3f);
                            path.Fill();

                            plateX += w + 3f;
                        }
                    }
                }
                finally
                {
                    UIGraphics.PopContext();
                }
            }
            finally
            {
                pixelBuffer.Unlock(CVPixelBufferLock.None);
            }

            using var formatDesc = CMVideoFormatDescription.CreateForImageBuffer(pixelBuffer, out var formatError);
            if (formatDesc == null || formatError != CMFormatDescriptionError.None)
            {
                return;
            }

            var timing = new CMSampleTimingInfo
            {
                PresentationTimeStamp = CMClock.HostTimeClock.Time,
                Duration = new CMTime(1, 1),
                DecodeTimeStamp = CMTime.Invalid
            };

            using var sampleBuffer = CMSampleBuffer.CreateReadyWithImageBuffer(
                pixelBuffer,
                formatDesc,
                ref timing,
                out var sampleError);

            if (sampleBuffer != null && sampleError == CMSampleBufferError.None)
            {
#pragma warning disable CA1422
                _sampleBufferLayer.Flush();
                _sampleBufferLayer.Enqueue(sampleBuffer);
#pragma warning restore CA1422
            }
        }
        catch
        {
            // Ignore frame rendering errors
        }
    }

    // =========================================================================
    // IAVPictureInPictureControllerDelegate
    // =========================================================================
    [Export("pictureInPictureControllerWillStartPictureInPicture:")]
    public void WillStartPictureInPicture(AVPictureInPictureController pictureInPictureController)
    {
        DevicePlatformService.NotifyPipModeChanged(true);
    }

    [Export("pictureInPictureControllerDidStopPictureInPicture:")]
    public void DidStopPictureInPicture(AVPictureInPictureController pictureInPictureController)
    {
        DevicePlatformService.NotifyPipModeChanged(false);
    }

    // =========================================================================
    // IAVPictureInPictureSampleBufferPlaybackDelegate
    // =========================================================================
    [Export("pictureInPictureController:setPlaying:")]
    public void SetPlaying(AVPictureInPictureController pictureInPictureController, bool playing)
    {
    }

    [Export("pictureInPictureControllerTimeRangeForPlayback:")]
    public CMTimeRange GetTimeRange(AVPictureInPictureController pictureInPictureController)
    {
        return new CMTimeRange
        {
            Start = CMTime.Zero,
            Duration = CMTime.PositiveInfinity
        };
    }

    [Export("pictureInPictureControllerIsPlaybackPaused:")]
    public bool IsPlaybackPaused(AVPictureInPictureController pictureInPictureController)
    {
        return false;
    }

    [Export("pictureInPictureController:didTransitionToRenderSize:")]
    public void DidTransitionToRenderSize(AVPictureInPictureController pictureInPictureController, CMVideoDimensions newRenderSize)
    {
        RenderAndEnqueueFrame(_lastStepDescription, _lastRemainingText, _lastProgress);
    }

    [Export("pictureInPictureController:skipByInterval:completionHandler:")]
    public void SkipByInterval(AVPictureInPictureController pictureInPictureController, CMTime skipInterval, Action completionHandler)
    {
        completionHandler?.Invoke();
    }

    private static UIColor ParseHexColor(string? hex)
    {
        if (!string.IsNullOrWhiteSpace(hex) && hex.StartsWith('#') && hex.Length == 7 &&
            byte.TryParse(hex.AsSpan(1, 2), System.Globalization.NumberStyles.HexNumber, null, out byte r) &&
            byte.TryParse(hex.AsSpan(3, 2), System.Globalization.NumberStyles.HexNumber, null, out byte g) &&
            byte.TryParse(hex.AsSpan(5, 2), System.Globalization.NumberStyles.HexNumber, null, out byte b))
        {
            return UIColor.FromRGB(r, g, b);
        }

        return UIColor.White;
    }
}
#endif
