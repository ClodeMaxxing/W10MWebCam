# W10MWebCam — developer notes

Reference for future work and maintenance. User-facing setup is in README.md, release history in CHANGELOG.md.

## Family

| App | What | Relationship |
|---|---|---|
| **W10MWebCam** (this repo; called LumiaCam up to 1.0) | USB/Wi-Fi webcam, raw H.264 over TCP for OBS | the original |
| [W10MSecurityCam](https://github.com/ClodeMaxxing/W10MSecurityCam) | RTSP security camera (NVR / VLC), login, audio, snapshots | fork of LumiaCam 1.0 |
| [W10MDashcam](https://github.com/ClodeMaxxing/W10MDashcam) | 4K dashcam: loop recording, clips, crash detection, GPS | fork of W10MSecurityCam |

All three share the camera engine (`Streaming/CameraEngine.cs`) and the native encoder sink (`*.Native`). A fix in one
usually applies to the others. Their NOTES.md files carry these notes' 1.0 sections under the name LumiaCam.

## Project layout

```
W10MWebCam.sln
W10MWebCam/                      C# UWP app (.NET Native, ARM), SDK 10.0.19041, min 10.0.14393
  MainPage.xaml(.cs)             UI, settings, start/stop, blackout, auto-start, crash guard, self-check, stall watchdog, logging
  MainPage.Camera.cs             focus / exposure / white balance, focus once, flashlight, camera power saving
  MainPage.Power.cs              power readout, charger notice, heat detection + optional step-down, stop on power loss
  MainPage.Web.cs                /status and /settings/state JSON, applying /settings/set through the phone's controls
  MainPage.Status.cs             status card (tiles, badges, meters), folding sections, cached brushes (theme from W10MDashcam)
  Streaming/CameraEngine.cs      MediaCapture setup, format list, encoder profile, engine selection, frame handler
  Streaming/CameraTuning.cs      camera controls (ported from W10MSecurityCam)
  Streaming/PowerMonitor.cs      battery flow and CPU (ported from W10MSecurityCam)
  Streaming/StreamServer.cs      TCP listeners (5000 Wi-Fi, 8020 USB), keyframe-synced broadcast, backlog control,
                                 HTTP sniffing on the same ports
  Streaming/HttpApi.cs           web routes, request reading/parsing, write protection
  Streaming/WebPage.cs           the settings page (HTML + script in a C# verbatim string: no double quotes inside)
  Streaming/LogServer.cs         HTTP on 5001 (Wi-Fi)
  Streaming/Mp4TapStream.cs      fallback engine: IRandomAccessStream the MP4 recorder writes into
  Streaming/Mp4H264Extractor.cs  self-synchronizing NAL scanner over the MP4 mdat bytes
  Streaming/AvcConfigReader.cs   SPS/PPS from the avcC of a short probe recording
  Streaming/AnnexB.cs            splits Annex-B byte streams into NAL units
W10MWebCam.Native/               C++/CX WinRT component (WRL)
  EncodedMediaSink.h/.cpp        IMFMediaSink / IMFStreamSink / IMFMediaTypeHandler / IMFClockStateSink
  EncodedFrameSink.h/.cpp        public ref class for C# (MediaExtension, FrameReceived, LogMessage)
tools/                           build / deploy / log / soak scripts
```

Build: `tools\Build-And-Deploy.cmd` (MSBuild Release|ARM → `out\` → WinAppDeployCmd, USB `-ip 127.0.0.1` first, then
Wi-Fi with the address in `tools\phone-ip.txt`). Bump `Version` in `W10MWebCam/Package.appxmanifest` for every install
(the phone refuses a package with the same version and different contents). The title shows Major.Minor(.Build).

Identity: `W10MWebCam`, publisher `CN=W10MWebCam`, PhoneProductId `db67b76b-1f27-4598-afb2-16fda115fa58`, project GUIDs
`{3561A25B-…}` (C#) and `{BC54E87C-…}` (native). All differ from LumiaCam, so both can be installed side by side; the
LocalSettings (and the encoder cache) do not carry over.

## Pipeline

```
Rear camera ─► MediaCapture (record stream, H.264 hardware encoder)
   │  StartRecordToCustomSinkAsync(profile, EncodedFrameSink.MediaExtension)      ◄── "low-latency" engine
   │     ProcessSample → copy → queue (max 3, else drop until next IDR) → worker thread → FrameReceived
   │  fallback: StartRecordToStreamAsync(MP4) → Mp4TapStream → Mp4H264Extractor     ◄── "MP4" engine
   ▼
CameraEngine.OnNal ─► StreamServer.Broadcast ─► each viewer: SPS+PPS, wait for IDR, then live NALs
                                               (raw Annex-B H.264 over TCP; no container, no audio)
```

- **SPS/PPS:** the native sink reads MF_MT_MPEG_SEQUENCE_HEADER; for robustness a one-time probe recording is parsed
  (avcC) and cached in LocalSettings under `Enc|WxH@fps`. `CameraEngine.ClearEncoderCache()` resets it.
- **Encoder properties** on the profile: MF_MT_MAX_KEYFRAME_SPACING, CODECAPI rate control / mean / max bitrate, GOP
  size, CODECAPI_AVLowLatencyMode. The Lumia honours bitrate and GOP on the native path (the MP4 path ignores bitrate).
- **Native sink:** stream 0 is pre-created from the profile properties (MFCreateMediaTypeFromProperties) and reused by
  AddStreamSink. PlaceMarker completes at once. Per-frame latency: the DeviceReferenceSystemTime attribute
  {6523775A-BA2D-405F-B2C5-01FF88E2E8F6} against MFGetSystemTime.
- **Callbacks never throw** (1.1, from W10MSecurityCam): the worker thread's calls into C#, and `RaiseFrame`/`RaiseLog`,
  are wrapped in try/catch; an exception that escaped used to reach `std::terminate` (fast-fail 0xC0000409 in ucrtbase,
  "the app closes a second after the first 4K frame"). The C# frame handler also catches everything, logs the first five
  with a `_frameStep` marker (release builds strip stack traces) and ignores empty samples.
- **Viewer backlog:** time-based (drop and resync at the next IDR when a viewer is > 1.5 s behind); a viewer with no
  successful send for 6 s is closed. A size-based limit (1 MB) wrongly dropped valid 4K video.

## Web interface (1.1)

- Port 8020 is the **only** port IP-over-USB forwards, so `StreamServer.WatchInputAsync` reads the first bytes a client
  sends: video viewers send nothing; `GET ` / `POST ` / `HEAD ` makes the client an HTTP client (`MarkHttp`), the whole
  request is read (`HttpApi.ReadAsync`, Content-Length body up to 64 KB, 10 s limit) and answered by `HttpApi`.
  Port 5001 (`LogServer`) serves the same routes for Wi-Fi.
- Viewers are logged only when their video starts flowing ("Viewer connected: PC over USB - video flowing"), so the
  page polling every 2 s does not flood the log. `ClientCount` counts synced viewers only.
- **Write protection** (no login in this app): `/settings/set` needs (1) the USB cable (remote address 127.0.0.1),
  (2) the header `X-Requested-With: W10MWebCam` (another website cannot add a custom header without a CORS grant,
  which is never given) and (3) a `Host` of 127.0.0.1 or localhost (defeats DNS rebinding). Wi-Fi is view only.
- Changes from the page are applied by setting the phone's own controls on the UI thread (`OnUiAsync`), so both screens
  agree and all rules (restart on video changes, preview only while stopped, flashlight limit) apply to both.

## Measurements (Lumia 950, W10M 10.0.15254.603)

| Item | Low-latency engine | MP4 engine |
|---|---|---|
| Sensor → encoded frame | ~112-115 ms avg at 4K30 | n/a |
| App hand-off queue | ~0.3 ms | — |
| Viewer send queue (USB) | ~1 ms | — |
| Delivery pattern | steady per frame | bursts with ~350 ms recorder pauses |
| Bitrate at 4K30 | ~35 Mbps (Auto) | ~68 Mbps (ignores the setting) |
| Phone CPU / app CPU at 4K30 | ~30 % / ~20 % (1.1, blackout on, preview off) | — |
| App memory | ~535 MB of 1536 MB | — |

- USB via IP-over-USB carried ≥ 68 Mbps sustained, so 4K30 has plenty of headroom.
- The remaining end-to-end delay is mostly the phone camera/encoder (~115 ms) plus OBS/FFmpeg decode and buffering.
  The OBS FFmpeg options in README.md matter more than anything left on the phone.
- **A/B tested, no effect** (removed in 1.0): MediaCategory.Communications + DesiredOptimization = LatencyThenQuality.
  Shorter keyframe intervals do not change latency; they only speed up joins and recovery.
- From the forks: keeping the preview running behind the black overlay doubles the app's CPU (12 % → 24 %). At 4K30 /
  24 Mbps on a PC port the phone reported "Charging" while 7-10 W left the battery; a 2 A charger keeps up.
  1080p at 6 Mbps let a hot phone resume charging within ~30 s.

## Transport discoveries

- Windows 10 Mobile has no USB webcam (UVC) gadget mode for apps. The usable USB path is Microsoft's **IP over USB**
  service (`IpOverUsbSvc`, from the Windows 10 SDK 1709 MSI). It forwards PC localhost ports to whitelisted phone
  ports only.
- Phone port **8020** (the `VSTestFramework` entry) is forwarded and reachable by a normal app: PC `127.0.0.1:8020` →
  phone `:8020`. Custom entries added under `HKLM\SOFTWARE\Microsoft\Windows Phone\IpOverUsbSdk\`
  (LumiaCamVideo / LumiaCamLog / LumiaCamTest*) did **not** work; `tools\Remove-Dead-IpOverUsb-Keys.cmd` deletes them.
- Wi-Fi works, but 2.4 GHz cannot carry 4K reliably. Ethernet through a ThinkPad USB-C Dock Gen 2 got no IP address
  (and would be 100 Mbps anyway).
- After a phone reboot the USB link sometimes stays down until the cable is replugged.

## Platform limits

- **Screen lock / app switch revokes the camera.** ExtendedExecution and the restricted `backgroundMediaRecording`
  capability did not help (removed in 1.0). Workaround: DisplayRequest + blackout overlay (black OLED pixels +
  BrightnessOverride to 0).
- **No start at boot** for third-party apps. Possible only with an interop/root unlock; not attempted.
- **No charging while off:** the phone boots when power is connected.
- **No temperature for apps.** The frame rate does not show heat either (it stayed at 25 fps while Windows warned of
  overheating). The reliable sign: charging pauses (status Idle) on a charger Windows calls Adequate, below 95 %, for
  20 s. Only `Discharging` means unplugged; a heat pause reports `Idle`.
- The battery driver's own charge rate is stale on this phone; `PowerMonitor` uses the capacity change instead.
- `SystemDiagnosticInfo` (phone CPU) needs build 17134; `PowerMonitor` catches the failure on older builds.

## Stability lessons

1. Subscribing `CameraStreamStateChanged` before `InitializeAsync` crashed at startup → subscribe after.
2. **The preview only changes while the stream is stopped.** Starting or stopping it during a 4K recording stalls the
   pipeline and can end the app. `CameraEngine.SetPreviewAsync` refuses while streaming; the start sequence decides the
   preview first; blackout only changes the overlay and brightness; the Preview switch applies at the next stop/start.
   Also: don't spin the preview up at launch when auto-start + blackout will turn it off; wait 2 s after init before
   auto-starting.
3. Work on the Media Foundation callback thread stalled 4K → copy and hand off to a worker thread.
4. Awaiting ExtendedExecution blocked the UI "busy" state → removed; a 30 s busy watchdog unlocks the controls.
5. Exceptions escaping the native worker thread end the app (see *Callbacks never throw*).
6. Stabilization (OIS) mode is only changed while no recording runs (`CameraTuning.ApplyPower` before the start), so
   changing it restarts the stream once. Focus, exposure and white balance can change while recording; `CameraTuning`
   only resends the control that changed (repeated driver calls during recording caused errors).
7. Exposure values must be a multiple of the camera's step (0.01 ms on the 950) or the driver answers "parameter is
   incorrect" (`SnapExposureMs`); manual exposure is capped at 1000/fps so the frame rate holds.
8. UWP `ToggleSwitch.Toggled` and `ComboBox.SelectionChanged` fire when set from code: every handler checks `_loading`.

Safety nets:
- **Crash guard / SAFE MODE:** `StartInProgress` is set before a stream start and cleared after 15 s of healthy
  streaming, on stop and on suspend. Set at launch → no auto-start and the MP4 engine for that launch only (the settings
  page has a button to return to the low-latency engine).
- **Self-check (1.1):** `LaunchInProgress` is set at launch and cleared after 20 s or on suspend. Two failed launches
  in a row → LocalSettings cleared (defaults) and `SELF-CHECK` logged.
- **Stall watchdog:** no native frames for 4 s → restart; two stalls within 2 min → MP4 engine until reopened.
- **Persistent log:** `LocalFolder\log.txt` (restarted above ~2 MB); the previous session is kept as
  `log-previous.txt` and its last 120 lines are appended to the served log.

## Dev-environment notes

- Batch files: a `)` inside an `if (...)` block must be escaped `^)`; `%errorlevel%` inside a block is expanded early —
  use `call set RC=%%errorlevel%%`.
- SDK 17763 wasn't installed → the project targets 19041 (min 14393 still runs on 15254). Avoid APIs newer than 14393
  without a guard (`ContentDialog.CloseButtonText` needs 15063).
- Copying files to the PC through Claude's file bridge: stage into a **new** folder each time (a reused staged path once
  delivered stale content), compare SHA-256 on the PC before building, keep sources CRLF.
- Files unpacked from a zip made in the cloud workspace carry its clock (3 hours behind the PC). MSBuild then thinks
  the outputs are newer and silently skips the C# compile: set `LastWriteTime` to now on the unpacked sources (or
  delete `W10MWebCam\obj`) before building. The 1.1 development build 1.1.0.2 shipped stale code this way.
- `cmd /c tools\Build-And-Deploy.cmd` from PowerShell ends with "Input redirection is not supported" because of the
  closing `timeout`; run it as `cmd /c "tools\Build-And-Deploy.cmd < nul"` or ignore it.
- Installing an update closes the running app (and it opens again by itself). WinAppDeployCmd over USB during a 4K
  stream takes up to ~2.5 min; it does finish.
- Device Portal can launch the app without touching the phone: GET `/api/os/info` with a cookie jar, take the newest
  `CSRF-Token` cookie, then `POST /api/taskmanager/app?appid=<base64 PRAID>&package=<base64 full name>` with header
  `X-CSRF-Token` and `Content-Length: 0`. A duplicated CSRF cookie gives 403. Crash dumps:
  `/api/debug/dump/usermode/crashdump`.
- The signing key `W10MWebCam/W10MWebCam_TemporaryKey.pfx` is git-ignored; `W10MWebCam.cer` is its public certificate.

## Future work

- **Audio:** the raw Annex-B stream has no place for it. W10MSecurityCam has AAC in the native sink (second stream sink)
  and RTP; an MPEG-TS wrapper on the TCP stream would be the lighter way here. OBS can use the PC microphone meanwhile.
- **RTSP as an optional transport:** W10MSecurityCam's `Rtsp/` folder is self-contained and OBS reads `rtsp://`.
- **Local backup recording:** W10MDashcam's `FragmentedMp4Writer` + `SegmentRecorder` write the same encoded frames to
  fragmented MP4 (no second encode). Never record into an indexed library folder (the indexer cost thousands of frames).
- A PC-side virtual camera that reads TCP 8020 directly (no OBS).
- Front camera / camera switch.
