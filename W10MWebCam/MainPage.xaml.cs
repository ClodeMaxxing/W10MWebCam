using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using W10MWebCam.Streaming;
using Windows.ApplicationModel;
using Windows.Graphics.Display;
using Windows.Networking;
using Windows.Networking.Connectivity;
using Windows.Storage;
using Windows.System.Display;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace W10MWebCam
{
    /// <summary>
    /// The single screen of the app: camera control, settings, blackout mode, crash guard and logging.
    /// Partial files: MainPage.Camera.cs (focus/exposure/white balance, flashlight), MainPage.Power.cs (power readout,
    /// charger and heat notices, stop on power loss), MainPage.Web.cs (the settings page's JSON and changes).
    /// The camera/encoder work lives in Streaming\CameraEngine.cs; networking in Streaming\StreamServer.cs.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        private const int WifiPort = 5000;  // raw H.264 over the network: tcp://PHONE-IP:5000
        private const int UsbPort = 8020;   // same stream over USB; Microsoft's IP-over-USB service maps PC 127.0.0.1:8020 to it
        private const int LogPort = 5001;   // http://PHONE-IP:5001 shows the log and the settings page over Wi-Fi

        private CameraEngine _camera;
        private readonly StreamServer _server = new StreamServer(WifiPort, UsbPort);
        private readonly HttpApi _http = new HttpApi();
        private readonly LogServer _logServer;
        private readonly CameraTuning _tuning = new CameraTuning();
        private readonly PowerMonitor _power = new PowerMonitor();
        private readonly DisplayRequest _displayRequest = new DisplayRequest();
        private readonly DispatcherTimer _statsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        private readonly DispatcherTimer _wakeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        private readonly ApplicationDataContainer _settings = ApplicationData.Current.LocalSettings;
        private readonly DateTime _launchedAt = DateTime.Now;

        /// <summary>True while settings are being loaded into the controls: their change events must not act or save.</summary>
        private bool _loading = true;
        private bool _busy;
        private DateTime _busySince;
        private bool _keepAwake;
        private BrightnessOverride _brightness;
        private bool _blackedOut;

        // crash guard + stall watchdog
        private DateTime _startedAt = DateTime.MinValue;
        private int _nativeStalls;
        private DateTime _lastStall = DateTime.MinValue;
        private bool _safeMode;

        private sealed class KeyframeOption
        {
            public double Seconds;
            public override string ToString() => Seconds + " s";
        }

        private sealed class BitrateOption
        {
            public uint Bps; // 0 = auto
            public override string ToString() => Bps == 0 ? "Auto" : (Bps / 1000000) + " Mbps";
        }

        private sealed class UnplugOption
        {
            public int Seconds; // 0 = never
            public override string ToString() => Seconds == 0 ? "Never" : Seconds < 60 ? Seconds + " s" : (Seconds / 60) + " min";
        }

        public static string AppVersion
        {
            get { var v = Package.Current.Id.Version; return v.Major + "." + v.Minor + (v.Build > 0 ? "." + v.Build : ""); }
        }

        public MainPage()
        {
            RotateLogFile();
            LaunchSelfCheck();
            InitializeComponent();

            VersionText.Text = "v" + AppVersion;

            Application.Current.UnhandledException += (s, e) => Log("CRASH (unhandled): " + e.Exception + " | " + e.Message);
            _server.Log = Log;
            _tuning.Log = Log;
            SetUpWeb();
            try { RefreshAddresses(); } catch (Exception ex) { Log("Addresses: " + ex.Message); } // the Host check needs them before the camera is up
            NetworkInformation.NetworkStatusChanged += OnNetworkStatusChanged;
            _server.Http = _http;
            _logServer = new LogServer(_http) { Log = Log };
            _statsTimer.Tick += StatsTimer_Tick;
            _wakeTimer.Tick += WakeTimer_Tick;
            Window.Current.VisibilityChanged += (s, e) => { if (!e.Visible) Log("App hidden (screen locked or switched away)"); };

            BitrateBox.Items.Add(new BitrateOption { Bps = 0 });
            foreach (var m in new uint[] { 4, 6, 8, 12, 20, 30, 40, 50, 60 })
                BitrateBox.Items.Add(new BitrateOption { Bps = m * 1000000 });
            BitrateBox.SelectedIndex = 0;
            foreach (var k in new[] { 0.5, 1.0, 2.0, 3.0 }) KeyframeBox.Items.Add(new KeyframeOption { Seconds = k });
            KeyframeBox.SelectedIndex = 1;
            foreach (var s in new[] { 0, 30, 60, 120, 300 }) UnplugBox.Items.Add(new UnplugOption { Seconds = s });
            UnplugBox.SelectedIndex = 0;

            Application.Current.Suspending += OnSuspending;
            Application.Current.Resuming += OnResuming;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            DisplayInformation.AutoRotationPreferences = DisplayOrientations.Landscape;
            await StartCameraAsync();
        }

        // ------------------------------------------------------------------ self-check

        private bool _selfCheckReset, _launchHealthy;

        /// <summary>
        /// Startup self-check (from W10MSecurityCam). A launch that never reached 20 s of healthy running (or a clean
        /// suspend) counts as failed. After two failed launches in a row the saved settings are reset to defaults, in case
        /// a saved setting is what makes the app fail. Complements the one-launch SAFE MODE below.
        /// </summary>
        private void LaunchSelfCheck()
        {
            try
            {
                int fails = _settings.Values["LaunchFailures"] is int lf ? lf : 0;
                if (_settings.Values["LaunchInProgress"] is bool lip && lip) fails++; else fails = 0;
                if (fails >= 2)
                {
                    // Every default is the safe choice (Video over Wi-Fi is off and is asked again), so clearing never
                    // opens the camera to the network. The login lives in the password vault and is kept.
                    _settings.Values.Clear();
                    fails = 0;
                    _selfCheckReset = true;
                }
                _settings.Values["LaunchFailures"] = fails;
                _settings.Values["LaunchInProgress"] = true;
            }
            catch { }
        }

        private void MarkLaunchHealthy()
        {
            if (_launchHealthy) return;
            _launchHealthy = true;
            try { _settings.Values["LaunchInProgress"] = false; _settings.Values["LaunchFailures"] = 0; } catch { }
        }

        // ------------------------------------------------------------------ startup

        private async Task StartCameraAsync()
        {
            try
            {
                if (_selfCheckReset) { _selfCheckReset = false; Log("SELF-CHECK: the app failed to start twice in a row, so its saved settings were reset to defaults."); }

                // Servers first, so the log is reachable even if the camera fails.
                try { await _server.StartAsync(); } catch (Exception ex) { Log("Video server: " + ex.Message); }
                try { await _logServer.StartAsync(LogPort); } catch (Exception ex) { Log("Web server: " + ex.Message); }
                await InitCameraAsync(false);
            }
            catch (Exception ex)
            {
                _loading = false;
                Log("ERROR starting camera: " + ex.Message);
            }
        }

        /// <summary>
        /// Opens the chosen camera, fills the controls and, at app start, auto-starts (or enters SAFE MODE).
        /// resumeStreaming: after switching cameras, start the stream again straight away.
        /// </summary>
        private async Task InitCameraAsync(bool resumeStreaming)
        {
            try
            {
                Log("Starting camera...");
                _camera = new CameraEngine { Log = Log, UseFrontCamera = (_settings.Values["Camera"] as string) == "front" };
                if (_safeMode) _camera.UseNativeSink = false;
                _camera.NalReady += _server.Broadcast;
                await _camera.InitializeAsync();
                _tuning.Describe(_camera.Capture);

                _loading = true;
                var formats = _camera.GetRecordFormats();
                ResolutionBox.Items.Clear();
                foreach (var f in formats) ResolutionBox.Items.Add(f);
                Log(formats.Count + " recording formats, up to " + (formats.Count > 0 ? formats[0].ToString() : "none"));

                string savedFormat = _settings.Values[FormatSettingKey] as string;
                ResolutionBox.SelectedItem = formats.FirstOrDefault(f => f.ToString() == savedFormat)
                    ?? formats.FirstOrDefault(f => f.Width == 1920 && f.Height == 1080 && Math.Round(f.Fps) == 30)
                    ?? formats.FirstOrDefault();
                if (_settings.Values["Bitrate"] is int savedBitrate && savedBitrate >= 0 && savedBitrate < BitrateBox.Items.Count)
                    BitrateBox.SelectedIndex = savedBitrate;
                if (_settings.Values["Keyframe"] is int kf && kf >= 0 && kf < KeyframeBox.Items.Count)
                    KeyframeBox.SelectedIndex = kf;
                AutoStartToggle.IsOn = GetBool("AutoStart", true);
                PowerSaveToggle.IsOn = GetBool("PowerSave", true);
                PreviewToggle.IsOn = GetBool("Preview", true);
                LoadPowerSettings();
                LoadCameraSettings();
                LoadNetworkSettings();
                _loading = false;

                bool crashedLastTime = !resumeStreaming && _settings.Values["StartInProgress"] is bool sip && sip;
                bool autoStarting = resumeStreaming || (AutoStartToggle.IsOn && !crashedLastTime);

                PreviewElement.Source = _camera.Capture;
                PreviewElement.FlowDirection = _camera.UsingFrontCamera ? FlowDirection.RightToLeft : FlowDirection.LeftToRight; // mirror the selfie view on the phone only
                // Don't spin the preview up just to switch it off again: starting 4K recording while the
                // preview is toggled right after camera power-up crashes the camera pipeline.
                if (PreviewToggle.IsOn && !(autoStarting && PowerSaveToggle.IsOn)) await _camera.SetPreviewAsync(true);
                UpdatePreviewNote();

                ShowAddresses();
                StartButton.IsEnabled = formats.Count > 0;
                _statsTimer.Start();
                Log("Ready.");
                if (!resumeStreaming) { var _ = AskWifiVideoOnceAsync(); }

                if (crashedLastTime)
                {
                    // Crash guard: the last launch died while starting a stream. Don't auto-start, and use the
                    // proven MP4 engine for this launch only.
                    _settings.Values["StartInProgress"] = false;
                    _camera.UseNativeSink = false;
                    _safeMode = true;
                    Log("SAFE MODE: the app closed unexpectedly while starting the stream last time.");
                    Log("Auto-start skipped; the standard (MP4) engine will be used until the app is reopened.");
                }
                else if (autoStarting && StartButton.IsEnabled)
                {
                    Log((resumeStreaming ? "Restarting the stream" : "Auto-start") + ": letting the camera settle for 2 s");
                    await Task.Delay(2000);
                    await ToggleStreamingAsync();
                }
            }
            catch (Exception ex)
            {
                _loading = false;
                Log("ERROR starting camera: " + ex.Message);
            }
        }

        /// <summary>The resolution is remembered per camera (the front one has a different list).</summary>
        private string FormatSettingKey => _camera != null && _camera.UsingFrontCamera ? "FormatFront" : "Format";

        private bool GetBool(string key, bool fallback) => _settings.Values[key] is bool b ? b : fallback;

        private void ShowAddresses()
        {
            RefreshAddresses();
            _addressesShown = true;
            UpdateAddresses();
            LogAddresses();
        }

        private void LogAddresses() =>
            Log("Addresses: video USB tcp://127.0.0.1:" + UsbPort + (_wifiAddress.Length > 0 ? ", Wi-Fi tcp://" + _wifiAddress + ":" + WifiPort : ", no Wi-Fi") +
                "; settings http://127.0.0.1:" + UsbPort + "/settings" + (_wifiAddress.Length > 0 ? ", http://" + _wifiAddress + ":" + LogPort + "/settings" : ""));

        /// <summary>
        /// Reads the phone's addresses: the first IPv4 one is shown as the Wi-Fi address, and all of them pass the web
        /// page's Host check. Returns true when the shown address changed.
        /// </summary>
        private bool RefreshAddresses()
        {
            var hosts = NetworkInformation.GetHostNames()
                .Where(h => (h.Type == HostNameType.Ipv4 || h.Type == HostNameType.Ipv6) && h.IPInformation != null)
                .ToList();
            string wifi = hosts.Where(h => h.Type == HostNameType.Ipv4)
                .Select(h => h.CanonicalName)
                .FirstOrDefault(ip => !ip.StartsWith("169.254.")) ?? ""; // link-local addresses aren't reachable from the PC
            _phoneAddresses = hosts.Select(h => h.CanonicalName).ToArray();
            bool changed = wifi != _wifiAddress;
            _wifiAddress = wifi;
            return changed;
        }

        /// <summary>A new DHCP lease or another Wi-Fi network: the shown addresses and the Host check follow.</summary>
        private void OnNetworkStatusChanged(object sender)
        {
            var _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                try
                {
                    if (!RefreshAddresses()) return;
                    if (_addressesShown) UpdateAddresses();
                    LogAddresses();
                }
                catch (Exception ex) { Log("Network change: " + ex.Message); }
            });
        }

        private volatile string _wifiAddress = "";
        private volatile string[] _phoneAddresses = new string[0];
        private bool _addressesShown;

        // ------------------------------------------------------------------ start / stop

        private async void StartButton_Click(object sender, RoutedEventArgs e) => await ToggleStreamingAsync();

        private async void Setting_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || _camera == null || !_camera.IsStreaming || _busy) return;
            Log("Setting changed - restarting stream");
            await RestartStreamAsync();
        }

        private async Task RestartStreamAsync()
        {
            if (_camera == null || !_camera.IsStreaming) return;
            await ToggleStreamingAsync(); // stop
            await ToggleStreamingAsync(); // start with the new settings
        }

        /// <summary>The format and bitrate to stream now: the chosen ones, or lowered while heat protection is on.</summary>
        private Tuple<FormatOption, uint> EffectiveVideo()
        {
            var f = ResolutionBox.SelectedItem as FormatOption;
            if (f == null) return null;
            var b = BitrateBox.SelectedItem as BitrateOption;
            uint bps = (b == null || b.Bps == 0) ? CameraEngine.AutoBitrate(f) : b.Bps;
            if (_protect)
            {
                if ((long)f.Width * f.Height > 1920L * 1080)
                {
                    var cool = ResolutionBox.Items.OfType<FormatOption>().Where(o => o.Width == 1920 && o.Height == 1080)
                        .OrderBy(o => Math.Abs(o.Fps - f.Fps)).FirstOrDefault();
                    if (cool != null) f = cool;
                }
                bps = Math.Min(bps, CoolBitrate);
            }
            return Tuple.Create(f, bps);
        }

        private async Task ToggleStreamingAsync()
        {
            if (_busy && (DateTime.Now - _busySince).TotalSeconds > 30)
            {
                Log("Previous start/stop never finished - unlocking controls");
                _busy = false;
            }
            if (_busy || _camera == null) return;
            _busy = true;
            _busySince = DateTime.Now;
            StartButton.IsEnabled = false;
            try
            {
                if (!_camera.IsStreaming)
                {
                    var video = EffectiveVideo();
                    if (video == null) { Log("Pick a resolution first"); return; }
                    if (KeyframeBox.SelectedItem is KeyframeOption ko) _camera.KeyframeSeconds = ko.Seconds;
                    if (!_protect) _settings.Values[FormatSettingKey] = video.Item1.ToString();
                    _settings.Values["Bitrate"] = BitrateBox.SelectedIndex;
                    _settings.Values["Keyframe"] = KeyframeBox.SelectedIndex;

                    _settings.Values["StartInProgress"] = true; // cleared after 15 s of healthy streaming
                    _startedAt = DateTime.Now;
                    // The preview only changes while no recording runs (changing it mid-recording stalls the 4K pipeline):
                    // decide it now, before recording starts.
                    await _camera.SetPreviewAsync(PreviewWanted);
                    _tuning.ApplyPower(_camera.Capture); // stabilization: also only while stopped
                    _tuning.FrameLimitMs = 1000.0 / Math.Max(1, video.Item1.Fps);
                    await _camera.StartStreamingAsync(video.Item1, video.Item2);
                    StartButton.Content = "Stop streaming";
                    await ApplyBlackoutAsync();
                    if (!_keepAwake) { _displayRequest.RequestActive(); _keepAwake = true; }
                    var _ = AfterStreamStartAsync();
                }
                else
                {
                    TorchOff();
                    await _camera.StopStreamingAsync();
                    ClearStartFlag();
                    StopUi();
                    await _camera.SetPreviewAsync(IdlePreviewWanted);
                    await ApplyBlackoutAsync();
                }
            }
            catch (Exception ex)
            {
                Log("ERROR: " + ex.Message);
                try { await _camera.StopStreamingAsync(); } catch { }
                ClearStartFlag();
                StopUi();
            }
            finally
            {
                StartButton.IsEnabled = true;
                _busy = false;
                UpdatePreviewNote();
            }
        }

        private void ClearStartFlag()
        {
            _settings.Values["StartInProgress"] = false;
            _startedAt = DateTime.MinValue;
        }

        private void StopUi()
        {
            StartButton.Content = "Start streaming";
            if (_keepAwake) { _displayRequest.RequestRelease(); _keepAwake = false; }
        }

        // ------------------------------------------------------------------ blackout (battery saver)

        /// <summary>
        /// While streaming: screen at minimum brightness and a full black overlay (tap to wake for 15 s). This only
        /// touches the overlay and brightness; the preview is decided when the stream starts.
        /// </summary>
        private async Task ApplyBlackoutAsync()
        {
            bool want = _camera != null && _camera.IsStreaming && PowerSaveToggle.IsOn;
            if (want == _blackedOut) return;
            _blackedOut = want;
            try
            {
                if (_brightness == null) _brightness = BrightnessOverride.GetForCurrentView();
                if (want && !_phoneAdjusting) // while adjusting the camera on the phone the screen stays lit (MainPage.Adjust.cs)
                {
                    if (_brightness.IsSupported)
                    {
                        _brightness.SetBrightnessLevel(0.0, DisplayBrightnessOverrideOptions.None);
                        _brightness.StartOverride();
                    }
                    BlackoutOverlay.Visibility = Visibility.Visible;
                }
                else
                {
                    _wakeTimer.Stop();
                    BlackoutOverlay.Visibility = Visibility.Collapsed;
                    if (_brightness.IsSupported) _brightness.StopOverride();
                }
            }
            catch (Exception ex) { Log("Blackout: " + ex.Message); }
            UpdatePreviewNote();
            await Task.CompletedTask;
        }

        private void UpdatePreviewNote()
        {
            bool previewing = _camera != null && _camera.Previewing;
            PreviewOffText.Text = _camera != null && _camera.IsStreaming
                ? (previewing ? "" : _phoneAdjusting ? "Starting the camera picture..." : "Streaming - preview off" + (PowerSaveToggle.IsOn ? " (blackout mode)" : " (it starts with the next stream start)"))
                : "Preview off";
            PreviewOffText.Visibility = previewing ? Visibility.Collapsed : Visibility.Visible;
        }

        private void BlackoutOverlay_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            BlackoutOverlay.Visibility = Visibility.Collapsed;
            if (_brightness != null && _brightness.IsSupported) _brightness.StopOverride();
            UpdatePreviewNote();
            _wakeTimer.Stop();
            _wakeTimer.Start();
        }

        private void WakeTimer_Tick(object sender, object e)
        {
            _wakeTimer.Stop();
            if (!_blackedOut || _phoneAdjusting || UnplugPanel.Visibility == Visibility.Visible) return;
            BlackoutOverlay.Visibility = Visibility.Visible;
            if (_brightness != null && _brightness.IsSupported)
            {
                _brightness.SetBrightnessLevel(0.0, DisplayBrightnessOverrideOptions.None);
                _brightness.StartOverride();
            }
        }

        private async void PowerSaveToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            _settings.Values["PowerSave"] = PowerSaveToggle.IsOn;
            await ApplyBlackoutAsync();
        }

        private void AutoStartToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            _settings.Values["AutoStart"] = AutoStartToggle.IsOn;
        }

        private async void PreviewToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            _settings.Values["Preview"] = PreviewToggle.IsOn;
            if (_camera == null) return;
            if (_camera.IsStreaming) Log("Preview " + (PreviewToggle.IsOn ? "on" : "off") + " applies when the stream is stopped or restarted");
            else await _camera.SetPreviewAsync(IdlePreviewWanted);
            UpdatePreviewNote();
        }

        // ------------------------------------------------------------------ stats + watchdog (once a second)

        private void StatsTimer_Tick(object sender, object e)
        {
            if (!_launchHealthy && (DateTime.Now - _launchedAt).TotalSeconds >= 20) MarkLaunchHealthy();
            PowerTick();
            SampleStats();
            CameraTick();
            UpdateStats();
        }

        private void UpdateStats()
        {
            if (_camera == null) return;
            bool streaming = _camera.IsStreaming;
            if (streaming)
            {
                _camera.UpdateStats();
                CheckStall();
                if (_startedAt != DateTime.MinValue && (DateTime.Now - _startedAt).TotalSeconds > 15)
                    ClearStartFlag(); // ran fine for 15 s
            }
            UpdateStatusCard(streaming);
        }

        /// <summary>If the low-latency engine stops delivering frames, restart; after two stalls, use the MP4 engine.</summary>
        private async void CheckStall()
        {
            if (_busy || _camera == null || !_camera.IsStreaming || _camera.PathName != "low-latency") return;
            if (_camera.SecondsSinceLastFrame < 4) return;
            if ((DateTime.Now - _lastStall).TotalSeconds > 120) _nativeStalls = 0; // stalls far apart don't add up
            _lastStall = DateTime.Now;
            _nativeStalls++;
            Log("Low-latency engine stalled (no frames for 4 s) - restart #" + _nativeStalls);
            if (_nativeStalls >= 2)
            {
                Log("Switching to the standard (MP4) engine until the app is reopened");
                _camera.UseNativeSink = false;
                _safeMode = true;
            }
            await RestartStreamAsync();
        }

        // ------------------------------------------------------------------ app lifecycle

        private async void OnSuspending(object sender, SuspendingEventArgs e)
        {
            var deferral = e.SuspendingOperation.GetDeferral();
            try
            {
                Log("App suspended (closed or switched away) - a session that ends without this line ended abruptly");
                MarkLaunchHealthy(); // a clean suspend is not a failed launch
                EndPhoneAdjust();
                _statsTimer.Stop();
                TorchOff();
                if (_camera != null) await _camera.ShutdownAsync();
                _camera = null;
                ClearStartFlag(); // a clean suspend is not a crash
                _server.Stop();
                _logServer.Stop();
                StopUi();
                _blackedOut = false;
                BlackoutOverlay.Visibility = Visibility.Collapsed;
                HideUnplugPanel();
            }
            finally { deferral.Complete(); }
        }

        private async void OnResuming(object sender, object e)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () => await StartCameraAsync());
        }

        // ------------------------------------------------------------------ logging

        private readonly LinkedList<string> _screenLog = new LinkedList<string>();
        private readonly List<string> _fullLog = new List<string>();
        private static readonly string LogFile = System.IO.Path.Combine(ApplicationData.Current.LocalFolder.Path, "log.txt");
        private string _previousSession = "";
        private int _linesSinceSizeCheck;

        /// <summary>Text served at / on the web interface (USB port 8020 and http://PHONE-IP:5001).</summary>
        private string GetFullLog()
        {
            string stats = "";
            var cam = _camera;
            if (cam != null)
                stats = string.Format("W10MWebCam {0}  streaming: {1}  fps: {2:0.0}  Mbps: {3:0.00}  keyframe every: {4:0.0}s  viewers: {5}  engine: {6}\n\n",
                    AppVersion, cam.IsStreaming, cam.Fps, cam.Mbps, cam.KeyframeIntervalSec, _server.ClientCount, cam.PathName);
            lock (_fullLog) return stats + string.Join("\n", _fullLog) +
                (_previousSession.Length > 0 ? "\n\n" + _previousSession : "");
        }

        /// <summary>Keep the last session's log (it survives a crash) and start a fresh one.</summary>
        /// <summary>
        /// Keeps the last three sessions' logs (log-previous.txt, -2, -3; they survive a crash) and starts a fresh one.
        /// The served log ends with the tail of each, so a crash is still visible after the app was reopened twice.
        /// </summary>
        private void RotateLogFile()
        {
            string folder = ApplicationData.Current.LocalFolder.Path;
            string Prev(int n) => System.IO.Path.Combine(folder, n == 1 ? "log-previous.txt" : "log-previous-" + n + ".txt");
            try
            {
                if (System.IO.File.Exists(LogFile))
                {
                    if (System.IO.File.Exists(Prev(3))) System.IO.File.Delete(Prev(3));
                    if (System.IO.File.Exists(Prev(2))) System.IO.File.Move(Prev(2), Prev(3));
                    if (System.IO.File.Exists(Prev(1))) System.IO.File.Move(Prev(1), Prev(2));
                    System.IO.File.Move(LogFile, Prev(1));
                }
                var parts = new List<string>();
                for (int n = 1; n <= 3; n++)
                {
                    if (!System.IO.File.Exists(Prev(n))) continue;
                    var lines = System.IO.File.ReadAllLines(Prev(n));
                    bool clean = lines.Any(l => l.Contains("App suspended"));
                    bool crash = lines.Any(l => l.Contains("CRASH"));
                    string head = n == 1 ? "previous session" : n == 2 ? "session before that" : "three sessions ago";
                    parts.Add("--- " + head + " (" + (crash ? "CRASHED" : clean ? "closed normally" : "ended abruptly: crash, phone off or older build") + "; last lines) ---\n" +
                              string.Join("\n", lines.Skip(Math.Max(0, lines.Length - (n == 1 ? 120 : 60)))));
                }
                _previousSession = string.Join("\n\n", parts);
            }
            catch (Exception ex) { _previousSession = "(could not read previous logs: " + ex.Message + ")"; }
        }

        private void Log(string message)
        {
            System.Diagnostics.Debug.WriteLine("[W10MWebCam] " + message);
            string line = DateTime.Now.ToString("HH:mm:ss.fff") + " " + message;
            lock (_fullLog)
            {
                _fullLog.Add(line);
                if (_fullLog.Count > 1000) _fullLog.RemoveRange(0, 200);
                try
                {
                    // Keep the on-phone log file small during long sessions: start over above ~2 MB.
                    if (++_linesSinceSizeCheck >= 500)
                    {
                        _linesSinceSizeCheck = 0;
                        if (System.IO.File.Exists(LogFile) && new System.IO.FileInfo(LogFile).Length > 2 * 1024 * 1024)
                            System.IO.File.Delete(LogFile);
                    }
                    System.IO.File.AppendAllText(LogFile, line + "\r\n");
                }
                catch { }
            }
            var _ = Dispatcher.RunAsync(CoreDispatcherPriority.Low, () =>
            {
                _screenLog.AddFirst(DateTime.Now.ToString("HH:mm:ss") + " " + message);
                while (_screenLog.Count > 25) _screenLog.RemoveLast();
                LogText.Text = string.Join("\n", _screenLog);
            });
        }
    }
}
