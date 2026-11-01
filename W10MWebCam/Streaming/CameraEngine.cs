using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Media.Capture;
using Windows.Media.Capture.Core;
using Windows.Media.MediaProperties;
using Windows.Storage;

namespace W10MWebCam.Streaming
{
    internal sealed class FormatOption
    {
        public VideoEncodingProperties Props;
        public uint Width, Height;
        public double Fps;
        public override string ToString() => Width + " x " + Height + " @ " + Math.Round(Fps) + " fps";
    }

    /// <summary>
    /// Owns the camera. Uses the phone's hardware H.264 encoder (via the normal MP4
    /// recording path) and taps the encoded frames out live through Mp4TapStream.
    /// </summary>
    internal sealed class CameraEngine
    {
        // MF_MT_MAX_KEYFRAME_SPACING: ask the encoder for a keyframe every N frames.
        private static readonly Guid MaxKeyframeSpacing = new Guid("c16eb52b-73a1-476f-8d62-839d6a020652");
        // Encoder (ICodecAPI) settings, passed through to the hardware encoder.
        private static readonly Guid RateControlMode = new Guid("1c0608e9-370c-4710-8a58-cb6181c42423"); // CODECAPI_AVEncCommonRateControlMode
        private static readonly Guid MeanBitRate = new Guid("f7222374-2144-4815-b550-a37f8e12ee52");     // CODECAPI_AVEncCommonMeanBitRate
        private static readonly Guid MaxBitRate = new Guid("9651eae4-39b9-4ebf-85ef-d7f444ec7465");      // CODECAPI_AVEncCommonMaxBitRate
        private static readonly Guid GopSize = new Guid("95f31b26-95a4-41aa-9303-246a7fc6eef1");         // CODECAPI_AVEncMPVGOPSize
        private static readonly Guid LowLatency = new Guid("9c27891a-ed7a-40e1-88e8-b22727a024ee");      // CODECAPI_AVLowLatencyMode

        public MediaCapture Capture { get; private set; }
        public Action<string> Log = _ => { };
        public event Action<byte[], int> NalReady;
        public bool IsStreaming { get; private set; }
        private bool _previewing;

        private Mp4H264Extractor _extractor;
        private W10MWebCam.Native.EncodedFrameSink _nativeSink;
        private bool _nativeHeaderChecked;
        private long _nativeFrames, _nativeMaxGapMs, _nativeLastMs = -1, _nativeStatMs;

        /// <summary>Use the low-latency native sink (falls back to the MP4 path automatically if it fails).</summary>
        public bool UseNativeSink = true;
        public string PathName { get; private set; } = "";
        private Mp4TapStream _tap;
        private H264Config _config;
        private string _configKey;
        private int _profileChoice = -1;
        private uint _requestBitrate;

        // stats
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private long _frames, _bytes; // written on the encoder thread, read on the UI thread: Interlocked only (64-bit on ARM)
        private long _lastStatTicks, _lastFrames, _lastBytes;
        private double _lastIdrSec = -1;
        public double Fps { get; private set; }
        public double Mbps { get; private set; }
        public double KeyframeIntervalSec { get; private set; }

        /// <summary>Use the front (selfie) camera instead of the rear one. Set before InitializeAsync.</summary>
        public bool UseFrontCamera;
        /// <summary>Whether the chosen camera was found (false: the other one is in use).</summary>
        public bool UsingFrontCamera { get; private set; }

        /// <summary>Seconds between keyframes.</summary>
        public double KeyframeSeconds = 1.0;

        public async Task InitializeAsync()
        {
            var devices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
            if (devices.Count == 0) throw new Exception("No camera found on this device.");
            var want = UseFrontCamera ? Panel.Front : Panel.Back;
            var dev = devices.FirstOrDefault(d => d.EnclosureLocation != null && d.EnclosureLocation.Panel == want);
            if (dev == null) Log((UseFrontCamera ? "No front" : "No rear") + " camera found - using the first camera instead");
            dev = dev ?? devices[0];
            UsingFrontCamera = dev.EnclosureLocation != null && dev.EnclosureLocation.Panel == Panel.Front;
            Log("Camera: " + dev.Name + (UsingFrontCamera ? " (front)" : ""));

            Capture = new MediaCapture();
            Capture.Failed += (s, e) => Log("Camera error: " + e.Message);
            var init = new MediaCaptureInitializationSettings
            {
                VideoDeviceId = dev.Id,
                StreamingCaptureMode = StreamingCaptureMode.Video
            };
            // Note: MediaCategory.Communications + DesiredOptimization=LatencyThenQuality were A/B tested on the
            // Lumia 950 and made no measurable difference, so they are not used (see NOTES.md).
            await Capture.InitializeAsync(init);
            try { Capture.CameraStreamStateChanged += (s, e) => Log("Camera stream state: " + s.CameraStreamState); }
            catch (Exception ex) { Log("(camera state events unavailable: " + ex.Message + ")"); }
        }

        public List<FormatOption> GetRecordFormats()
        {
            var all = Capture.VideoDeviceController
                .GetAvailableMediaStreamProperties(MediaStreamType.VideoRecord)
                .OfType<VideoEncodingProperties>()
                .Where(p => p.FrameRate.Denominator != 0)
                .Select(p => new FormatOption
                {
                    Props = p,
                    Width = p.Width,
                    Height = p.Height,
                    Fps = (double)p.FrameRate.Numerator / p.FrameRate.Denominator
                })
                .Where(f => f.Fps >= 14)
                .ToList();

            // one entry per size+rate, preferring NV12
            return all
                .GroupBy(f => f.Width + "x" + f.Height + "@" + Math.Round(f.Fps))
                .Select(g => g.OrderByDescending(f => string.Equals(f.Props.Subtype, "NV12", StringComparison.OrdinalIgnoreCase)).First())
                .OrderByDescending(f => (long)f.Width * f.Height)
                .ThenByDescending(f => f.Fps)
                .ToList();
        }

        /// <summary>True while the camera preview is running.</summary>
        public bool Previewing => _previewing;

        public async Task SetPreviewAsync(bool on)
        {
            if (Capture == null || on == _previewing) return; // avoid redundant start/stop calls on the camera
            // Starting or stopping the preview while a recording runs stalls the 4K pipeline and can end the app,
            // so the preview only ever changes while the stream is stopped (found in W10MSecurityCam).
            if (IsStreaming) { Log("Preview change skipped while streaming (applies when the stream restarts)"); return; }
            try
            {
                if (on) await Capture.StartPreviewAsync();
                else await Capture.StopPreviewAsync();
                _previewing = on;
            }
            catch (Exception ex) { Log("Preview: " + ex.Message); }
        }

        public static uint AutoBitrate(FormatOption f)
        {
            double bps = f.Width * (double)f.Height * f.Fps * 0.15;
            return (uint)Math.Max(2000000, Math.Min(60000000, bps));
        }

        private MediaEncodingProfile CreateProfile(FormatOption f, uint bitrate, int choice)
        {
            var p = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.Auto);
            p.Audio = null;
            p.Video.Width = f.Width;
            p.Video.Height = f.Height;
            p.Video.FrameRate.Numerator = f.Props.FrameRate.Numerator;
            p.Video.FrameRate.Denominator = f.Props.FrameRate.Denominator;
            p.Video.Bitrate = bitrate;
            uint gop = (uint)Math.Max(1, Math.Round(f.Fps * KeyframeSeconds));
            if (choice <= 1) p.Video.ProfileId = H264ProfileIds.Baseline; // no B-frames = lowest delay
            if (choice == 2) p.Video.ProfileId = H264ProfileIds.Main;
            if (choice <= 2) p.Video.Properties[MaxKeyframeSpacing] = gop;
            if (choice == 0)
            {
                p.Video.Properties[RateControlMode] = (uint)1; // peak-constrained VBR
                p.Video.Properties[MeanBitRate] = bitrate;
                p.Video.Properties[MaxBitRate] = (uint)Math.Min(uint.MaxValue, bitrate * 1.25);
                p.Video.Properties[GopSize] = gop;
                p.Video.Properties[LowLatency] = true;
            }
            return p;
        }

        private static readonly string[] ChoiceNames = { "Baseline + bitrate control", "Baseline", "Main", "encoder default" };

        /// <summary>
        /// Records ~1.5 s to a temp file once per setting. This (1) finds an encoder
        /// configuration the phone accepts, (2) reads the SPS/PPS decoder setup data,
        /// and (3) measures the real bitrate so we can correct for the encoder's overshoot.
        /// </summary>
        private async Task ProbeAsync(FormatOption f, uint targetBitrate)
        {
            string key = f + "/" + targetBitrate;
            if (_configKey == key && _config != null) return;

            // The phone's encoder ignores the requested bitrate, so the decoder setup (SPS/PPS)
            // for a resolution never changes. Remember it so later starts need no probe recording.
            var settings = ApplicationData.Current.LocalSettings.Values;
            string cacheKey = "Enc|" + (UsingFrontCamera ? "front|" : "") + f.Width + "x" + f.Height + "@" + Math.Round(f.Fps);
            if (settings[cacheKey] is string cached && TryLoadCached(cached))
            {
                _configKey = key;
                _requestBitrate = targetBitrate;
                Log("Using remembered encoder setup for " + f + " (no probe needed)");
                return;
            }

            Exception last = null;
            for (int choice = 0; choice < ChoiceNames.Length; choice++)
            {
                try
                {
                    Log("Probing encoder once for " + f + " (" + ChoiceNames[choice] + ")...");
                    var probe = await RecordProbeAsync(CreateProfile(f, targetBitrate, choice));
                    _config = probe.Item1;
                    _profileChoice = choice;
                    _requestBitrate = targetBitrate;
                    _configKey = key;
                    Log("Encoder OK: H.264 profile " + _config.Profile + " level " + _config.Level / 10.0 + ", " + (probe.Item2 / 1e6).ToString("0.0") + " Mbps");
                    settings[cacheKey] = SaveCached();
                    return;
                }
                catch (Exception ex)
                {
                    last = ex;
                    Log("  failed: " + ex.Message);
                    try { await Capture.StopRecordAsync(); } catch { }
                }
            }
            throw new Exception("Encoder would not start at this setting: " + last?.Message);
        }

        private string SaveCached()
        {
            return _profileChoice + "|" + _config.NalLengthSize + "|" + _config.Profile + "|" + _config.Level + "|" +
                   string.Join(",", _config.Sps.Select(Convert.ToBase64String)) + "|" +
                   string.Join(",", _config.Pps.Select(Convert.ToBase64String));
        }

        private bool TryLoadCached(string s)
        {
            try
            {
                var p = s.Split('|');
                var cfg = new H264Config
                {
                    NalLengthSize = int.Parse(p[1]),
                    Profile = int.Parse(p[2]),
                    Level = int.Parse(p[3])
                };
                cfg.Sps.AddRange(p[4].Split(',').Select(Convert.FromBase64String));
                cfg.Pps.AddRange(p[5].Split(',').Select(Convert.FromBase64String));
                if (cfg.Sps.Count == 0 || cfg.Pps.Count == 0) return false;
                _config = cfg;
                _profileChoice = int.Parse(p[0]);
                return true;
            }
            catch { return false; }
        }

        /// <summary>Forget remembered encoder setups (used if a stream ever fails to decode).</summary>
        public static void ClearEncoderCache()
        {
            var values = ApplicationData.Current.LocalSettings.Values;
            foreach (var k in values.Keys.Where(k => k.StartsWith("Enc|")).ToList()) values.Remove(k);
        }

        private async Task<Tuple<H264Config, double>> RecordProbeAsync(MediaEncodingProfile profile)
        {
            var file = await ApplicationData.Current.TemporaryFolder.CreateFileAsync("probe.mp4", CreationCollisionOption.ReplaceExisting);
            await Capture.StartRecordToStorageFileAsync(profile, file);
            var sw = Stopwatch.StartNew();
            await Task.Delay(2000);
            await Capture.StopRecordAsync();
            double seconds = Math.Max(0.5, sw.Elapsed.TotalSeconds);
            var buffer = await FileIO.ReadBufferAsync(file);
            byte[] bytes = buffer.ToArray();
            var cfg = AvcConfigReader.Find(bytes);
            if (cfg == null) throw new Exception("no avcC found in probe file (" + bytes.Length + " bytes)");
            return Tuple.Create(cfg, bytes.Length * 8 / seconds);
        }

        public async Task StartStreamingAsync(FormatOption f, uint bitrate)
        {
            if (IsStreaming) return;
            Log("Setting camera to " + f + ", " + (bitrate / 1e6).ToString("0.#") + " Mbps");
            await Capture.VideoDeviceController.SetMediaStreamPropertiesAsync(MediaStreamType.VideoRecord, f.Props);
            await ProbeAsync(f, bitrate);

            // Hand the decoder setup to the server first.
            foreach (var sps in _config.Sps) NalReady?.Invoke(sps, 7);
            foreach (var pps in _config.Pps) NalReady?.Invoke(pps, 8);

            System.Threading.Interlocked.Exchange(ref _frames, 0);
            System.Threading.Interlocked.Exchange(ref _bytes, 0);
            _lastFrames = _lastBytes = 0;
            _lastStatTicks = _clock.ElapsedTicks;
            _lastIdrSec = -1;

            if (UseNativeSink)
            {
                try
                {
                    var profile = CreateProfile(f, _requestBitrate, _profileChoice);
                    _nativeSink = new W10MWebCam.Native.EncodedFrameSink(profile.Video);
                    _nativeSink.LogMessage += m => Log("[native] " + m);
                    _nativeSink.FrameReceived += OnEncodedFrame;
                    _nativeHeaderChecked = false;
                    _nativeFrames = 0; _nativeMaxGapMs = 0; _nativeLastMs = -1; _nativeStatMs = _clock.ElapsedMilliseconds; _nativeStartMs = _clock.ElapsedMilliseconds;
                    await Capture.StartRecordToCustomSinkAsync(profile, _nativeSink.MediaExtension);
                    IsStreaming = true;
                    PathName = "low-latency";
                    Log("Streaming started (low-latency native path)");
                    return;
                }
                catch (Exception ex)
                {
                    Log("Native path failed: " + ex.Message + " - using MP4 path instead");
                    try { await Capture.StopRecordAsync(); } catch { }
                    DisposeNativeSink();
                }
            }

            _extractor = new Mp4H264Extractor(Log) { NalLengthSize = _config.NalLengthSize };
            _extractor.NalReady += OnNal;
            _tap = new Mp4TapStream(_extractor, Log);
            await Capture.StartRecordToStreamAsync(CreateProfile(f, _requestBitrate, _profileChoice), _tap);
            IsStreaming = true;
            PathName = "MP4";
            Log("Streaming started (MP4 path)");
        }

        private void DisposeNativeSink()
        {
            if (_nativeSink == null) return;
            _nativeSink.FrameReceived -= OnEncodedFrame;
            _nativeSink.Dispose();
            _nativeSink = null;
        }

        /// <summary>Encoded sample straight from the hardware encoder (native path).</summary>
        private long _latSum, _latCount, _latMax, _devSum, _devCount, _devMax, _maxFrameBytes;

        private int _frameErrors;
        private volatile int _frameStep; // where the frame handler was when it threw (release builds strip stack traces)

        private void OnEncodedFrame(byte[] data, long sampleTime, bool keyframe, long queueDelay, long deviceLatency)
        {
            // Runs on the native sink's worker thread. An exception escaping here ended the app with a fast-fail
            // (0xC0000409) in the forks, so catch everything and log the first few.
            try { OnEncodedFrameCore(data, sampleTime, keyframe, queueDelay, deviceLatency); }
            catch (Exception ex)
            {
                if (_frameErrors++ < 5)
                    Log("Frame handler error at step " + _frameStep + " (frame skipped): " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private void OnEncodedFrameCore(byte[] data, long sampleTime, bool keyframe, long queueDelay, long deviceLatency)
        {
            if (data == null || data.Length == 0) return; // an empty sample at start-up: nothing to send
            _frameStep = 1;
            if (queueDelay >= 0 && queueDelay < 100000000) { _latSum += queueDelay; _latCount++; _latMax = Math.Max(_latMax, queueDelay); }
            if (deviceLatency >= 0 && deviceLatency < 100000000) { _devSum += deviceLatency; _devCount++; _devMax = Math.Max(_devMax, deviceLatency); }
            _maxFrameBytes = Math.Max(_maxFrameBytes, data.Length);
            if (!_nativeHeaderChecked)
            {
                _nativeHeaderChecked = true;
                try
                {
                    var header = _nativeSink?.GetSequenceHeader();
                    if (header != null && header.Length > 0)
                    {
                        Log("Encoder sequence header: " + header.Length + " bytes");
                        foreach (var nal in AnnexB.Split(header)) NalReady?.Invoke(nal, nal[0] & 0x1F);
                    }
                }
                catch (Exception ex) { Log("Sequence header: " + ex.Message); }
            }

            long now = _clock.ElapsedMilliseconds;
            if (_nativeLastMs >= 0) _nativeMaxGapMs = Math.Max(_nativeMaxGapMs, now - _nativeLastMs);
            _nativeLastMs = now;
            _nativeFrames++;
            if (now - _nativeStatMs >= 10000) // periodic performance line in the log
            {
                string dev = _devCount > 0
                    ? string.Format("sensor->encoded avg {0} ms / max {1} ms", _devSum / _devCount / 10000, _devMax / 10000)
                    : "sensor->encoded n/a";
                string q = _latCount > 0
                    ? string.Format(", hand-off queue avg {0:0.0} ms / max {1:0.0} ms", _latSum / (double)_latCount / 10000, _latMax / 10000.0)
                    : "";
                Log(string.Format("Encoder delivery (last {0:0.0}s): {1} samples, longest pause {2} ms, biggest frame {3} KB, {4}{5}",
                    (now - _nativeStatMs) / 1000.0, _nativeFrames, _nativeMaxGapMs, _maxFrameBytes / 1024, dev, q));
                LastLatencyMs = _devCount > 0 ? _devSum / _devCount / 10000 : -1;
                _nativeStatMs = now; _nativeFrames = 0; _nativeMaxGapMs = 0;
                _latSum = _latCount = _latMax = _devSum = _devCount = _devMax = _maxFrameBytes = 0;
            }

            _frameStep = 2;
            LastSampleTime = sampleTime;
            foreach (var nal in AnnexB.Split(data))
                if (nal.Length > 0) OnNal(nal, nal[0] & 0x1F);
        }

        /// <summary>Seconds since the low-latency engine last delivered a frame (counts from stream start).</summary>
        public double SecondsSinceLastFrame =>
            (_clock.ElapsedMilliseconds - (_nativeLastMs >= 0 ? _nativeLastMs : _nativeStartMs)) / 1000.0;
        private long _nativeStartMs;

        /// <summary>Average capture-to-encoded latency over the last 5 s window (ms), or -1.</summary>
        public long LastLatencyMs { get; private set; } = -1;

        /// <summary>Encoder timestamp of the latest sample (100 ns units); used later for RTSP.</summary>
        public long LastSampleTime { get; private set; }

        public async Task StopStreamingAsync()
        {
            if (!IsStreaming) return;
            IsStreaming = false;
            if (_extractor != null) _extractor.Stopped = true; // ignore the index the recorder writes at the end
            try { await Capture.StopRecordAsync(); } catch (Exception ex) { Log("Stop: " + ex.Message); }
            _tap?.Dispose();
            _tap = null;
            _extractor = null;
            DisposeNativeSink();
            Fps = Mbps = 0;
            Log("Streaming stopped");
        }

        private void OnNal(byte[] nal, int type)
        {
            System.Threading.Interlocked.Add(ref _bytes, nal.Length + 4);
            bool firstSlice = (type == 1 || type == 5) && nal.Length > 1 && (nal[1] & 0x80) != 0;
            if (firstSlice)
            {
                System.Threading.Interlocked.Increment(ref _frames);
                if (type == 5)
                {
                    double now = _clock.Elapsed.TotalSeconds;
                    if (_lastIdrSec >= 0) KeyframeIntervalSec = now - _lastIdrSec;
                    _lastIdrSec = now;
                }
            }
            NalReady?.Invoke(nal, type);
        }

        /// <summary>Call about once a second to refresh Fps / Mbps.</summary>
        public void UpdateStats()
        {
            long now = _clock.ElapsedTicks;
            double sec = (now - _lastStatTicks) / (double)Stopwatch.Frequency;
            if (sec <= 0) return;
            long f = System.Threading.Interlocked.Read(ref _frames), b = System.Threading.Interlocked.Read(ref _bytes);
            Fps = (f - _lastFrames) / sec;
            Mbps = (b - _lastBytes) * 8 / sec / 1e6;
            _lastFrames = f; _lastBytes = b; _lastStatTicks = now;
        }

        public async Task ShutdownAsync()
        {
            await StopStreamingAsync();
            if (Capture != null)
            {
                try { await Capture.StopPreviewAsync(); } catch { }
                Capture.Dispose();
                Capture = null;
            }
        }
    }
}
