using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Devices.Power;
using Windows.Foundation.Metadata;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.System.Power;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace W10MWebCam
{
    /// <summary>
    /// Power and heat (from W10MSecurityCam and W10MDashcam):
    /// - a power readout (net watts into/out of the battery, CPU) logged every minute;
    /// - "charger insufficient" when the battery drains although the phone says it is charging (common on PC USB ports);
    /// - heat: Windows gives apps no temperature, but charging pausing (Idle) on a charger Windows calls Adequate, below
    ///   95 %, for 20 s means the battery is hot. "Lower to 1080p" (on by default) then makes the app act: 1080p / 6 Mbps,
    ///   black screen, stabilization off, focus held and the flashlight locked until charging has resumed; off = warn only;
    /// - optional stop on power loss: a countdown when the phone goes on battery, then the stream stops.
    /// </summary>
    public sealed partial class MainPage
    {
        private const uint CoolBitrate = 6000000;
        private int _powerTick, _pausedSecs, _chargingSecs;
        private bool _protect, _heatWarned;
        private DateTime _protectSince = DateTime.MinValue;
        private string _notice = "", _alert = "", _chargerNotice = "", _heat = "Healthy";
        private BatteryReport _battery;
        private MediaPlayer _chime;

        /// <summary>A short sound from the app's own files (media volume): ok = all good, else a warning. From W10MDashcam.</summary>
        private void PlayChime(bool ok)
        {
            try
            {
                if (_chime == null) _chime = new MediaPlayer { AudioCategory = MediaPlayerAudioCategory.SoundEffects, Volume = 1.0 };
                _chime.Source = MediaSource.CreateFromUri(new Uri(ok ? "ms-appx:///Assets/ClipSaved.wav" : "ms-appx:///Assets/ClipNone.wav"));
                _chime.Play();
            }
            catch (Exception ex) { Log("Chime: " + ex.Message); }
        }

        private static async Task BuzzAsync(int times)
        {
            try
            {
                if (!ApiInformation.IsTypePresent("Windows.Phone.Devices.Notification.VibrationDevice")) return;
                var v = Windows.Phone.Devices.Notification.VibrationDevice.GetDefault();
                if (v == null) return;
                for (int i = 0; i < times; i++)
                {
                    v.Vibrate(TimeSpan.FromMilliseconds(times == 1 ? 250 : 120));
                    await Task.Delay(times == 1 ? 250 : 260);
                }
            }
            catch { }
        }

        private void LoadPowerSettings()
        {
            HeatAutoToggle.IsOn = GetBool("HeatAuto", true);
            int secs = _settings.Values["UnplugStopSecs"] is int s ? s : 0;
            for (int i = 0; i < UnplugBox.Items.Count; i++)
                if (((UnplugOption)UnplugBox.Items[i]).Seconds == secs) UnplugBox.SelectedIndex = i;
        }

        private int UnplugStopSeconds => (UnplugBox.SelectedItem as UnplugOption)?.Seconds ?? 0;

        private void HeatAutoToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            _settings.Values["HeatAuto"] = HeatAutoToggle.IsOn;
            if (!HeatAutoToggle.IsOn && _protect) { var _ = LeaveProtectionAsync("switched off"); }
        }

        private void UnplugBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            _settings.Values["UnplugStopSecs"] = UnplugStopSeconds;
            if (UnplugStopSeconds == 0) HideUnplugPanel();
        }

        private static int? Percent(BatteryReport r)
        {
            if (r?.RemainingCapacityInMilliwattHours == null || r.FullChargeCapacityInMilliwattHours == null || r.FullChargeCapacityInMilliwattHours.Value <= 0) return null;
            return (int)Math.Round(100.0 * r.RemainingCapacityInMilliwattHours.Value / r.FullChargeCapacityInMilliwattHours.Value);
        }

        /// <summary>Once a second from the stats timer.</summary>
        private void PowerTick()
        {
            try { _battery = Battery.AggregateBattery.GetReport(); } catch { _battery = null; }
            if (_powerTick % 5 == 0) _power.Sample();
            if (_powerTick % 60 == 0 && _powerTick > 0)
            {
                string cpu = _power.CpuText();
                Log(_power.PowerText() + (cpu.Length > 0 ? "; " + cpu : "") + BatteryLogText());
            }
            _powerTick++;
            if (_powerTick % 60 == 0) CheckMemory();
            UpdateHeat();
            UpdateUnplug();
            UpdateNotice();
        }

        // ---- memory (to spot slow leaks in long runs), as in W10MSecurityCam / W10MDashcam

        private ulong _peakMemory;
        private DateTime _lastMemoryLog = DateTime.MinValue;

        private static ulong AppMemory() { try { return Windows.System.MemoryManager.AppMemoryUsage; } catch { return 0; } }
        private static ulong AppMemoryLimit() { try { return Windows.System.MemoryManager.AppMemoryUsageLimit; } catch { return 0; } }

        /// <summary>Every minute: track peak memory; every 30 minutes (and every 5 when it is past 75 % of the limit) log it.</summary>
        private void CheckMemory()
        {
            ulong mem = AppMemory(), limit = AppMemoryLimit();
            if (mem > _peakMemory) _peakMemory = mem;
            bool high = limit > 0 && mem > limit * 3 / 4;
            if ((DateTime.Now - _lastMemoryLog).TotalMinutes >= 30 || (high && (DateTime.Now - _lastMemoryLog).TotalMinutes >= 5))
            {
                _lastMemoryLog = DateTime.Now;
                Log(string.Format("{0}Memory: {1:0.0} MB (peak {2:0.0}, limit {3:0}, managed {4:0.0}), uptime {5:0.0} h",
                    high ? "WARNING " : "", mem / 1048576.0, _peakMemory / 1048576.0, limit / 1048576.0,
                    GC.GetTotalMemory(false) / 1048576.0, (DateTime.Now - _launchedAt).TotalHours));
            }
        }

        private string BatteryLogText()
        {
            int? pct = Percent(_battery);
            return pct.HasValue ? "; battery " + pct + " % " + (_battery.Status.ToString().ToLowerInvariant()) : "";
        }

        /// <summary>Text of the notice when the battery drains although the phone says it is charging (empty otherwise).</summary>
        private string ChargerNotice()
        {
            var w = _power.NetWatts; // 5-minute average, restarted when the charging state changes
            if (_battery == null || _battery.Status != BatteryStatus.Charging || !w.HasValue || w.Value > -0.3) return "";
            return "Charger insufficient: the battery is draining while charging (" + w.Value.ToString("0.0") +
                   " W). A PC USB port often can't keep up with 4K; use a 2 A or better charger or a powered hub, or a lower resolution.";
        }

        private void UpdateHeat()
        {
            int? pct = Percent(_battery);
            bool paused = _battery != null && _battery.Status == BatteryStatus.Idle && _power.Supply == "Adequate" && (!pct.HasValue || pct.Value < 95);
            bool charging = _battery != null && _battery.Status == BatteryStatus.Charging;
            _pausedSecs = paused ? _pausedSecs + 1 : 0;
            _chargingSecs = charging ? _chargingSecs + 1 : 0;
            bool hot = _pausedSecs >= 20;
            if (hot && !_heatWarned)
            {
                _heatWarned = true;
                Log("HEAT: charging has paused on a good charger for 20 s - the phone is probably too hot" +
                    (HeatAutoToggle.IsOn ? "" : " (warning only; 'Lower to 1080p' is off)"));
            }
            if (!hot && _chargingSecs >= 30) _heatWarned = false;
            if (hot && HeatAutoToggle.IsOn && !_protect) { var _ = EnterProtectionAsync(); }
            else if (_protect && _chargingSecs >= 30 && (DateTime.Now - _protectSince).TotalMinutes >= 3) { var _ = LeaveProtectionAsync("charging has resumed"); }
            else if (_protectRestartOwed && !_protectBusy && !_busy) { var _ = RestartForProtectionAsync(); }
            _heat = _protect ? "Heat protection on" : hot ? "Hot (charging paused)" : paused ? "Charging paused" : "Healthy";
        }

        // ---- Heat protection (as in W10MSecurityCam / W10MDashcam): when charging stalls on a good charger, lower the video,
        // force every power-saving option on and lock the flashlight; put the person's own settings back once charging
        // resumes. The forced values are never written to the saved settings (SaveCameraSettings and the blackout switch
        // skip them while _protect), so an app restart in between also comes back with the person's own choices.
        private bool _protectBusy, _protectRestartOwed;
        private bool _pOis, _pFocus;

        private async System.Threading.Tasks.Task EnterProtectionAsync()
        {
            if (_protect || _protectBusy) return;
            _protectBusy = true;
            try
            {
                _pOis = _tuning.SaveOis; _pFocus = _tuning.SaveFocus;
                _protect = true;
                _protectSince = DateTime.Now;
                EndPhoneAdjust();
                TorchOff();
                var after = EffectiveVideo();
                Log("HEAT PROTECTION ON: charging has paused on a good charger, so the phone is probably too hot. Lowering the stream to " +
                    after?.Item1 + ", " + (after?.Item2 / 1e6)?.ToString("0") + " Mbps, black screen, stabilization off, focus held and the flashlight locked until charging resumes (your settings are kept)");
                PlayChime(false);
                var buzz = BuzzAsync(2);
                _tuning.SaveOis = true; _tuning.SaveFocus = true;
                ShowForcedSwitches();
                ForceDisplayForProtection(); // black screen on, no preview behind it (MainPage.Display.cs)
                UpdateProtectUi();
                // One restart picks up the video format and the stabilization setting together (both only change while stopped).
                await RestartForProtectionAsync();
            }
            catch (Exception ex) { Log("Heat protection: " + ex.Message); }
            finally { _protectBusy = false; }
        }

        private async System.Threading.Tasks.Task LeaveProtectionAsync(string why)
        {
            if (!_protect || _protectBusy) return;
            _protectBusy = true;
            try
            {
                _protect = false;
                Log("HEAT PROTECTION OFF (" + why + "): back to your own video and power-saving settings (the flashlight stays off)");
                PlayChime(true);
                var buzz = BuzzAsync(1);
                _tuning.SaveOis = _pOis; _tuning.SaveFocus = _pFocus;
                ShowForcedSwitches();
                RestoreDisplayAfterProtection();
                UpdateProtectUi();
                await RestartForProtectionAsync();
            }
            catch (Exception ex) { Log("Heat protection: " + ex.Message); }
            finally { _protectBusy = false; }
        }

        /// <summary>The stabilization / hold-focus switches show the forced (or restored) values without acting on them.</summary>
        private void ShowForcedSwitches()
        {
            bool was = _loading;
            _loading = true;
            try { SaveOisToggle.IsOn = _tuning.SaveOis; SaveFocusToggle.IsOn = _tuning.SaveFocus; }
            finally { _loading = was; }
        }

        /// <summary>
        /// The one restart for heat protection. When the stream is busy starting or stopping, the restart is owed and
        /// UpdateHeat retries it, so the stream can't stay at 4K while heat protection is on.
        /// </summary>
        private async System.Threading.Tasks.Task RestartForProtectionAsync()
        {
            if (_camera == null || !_camera.IsStreaming) { _protectRestartOwed = false; return; } // the next start uses the new settings
            if (_busy) { if (!_protectRestartOwed) Log("Heat protection: stream busy, restarting it shortly"); _protectRestartOwed = true; return; }
            _protectRestartOwed = false;
            Log("Heat protection: restarting the stream once");
            await RestartStreamAsync();
        }

        /// <summary>Greys out what the person cannot change while heat protection is on.</summary>
        private void UpdateProtectUi()
        {
            bool free = !_protect;
            ResolutionBox.IsEnabled = BitrateBox.IsEnabled = free;
            PowerSaveToggle.IsEnabled = KeepPreviewToggle.IsEnabled = free;
            SaveOisToggle.IsEnabled = free && _tuning.OisSupported;
            SaveFocusToggle.IsEnabled = free;
            UpdateTorchUi();
        }

        /// <summary>The red notice (heat, safe mode) and the amber one (charger) on the status card; the same text goes to the web page.</summary>
        private void UpdateNotice()
        {
            var alerts = new List<string>();
            if (_protect) alerts.Add("Heat protection: charging paused because the phone is hot - usually the charger can't keep up (a PC USB port or a small adapter; use 2 A or more). Video is 1080p / 6 Mbps, the screen is black, stabilization is off and the flashlight is locked until it charges normally again.");
            else if (_pausedSecs >= 20) alerts.Add("Charging has paused: the phone is probably hot - usually the charger can't keep up (a PC USB port or a small adapter; use 2 A or more)." + (HeatAutoToggle.IsOn ? "" : " 'Lower to 1080p' under Power is off, so this is a warning only."));
            if (_safeMode) alerts.Add("Safe mode: the standard (MP4) engine is in use until the app is reopened.");
            string charger = ChargerNotice();
            string alert = string.Join(" ", alerts);
            _notice = string.Join(" ", alerts.Concat(charger.Length > 0 ? new[] { charger } : new string[0]));
            _alert = alert;
            _chargerNotice = charger;
            AlertText.Text = alert;
            AlertBox.Visibility = alert.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            NormalModeButton.Visibility = _safeMode ? Visibility.Visible : Visibility.Collapsed;
            NoticeText.Text = charger;
            NoticeBox.Visibility = charger.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            string cpu = _power.CpuText();
            int? pct = Percent(_battery);
            PowerText.Text = (pct.HasValue ? "Battery " + pct + " % " + _battery.Status.ToString().ToLowerInvariant() + " · " : "") +
                             _power.PowerText().Replace("Power: ", "") + (cpu.Length > 0 ? " · " + cpu : "");
        }

        // ---- stop on power loss (W10MDashcam): only "Discharging" counts; a heat pause reports Idle.

        private DateTime _unplugAt = DateTime.MaxValue;
        private bool _unplugSkipped, _stoppedForPower;

        private void UpdateUnplug()
        {
            bool onBattery = _battery != null && _battery.Status == BatteryStatus.Discharging;
            bool streaming = _camera != null && _camera.IsStreaming;
            if (!onBattery)
            {
                _unplugSkipped = false;
                if (_unplugAt != DateTime.MaxValue) { Log("Power is back - the stream keeps running"); HideUnplugPanel(); }
                if (_stoppedForPower && !streaming && !_busy && _camera != null)
                {
                    _stoppedForPower = false;
                    Log("Power is back - starting the stream again");
                    var _ = ToggleStreamingAsync();
                }
                return;
            }
            int limit = UnplugStopSeconds;
            if (limit == 0 || !streaming || _unplugSkipped || _busy) { if (!streaming) HideUnplugPanel(); return; }
            if (_unplugAt == DateTime.MaxValue)
            {
                _unplugAt = DateTime.Now.AddSeconds(limit);
                Log("On battery - the stream stops in " + limit + " s unless power returns");
                PlayChime(false);
                var buzz = BuzzAsync(2);
            }
            int left = (int)Math.Ceiling((_unplugAt - DateTime.Now).TotalSeconds);
            if (left <= 0)
            {
                HideUnplugPanel();
                _stoppedForPower = true;
                Log("Stopping the stream: the phone has been on battery for " + limit + " s");
                var _ = ToggleStreamingAsync();
                return;
            }
            UnplugText.Text = "The phone is on battery.\nStreaming stops in " + left + " s.";
            if (UnplugPanel.Visibility != Visibility.Visible)
            {
                UnplugPanel.Visibility = Visibility.Visible;
                var _ = ApplyDisplayAsync(); // lifts the black screen so the countdown is visible (PanelOnScreen)
            }
        }

        private void HideUnplugPanel()
        {
            _unplugAt = DateTime.MaxValue;
            if (UnplugPanel.Visibility != Visibility.Visible) return;
            UnplugPanel.Visibility = Visibility.Collapsed;
            var _ = ApplyDisplayAsync(); // back to black
        }

        private void KeepStreamingButton_Click(object sender, RoutedEventArgs e) => KeepStreaming();

        private string KeepStreaming()
        {
            if (_unplugAt == DateTime.MaxValue) return "No countdown running";
            _unplugSkipped = true;
            HideUnplugPanel();
            Log("Keep streaming: the countdown is skipped until power returns");
            return "Countdown skipped until power returns";
        }
    }
}
