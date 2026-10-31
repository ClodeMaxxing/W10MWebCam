# Changelog

## 1.1 — W10MWebCam (first public release)

Renamed from LumiaCam, and the improvements from its forks (W10MSecurityCam 1.1, W10MDashcam 0.7.6) brought back.

- **New name and identity:** W10MWebCam (`CN=W10MWebCam`, new package and project IDs, new test certificate). It installs
  beside LumiaCam; LumiaCam's settings are not carried over. Uninstall LumiaCam once W10MWebCam works.
- **Crash fix:** exceptions can no longer escape the native encoder sink's worker thread (they ended the app with a
  fast-fail in the forks); the C# frame handler also catches and logs errors and ignores empty samples.
- **Preview rule:** the preview only starts or stops while the stream is stopped. The Preview switch applies at the
  next stop/start, and turning the blackout on or off during a stream only changes the overlay and brightness.
- **Self-check:** two failed launches in a row reset the saved settings to defaults (`SELF-CHECK` in the log).
  Complements SAFE MODE, which affects one launch.
- **Settings page on the PC:** `http://127.0.0.1:8020/settings` over the USB cable (status, video, camera, phone and power
  settings); view only over Wi-Fi at `http://PHONE-IP:5001/settings`. `/status` gives health as JSON. Changes need the
  USB cable and the page itself (custom header and host checks).
- **Camera controls:** automatic or manual focus, exposure and white balance (sliders on the settings page), Focus once
  and lock, flashlight (only while streaming, off after 30 s), stabilization off for a fixed mount, hold focus once
  settled. The last two are off by default.
- **Power readout:** battery flow in watts (5-minute average), phone and app CPU on the phone, the page and a "Power:"
  log line every minute.
- **Charger insufficient notice** when the battery drains although the phone says it is charging.
- **Heat:** a warning when charging pauses on a good charger for 20 s; optional automatic step-down to 1080p / 6 Mbps
  until charging resumes (off by default; your settings are never overwritten).
- **Stop when unplugged** (optional): countdown on the screen, then the stream stops; it restarts when power returns.
- Quieter log: web requests are not logged as viewers; viewers are logged when their video starts flowing.
- Install scripts read the phone's Wi-Fi address from `tools\phone-ip.txt` instead of a fixed address.
- Tools: `Soak-Monitor.ps1` (long-run CSV + HTML report from `/status`), `Monitor-Portal.ps1` (Device Portal load),
  `Remove-Dead-IpOverUsb-Keys.cmd`.
- **Green theme, shared with W10MSecurityCam and W10MDashcam,** on the phone and the settings page: a status card with a
  pulsing dot, state, resolution and viewer pills, big fps / Mbps figures and tiles (battery bar, Wi-Fi bars, temperature
  badge, phone and app CPU, memory, engine badge, encoder latency, power in watts); red and amber notices; folding
  sections (Video, Camera, Power, Details and log) with (i) pop-ups instead of long labels. Brushes are cached and the
  tiles are not redrawn while the screen is black.
- **Manual camera sliders on the phone** (as in W10MDashcam): switching off "Automatic focus, exposure and white balance"
  shows a slider and an Auto / Manual switch for each, starting from what the camera is using; all three back on Auto
  hides them again.
- **Picture while adjusting:** unticking automatic (or touching a manual control) on the phone lifts the black screen and
  shows the camera picture; 60 s after the last touch the picture ends and the screen goes black again. While streaming,
  starting and ending the picture each restart the stream once (the preview can't change during a 4K recording); OBS
  reconnects by itself. Changes on the PC settings page never do this.
- The log keeps the last three sessions (each marked crashed, closed normally or ended abruptly), so a crash is still
  visible after the app has been reopened; camera-control handlers catch and log errors instead of ending the app.
- Switching focus to manual starts from the last sensible lens position: the 950's autofocus sometimes reports the near
  end stop (0) while the picture is sharp, which made the manual picture jump out of focus.
- 6 Mbps added to the bitrate list.

## 1.0 — LumiaCam

- Low-latency native encoder sink at 4K30 (~115 ms sensor to encoded frame), MP4 engine as automatic fallback.
- USB streaming through IP-over-USB port 8020, Wi-Fi on port 5000, log on 5001.
- Blackout screen (the phone can charge while streaming 4K), auto-start, crash guard / SAFE MODE, stall watchdog,
  persistent log.
- Latency mode and background-recording capability removed after A/B tests showed no effect.
