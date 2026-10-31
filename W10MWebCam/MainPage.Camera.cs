using System;
using System.Threading.Tasks;
using Windows.UI.Xaml;

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
            CameraAutoToggle.IsOn = _tuning.Auto;
            SaveOisToggle.IsOn = _tuning.SaveOis;
            SaveOisToggle.IsEnabled = _tuning.OisSupported;
            SaveFocusToggle.IsOn = _tuning.SaveFocus;
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
            _settings.Values["SaveOis"] = _tuning.SaveOis;
            _settings.Values["SaveFocus"] = _tuning.SaveFocus;
        }

        /// <summary>After each stream start: send every camera setting again, then (optionally) park the focus.</summary>
        private async Task AfterStreamStartAsync()
        {
            try
            {
                var cap = _camera?.Capture;
                if (cap == null) return;
                await _tuning.ApplyAsync(cap, force: true);
                UpdateCameraUi();
                _nextHoldAt = DateTime.Now; // "Hold focus once settled": CameraTick tries it (again every minute until it settles)
            }
            catch (Exception ex) { Log("Camera settings: " + ex.Message); }
        }

        /// <summary>Applies changed camera settings now (focus, exposure and white balance can change while streaming).</summary>
        private async Task ApplyCameraNowAsync()
        {
            SaveCameraSettings();
            var cap = _camera?.Capture;
            if (cap != null) await _tuning.ApplyAsync(cap);
            UpdateCameraUi();
        }

        private void UpdateCameraUi()
        {
            bool was = _loading;
            _loading = true;
            try
            {
                CameraAutoToggle.IsOn = _tuning.Auto;
                CameraText.Text = "Camera: " + _tuning.Mode + (_tuning.State.Length > 0 ? " - " + _tuning.State : "");
            }
            finally { _loading = was; }
        }

        private async void CameraAutoToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            if (!CameraAutoToggle.IsOn) _tuning.SeedFromCamera(_camera?.Capture, true, true, true); // hold the current picture
            _tuning.ReleaseFocusHold();
            _tuning.Auto = CameraAutoToggle.IsOn;
            await ApplyCameraNowAsync();
        }

        private async void FocusOnceButton_Click(object sender, RoutedEventArgs e) => await FocusOnceAsync();

        private async Task<string> FocusOnceAsync()
        {
            var cap = _camera?.Capture;
            if (cap == null) return "The camera is not ready";
            FocusOnceButton.IsEnabled = false;
            try
            {
                bool ok = await _tuning.FocusOnceAsync(cap);
                await ApplyCameraNowAsync();
                return ok ? "Focus locked" : "Focus once failed (see the log)";
            }
            finally { FocusOnceButton.IsEnabled = _tuning.FocusSupported; }
        }

        private async void SaveOisToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            _tuning.SaveOis = SaveOisToggle.IsOn;
            SaveCameraSettings();
            // Stabilization only changes while no recording runs, so a running stream restarts once.
            if (_camera != null && _camera.IsStreaming && !_busy) { Log("Stabilization changed - restarting stream"); await RestartStreamAsync(); }
            else _tuning.ApplyPower(_camera?.Capture);
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
            if (on && (!_tuning.TorchSupported || _camera == null || !_camera.IsStreaming)) { on = false; result = "The flashlight works only while streaming"; }
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
                bool want = _torchWanted && _camera.IsStreaming;
                if (t.Enabled != want) t.Enabled = want;
            }
            catch (Exception ex)
            {
                Log("Flashlight: " + ex.Message.Trim());
                _torchWanted = false;
            }
        }

        private void UpdateTorchUi()
        {
            bool was = _loading;
            _loading = true;
            try
            {
                TorchToggle.IsOn = _torchWanted;
                TorchToggle.IsEnabled = _torchWanted || (_tuning.TorchSupported && _camera != null && _camera.IsStreaming);
            }
            finally { _loading = was; }
        }

        private async Task HoldFocusLaterAsync()
        {
            try
            {
                var cap = _camera?.Capture;
                if (cap != null && await _tuning.HoldFocusAsync(cap)) { _nextHoldAt = DateTime.MaxValue; UpdateCameraUi(); }
            }
            catch (Exception ex) { Log("Hold focus: " + ex.Message); }
            finally { _holdBusy = false; }
        }

        private int TorchSecondsLeft => _torchWanted ? Math.Max(0, (int)Math.Ceiling((_torchOffAt - DateTime.Now).TotalSeconds)) : 0;

        /// <summary>Once a second: the flashlight time limit, and (every 5 s) what the camera is using now.</summary>
        private void CameraTick()
        {
            if (_torchWanted && (DateTime.Now >= _torchOffAt || _camera == null || !_camera.IsStreaming))
            {
                SetTorch(false);
                Log("Flashlight switched off automatically");
            }
            if (TorchToggle.IsEnabled != (_torchWanted || (_tuning.TorchSupported && _camera != null && _camera.IsStreaming))) UpdateTorchUi();
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
