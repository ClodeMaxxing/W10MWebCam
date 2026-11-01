using System;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace W10MWebCam
{
    /// <summary>
    /// Camera controls (ported from W10MSecurityCam): automatic or manual focus, exposure and white balance, focus once
    /// and lock, the flashlight, and the power-saving camera features. The phone shows the essentials; the sliders are
    /// on the PC settings page (http://127.0.0.1:8020/settings).
    /// </summary>
    public sealed partial class MainPage
    {
        private bool _torchWanted;
        private DateTime _torchOffAt = DateTime.MaxValue;
        private const int TorchSeconds = 30; // the LED heats the phone fast and cannot be dimmed on the 950
        private int _cameraReadTick;
        private bool _holdBusy;
        private DateTime _nextHoldAt = DateTime.MaxValue;

        private void LoadCameraSettings()
        {
            _tuning.FocusAuto = GetBool("CamFocusAuto", true);
            _tuning.ExposureAuto = GetBool("CamExposureAuto", true);
            _tuning.WbAuto = GetBool("CamWbAuto", true);
            if (_settings.Values["CamFocus"] is double fp) _tuning.FocusPercent = fp;
            if (_settings.Values["CamExposureMs"] is double ms) _tuning.ExposureMs = ms;
            if (_settings.Values["CamWb"] is int wb && wb > 0) _tuning.WhiteBalanceKelvin = (uint)wb;
            _tuning.SaveOis = GetBool("SaveOis", false);
            _tuning.SaveFocus = GetBool("SaveFocus", false);
            // Crash guards (from W10MSecurityCam / W10MDashcam): if the app died while applying the camera settings last
            // time, a saved manual value may be the cause, so go back to automatic instead of crashing again; the same for
            // the stabilization setting.
            if (GetBool("TuningInProgress", false))
            {
                _settings.Values["TuningInProgress"] = false;
                _tuning.Auto = true;
                Log("Camera settings reset to automatic: the app closed while applying them last time.");
            }
            if (GetBool("PowerSaveInProgress", false))
            {
                _settings.Values["PowerSaveInProgress"] = false;
                _tuning.SaveOis = false;
                Log("Stabilization setting reset: the app closed while applying it last time.");
            }
            SaveCameraSettings();
            CameraAutoToggle.IsOn = _tuning.Auto;
            SaveOisToggle.IsOn = _tuning.SaveOis;
            SaveOisToggle.IsEnabled = _tuning.OisSupported;
            SaveFocusToggle.IsOn = _tuning.SaveFocus;
            if (OisToggle != null) { OisToggle.IsOn = !_tuning.SaveOis; OisToggle.IsEnabled = SaveOisToggle.IsEnabled; } // Image stabilization on = SaveOis off
            FocusOnceButton.IsEnabled = _tuning.FocusSupported;
            UpdateCameraUi();
        }

        private void SaveCameraSettings()
        {
            _settings.Values["CamFocusAuto"] = _tuning.FocusAuto;
            _settings.Values["CamExposureAuto"] = _tuning.ExposureAuto;
            _settings.Values["CamWbAuto"] = _tuning.WbAuto;
            _settings.Values["CamFocus"] = _tuning.FocusPercent;
            _settings.Values["CamExposureMs"] = _tuning.ExposureMs;
            _settings.Values["CamWb"] = (int)_tuning.WhiteBalanceKelvin;
            if (_protect) return; // stabilization and hold focus are forced on meanwhile: the person's own choices stay saved
            _settings.Values["SaveOis"] = _tuning.SaveOis;
            _settings.Values["SaveFocus"] = _tuning.SaveFocus;
        }

        /// <summary>After each stream start: send every camera setting again, then (optionally) park the focus.</summary>
        private async Task AfterStreamStartAsync()
        {
            try
            {
                if (_camera?.Capture == null) return;
                await Task.Delay(2500); // let the first frames flow before touching the camera controls again (W10MSecurityCam / W10MDashcam)
                if (_camera?.Capture == null || !_camera.IsStreaming) return;
                await ApplyTuningAsync(force: true);
                _nextHoldAt = DateTime.Now.AddSeconds(30); // "Hold focus once settled": CameraTick tries it once the picture has settled
            }
            catch (Exception ex) { Log("Camera settings: " + ex.Message); }
        }

        /// <summary>Applies changed camera settings now (focus, exposure and white balance can change while streaming).</summary>
        private async Task ApplyCameraNowAsync()
        {
            // Called from event handlers and timers (async void): nothing may escape, or the app ends.
            try
            {
                SaveCameraSettings();
                await ApplyTuningAsync();
            }
            catch (Exception ex) { Log("Camera settings: " + ex.GetType().Name + ": " + ex.Message); }
        }

        private bool _applyingTuning, _tuningPending, _tuningForce;

        /// <summary>
        /// Sends the camera settings. Changes that arrive while one is being sent are queued, not dropped, and run one
        /// after another (never two at once: repeated driver calls in parallel caused errors in W10MSecurityCam).
        /// force = resend everything (the camera just (re)started).
        /// </summary>
        private async Task ApplyTuningAsync(bool force = false)
        {
            if (_camera?.Capture == null) return;
            if (force) _tuningForce = true;
            if (_applyingTuning) { _tuningPending = true; return; }
            _applyingTuning = true;
            _settings.Values["TuningInProgress"] = true; // cleared when the camera accepted the settings; see the crash guard in LoadCameraSettings
            try
            {
                do
                {
                    _tuningPending = false;
                    bool f = _tuningForce; _tuningForce = false;
                    var cap = _camera?.Capture;
                    if (cap == null) break;
                    await _tuning.ApplyAsync(cap, f);
                } while (_tuningPending);
                _settings.Values["TuningInProgress"] = false;
                UpdateCameraUi();
            }
            finally { _applyingTuning = false; }
        }

        // ---- manual sliders (as in W10MDashcam): shown while at least one of focus / exposure / white balance is manual.

        private DispatcherTimer _tuningTimer;
        private DateTime _sliderTouchedAt = DateTime.MinValue;

        // The exposure slider is logarithmic (0..100 -> shortest..longest shutter time): useful values span 1000x.
        private double ExposureMaxForSlider => Math.Max(_tuning.ExposureMinUseful + 0.001, Math.Min(_tuning.ExposureMaxMs, _tuning.FrameLimitMs));
        private double SliderToExposure(double v) =>
            _tuning.ExposureMinUseful * Math.Pow(ExposureMaxForSlider / _tuning.ExposureMinUseful, v / 100.0);
        private double ExposureToSlider(double ms) =>
            100.0 * Math.Log(Math.Max(ms, _tuning.ExposureMinUseful) / _tuning.ExposureMinUseful) / Math.Log(ExposureMaxForSlider / _tuning.ExposureMinUseful);

        /// <summary>Puts the camera settings (and the camera's ranges) on screen. Also called after changes from the web page.</summary>
        private void UpdateCameraUi()
        {
            bool was = _loading;
            _loading = true;
            try
            {
                CameraAutoToggle.IsOn = _tuning.Auto;
                CameraText.Text = "Now: " + (_tuning.State.Length > 0 ? _tuning.State : _tuning.Mode);
                // the manual panel is always on screen; its sliders are greyed while automatic
                FocusAutoToggle.IsOn = _tuning.FocusAuto;
                ExposureAutoToggle.IsOn = _tuning.ExposureAuto;
                WbAutoToggle.IsOn = _tuning.WbAuto;
                FocusAutoToggle.IsEnabled = _tuning.FocusSupported;
                ExposureAutoToggle.IsEnabled = _tuning.ExposureSupported;
                WbAutoToggle.IsEnabled = _tuning.WhiteBalanceSupported;
                FocusSlider.IsEnabled = _tuning.FocusSupported && !_tuning.FocusAuto;
                ExposureSlider.IsEnabled = _tuning.ExposureSupported && !_tuning.ExposureAuto;
                WbSlider.IsEnabled = _tuning.WhiteBalanceSupported && !_tuning.WbAuto;
                // Don't pull a slider out from under a finger that is still moving it.
                if ((DateTime.Now - _sliderTouchedAt).TotalSeconds > 3)
                {
                    FocusSlider.Value = _tuning.FocusPercent;
                    if (_tuning.ExposureSupported) ExposureSlider.Value = Math.Max(0, Math.Min(100, ExposureToSlider(_tuning.ExposureMs)));
                    if (_tuning.WhiteBalanceSupported)
                    {
                        WbSlider.Minimum = _tuning.WbMin;
                        WbSlider.Maximum = Math.Max(_tuning.WbMin + 1, _tuning.WbMax);
                        WbSlider.Value = Math.Max(_tuning.WbMin, Math.Min(_tuning.WbMax, _tuning.WhiteBalanceKelvin));
                    }
                }
                UpdateTuningLabels();
            }
            finally { _loading = was; }
        }

        /// <summary>The value line of each camera tile: the value (dim while automatic) and a small note beside it.</summary>
        private void UpdateTuningLabels()
        {
            if (FocusNote == null) return;
            var bright = Brush(0xFF, 0xFF, 0xFF); var dim = Brush(0x7F, 0xAE, 0x86);
            string iso = System.Text.RegularExpressions.Regex.Match(_tuning.State ?? "", @"ISO (\d+)").Groups[1].Value;
            string expNow = string.Format("{0:0.##} ms (1/{1:0} s)", _tuning.ExposureMs, 1000.0 / Math.Max(0.001, _tuning.ExposureMs));
            FocusLabel.Text = !_tuning.FocusSupported ? "n/a" : _tuning.FocusAuto ? "auto" : Math.Round(_tuning.FocusPercent).ToString();
            FocusNote.Text = !_tuning.FocusSupported ? "not adjustable" : _tuning.FocusHeld ? "held, lens parked" : _tuning.FocusAuto ? "continuous" : _focusLockedByOnce ? "locked · 0 near, 100 far" : "0 near, 100 far";
            ExposureLabel.Text = !_tuning.ExposureSupported ? "n/a" : _tuning.ExposureAuto ? "auto" : string.Format("{0:0.##} ms", _tuning.ExposureMs);
            ExposureNote.Text = !_tuning.ExposureSupported ? "not adjustable" : (_tuning.ExposureAuto ? expNow : string.Format("1/{0:0} s", 1000.0 / Math.Max(0.001, _tuning.ExposureMs))) + (iso.Length > 0 ? " · ISO " + iso : "") + (_tuning.ExposureAuto ? " now" : "");
            WbLabel.Text = !_tuning.WhiteBalanceSupported ? "n/a" : _tuning.WbAuto ? "auto" : _tuning.WhiteBalanceKelvin + " K";
            WbNote.Text = !_tuning.WhiteBalanceSupported ? "not adjustable" : _tuning.WbAuto ? _tuning.WhiteBalanceKelvin + " K now" : "";
            FocusLabel.Foreground = _tuning.FocusAuto ? dim : bright;
            ExposureLabel.Foreground = _tuning.ExposureAuto ? dim : bright;
            WbLabel.Foreground = _tuning.WbAuto ? dim : bright;
            SetSeg(FocusSegA, FocusSegM, _tuning.FocusAuto);
            SetSeg(ExposureSegA, ExposureSegM, _tuning.ExposureAuto);
            SetSeg(WbSegA, WbSegM, _tuning.WbAuto);
            UpdateFocusOnceRow();
        }

        private async void CameraAutoToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            // Unticking automatic shows the camera picture on the phone for adjusting (MainPage.Adjust.cs).
            if (!CameraAutoToggle.IsOn) TouchPhoneAdjust(); else KeepAdjusting();
            // Switching the main switch off makes all three manual, starting from what the camera is using right now.
            if (!CameraAutoToggle.IsOn) _tuning.SeedFromCamera(_camera?.Capture, _tuning.FocusAuto, _tuning.ExposureAuto, _tuning.WbAuto);
            _tuning.ReleaseFocusHold();
            _tuning.Auto = CameraAutoToggle.IsOn;
            UpdateCameraUi(); // the sliders enable at once, not after the camera has taken the settings
            await ApplyCameraNowAsync();
        }

        private async void SubAuto_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            TouchPhoneAdjust();
            bool on = ((ToggleSwitch)sender).IsOn;
            if (!on) _tuning.SeedFromCamera(_camera?.Capture, sender == FocusAutoToggle, sender == ExposureAutoToggle, sender == WbAutoToggle);
            if (sender == FocusAutoToggle) { _tuning.FocusAuto = on; _tuning.ReleaseFocusHold(); }
            else if (sender == ExposureAutoToggle) _tuning.ExposureAuto = on;
            else if (sender == WbAutoToggle) _tuning.WbAuto = on;
            if (sender == FocusAutoToggle) { _focusLockedByOnce = false; if (on) _tuning.ReleaseFocusHold(); }
            UpdateCameraUi(); // the slider enables at once; all three on Auto switches the main switch on
            await ApplyCameraNowAsync();
        }

        private void TuningSlider_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (_loading || FocusLabel == null) return;
            _sliderTouchedAt = DateTime.Now;
            TouchPhoneAdjust();
            _focusLockedByOnce = false; // a focus set by hand is not the Focus once lock
            if (sender == FocusSlider) _tuning.FocusPercent = FocusSlider.Value;
            else if (sender == ExposureSlider) _tuning.ExposureMs = _tuning.SnapExposureMs(SliderToExposure(ExposureSlider.Value));
            else if (sender == WbSlider) _tuning.WhiteBalanceKelvin = (uint)(Math.Round(WbSlider.Value / 100.0) * 100);
            UpdateTuningLabels();
            if (_tuningTimer == null)
            {
                _tuningTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                _tuningTimer.Tick += async (s, a) => { _tuningTimer.Stop(); await ApplyCameraNowAsync(); };
            }
            _tuningTimer.Stop(); _tuningTimer.Start(); // apply once the slider stops moving
        }

        /// <summary>The Focus once row: off = autofocus once and lock there; on (locked, or held by the power-saving hold) = release to continuous autofocus.</summary>
        private async void FocusOnceButton_Click(object sender, RoutedEventArgs e)
        {
            TouchPhoneAdjust();
            if (FocusOnceOn) { await ReleaseFocusAsync(); return; }
            await FocusOnceAsync();
        }

        private bool _focusLockedByOnce; // the lock came from Focus once (a manual focus set by hand is not "locked")
        private bool FocusOnceOn => _tuning.FocusSupported && (_tuning.FocusHeld || (!_tuning.FocusAuto && _focusLockedByOnce));

        private void UpdateFocusOnceRow()
        {
            if (FocusOnceBar == null) return;
            bool on = FocusOnceOn;
            FocusOnceButton.IsEnabled = _tuning.FocusSupported && _camera != null;
            FocusOnceBar.Background = on ? GreenFill : Brush(0x16, 0x30, 0x1E);
            FocusOnceStateText.Foreground = on ? GreenFill : Brush(0x7F, 0xAE, 0x86);
            FocusOnceStateText.Text = !_tuning.FocusSupported ? "NOT ADJUSTABLE" : _tuning.FocusHeld ? "ON · HELD" : on ? "ON · LOCKED" : "OFF";
        }

        /// <summary>Back to continuous autofocus (after a lock or a hold).</summary>
        private async Task ReleaseFocusAsync()
        {
            _focusLockedByOnce = false;
            _tuning.ReleaseFocusHold();
            _tuning.FocusAuto = true;
            SaveCameraSettings();
            UpdateCameraUi();
            Log("Focus released: continuous autofocus again");
            await ApplyCameraNowAsync();
        }

        private async Task<string> FocusOnceAsync()
        {
            var cap = _camera?.Capture;
            if (cap == null) return "The camera is not ready";
            FocusOnceButton.IsEnabled = false;
            try
            {
                _tuning.ReleaseFocusHold();
                bool ok = await _tuning.FocusOnceAsync(cap);
                _focusLockedByOnce = ok;
                await ApplyCameraNowAsync();
                UpdateCameraUi();
                return ok ? "Focus locked" : "Focus once failed (see the log)";
            }
            catch (Exception ex) { Log("Focus once: " + ex.Message); return "Focus once failed (see the log)"; }
            finally { FocusOnceButton.IsEnabled = _tuning.FocusSupported; UpdateFocusOnceRow(); }
        }

        /// <summary>Image stabilization (Camera): off saves processing and heat on a fixed mount (one stream restart).</summary>
        private void OisToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading || !_uiReady) return;
            SaveOisToggle.IsOn = !OisToggle.IsOn; // its handler saves, applies and restarts
        }

        private async void SaveOisToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            _tuning.SaveOis = SaveOisToggle.IsOn;
            SaveCameraSettings();
            // Stabilization only changes while no recording runs, so a running stream restarts once.
            if (_camera != null && _camera.IsStreaming && !_busy) { Log("Stabilization changed - restarting stream"); await RestartStreamAsync(); }
            else ApplyCameraPower();
        }

        private async void SaveFocusToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            _tuning.SaveFocus = SaveFocusToggle.IsOn;
            SaveCameraSettings();
            var cap = _camera?.Capture;
            if (!_tuning.SaveFocus && _tuning.FocusHeld) { _tuning.ReleaseFocusHold(); await ApplyCameraNowAsync(); }
            else if (_tuning.SaveFocus) _nextHoldAt = DateTime.Now;
        }

        // ---- flashlight: only while streaming, off after 30 s, never remembered as "on"

        private void TorchToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            SetTorch(TorchToggle.IsOn);
        }

        private string SetTorch(bool on)
        {
            string result;
            if (on && _protect) { on = false; result = "Flashlight unavailable: heat protection is on"; }
            else if (on && (!_tuning.TorchSupported || _camera?.Capture == null)) { on = false; result = "Flashlight: the camera is not open yet (or this camera has no flashlight control)"; }
            else result = on ? "Flashlight on for " + TorchSeconds + " s" : "Flashlight off";
            bool was = _torchWanted;
            _torchWanted = on;
            _torchOffAt = on ? DateTime.Now.AddSeconds(TorchSeconds) : DateTime.MaxValue;
            ApplyTorch();
            if (was != _torchWanted) Log(result);
            UpdateTorchUi();
            return result;
        }

        private void TorchOff()
        {
            if (!_torchWanted) return;
            _torchWanted = false;
            _torchOffAt = DateTime.MaxValue;
            ApplyTorch();
            UpdateTorchUi();
        }

        private void ApplyTorch()
        {
            try
            {
                var cap = _camera?.Capture;
                if (cap == null || !_tuning.TorchSupported) return;
                var t = cap.VideoDeviceController.TorchControl;
                bool want = _torchWanted;
                if (t.Enabled != want) t.Enabled = want;
                // the LED only burns while a camera pipeline runs: with the stream off, the preview carries it
                if (want && !_camera.IsStreaming && !_camera.Previewing) { _torchPreview = true; var _ = _camera.SetPreviewAsync(true); }
                else if (!want && _torchPreview) { _torchPreview = false; var _ = ApplyDisplayAsync(); } // back to what the switches say
            }
            catch (Exception ex)
            {
                Log("Flashlight: " + ex.Message.Trim());
                _torchWanted = false;
            }
        }

        private bool _torchPreview; // the preview was started only to carry the LED (stream off): ended with the flashlight

        private void UpdateTorchUi()
        {
            bool was = _loading;
            _loading = true;
            try
            {
                TorchToggle.IsOn = _torchWanted;
                TorchToggle.IsEnabled = _torchWanted || (_tuning.TorchSupported && _camera?.Capture != null && !_protect);
            }
            finally { _loading = was; }
            UpdateTorchButton();
        }

        /// <summary>The Flashlight light-bar button: lit with the seconds left while on; the reason it can't be used while off.</summary>
        private void UpdateTorchButton()
        {
            if (TorchBar == null) return;
            bool open = _camera?.Capture != null;
            TorchButton.IsEnabled = TorchToggle.IsEnabled;
            TorchBar.Background = _torchWanted ? GreenFill : Brush(0x16, 0x30, 0x1E);
            TorchStateText.Foreground = _torchWanted ? GreenFill : Brush(0x7F, 0xAE, 0x86);
            TorchStateText.Text = _torchWanted ? "ON · " + TorchSecondsLeft + " s"
                : _protect ? "OFF · HEAT PROTECTION"
                : open && !_tuning.TorchSupported ? "OFF · NOT ON THIS CAMERA"
                : !open ? "OFF · CAMERA CLOSED" : "OFF";
        }

        private void TorchButton_Click(object sender, RoutedEventArgs e) => SetTorch(!_torchWanted);

        /// <summary>Stabilization (only while no recording runs), with the crash guard: if the app dies here, the next launch switches it off.</summary>
        private void ApplyCameraPower()
        {
            if (_camera?.Capture == null) return;
            _settings.Values["PowerSaveInProgress"] = true;
            try { _tuning.ApplyPower(_camera.Capture); }
            catch (Exception ex) { Log("Camera power saving: " + ex.Message); }
            _settings.Values["PowerSaveInProgress"] = false;
        }

        private async Task HoldFocusLaterAsync()
        {
            try
            {
                var cap = _camera?.Capture;
                if (cap != null && await _tuning.HoldFocusAsync(cap)) { _nextHoldAt = DateTime.MaxValue; UpdateCameraUi(); }
                else _nextHoldAt = DateTime.Now.AddMinutes(10); // e.g. too dark to focus: try again later, not every minute
            }
            catch (Exception ex) { Log("Hold focus: " + ex.Message); _nextHoldAt = DateTime.Now.AddMinutes(10); }
            finally { _holdBusy = false; }
        }

        private int TorchSecondsLeft => _torchWanted ? Math.Max(0, (int)Math.Ceiling((_torchOffAt - DateTime.Now).TotalSeconds)) : 0;

        /// <summary>Once a second: the flashlight time limit, and (every 5 s) what the camera is using now.</summary>
        private void CameraTick()
        {
            if (_torchWanted && (DateTime.Now >= _torchOffAt || _camera?.Capture == null))
            {
                SetTorch(false);
                Log("Flashlight switched off automatically");
            }
            if (TorchToggle.IsEnabled != (_torchWanted || (_tuning.TorchSupported && _camera?.Capture != null && !_protect))) UpdateTorchUi();
            else if (_torchWanted) { ApplyTorch(); UpdateTorchButton(); } // the LED again after a stream start/stop re-opened the pipeline; the seconds left
            if (_tuning.SaveFocus && _tuning.FocusAuto && !_tuning.FocusHeld && !_holdBusy && !_busy &&
                _camera != null && _camera.IsStreaming && DateTime.Now >= _nextHoldAt)
            {
                _holdBusy = true;
                _nextHoldAt = DateTime.Now.AddMinutes(1);
                var _ = HoldFocusLaterAsync();
            }
            if (++_cameraReadTick % 15 == 0 && _camera?.Capture != null)
            {
                _tuning.Read(_camera.Capture);
                UpdateCameraUi();
            }
        }
    }
}
