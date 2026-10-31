using System;
using System.Collections.Generic;
using System.Linq;
using Windows.Devices.Power;
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
    ///   95 %, for 20 s means the battery is hot. By default this only warns; "Lower to 1080p" makes it act;
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

        private void LoadPowerSettings()
        {
            HeatAutoToggle.IsOn = GetBool("HeatAuto", false);
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
            UpdateHeat();
            UpdateUnplug();
            UpdateNotice();
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
            _heat = _protect ? "Heat protection on" : hot ? "Hot (charging paused)" : paused ? "Charging paused" : "Healthy";
        }

        private async System.Threading.Tasks.Task EnterProtectionAsync()
        {
            if (_protect) return;
            var before = EffectiveVideo();
            _protect = true;
            _protectSince = DateTime.Now;
            var after = EffectiveVideo();
            TorchOff();
            Log("HEAT PROTECTION ON: lowering the stream to " + after?.Item1 + ", " + (after?.Item2 / 1e6)?.ToString("0") + " Mbps until charging resumes (your settings are kept)");
            if (_camera != null && _camera.IsStreaming && before != null && after != null &&
                (before.Item1 != after.Item1 || before.Item2 != after.Item2))
                await RestartStreamAsync();
        }

        private async System.Threading.Tasks.Task LeaveProtectionAsync(string why)
        {
            if (!_protect) return;
            _protect = false;
            Log("HEAT PROTECTION OFF (" + why + "): back to your own video settings");
            if (_camera != null && _camera.IsStreaming) await RestartStreamAsync();
        }

        /// <summary>The red notice (heat, safe mode) and the amber one (charger) on the status card; the same text goes to the web page.</summary>
        private void UpdateNotice()
        {
            var alerts = new List<string>();
            if (_protect) alerts.Add("Heat protection: the stream is lowered to 1080p / 6 Mbps until charging resumes.");
            else if (_pausedSecs >= 20) alerts.Add("The phone is probably too hot: charging has paused on a good charger. Give it air, lower the resolution, or turn on 'Lower to 1080p' under Power.");
            if (_safeMode) alerts.Add("Safe mode: the standard (MP4) engine is in use until the app is reopened.");
            string charger = ChargerNotice();
            string alert = string.Join(" ", alerts);
            _notice = string.Join(" ", alerts.Concat(charger.Length > 0 ? new[] { charger } : new string[0]));
            _alert = alert;
            _chargerNotice = charger;
            AlertText.Text = alert;
            AlertBox.Visibility = alert.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
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
                BlackoutOverlay.Visibility = Visibility.Collapsed; // lift the black screen so the countdown is visible
                if (_brightness != null && _brightness.IsSupported) _brightness.StopOverride();
            }
        }

        private void HideUnplugPanel()
        {
            _unplugAt = DateTime.MaxValue;
            if (UnplugPanel.Visibility != Visibility.Visible) return;
            UnplugPanel.Visibility = Visibility.Collapsed;
            if (_blackedOut) { _wakeTimer.Stop(); _wakeTimer.Start(); } // back to black after the usual 15 s
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
