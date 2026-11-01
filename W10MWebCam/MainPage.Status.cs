using System;
using System.Collections.Generic;
using Windows.Networking.Connectivity;
using Windows.UI.Xaml;
using W10MWebCam.Streaming;
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
        /// <summary>A drawer's header band: Tag = "PanelName|ArrowName". Open = the body shows and the band keeps only its top corners round.</summary>
        private void Fold_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            var head = (Border)sender;
            if (!(FindName(((string)head.Tag).Split('|')[0]) is UIElement panel)) return;
            SetFold(head, panel.Visibility != Visibility.Visible);
        }

        private void SetFold(Border head, bool open)
        {
            var parts = ((string)head.Tag).Split('|');
            if (FindName(parts[0]) is UIElement panel) panel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            if (FindName(parts[1]) is TextBlock arrow) arrow.Text = open ? "▾" : "▸";
            head.CornerRadius = open ? new CornerRadius(8, 8, 0, 0) : new CornerRadius(8);
        }

        /// <summary>A tap on a RowSwitch row flips its ToggleSwitch (the template has no knob, so the switch does not toggle by itself).</summary>
        private void RowSwitch_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            DependencyObject n = sender as DependencyObject;
            while (n != null && !(n is ToggleSwitch)) n = Windows.UI.Xaml.Media.VisualTreeHelper.GetParent(n);
            if (n is ToggleSwitch sw && sw.IsEnabled) { sw.IsOn = !sw.IsOn; e.Handled = true; }
        }

        /// <summary>The stream row's edge light: green while streaming (once a second, and on start / stop).</summary>
        private void UpdateStreamButton(bool streaming)
        {
            if (StreamBar == null) return;
            StreamBar.Background = streaming ? GreenFill : Brush(0x16, 0x30, 0x1E);
        }

        /// <summary>Colours the active half of an Auto / Manual segment.</summary>
        private static void SetSeg(Border a, Border m, bool auto)
        {
            var on = Brush(0x1F, 0x3A, 0x26); var off = Brush(0x0A, 0x16, 0x0D);
            var onText = Brush(0xCF, 0xFF, 0xD2); var offText = Brush(0x7F, 0xAE, 0x86);
            a.Background = auto ? on : off; m.Background = auto ? off : on;
            ((TextBlock)a.Child).Foreground = auto ? onText : offText;
            ((TextBlock)m.Child).Foreground = auto ? offText : onText;
        }

        /// <summary>A tap on a segment flips the hidden switch it names (Tag), whose handler does the rest.</summary>
        private void Seg_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            if (FindName((string)((Border)sender).Tag) is ToggleSwitch sw && sw.IsEnabled) sw.IsOn = !sw.IsOn;
        }

        // ---- the pickers: a tile opens a themed list of the hidden ComboBox's items; a tap on a row selects it in the ComboBox.
        private void Pick_Click(object sender, RoutedEventArgs e)
        {
            var button = (Button)sender;
            if (!(FindName((string)button.Tag) is ComboBox box)) return;
            var rows = new List<PickRowItem>();
            foreach (var item in box.Items)
            {
                bool cur = ReferenceEquals(item, box.SelectedItem);
                rows.Add(new PickRowItem
                {
                    Value = item, Text = item.ToString(),
                    Light = cur ? GreenFill : Brush(0x07, 0x11, 0x09),
                    Fore = cur ? Brush(0xCF, 0xFF, 0xD2) : Brush(0x9C, 0xC3, 0xA2),
                    Back = cur ? Brush(0x16, 0x3A, 0x1E) : Brush(0x07, 0x11, 0x09)
                });
            }
            var list = new ListView
            {
                ItemsSource = rows, IsItemClickEnabled = true, SelectionMode = ListViewSelectionMode.None,
                ItemTemplate = (DataTemplate)Resources["PickRow"], ItemContainerStyle = (Style)Resources["PickItem"], Tag = box
            };
            var flyout = new Flyout { Content = list, Placement = Windows.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.Bottom, FlyoutPresenterStyle = (Style)Resources["PickFlyout"] };
            list.ItemClick += (s, a) =>
            {
                if (a.ClickedItem is PickRowItem r) box.SelectedItem = r.Value; // the ComboBox's handler applies it
                flyout.Hide();
            };
            flyout.ShowAt(button);
        }

        /// <summary>The value shown on each picker tile (after every selection change and at start).</summary>
        private void UpdatePickTexts()
        {
            if (ResolutionPickText == null) return;
            ResolutionPickText.Text = ResolutionBox.SelectedItem?.ToString() ?? "-";
            BitratePickText.Text = BitrateBox.SelectedItem?.ToString() ?? "-";
            KeyframePickText.Text = KeyframeBox.SelectedItem?.ToString() ?? "-";
            CameraPickText.Text = CameraBox.SelectedItem?.ToString() ?? "-";
            UnplugPickText.Text = UnplugBox.SelectedItem?.ToString() ?? "-";
            ResolutionPick.IsEnabled = ResolutionBox.IsEnabled; BitratePick.IsEnabled = BitrateBox.IsEnabled;
            KeyframePick.IsEnabled = KeyframeBox.IsEnabled; CameraPick.IsEnabled = CameraBox.IsEnabled; UnplugPick.IsEnabled = UnplugBox.IsEnabled;
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
            if (HotUnprotected) return Tuple.Create("HOT, no protection", 2);
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
            StateText.Text = _protect && streaming ? "HEAT PROTECTION" : streaming ? "STREAMING" : _busy || !StartButton.IsEnabled ? "STARTING..." : "START STREAM";
            StateText.Foreground = _protect ? Brush(0xFF, 0x6B, 0x6B) : streaming ? Brush(0xFF, 0xFF, 0xFF) : Brush(0xCF, 0xE8, 0xD4);
            // the recording light: red while streaming (pulsing), dark red when stopped
            StatusDot.Fill = streaming ? Brush(0xFF, 0x30, 0x30) : Brush(0x3A, 0x14, 0x14);
            StatusDot.Stroke = streaming ? Brush(0xFF, 0x80, 0x80) : Brush(0x5A, 0x20, 0x20);
            // the format pill on the stream row: lit while streaming, the chosen format dimmed while stopped
            var f = streaming ? EffectiveVideo()?.Item1 : ResolutionBox.SelectedItem as FormatOption;
            string res = f == null ? "-" : (f.Height >= 2160 ? "4K" : f.Height + "p") + Math.Round(f.Fps) + (streaming && _safeMode ? " SAFE" : "");
            var dim = Brush(0x7F, 0xAE, 0x86); var bright = Brush(0xFF, 0xFF, 0xFF);
            ResText.Text = res;
            ResText.Foreground = streaming ? Brush(0xCF, 0xFF, 0xD2) : dim;
            ResPill.BorderBrush = streaming ? GreenFill : Brush(0x2A, 0x4A, 0x32);
            // the three stream tiles: live numbers while streaming
            FpsTileText.Text = streaming ? _camera.Fps.ToString("0") : "-";
            MbpsTileText.Text = streaming ? _camera.Mbps.ToString("0.0") : "-";
            ViewersText.Text = streaming ? viewers.ToString() : "-";
            FpsTileText.Foreground = MbpsTileText.Foreground = ViewersText.Foreground = streaming ? bright : dim;
            UpdateServicePills(streaming);
            UpdateStreamButton(streaming);
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
