# Changelog

## 1.4.2 (package 1.4.2.0)

**Preview and black screen, one rule set in all three apps** (`MainPage.Display.cs`, kept in step by Sync-Core):
- **Preview on the phone** moved from *Video* to *Camera* and is **off by default** (was on). On: the picture while
  stopped, and while streaming unless the screen is blacked out.
- **Black screen while streaming** also stops the preview. New under *Power*: **Keep the preview running behind the
  black screen** (off by default: it costs CPU and heat), so a tap on the black screen shows the picture at once;
  heat protection forces it off and puts it back afterwards.
- **Show camera picture (60 s)** and **Black out the screen now** buttons on the main screen, as in the other apps;
  **a tap on the dark preview area** shows the picture too. A preview that disagrees with the black screen is fixed
  with one restart (automatic ones at most once a minute), so the screen can no longer go black with the preview
  still running.
- The manual focus / exposure / white balance sliders are always on screen, greyed while automatic, and enable the
  moment *Automatic* is switched off instead of after the camera has taken the settings.
- The settings page gains *Keep the preview running behind the black screen*; `/settings/set` gains `keeppreview=`.

## 1.4.1 (package 1.4.1.0)

Feature parity with W10MSecurityCam and W10MDashcam: what one app of the family did better now exists in all three where
it applies. The USB and Wi-Fi video, the viewers and the login are unchanged.

**What you notice**
- **Heat protection is on by default and does the full set:** when charging pauses on a good charger for 20 s the
  stream drops to 1080p / 6 Mbps and the screen goes black, stabilization is switched off, focus is held and the
  flashlight is locked until charging has resumed; the greyed controls and the web page say so, and your own settings
  come back afterwards. *Lower to 1080p if the phone overheats* off = warning only, as before. When the stream was busy
  at that moment the restart is now owed and retried, so the stream can no longer stay at 4K under heat protection.
- **A chime and a vibration** when the power-loss countdown starts and when heat protection sets in; a short one when
  charging resumes.
- **Service pills under the start button:** USB video (viewers), Wi-Fi video (address, viewers or OFF) and the web
  page, green while streaming and grey when stopped. Tapping the Wi-Fi or Web pill flips the same switch as under
  *Network*; tapping USB opens *Network*. The button says *Start video stream*.
- **Changing the login needs the current password** (new *Current password* box), as in W10MDashcam; the reset to
  admin / admin needs it too.
- **4K start hygiene** from the other apps: 6 s of settling before a 4K auto-start (a cold 4K start right after power-up
  crashed the camera pipeline there), and the camera settings are re-sent 2.5 s after a start instead of at once.
- **Crash guards for the camera settings:** if the app dies while sending them (or the stabilization setting), it comes
  back with automatic settings instead of crashing again on a saved value. Settings are sent one after another, never
  two at once.
- *Hold focus once it has settled* first tries 30 s after a start and, when it fails (too dark), again after 10 minutes
  instead of every minute.

**Under the hood**
- A memory line in the log every 30 minutes (and every 5 past 75 % of the limit), with the peak; `/status` gains
  `peakMemoryMB`, `managedMemoryMB`, `restarts` and `lastRestartReason`.
- `/status` also carries the other apps' field names (`uptimeSeconds`, `batteryPercent`, `sensorToEncodedMs`,
  `keyframeSeconds`, `wifiSignalBars`, `cpuPhonePct`, `cpuAppPct`, `bitrateSetting`, `cameraMode`, `loginRequired`,
  `time`) plus `charging` and `onExternalPower`; the existing names stay.
- Changes (`/settings/set`) are read from the request body only, never from the URL.
- The web page reloads itself after an app update.
- `tools\Soak-Monitor.ps1` is the same script in all three repositories and collects the phone log every 5 minutes
  while the log page is on; `tools\Make-Release.ps1` finds the app's name itself and takes `-Version`.
- The project references the Windows Mobile Extensions for the vibration API.

## 1.3 (package 1.3.0.0)

**What you notice**
- **New passwords need at least 8 characters** (8-64), on the phone and on the settings page. A shorter password set
  before keeps working until you change it.
- **Wrong logins are slowed down:** after 5 wrong logins from one address within a minute, that address is refused for
  30 seconds (`429 Too Many Requests`). A right login clears the count. Viewing the page without a login is not
  affected.
- The login is renewed every 5 minutes; the browser takes the new one by itself without asking for the password again.
  A wrong or outdated login the browser sends with `/settings/state` now gets a 401, so the browser renews it instead
  of the page silently dropping to view only.
- **Every page checks the address it was opened with,** not only changes: the `Host` must be `127.0.0.1` or
  `localhost` over USB, or one of the phone's addresses over Wi-Fi, otherwise the phone answers 403 (so another site
  that points its own name at the phone can't read the log or the settings). Tools that send no `Host` still get the
  pages. The phone's addresses are read again when the network changes (a new DHCP lease, another Wi-Fi network), so
  the shown addresses and this check follow; every current address passes, not only the first one shown.
- **Viewer limits:** USB and Wi-Fi each take at most 4 video viewers, at most 2 from one Wi-Fi address, and answer at
  most 8 web requests at a time (more get `503` "Busy, try again"). A viewer over the limit is closed just before its
  video would start.
- The Wi-Fi web port (5001) answers at most 16 connections at once (more get `503`). A web request has to arrive
  complete within 10 seconds on every port, and a request with a body over 64 KB is refused.
- The pages can no longer be shown inside a frame on another site. An error answer (500) only says "Internal error,
  see the app log"; the details go to the app log.
- The bitrate is saved in bits per second, as in W10MSecurityCam and W10MDashcam, so a change to the bitrate list can
  no longer pick another bitrate. The saved choice is converted once on the first start after the update.

**Fixes**
- A `HEAD` request gets the headers (`Content-Length` included) without the body.
- Why a viewer or web client dropped is logged again (at most one line per 10 seconds, not when the phone closed it on
  purpose).
- If the Windows password vault fails while storing the default login, the app still starts and the log says so.
- Errors in the encoder sink's frame handler are reported again after a stream restart.
- Failed web requests on port 5001 are logged, at most one line per 30 seconds; refused viewers and requests at most
  once per 10 seconds.

**Under the hood**
- Login: random nonces, kept for 5 minutes; `qop="auth"` with a nonce count that must go up; each login answer must be
  for the address requested. The login is checked once per request.
- The video ports send each frame to a viewer as one buffer and one write instead of one write per NAL unit.
- The fps and Mbps counters are read and written safely between the encoder thread and the screen (64-bit values on
  ARM).
- The encoder sink hands each frame to the app without an extra copy.
- The log: a line is only queued when it is logged; the file and the screen are updated once a second (also on suspend
  and after a crash). The served log is unchanged.
- Unused code removed (encoder-cache clear, `MaxQueueBytes`, `LastSampleTime`, `PowerMonitor.Dump`).

**Tools**
- `tools\Sync-Core.ps1` compares the source files the three apps share (`-Check`, the default) and copies one app's
  copy over the others (`-Fix -From APP`). It needs all three repositories on the PC.
- `tools\Build-And-Deploy.cmd` finds Visual Studio 2019's MSBuild with vswhere (the 2019 Community path if vswhere is
  missing) and logs which one it used. It and `tools\Install-Only.cmd` now report the right exit code after the
  pairing retry.

## 1.2 (package 1.2.0.0)

- **Video over Wi-Fi is off by default.** The raw video has no login (OBS and VLC can't send one), so with it on anyone
  on the network could watch. While no choice is saved (a new install, an update from a version that had it on by
  default, or after the self-check reset), the phone asks once: *Keep it off* / *Turn it on*. The USB stream starts
  meanwhile, so a phone used unattended still works. A choice made before is kept.
- The startup self-check (two failed launches reset the settings) can no longer switch Video over Wi-Fi back on.

## 1.1.2 (package 1.1.0.16)

- The phone and the settings page no longer say that changes over USB need no login, which 1.1.1 changed: the login
  heading is now **Login (for changes)**, and the notices, the login status and the USB address on the page say the
  login is needed over USB and Wi-Fi.
- The login tooltip on the page said an empty password removes the login; it resets it to admin / admin.

## 1.1.1 (package 1.1.0.15)

- **Settings changes need the login over the USB cable too.** Any program or web page on the PC the phone is plugged
  into can reach the USB port, so being on USB proves nothing. The page asks for the login before the first change
  (default admin / admin until you change it on the phone). The video stream and the read-only pages are unchanged.
- Page and phone help texts no longer say USB can always change settings.

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
- **Network switches** (as in W10MDashcam): Web page (settings and /status; on by default), Log page (/log) and Stats
  page (/stats), both off by default. The video is never affected. `/` now opens the settings page; the log is at /log.
- **Stats page** (/stats, ported from W10MDashcam): graphs of the last 12 hours (frame rate, bitrate, latency, viewers,
  battery, power, CPU, memory, Wi-Fi, states) and a summary (streaming %, stream stops, app restarts, heat, charger
  short, memory trend). Samples every 30 s only while it is switched on.
- **Front camera:** a Rear / Front choice (phone Video section and the settings page). Switching reopens the camera and
  restarts a running stream; each camera remembers its own resolution. The phone's preview of the front camera is mirrored;
  the stream is not.
- **Login for changes over Wi-Fi:** the settings page can change settings over Wi-Fi after the browser asks for the
  login (HTTP Digest, as in W10MSecurityCam). Default admin / admin with a warning until it is changed (Network > Login).
  USB never needs it.
- **Video over Wi-Fi switch** (on by default) with an "unauthenticated network video" warning: the raw stream has no
  login, so anyone on the network can watch while it is on. Off: only the PC on the USB cable gets video.
- The settings page's Network section shows each switch's real state (ON / OFF), also when it can't change it.
- **Clearer addresses:** the phone's main screen shows only the video and settings addresses, grouped USB cable / Wi-Fi;
  the Network section and the settings page list every page's USB and Wi-Fi address under its switch. The settings page
  has a Connect section with the same grouping, and a Viewer setup section for OBS, VLC (`:demux=h264`) and ffplay, on the page and on the phone.
- **Return to normal mode** button on the phone's red safe-mode notice (it was only on the settings page).
- Tools: `Crash-Dumps.ps1` with `Enable-Crash-Dumps.cmd` / `Get-Crash-Dump.cmd` for Windows crash dumps through Device
  Portal; `Get-Phone-Log.cmd` reads /log and says when the log page is off.
- 6 Mbps added to the bitrate list.

## 1.0 — LumiaCam

- Low-latency native encoder sink at 4K30 (~115 ms sensor to encoded frame), MP4 engine as automatic fallback.
- USB streaming through IP-over-USB port 8020, Wi-Fi on port 5000, log on 5001.
- Blackout screen (the phone can charge while streaming 4K), auto-start, crash guard / SAFE MODE, stall watchdog,
  persistent log.
- Latency mode and background-recording capability removed after A/B tests showed no effect.
