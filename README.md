# W10MWebCam

Turns a Lumia 950 (Windows 10 Mobile) into a low-latency webcam for a Windows PC. The phone's hardware encoder
produces H.264 (up to 4K30) and the app sends it raw over **USB** (recommended) or **Wi-Fi**. On the PC, OBS receives
it and exposes it as a webcam through **OBS Virtual Camera**, so it works in Zoom, Teams, Discord, browsers and so on.

Typical numbers at 4K30 over USB: about 115 ms from the camera sensor to an encoded frame on the phone, about 35 Mbps,
steady frame pacing. With the blackout screen on, the phone can charge while it streams (given enough power, see
*Power and heat*).

W10MWebCam was called **LumiaCam** up to version 1.0. Its sister apps grew out of it:
[W10MSecurityCam](https://github.com/ClodeMaxxing/W10MSecurityCam) (RTSP security camera) and
[W10MDashcam](https://github.com/ClodeMaxxing/W10MDashcam) (dashcam). Version 1.1 brings back their stability fixes,
power and heat handling, camera controls and settings page.

It should run on other phones with Windows 10 Mobile 1607 or later (Lumia 950 XL, 550, 650, HP Elite x3, ...), with the
formats their cameras offer, but it has only been tested on a Lumia 950 (build 10.0.15254.603).

---

## Everyday use

1. Plug the phone into the PC with USB-C and open **W10MWebCam**. It starts streaming by itself after about 2 s and
   the screen goes black (tap it to see the controls for 15 s).
2. In OBS, the Media Source pointed at the phone (see below) picks the stream up. Click **Start Virtual Camera**.
3. In your video app, choose **OBS Virtual Camera** as the camera.
4. To change settings from the PC, open **http://127.0.0.1:8020/settings** in a browser.

If OBS shows nothing after a phone reboot, **unplug and replug the USB cable**: the PC side of the USB link sometimes
doesn't come back on its own.

Keep the phone **unlocked with W10MWebCam in front**. Windows 10 Mobile takes the camera away from any app when the
screen locks or another app opens, and there is no way around that for an app. The blackout screen keeps the screen
"on" but black, which is fine.

## Addresses

| What | Address |
|---|---|
| Video over USB | `tcp://127.0.0.1:8020` |
| Video over Wi-Fi | `tcp://PHONE-IP:5000` (the app shows the IP) |
| Settings page (USB, can change settings) | `http://127.0.0.1:8020/settings` (or just `http://127.0.0.1:8020/`) |
| Settings page (Wi-Fi, view only) | `http://PHONE-IP:5001/settings` |
| Health as JSON | `/status` on either of the two web addresses |
| Log *(off by default)* | `/log` on either web address |
| Stats, last 12 h of graphs *(off by default)* | `/stats` on either web address |

**Changing settings:** over the USB cable the settings page can always change settings (only the PC the phone is
plugged into can reach it). Over Wi-Fi the browser asks for the **login** before the first change (or press **Log in**).
A new install uses **admin / admin**, with a warning on the phone and the page until you change it (Network > Login, or
on the settings page). An empty password resets it to admin / admin. Viewing over Wi-Fi needs no login.

The phone's main screen shows just the two addresses you need, grouped: **USB cable** (video, settings) and **Wi-Fi**
(video, settings). The Network section lists every page's USB and Wi-Fi address under its switch.

**Network switches** (phone: *Network* section; the settings page shows each one's real state as ON / OFF).
**Video over Wi-Fi** (on): the video for any viewer not on the USB cable. The raw stream has **no login** (OBS and VLC
can't send one), so while it is on anyone on your network can watch; the app shows an "unauthenticated network video"
warning. Switch it off if you only use the USB cable. **Web page** (on): the settings page and `/status`; switched off,
only the video is served, and only the phone can switch it back on. **Log page** and **Stats page** (both off): switch
them on when you want them, on the phone or on the settings page. The stats page keeps a sample every 30 s, only while
it is on, so it shows the time since it was switched on (up to 12 hours). The video is never affected by these.

### OBS Media Source settings

- Untick **Local File**. **Input**: `tcp://127.0.0.1:8020`
- **Input Format**: `h264`
- **Reconnect Delay**: 1 s; tick **Restart playback when source becomes active**
- **FFmpeg Options**: `fflags=nobuffer flags=low_delay analyzeduration=0 probesize=500000`
- Untick **Use hardware decoding when available** if you see artifacts (either works on most PCs)
- Set the canvas to the stream resolution (for example 3840×2160) for a 1:1 picture.

Quick test without OBS: `ffplay -fflags nobuffer -flags low_delay -f h264 tcp://127.0.0.1:8020`

**VLC** can't detect the raw H.264 by itself (it connects but never plays). Tell it the format: Media > Open Network
Stream, enter the address, tick *Show more options* and add `:demux=h264` to *Edit Options*; or from a command prompt:
`"C:\Program Files\VideoLAN\VLC\vlc.exe" --demux=h264 tcp://PHONE-IP:5000`. Over Wi-Fi, 4K needs fast Wi-Fi: on a
2.4 GHz network the stream measured about 7 Mbps, so use 1080p (about 9 Mbps) or a lower bitrate there.

## Phone screen and settings page

Both use the same green look as W10MSecurityCam and W10MDashcam. At the top: **Start/Stop streaming** and the
**status card** (state, resolution, viewers, fps and Mbps, and tiles for battery, Wi-Fi, temperature, phone and app CPU,
memory, engine, latency and power). Red notices are for heat and safe mode, amber ones for the charger. The settings fold
away in sections (Video, Camera, Power, Network, Details and log); the (i) buttons explain each one. On the settings page, hover
or tap a tile or an (i) for its explanation; folded sections are remembered in that browser.

On the phone and on the settings page:

| Setting | What it does | Suggested |
|---|---|---|
| Resolution | Recording format of the rear camera | 3840×2160 @ 30 for content, 1920×1080 @ 30 for calls |
| Bitrate | Encoder target. *Auto* ≈ 37 Mbps at 4K30, ≈ 9 Mbps at 1080p30 | Auto |
| Keyframe every | How often a full frame is sent. Shorter = faster joins and recovery, slightly more data | 1 s |
| Blackout screen while streaming | Black OLED screen and minimum brightness. Saves battery and lets the phone charge | On |
| Start streaming when app opens | Auto-start | On |
| Preview on phone | Live picture on the phone. Changes take effect while the stream is stopped (changing the preview during a 4K recording can stall it) | Your choice |
| Automatic focus, exposure, white balance | Off shows a slider and Auto / Manual switch for each, and the camera picture on the phone until 60 s after the last touch (while streaming, starting and ending the picture restart the stream once each) | On |
| Focus once and lock | Focuses once and keeps the lens there (good for a fixed scene) | |
| Flashlight | Only while streaming; switches off after 30 s because the LED heats the phone and cannot be dimmed | |
| Stabilization off | For a fixed mount: less processing and heat. Restarts the stream once | Off |
| Hold focus once it has settled | Parks the lens after autofocus settles (stops hunting). Not for a subject that moves toward or away from the camera | Off |
| Lower to 1080p / 6 Mbps if the phone overheats | See *Power and heat* | Off |
| When unplugged, stop streaming after | See *Power and heat* | Never |

Changing resolution, bitrate, keyframe or stabilization while streaming restarts the stream for a moment; OBS
reconnects by itself.

The engine tile reads *Fast* for the low-latency engine and *Safe mode* for the MP4 fallback; *Latency* is the delay
from the camera sensor to the encoded frame inside the phone. The full power readout and the log are under
*Details and log*.

## Power and heat

A PC USB port often cannot supply what 4K encoding draws. The phone may say "Charging" while the battery still drains,
and a hot battery makes Windows pause charging.

- **Power readout:** watts into or out of the battery (a 5-minute average, restarted whenever the charging state
  changes, so it needs a few minutes) plus phone and app CPU. Logged once a minute.
- **"Charger insufficient"** appears when the battery drains although the phone says it is charging. Use a 2 A or
  better charger (through a powered hub or a dock), or a lower resolution.
- **Heat:** Windows gives apps no temperature. When charging pauses for 20 s on a charger Windows calls adequate (below
  95 %), the battery is hot and the app warns. With **Lower to 1080p / 6 Mbps if the phone overheats** on, it also
  lowers the stream until charging has resumed for 30 s (and at least 3 minutes); your own settings are never
  overwritten. It is off by default, so a recording never changes resolution unexpectedly.
- **Stop when unplugged** (off by default): when the phone goes on battery while streaming, a countdown appears on the
  screen (it lifts the blackout) and the stream stops when it ends, so the phone isn't run flat. **Keep streaming**
  skips it once. The stream starts again when power returns while the app is open.

## Troubleshooting

| Symptom | Fix |
|---|---|
| OBS black / "connecting" | Replug USB. Check the app says it is streaming and viewers ≥ 1. Toggle the OBS source off and on. |
| **SAFE MODE** on the phone | The previous launch closed while starting a stream. This launch uses the standard (MP4) engine and doesn't auto-start; press Start. **Return to normal mode** (on the phone's red notice or the settings page) goes back to the low-latency engine. |
| **SELF-CHECK** in the log | The app failed to start twice in a row, so its saved settings were reset to defaults. |
| Engine shows `MP4` | The low-latency engine stalled twice and the app switched automatically. **Return to normal mode** retries the low-latency engine. |
| The app closes by itself | Switch on Network > Log page and run `tools\Get-Phone-Log.cmd`: the log keeps the last three sessions, marked CRASHED / closed normally / ended abruptly. For a Windows crash dump, see `Crash-Dumps.ps1` below. |
| Stream stops when the screen locks | Expected (Windows limitation). Keep the app in front; the blackout screen keeps the screen awake. |
| Battery drains / "Charger insufficient" | Better power (2 A charger, powered hub) or 1080p. Keep the blackout on. |
| Heat notice | Give the phone air (out of a case, not in the sun), lower the resolution, or turn on *Lower to 1080p*. |
| Install says "same identity but different contents" | Increase `Version` in `W10MWebCam/Package.appxmanifest`. |
| Install asks for a PIN | Phone: Settings > Update & security > For developers > Pair, type the code. |

## Install a release

Download `W10MWebCam_<version>_ARM.zip` from the [Releases page](https://github.com/ClodeMaxxing/W10MWebCam/releases).
It holds the app package, the three Microsoft runtime packages it needs, the test certificate and `INSTALL.txt` with the
steps. Check the zip against `SHA256SUMS.txt`. In short: turn on developer mode on the phone, then install the package
with its dependencies from the phone's Device Portal (*Apps* > *Install app*) or with `WinAppDeployCmd` from the
Windows SDK. The package is signed with the included self-signed certificate, which the phone accepts in developer mode.

W10MWebCam installs beside LumiaCam, W10MSecurityCam and W10MDashcam (each has its own identity). Only one app can use
the camera at a time. If you used LumiaCam, uninstall it; its settings are not carried over.

## PC setup (one time)

1. **IP over USB:** the *Windows Phone IP over USB Transport* service, which maps PC `127.0.0.1:8020` to the phone over
   USB. It comes with the Windows 10 SDK 1709 installer (`Windows Phone IP over USB Transport (IpOverUsb)-x86_en-us.msi`)
   and with older Visual Studio phone tools.
2. Phone: developer mode on, device discovery on, paired with the PC.
3. OBS Studio with a Media Source set up as above.

## Build from source

Visual Studio 2019 with *Universal Windows Platform development*, *C++ UWP tools*, *MSVC ARM build tools* and Windows
SDK 10.0.19041. The project targets ARM, minimum Windows 10 1607 (14393).

The signing key (`W10MWebCam/W10MWebCam_TemporaryKey.pfx`) is not in the repository. Create your own: open
`Package.appxmanifest` in Visual Studio > *Packaging* > *Choose Certificate* > *Create test certificate* with publisher
`CN=W10MWebCam`.

| Script (`tools\`) | Use |
|---|---|
| `Build-And-Deploy.cmd` | Build Release ARM and install on the phone (USB first, then Wi-Fi). The app opens by itself after install. Bump `Version` in the manifest for every install. |
| `Install-Only.cmd` | Re-install the last build without rebuilding |
| `Get-Phone-Log.cmd` | Download the phone's log (the last three sessions, which survive a crash) to `tools\phone-log.txt` and open it. Needs Network > Log page on |
| `Test-USB-Video.cmd` | Records 15 s of the USB stream to `tools\usb8020-test.h264` to check it works without OBS |
| `Soak-Monitor.ps1` | Long-run recorder: samples `/status` every 30 s (over USB, or `-Phone IP` over Wi-Fi) to `tools\soak\*.csv` and keeps an HTML report with graphs and a summary (availability, restarts, heat time, fps, memory trend) |
| `Monitor-Portal.ps1` | Logs whole-phone CPU, GPU and battery from Device Portal once a minute (`-Pair PIN` first) |
| `Enable-Crash-Dumps.cmd`, `Get-Crash-Dump.cmd` (`Crash-Dumps.ps1`) | Switch on Windows crash dumps for W10MWebCam through Device Portal (per installed version: run again after an update), list them, and download them to `tools\dump\`. First time: `Crash-Dumps.ps1 -Pair PIN` with the pairing code from the phone |
| `Remove-Dead-IpOverUsb-Keys.cmd` | Optional, run as admin: deletes unused `LumiaCam*` IP-over-USB registry entries from early development |

Put the phone's Wi-Fi address in `tools\phone-ip.txt` (one line, not committed) for the Wi-Fi install fallback and the
Wi-Fi tools.

Developer notes are in [NOTES.md](NOTES.md). Release history is in [CHANGELOG.md](CHANGELOG.md).

## Credits

W10MWebCam (formerly LumiaCam) is the original of the family: the camera engine and the low-latency encoder sink are
shared with W10MSecurityCam and W10MDashcam. The 1.1 additions (crash fix in the native sink, power monitor, heat
detection, camera controls, settings page ideas, self-check, soak tools) were ported back from those two forks.

## Licence

MIT, see [LICENSE](LICENSE). It comes with no warranty.
