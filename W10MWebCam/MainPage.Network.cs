using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace W10MWebCam
{
    /// <summary>
    /// Network switches (as in W10MDashcam): the web page (settings and /status; on by default), the log page (/log) and
    /// the stats page (/stats); the last two are off by default and need the web page on. The video on ports 8020 and
    /// 5000 is never affected. Also here: the stats samples, the rear/front camera choice and the safe-mode button.
    /// </summary>
    public sealed partial class MainPage
    {
        private void LoadNetworkSettings()
        {
            WebToggle.IsOn = GetBool("WebOn", true);
            LogToggle.IsOn = GetBool("LogOn", false);
            StatsToggle.IsOn = GetBool("StatsOn", false);
            // Off until the owner says otherwise: the raw video has no login (see AskWifiVideoOnceAsync).
            WifiVideoToggle.IsOn = GetBool("WifiVideoOn", false);
            if (CameraBox.Items.Count == 0) { CameraBox.Items.Add("Rear"); CameraBox.Items.Add("Front"); }
            CameraBox.SelectedIndex = _camera != null && _camera.UsingFrontCamera ? 1 : 0;
            ApplyNetworkSwitches();
        }

        private void ApplyNetworkSwitches()
        {
            _http.WebEnabled = WebToggle.IsOn;
            _http.LogEnabled = LogToggle.IsOn;
            _http.StatsEnabled = StatsToggle.IsOn;
            _server.WifiVideoEnabled = WifiVideoToggle.IsOn;
            WifiVideoWarning.Visibility = WifiVideoToggle.IsOn ? Visibility.Visible : Visibility.Collapsed;
            LogToggle.IsEnabled = StatsToggle.IsEnabled = WebToggle.IsOn;
            UpdateAddresses();
        }

        private string UsbUrl(string path) => "http://127.0.0.1:" + UsbPort + path;
        private string WifiUrl(string path) => _wifiAddress.Length > 0 ? "http://" + _wifiAddress + ":" + LogPort + path : "(no Wi-Fi)";

        /// <summary>
        /// The main screen shows only the video and the settings page, grouped by USB cable and Wi-Fi; the Network section
        /// shows each page's addresses under its switch.
        /// </summary>
        private void UpdateAddresses()
        {
            bool web = WebToggle.IsOn, wifiVideo = WifiVideoToggle.IsOn;
            UsbAddressText.Text = "Video     tcp://127.0.0.1:" + UsbPort + "\nSettings  " + (web ? UsbUrl("/settings") : "(web page off)");
            WifiAddressText.Text = _wifiAddress.Length == 0 ? "Not connected to Wi-Fi"
                : "Video     " + (wifiVideo ? "tcp://" + _wifiAddress + ":" + WifiPort : "(off - Network)") +
                  "\nSettings  " + (web ? WifiUrl("/settings") + "  (login)" : "(web page off)");
            WifiVideoAddressText.Text = _wifiAddress.Length == 0 ? "Wi-Fi  (not connected)" : "Wi-Fi  tcp://" + _wifiAddress + ":" + WifiPort;
            WifiVideoAddressText.Opacity = wifiVideo ? 1 : 0.4;
            // Viewer setup: the USB address, and the Wi-Fi one when there is Wi-Fi video
            string wifiTcp = _wifiAddress.Length > 0 && wifiVideo ? "tcp://" + _wifiAddress + ":" + WifiPort : null;
            // (label on one line, value below: the phone panel is narrow)
            ViewerObsText.Text = "Input\n  tcp://127.0.0.1:" + UsbPort + (wifiTcp != null ? "\n  " + wifiTcp + "  (Wi-Fi)" : "") +
                                 "\nInput format\n  h264\nFFmpeg options\n  fflags=nobuffer flags=low_delay analyzeduration=0 probesize=500000";
            ViewerVlcText.Text = "Edit Options\n  :demux=h264\nOr a command\n  vlc --demux=h264 tcp://127.0.0.1:" + UsbPort +
                                 (wifiTcp != null ? "\n  vlc --demux=h264 " + wifiTcp : "");
            ViewerFfplayText.Text = "ffplay -fflags nobuffer -flags low_delay -f h264 tcp://127.0.0.1:" + UsbPort;
            WebPageAddressText.Text = "USB    " + UsbUrl("/settings") + "\n       " + UsbUrl("/status") +
                                      "\nWi-Fi  " + WifiUrl("/settings") + "\n       " + WifiUrl("/status");
            LogPageAddressText.Text = "USB    " + UsbUrl("/log") + "\nWi-Fi  " + WifiUrl("/log");
            StatsPageAddressText.Text = "USB    " + UsbUrl("/stats") + "\nWi-Fi  " + WifiUrl("/stats");
            WebPageAddressText.Opacity = web ? 1 : 0.4;
            LogPageAddressText.Opacity = web && LogToggle.IsOn ? 1 : 0.4;
            StatsPageAddressText.Opacity = web && StatsToggle.IsOn ? 1 : 0.4;
            LoginStatusText.Text = _http.Login.IsDefault
                ? "Default login admin / admin: change it, or anyone on your network can change settings."
                : "User \"" + _http.Login.UserName + "\": the browser asks for this login before changes, over USB and Wi-Fi.";
            LoginNotice.Visibility = _http.Login.IsDefault && web ? Visibility.Visible : Visibility.Collapsed;
            if (string.IsNullOrEmpty(UserBox.Text)) UserBox.Text = _http.Login.UserName;
        }

        private void SaveLoginButton_Click(object sender, RoutedEventArgs e) => SaveLogin(UserBox.Text, PassBox.Password);

        /// <summary>Sets the login for changes (an empty password resets it to admin / admin). Returns a result line, or an error.</summary>
        private string SaveLogin(string user, string password)
        {
            user = (user ?? "").Trim();
            if (user.Length > 0 && !System.Text.RegularExpressions.Regex.IsMatch(user, @"^[A-Za-z0-9._-]{1,32}$"))
                return "Not changed: the user name may use letters, digits . _ - (1-32 characters)";
            if (!string.IsNullOrEmpty(password) && (password.Length < Streaming.WebLogin.MinPasswordLength || password.Length > 64 || password.Any(ch => ch < ' ')))
                return "Not changed: the password needs " + Streaming.WebLogin.MinPasswordLength + "-64 characters";
            try { _http.Login.Save(user, password); }
            catch (Exception ex) { Log("Login: " + ex.Message); return "Login not saved: " + ex.Message; }
            PassBox.Password = "";
            string result = _http.Login.IsDefault ? "Login reset to the default (admin / admin)" : "Login set for \"" + _http.Login.UserName + "\": changes need it";
            Log(result);
            UpdateAddresses();
            return result;
        }

        /// <summary>
        /// Asks once whether to send the video over Wi-Fi, while no choice is saved yet (a new install, an update from a
        /// version that had it on by default, or after the self-check reset the settings). Not awaited by the caller: the
        /// USB stream starts meanwhile, so a phone used unattended still works. Closing the dialog without a choice asks
        /// again at the next start.
        /// </summary>
        private async Task AskWifiVideoOnceAsync()
        {
            if (_settings.Values["WifiVideoOn"] is bool) return;
            var dialog = new ContentDialog
            {
                Title = "Video over Wi-Fi?",
                Content = new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    Text = "The video over the USB cable always works. Video over Wi-Fi lets OBS, VLC or ffplay on another device " +
                           "watch, but it has no login (they can't send one): anyone on this network could watch the camera.\n\n" +
                           "You can change this later under Network > Video over Wi-Fi."
                },
                PrimaryButtonText = "Keep it off",
                SecondaryButtonText = "Turn it on"
            };
            ContentDialogResult answer;
            try { answer = await dialog.ShowAsync(); }
            catch (Exception ex) { Log("Video over Wi-Fi question: " + ex.Message); return; }
            if (_settings.Values["WifiVideoOn"] is bool) return; // already chosen meanwhile (settings page)
            if (answer == ContentDialogResult.Secondary) WifiVideoToggle.IsOn = true; // Toggled saves, applies and logs it
            else if (answer == ContentDialogResult.Primary)
            {
                _settings.Values["WifiVideoOn"] = false;
                Log("Video over Wi-Fi off (USB only)");
            }
        }

        private void WifiVideoToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            _settings.Values["WifiVideoOn"] = WifiVideoToggle.IsOn;
            ApplyNetworkSwitches();
            if (!WifiVideoToggle.IsOn) _server.CloseWifiViewers();
            Log("Video over Wi-Fi " + (WifiVideoToggle.IsOn ? "on (no login: anyone on the network can watch)" : "off (USB only)"));
        }

        private void WebToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            _settings.Values["WebOn"] = WebToggle.IsOn;
            ApplyNetworkSwitches();
            Log("Web page " + (WebToggle.IsOn ? "on" : "off (only the video is served)"));
        }

        private void LogToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            _settings.Values["LogOn"] = LogToggle.IsOn;
            ApplyNetworkSwitches();
            Log("Log page " + (LogToggle.IsOn ? "on" : "off"));
        }

        private void StatsToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            _settings.Values["StatsOn"] = StatsToggle.IsOn;
            ApplyNetworkSwitches();
            Log("Stats page " + (StatsToggle.IsOn ? "on: a sample every 30 s from now" : "off"));
        }

        // ---------------------------------------------------------------- stats samples (for /stats)

        private const int StatsKeep = 1440; // 12 hours at one sample every 30 s
        private readonly Queue<string> _statRows = new Queue<string>();
        private readonly object _statLock = new object();
        private DateTime _lastStatSample = DateTime.MinValue;

        private static string F(double? v, string fmt = "0.##") => v.HasValue ? v.Value.ToString(fmt, CultureInfo.InvariantCulture) : "";

        /// <summary>Once a second from the stats timer: one row every 30 s, only while the stats page is on.</summary>
        private void SampleStats()
        {
            if (!StatsToggle.IsOn || (DateTime.Now - _lastStatSample).TotalSeconds < 30) return;
            _lastStatSample = DateTime.Now;
            try
            {
                bool streaming = _camera != null && _camera.IsStreaming;
                bool charging = _battery != null && _battery.Status == Windows.System.Power.BatteryStatus.Charging;
                var video = EffectiveVideo();
                var b = BitrateBox.SelectedItem;
                double mem = 0;
                try { mem = Windows.System.MemoryManager.AppMemoryUsage / 1048576.0; } catch { }
                int bars = WifiBars();
                // time,ok,version,streaming,fps,mbps,viewers,format,bitrate,latency_ms,battery_pct,charging,net_w,draw_w,
                // heat_warn,protect,cpu_phone,cpu_app,memory_mb,wifi_bars,uptime_s,safe
                string row = string.Join(",", new[]
                {
                    DateTime.Now.ToString("s", CultureInfo.InvariantCulture), "1", AppVersion,
                    streaming ? "1" : "0",
                    streaming ? F(_camera.Fps, "0.0") : "", streaming ? F(_camera.Mbps, "0.0") : "",
                    _server.ClientCount.ToString(CultureInfo.InvariantCulture),
                    video != null ? (video.Item1.Height >= 2160 ? "4K" : video.Item1.Height + "p") + Math.Round(video.Item1.Fps) : "",
                    b != null ? b.ToString() : "",
                    streaming && _camera.LastLatencyMs >= 0 ? _camera.LastLatencyMs.ToString(CultureInfo.InvariantCulture) : "",
                    F(Percent(_battery)), charging ? "1" : "0",
                    F(_power.NetWatts), charging ? "" : F(_power.DrawWatts),
                    _pausedSecs >= 20 ? "1" : "0", _protect ? "1" : "0",
                    F(_power.SysCpuPct, "0"), F(_power.ProcCpuPct, "0"),
                    F(mem, "0"), bars >= 0 ? bars.ToString(CultureInfo.InvariantCulture) : "",
                    F((DateTime.Now - _launchedAt).TotalSeconds, "0"),
                    _safeMode ? "1" : "0"
                }.Select(x => (x ?? "").Replace(",", ";").Replace("\"", "")));
                lock (_statLock)
                {
                    _statRows.Enqueue(row);
                    while (_statRows.Count > StatsKeep) _statRows.Dequeue();
                }
            }
            catch (Exception ex) { Log("Stats sample: " + ex.Message); }
        }

        /// <summary>The rows for the stats page (network thread).</summary>
        private string StatsRows()
        {
            lock (_statLock) return string.Join(",\n", _statRows.Select(x => "'" + x.Replace("'", "") + "'"));
        }

        // ---------------------------------------------------------------- safe mode

        private async void NormalModeButton_Click(object sender, RoutedEventArgs e) => await ReturnToNormalModeAsync();

        /// <summary>Leaves SAFE MODE: the low-latency engine again (restarts a running stream once).</summary>
        private async Task<string> ReturnToNormalModeAsync()
        {
            if (!_safeMode || _camera == null) return "Not in safe mode";
            _safeMode = false;
            _nativeStalls = 0;
            _lastRestartReason = "";
            _camera.UseNativeSink = true;
            Log("Back to normal mode: the low-latency engine");
            try { if (_camera.IsStreaming) await RestartStreamAsync(); }
            catch (Exception ex) { Log("Normal mode: " + ex.Message); }
            return "Back to the low-latency engine";
        }

        // ---------------------------------------------------------------- rear / front camera

        private async void CameraBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || _camera == null) return;
            bool front = CameraBox.SelectedIndex == 1;
            if (front == _camera.UsingFrontCamera) return;
            await SwitchCameraAsync(front);
        }

        private bool _switchingCamera;

        /// <summary>Closes the camera and opens the other one; a running stream starts again on it.</summary>
        private async Task<string> SwitchCameraAsync(bool front)
        {
            if (_switchingCamera || _busy || _camera == null) return "Busy - try again in a moment";
            _switchingCamera = true;
            try
            {
                _settings.Values["Camera"] = front ? "front" : "rear";
                bool wasStreaming = _camera.IsStreaming;
                Log("Switching to the " + (front ? "front" : "rear") + " camera" + (wasStreaming ? " (the stream restarts)" : ""));
                EndPhoneAdjust();
                if (wasStreaming) await ToggleStreamingAsync(); // stop
                TorchOff();
                var old = _camera;
                _camera = null;
                old.NalReady -= _server.Broadcast;
                old.AccessUnitReady -= _server.BroadcastAccessUnit;
                PreviewElement.Source = null;
                try { await old.ShutdownAsync(); } catch (Exception ex) { Log("Closing the camera: " + ex.Message); }
                await InitCameraAsync(wasStreaming);
                bool ok = _camera != null && _camera.UsingFrontCamera == front;
                if (!ok)
                {
                    _settings.Values["Camera"] = _camera != null && _camera.UsingFrontCamera ? "front" : "rear";
                    return "That camera is not available";
                }
                return "Switched to the " + (front ? "front" : "rear") + " camera";
            }
            catch (Exception ex)
            {
                Log("Switching camera: " + ex.Message);
                return "Switching camera failed (see the log)";
            }
            finally { _switchingCamera = false; }
        }
    }
}
