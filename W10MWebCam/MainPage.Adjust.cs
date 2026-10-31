using System;
using System.Threading.Tasks;
using Windows.UI.Xaml;

namespace W10MWebCam
{
    /// <summary>
    /// Adjusting the camera on the phone (as in W10MDashcam): switching automatic focus/exposure/white balance off, or
    /// touching a manual control, lifts the black screen and shows the camera picture. 60 s after the last touch the
    /// picture ends and the screen goes black again.
    ///
    /// The preview cannot start or stop while a recording runs (that stalls the 4K pipeline and can end the app), so
    /// while streaming each change costs one quick stream restart: one to start the picture, one to end it. OBS
    /// reconnects by itself. Changes made on the PC settings page never do this.
    /// </summary>
    public sealed partial class MainPage
    {
        private const int AdjustSeconds = 60;
        private bool _phoneAdjusting;
        private DispatcherTimer _adjustTimer;

        /// <summary>Whether the preview should run during a stream: while adjusting, or when chosen and the screen isn't blacked out.</summary>
        private bool PreviewWanted => _phoneAdjusting || (PreviewToggle.IsOn && !PowerSaveToggle.IsOn);

        /// <summary>Whether the preview should run while stopped.</summary>
        private bool IdlePreviewWanted => _phoneAdjusting || PreviewToggle.IsOn;

        /// <summary>A camera control was touched on the phone: show the picture, and keep it for 60 s after the last touch.</summary>
        private void TouchPhoneAdjust() { var _ = TouchPhoneAdjustAsync(); }

        private async Task TouchPhoneAdjustAsync()
        {
            if (_adjustTimer == null)
            {
                _adjustTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(AdjustSeconds) };
                _adjustTimer.Tick += AdjustTimer_Tick;
            }
            _adjustTimer.Stop();
            _adjustTimer.Start();
            if (!_phoneAdjusting)
            {
                _phoneAdjusting = true;
                Log("Adjusting the camera on the phone: picture on until " + AdjustSeconds + " s after the last touch");
            }
            // lift the black screen while adjusting
            _wakeTimer.Stop();
            if (BlackoutOverlay.Visibility == Visibility.Visible)
            {
                BlackoutOverlay.Visibility = Visibility.Collapsed;
                if (_brightness != null && _brightness.IsSupported) _brightness.StopOverride();
            }
            await MatchPreviewAsync("to show the camera picture");
        }

        /// <summary>Keeps the timer going while the person is still adjusting (does not start adjusting by itself).</summary>
        private void KeepAdjusting()
        {
            if (_phoneAdjusting) TouchPhoneAdjust();
        }

        private async void AdjustTimer_Tick(object sender, object e)
        {
            _adjustTimer.Stop();
            if (!_phoneAdjusting) return;
            if (_busy) { _adjustTimer.Start(); return; } // a start/stop is running: try again a minute later
            _phoneAdjusting = false;
            Log("No camera adjustments for " + AdjustSeconds + " s: ending the picture");
            await MatchPreviewAsync("to end the camera picture (power saving)");
            if (_blackedOut && UnplugPanel.Visibility != Visibility.Visible)
            {
                BlackoutOverlay.Visibility = Visibility.Visible;
                if (_brightness != null && _brightness.IsSupported)
                {
                    _brightness.SetBrightnessLevel(0.0, Windows.Graphics.Display.DisplayBrightnessOverrideOptions.None);
                    _brightness.StartOverride();
                }
            }
            UpdatePreviewNote();
        }

        /// <summary>
        /// Brings the preview in line with what is wanted. While stopped it simply starts or stops; while streaming it
        /// needs one stream restart (the start sequence then sets the preview before recording begins).
        /// </summary>
        private async Task MatchPreviewAsync(string why)
        {
            try
            {
                if (_camera == null) return;
                if (!_camera.IsStreaming)
                {
                    if (!_busy) await _camera.SetPreviewAsync(IdlePreviewWanted);
                }
                else if (_camera.Previewing != PreviewWanted && !_busy)
                {
                    Log("Restarting the stream once " + why + " (the preview can't change during a recording)");
                    await RestartStreamAsync();
                }
                UpdatePreviewNote();
            }
            catch (Exception ex) { Log("Camera picture: " + ex.GetType().Name + ": " + ex.Message); }
        }

        private void EndPhoneAdjust()
        {
            _phoneAdjusting = false;
            _adjustTimer?.Stop();
        }
    }
}
