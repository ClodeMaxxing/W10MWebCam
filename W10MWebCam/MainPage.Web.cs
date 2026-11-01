using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using W10MWebCam.Streaming;
using Windows.Data.Json;
using Windows.UI.Core;

namespace W10MWebCam
{
    /// <summary>
    /// The PC settings page's data: /status and /settings/state as JSON, and /settings/set (USB only, see HttpApi).
    /// Changes are made through the phone's own controls, so the phone and the page always agree and every rule
    /// (restart on video changes, preview only while stopped, the flashlight time limit) applies to both.
    /// </summary>
    public sealed partial class MainPage
    {
        private void SetUpWeb()
        {
            _http.GetLog = GetFullLog;
            _http.GetStatusJson = () => OnUiAsync(() => Task.FromResult(StatusJson().Stringify()));
            _http.GetSettingsJson = () => OnUiAsync(() => Task.FromResult(SettingsJson().Stringify()));
            _http.ApplySettings = form => OnUiAsync(() => ApplySettingsAsync(form));
            _http.GetStatsRows = StatsRows;
            _http.GetPhoneAddresses = () => _phoneAddresses;
            _http.Log = Log;
            _http.Login.Log = Log;
            try { _http.Login.Load(); } catch (Exception ex) { Log("Login: " + ex.Message); }
        }

        /// <summary>Runs f on the UI thread (the controls live there) and returns its result to the web server thread.</summary>
        private Task<T> OnUiAsync<T>(Func<Task<T>> f)
        {
            var tcs = new TaskCompletionSource<T>();
            var _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () =>
            {
                try { tcs.TrySetResult(await f()); }
                catch (Exception ex) { tcs.TrySetException(ex); }
            });
            return tcs.Task;
        }

        private static IJsonValue Num(double? v) => v.HasValue && !double.IsNaN(v.Value) && !double.IsInfinity(v.Value)
            ? JsonValue.CreateNumberValue(Math.Round(v.Value, 3)) : JsonValue.CreateNullValue();
        private static IJsonValue Str(string s) => JsonValue.CreateStringValue(s ?? "");
        private static IJsonValue Bool(bool b) => JsonValue.CreateBooleanValue(b);

        /// <summary>Health for monitoring (tools\Soak-Monitor.ps1) and the page's status card.</summary>
        private JsonObject StatusJson()
        {
            var o = new JsonObject();
            var cam = _camera;
            bool streaming = cam != null && cam.IsStreaming;
            o["app"] = Str("W10MWebCam");
            o["version"] = Str(AppVersion);
            o["uptime"] = Num((DateTime.Now - _launchedAt).TotalSeconds);
            o["streaming"] = Bool(streaming);
            o["engine"] = Str(cam?.PathName ?? "");
            o["safeMode"] = Bool(_safeMode);
            o["restarts"] = Num(_restarts);
            o["lastRestartReason"] = Str(_lastRestartReason);
            var video = EffectiveVideo();
            o["format"] = Str(video?.Item1?.ToString() ?? "");
            o["fps"] = Num(cam?.Fps ?? 0);
            o["mbps"] = Num(cam?.Mbps ?? 0);
            o["keyframeSec"] = Num(cam?.KeyframeIntervalSec ?? 0);
            o["latencyMs"] = Num(cam != null && cam.LastLatencyMs >= 0 ? (double?)cam.LastLatencyMs : null);
            o["viewers"] = Num(_server.ClientCount);
            int? pct = Percent(_battery);
            o["battery"] = Num(pct);
            o["batteryStatus"] = Str(_battery?.Status.ToString() ?? "unknown");
            // charging = current flows into the battery; onExternalPower = a charger is connected (Charging or Idle)
            o["charging"] = Bool(_battery != null && _battery.Status == Windows.System.Power.BatteryStatus.Charging);
            o["onExternalPower"] = Bool(_battery != null && _battery.Status != Windows.System.Power.BatteryStatus.Discharging);
            o["charger"] = Str(_power.Supply);
            o["netWatts"] = Num(_power.NetWatts);
            o["drawWatts"] = Num(_power.DrawWatts);
            o["powerText"] = Str(_power.PowerText());
            o["cpuPhone"] = Num(_power.SysCpuPct);
            o["cpuApp"] = Num(_power.ProcCpuPct);
            try
            {
                o["memoryMB"] = Num(Windows.System.MemoryManager.AppMemoryUsage / 1048576.0);
                o["memoryLimitMB"] = Num(Windows.System.MemoryManager.AppMemoryUsageLimit / 1048576.0);
                ulong mem = Windows.System.MemoryManager.AppMemoryUsage;
                if (mem > _peakMemory) _peakMemory = mem;
                o["peakMemoryMB"] = Num(_peakMemory / 1048576.0);
                o["managedMemoryMB"] = Num(GC.GetTotalMemory(false) / 1048576.0);
            }
            catch { }
            o["heat"] = Str(_heat);
            var hb = HeatBadge();
            o["heatBadge"] = Str(hb.Item1);
            o["heatLevel"] = Num(hb.Item2);
            o["alert"] = Str(_alert);
            o["chargerNotice"] = Str(_chargerNotice);
            o["batteryFalling"] = Bool(BatteryFalling);
            o["wifiBars"] = Num(WifiBars() >= 0 ? (double?)WifiBars() : null);
            o["protect"] = Bool(_protect);
            o["notice"] = Str(_notice);
            o["torchOn"] = Bool(_torchWanted);
            o["torchLeftSec"] = Num(TorchSecondsLeft);
            o["unplugLeftSec"] = Num(_unplugAt == DateTime.MaxValue ? (double?)null : Math.Max(0, (_unplugAt - DateTime.Now).TotalSeconds));
            o["camera"] = Str(_tuning.State);
            o["blackout"] = Bool(_blackedOut);
            o["previewing"] = Bool(cam != null && cam.Previewing);
            o["wifi"] = Str(_wifiAddress);
            // The same names as W10MSecurityCam's and W10MDashcam's /status, so one Soak-Monitor serves all three apps.
            o["uptimeSeconds"] = o["uptime"];
            o["batteryPercent"] = o["battery"];
            o["sensorToEncodedMs"] = Num(cam?.LastLatencyMs ?? -1);
            o["keyframeSeconds"] = o["keyframeSec"];
            o["wifiSignalBars"] = o["wifiBars"];
            o["cpuPhonePct"] = o["cpuPhone"];
            o["cpuAppPct"] = o["cpuApp"];
            o["bitrateSetting"] = Str(BitrateBox.SelectedItem?.ToString() ?? "");
            o["cameraMode"] = Str(_tuning.Mode);
            o["loginRequired"] = Bool(_http.Login.Enabled);
            o["time"] = Str(DateTime.Now.ToString("yyyy-MM-ddTHH:mm:sszzz"));
            return o;
        }

        /// <summary>Everything the settings page shows: the status plus each setting and its choices.</summary>
        private JsonObject SettingsJson()
        {
            var o = StatusJson();
            o["formats"] = List(ResolutionBox.Items.Select(i => i.ToString()));
            o["formatIndex"] = Num(ResolutionBox.SelectedIndex);
            o["bitrates"] = List(BitrateBox.Items.Select(i => i.ToString()));
            o["bitrateIndex"] = Num(BitrateBox.SelectedIndex);
            o["keyframes"] = List(KeyframeBox.Items.Select(i => i.ToString()));
            o["keyframeIndex"] = Num(KeyframeBox.SelectedIndex);
            o["unplugChoices"] = List(UnplugBox.Items.Select(i => i.ToString()));
            o["unplugIndex"] = Num(UnplugBox.SelectedIndex);
            o["powerSave"] = Bool(PowerSaveToggle.IsOn);
            o["autoStart"] = Bool(AutoStartToggle.IsOn);
            o["preview"] = Bool(PreviewToggle.IsOn);
            o["heatAuto"] = Bool(HeatAutoToggle.IsOn);
            o["focusSupported"] = Bool(_tuning.FocusSupported);
            o["exposureSupported"] = Bool(_tuning.ExposureSupported);
            o["wbSupported"] = Bool(_tuning.WhiteBalanceSupported);
            o["focusAuto"] = Bool(_tuning.FocusAuto);
            o["exposureAuto"] = Bool(_tuning.ExposureAuto);
            o["wbAuto"] = Bool(_tuning.WbAuto);
            o["focus"] = Num(_tuning.FocusPercent);
            o["focusHeld"] = Bool(_tuning.FocusHeld);
            o["exposureMs"] = Num(_tuning.ExposureMs);
            o["expMin"] = Num(_tuning.ExposureSupported ? _tuning.ExposureMinUseful : 0.5);
            o["expMax"] = Num(_tuning.ExposureSupported ? Math.Max(_tuning.ExposureMinUseful * 2, Math.Min(_tuning.ExposureMaxMs, _tuning.FrameLimitMs)) : 33);
            o["wb"] = Num(_tuning.WhiteBalanceKelvin);
            o["wbMin"] = Num(_tuning.WbMin > 0 ? _tuning.WbMin : 2000);
            o["wbMax"] = Num(_tuning.WbMax > 0 ? _tuning.WbMax : 8000);
            o["torchSupported"] = Bool(_tuning.TorchSupported);
            o["oisSupported"] = Bool(_tuning.OisSupported);
            o["saveOis"] = Bool(_tuning.SaveOis);
            o["saveFocus"] = Bool(_tuning.SaveFocus);
            o["usbPort"] = Num(UsbPort);
            o["wifiPort"] = Num(WifiPort);
            o["logPort"] = Num(LogPort);
            o["logOn"] = Bool(LogToggle.IsOn);
            o["statsOn"] = Bool(StatsToggle.IsOn);
            o["webOn"] = Bool(WebToggle.IsOn);
            o["wifiVideoOn"] = Bool(WifiVideoToggle.IsOn);
            o["defaultLogin"] = Bool(_http.Login.IsDefault);
            o["front"] = Bool(_camera != null && _camera.UsingFrontCamera);
            return o;
        }

        private static JsonArray List(IEnumerable<string> items)
        {
            var a = new JsonArray();
            foreach (var s in items) a.Add(JsonValue.CreateStringValue(s));
            return a;
        }

        /// <summary>"USB" or "Wi-Fi": where a change came from (added by HttpApi).</summary>
        private static string Via(IDictionary<string, string> f) => f.TryGetValue("_via", out var v) ? v : "USB";

        private static bool On(string v) => v == "on" || v == "1" || v == "true" || v == "auto";

        /// <summary>Applies the page's changes by setting the phone's own controls. Returns a one-line result.</summary>
        private async Task<string> ApplySettingsAsync(IDictionary<string, string> f)
        {
            var done = new List<string>();
            string v;
            bool cameraChanged = false;

            if (_protect)
            {
                bool had = false;
                foreach (var k in new[] { "format", "bitrate", "powersave", "saveois", "savefocus", "torch" }) had |= f.Remove(k);
                if (had) done.Add("heat protection is on: video, blackout, stabilization, hold focus and the flashlight can't be changed until charging resumes");
            }

            if (f.TryGetValue("stream", out v))
            {
                bool want = v == "start";
                if (_camera != null && want != _camera.IsStreaming) { await ToggleStreamingAsync(); done.Add(want ? "stream started" : "stream stopped"); }
            }
            if (f.TryGetValue("engine", out v) && v == "normal" && _safeMode) done.Add(await ReturnToNormalModeAsync());
            if (f.TryGetValue("lens", out v) && (v == "front" || v == "rear") && _camera != null && (v == "front") != _camera.UsingFrontCamera)
            {
                done.Add(await SwitchCameraAsync(v == "front"));
                if (done.Count > 0 && f.Count == 2) { Log("Settings page (" + Via(f) + "): " + done[0]); return "Done: " + done[0]; }
            }
            // The web page itself can only be switched off on the phone (the page would cut itself off).
            if (f.TryGetValue("logpage", out v)) { LogToggle.IsOn = On(v); done.Add("log page " + (On(v) ? "on" : "off")); }
            if (f.TryGetValue("statspage", out v)) { StatsToggle.IsOn = On(v); done.Add("stats page " + (On(v) ? "on" : "off")); }
            if (f.TryGetValue("wifivideo", out v)) { WifiVideoToggle.IsOn = On(v); done.Add("video over Wi-Fi " + (On(v) ? "on" : "off")); }
            if (f.TryGetValue("password", out v)) done.Add(SaveLogin(f.TryGetValue("user", out var lu) ? lu : _http.Login.UserName, v));
            // Video settings: changing a combo box restarts a running stream (Setting_Changed), as on the phone.
            if (f.TryGetValue("format", out v) && int.TryParse(v, out int fi) && fi >= 0 && fi < ResolutionBox.Items.Count && fi != ResolutionBox.SelectedIndex)
            { ResolutionBox.SelectedIndex = fi; _settings.Values[FormatSettingKey] = ResolutionBox.SelectedItem.ToString(); done.Add("resolution " + ResolutionBox.SelectedItem); }
            if (f.TryGetValue("bitrate", out v) && int.TryParse(v, out int bi) && bi >= 0 && bi < BitrateBox.Items.Count && bi != BitrateBox.SelectedIndex)
            { BitrateBox.SelectedIndex = bi; _settings.Values["BitrateBps"] = (int)SelectedBitrateBps; done.Add("bitrate " + BitrateBox.SelectedItem); }
            if (f.TryGetValue("keyframe", out v) && int.TryParse(v, out int ki) && ki >= 0 && ki < KeyframeBox.Items.Count && ki != KeyframeBox.SelectedIndex)
            { KeyframeBox.SelectedIndex = ki; _settings.Values["Keyframe"] = ki; done.Add("keyframe every " + KeyframeBox.SelectedItem); }
            if (f.TryGetValue("unplug", out v) && int.TryParse(v, out int ui) && ui >= 0 && ui < UnplugBox.Items.Count)
            { UnplugBox.SelectedIndex = ui; done.Add("stop when unplugged: " + UnplugBox.SelectedItem); }
            if (f.TryGetValue("powersave", out v)) { PowerSaveToggle.IsOn = On(v); done.Add("blackout " + (On(v) ? "on" : "off")); }
            if (f.TryGetValue("autostart", out v)) { AutoStartToggle.IsOn = On(v); done.Add("auto-start " + (On(v) ? "on" : "off")); }
            if (f.TryGetValue("preview", out v)) { PreviewToggle.IsOn = On(v); done.Add("preview " + (On(v) ? "on" : "off")); }
            if (f.TryGetValue("heatauto", out v)) { HeatAutoToggle.IsOn = On(v); done.Add("lower to 1080p when hot " + (On(v) ? "on" : "off")); }
            if (f.TryGetValue("keepstreaming", out v) && _unplugAt != DateTime.MaxValue) done.Add(KeepStreaming());

            // Camera: focus / exposure / white balance apply immediately, even while streaming.
            if (f.TryGetValue("camera", out v))
            {
                if (v != "auto") _tuning.SeedFromCamera(_camera?.Capture, true, true, true);
                _tuning.ReleaseFocusHold();
                _tuning.Auto = v == "auto";
                cameraChanged = true;
                done.Add("camera " + _tuning.Mode);
            }
            if (f.TryGetValue("focus", out v))
            {
                if (v == "once") done.Add(await FocusOnceAsync());
                else if (v == "auto" || v == "manual")
                {
                    if (v == "manual") _tuning.SeedFromCamera(_camera?.Capture, true, false, false);
                    _tuning.ReleaseFocusHold();
                    _tuning.FocusAuto = v == "auto"; cameraChanged = true;
                }
                else if (double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double fp))
                { _tuning.FocusAuto = false; _tuning.FocusPercent = Math.Max(0, Math.Min(100, fp)); cameraChanged = true; }
            }
            if (f.TryGetValue("exposure", out v))
            {
                if (v == "auto" || v == "manual")
                {
                    if (v == "manual") _tuning.SeedFromCamera(_camera?.Capture, false, true, false);
                    _tuning.ExposureAuto = v == "auto"; cameraChanged = true;
                }
                else if (double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double ms) && ms > 0)
                { _tuning.ExposureAuto = false; _tuning.ExposureMs = _tuning.SnapExposureMs(ms); cameraChanged = true; }
            }
            if (f.TryGetValue("wb", out v))
            {
                if (v == "auto" || v == "manual")
                {
                    if (v == "manual") _tuning.SeedFromCamera(_camera?.Capture, false, false, true);
                    _tuning.WbAuto = v == "auto"; cameraChanged = true;
                }
                else if (uint.TryParse(v, out uint k))
                { _tuning.WbAuto = false; _tuning.WhiteBalanceKelvin = k; cameraChanged = true; }
            }
            if (cameraChanged) { await ApplyCameraNowAsync(); if (!done.Any(d => d.StartsWith("camera"))) done.Add("camera: " + _tuning.Mode); }
            if (f.TryGetValue("torch", out v)) done.Add(SetTorch(On(v)));
            if (f.TryGetValue("saveois", out v) && On(v) != SaveOisToggle.IsOn && _tuning.OisSupported)
            { SaveOisToggle.IsOn = On(v); done.Add("stabilization " + (On(v) ? "off (fixed mount)" : "on")); }
            if (f.TryGetValue("savefocus", out v) && On(v) != SaveFocusToggle.IsOn)
            { SaveFocusToggle.IsOn = On(v); done.Add("hold focus " + (On(v) ? "on" : "off")); }

            if (done.Count == 0) return "Nothing changed";
            Log("Settings page (" + Via(f) + "): " + string.Join(", ", done));
            return "Done: " + string.Join(", ", done);
        }
    }
}
