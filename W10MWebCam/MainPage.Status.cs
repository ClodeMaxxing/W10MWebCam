using System;
using System.Collections.Generic;
using Windows.Networking.Connectivity;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace W10MWebCam
{
    /// <summary>
    /// The status card and folding sections, in the green theme shared with W10MSecurityCam and W10MDashcam:
    /// state dot and label, resolution and viewer pills, big fps / Mbps, and tiles for battery, Wi-Fi, temperature,
    /// CPU (phone and app), memory, engine, latency and power.
    /// </summary>
    public sealed partial class MainPage
    {
        // One brush per colour, made once: the status updates every second would otherwise make dozens of new ones.
        private static Dictionary<int, SolidColorBrush> _brushes;
        private static SolidColorBrush Brush(byte r, byte g, byte b)
        {
            if (_brushes == null) _brushes = new Dictionary<int, SolidColorBrush>();
            int key = r << 16 | g << 8 | b;
            if (!_brushes.TryGetValue(key, out var brush))
                _brushes[key] = brush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, r, g, b));
            return brush;
        }

        private static readonly SolidColorBrush GreenFill = Brush(0x39, 0xFF, 0x14), AmberFill = Brush(0xE8, 0xD0, 0x40),
            RedFill = Brush(0xFF, 0x55, 0x55), OffFill = Brush(0x16, 0x30, 0x1E);

        private bool _dotPulsing;

        /// <summary>Folding section headers: Tag is "PanelName|LABEL".</summary>
        private void Fold_Click(object sender, RoutedEventArgs e)
        {
            var b = (Button)sender;
            var parts = ((string)b.Tag).Split('|');
            if (!(FindName(parts[0]) is UIElement panel)) return;
            bool open = panel.Visibility != Visibility.Visible;
            panel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            b.Content = parts[1] + (open ? "  ▾" : "  ▸");
        }

        /// <summary>A status badge: 0 = good (green), 1 = caution (amber), 2 = bad (red).</summary>
        private static void SetBadge(Border b, Windows.UI.Xaml.Shapes.Ellipse dot, TextBlock text, string label, int level)
        {
            text.Text = label;
            var fg = level == 2 ? RedFill : level == 1 ? AmberFill : GreenFill;
            text.Foreground = fg; dot.Fill = fg;
            b.BorderBrush = level == 2 ? Brush(0x7D, 0x2E, 0x2E) : level == 1 ? Brush(0x7D, 0x6E, 0x2E) : Brush(0x2E, 0x7D, 0x46);
            b.Background = level == 2 ? Brush(0x2A, 0x10, 0x10) : level == 1 ? Brush(0x2A, 0x26, 0x10) : Brush(0x0F, 0x2A, 0x17);
        }

        /// <summary>A meter bar in a tile: the two star columns split the width by percentage.</summary>
        private static void SetBar(Grid bar, Border fill, double pct, SolidColorBrush color)
        {
            pct = Math.Max(0, Math.Min(100, pct));
            bar.ColumnDefinitions[0].Width = new GridLength(pct, GridUnitType.Star);
            bar.ColumnDefinitions[1].Width = new GridLength(100 - pct, GridUnitType.Star);
            fill.Background = color;
        }

        private static int WifiBars()
        {
            try
            {
                var p = NetworkInformation.GetInternetConnectionProfile();
                if (p == null || !p.IsWlanConnectionProfile) return -1;
                var b = p.GetSignalBars();
                return b.HasValue ? b.Value : -1;
            }
            catch { return -1; }
        }

        /// <summary>Heat as a short badge: label and level (0 good, 1 caution, 2 bad).</summary>
        private Tuple<string, int> HeatBadge()
        {
            if (_protect) return Tuple.Create("Protect", 2);
            if (_pausedSecs >= 20) return Tuple.Create("Hot", 2);
            if (_pausedSecs > 0) return Tuple.Create("Paused", 1);
            return Tuple.Create("Healthy", 0);
        }

        /// <summary>The battery is going down: on battery, or draining although it says it is charging.</summary>
        private bool BatteryFalling =>
            _battery != null && (_battery.Status == Windows.System.Power.BatteryStatus.Discharging || _chargerNotice.Length > 0);

        /// <summary>Once a second: the card's header and big numbers, then the tiles (not while nobody can see them).</summary>
        private void UpdateStatusCard(bool streaming)
        {
            int viewers = _server.ClientCount;
            ViewersText.Text = viewers + (viewers == 1 ? " viewer" : " viewers");
            StateText.Text = _protect && streaming ? "HEAT PROTECTION" : streaming ? "STREAMING" : _busy ? "STARTING" : "STOPPED";
            StateText.Foreground = _protect ? Brush(0xFF, 0x6B, 0x6B) : Brush(0x9C, 0xF0, 0xB0);
            StatusDot.Fill = _protect ? RedFill : streaming ? GreenFill : Brush(0x55, 0x55, 0x55);
            var f = streaming ? EffectiveVideo()?.Item1 : null;
            ResPill.Visibility = f != null ? Visibility.Visible : Visibility.Collapsed;
            if (f != null) ResText.Text = (f.Height >= 2160 ? "4K" : f.Height + "p") + Math.Round(f.Fps) + (_safeMode ? " SAFE" : "");
            StatsText.Visibility = streaming ? Visibility.Visible : Visibility.Collapsed;
            if (streaming) StatsText.Text = string.Format("{0,2:0} fps {1,4:0.0} Mbps", _camera.Fps, _camera.Mbps);
            UpdateServicePills(streaming);
            if (!_blackedOut || BlackoutOverlay.Visibility != Visibility.Visible) UpdateTiles(streaming);
            bool pulse = streaming && !(_blackedOut && BlackoutOverlay.Visibility == Visibility.Visible); // only while someone can see it
            if (pulse != _dotPulsing)
            {
                _dotPulsing = pulse;
                if (pulse) DotPulse.Begin(); else { DotPulse.Stop(); StatusDot.Opacity = 1; }
            }
        }

        private void UpdateTiles(bool streaming)
        {
            try
            {
                int? pct = Percent(_battery);
                bool charging = _battery != null && _battery.Status == Windows.System.Power.BatteryStatus.Charging;
                BattText.Text = pct.HasValue ? pct + "% " + (charging ? "chg" : _battery.Status.ToString().ToLowerInvariant()) : "n/a";
                int p = pct ?? 0;
                SetBar(BattBar, BattBarFill, p, BatteryFalling || p < 20 ? RedFill : p < 40 ? AmberFill : GreenFill);

                int bars = WifiBars();
                WifiText.Text = bars >= 0 ? bars + " of 4" : "off";
                var on = bars < 2 ? AmberFill : GreenFill;
                Wifi1.Fill = bars >= 1 ? on : OffFill; Wifi2.Fill = bars >= 2 ? on : OffFill;
                Wifi3.Fill = bars >= 3 ? on : OffFill; Wifi4.Fill = bars >= 4 ? on : OffFill;

                var heat = HeatBadge();
                SetBadge(TempBadge, TempBadgeDot, TempBadgeText, heat.Item1, heat.Item2);

                double cp = _power.SysCpuPct ?? 0, ca = _power.ProcCpuPct ?? 0;
                CpuPhoneText.Text = _power.SysCpuPct.HasValue ? Math.Round(cp) + "%" : "n/a";
                CpuAppText.Text = _power.ProcCpuPct.HasValue ? Math.Round(ca) + "%" : "n/a";
                SetBar(CpuPhoneBar, CpuPhoneBarFill, cp, cp > 70 ? AmberFill : GreenFill);
                SetBar(CpuAppBar, CpuAppBarFill, ca, ca > 50 ? AmberFill : GreenFill);

                double mb = 0, lim = 1536;
                try { mb = Windows.System.MemoryManager.AppMemoryUsage / 1048576.0; lim = Math.Max(1, Windows.System.MemoryManager.AppMemoryUsageLimit / 1048576.0); } catch { }
                double mp = 100 * mb / lim;
                MemText.Text = Math.Round(mb) + " MB";
                SetBar(MemBar, MemBarFill, mp, mp > 80 ? RedFill : mp > 60 ? AmberFill : GreenFill);

                string engine = _camera == null ? "-" : _safeMode || !_camera.UseNativeSink ? "Safe mode" : streaming ? (_camera.PathName == "low-latency" ? "Fast" : _camera.PathName) : "Ready";
                SetBadge(EngineBadge, EngineBadgeDot, EngineBadgeText, engine, engine == "Fast" ? 0 : engine == "Ready" ? 0 : 1);

                long lat = streaming && _camera != null ? _camera.LastLatencyMs : -1;
                LatText.Text = lat >= 0 ? lat + " ms" : "n/a";
                SetBar(LatBar, LatBarFill, lat >= 0 ? lat / 3.0 : 0, lat > 250 ? RedFill : lat > 150 ? AmberFill : GreenFill);

                double? w = !charging && _power.DrawWatts.HasValue ? _power.DrawWatts
                    : _power.NetWatts.HasValue ? (double?)(charging ? _power.NetWatts.Value : Math.Abs(_power.NetWatts.Value)) : null;
                PowerLabel.Text = charging ? "INTO BATTERY" : "BATTERY DRAW";
                PowerValText.Text = w.HasValue ? (charging && w.Value >= 0 ? "+" : "") + w.Value.ToString("0.0") + " W" : "measuring";
                SetBar(PowerBar, PowerBarFill, w.HasValue ? 100 * Math.Abs(w.Value) / 15 : 0,
                    charging ? (w < 0 ? RedFill : w < 1 ? AmberFill : GreenFill) : (w > 8 ? RedFill : AmberFill));
            }
            catch { }
        }
    }
}
