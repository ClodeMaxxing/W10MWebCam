using System;
using System.Collections.Generic;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.Networking.Sockets;

namespace W10MWebCam.Streaming
{
    /// <summary>
    /// Plain TCP server that sends a raw H.264 (Annex-B) stream to every viewer.
    /// Viewers: OBS Media Source / ffplay / VLC pointed at tcp://PHONE-IP:5000.
    /// A viewer that falls behind is skipped forward to the next keyframe, so delay never builds up.
    /// The USB port (8020) is the only port Microsoft's IP-over-USB forwards, so a client that starts with an HTTP request
    /// line ("GET", "POST") gets the web interface (HttpApi) instead of video.
    /// At most 4 video viewers (2 per Wi-Fi address) and 8 web requests at a time, each for USB and for Wi-Fi.
    /// </summary>
    internal sealed class StreamServer
    {
        private static readonly byte[] StartCode = { 0, 0, 0, 1 };

        private readonly int[] _ports;
        private readonly object _lock = new object();
        private readonly List<Client> _clients = new List<Client>();
        private readonly List<StreamSocketListener> _listeners = new List<StreamSocketListener>();
        private byte[] _sps, _pps;

        public Action<string> Log = _ => { };
        /// <summary>Answers clients that send an HTTP request instead of watching the video.</summary>
        public HttpApi Http;

        /// <summary>
        /// Video for viewers that are not on the USB cable (any other address, on either port). The raw stream has no login,
        /// so with this on anyone on the network can watch. Off: such viewers are disconnected; USB is unaffected.
        /// </summary>
        public volatile bool WifiVideoEnabled;

        /// <summary>Disconnects every video viewer that is not on the USB cable (called when Wi-Fi video is switched off).</summary>
        public void CloseWifiViewers()
        {
            List<Client> drop;
            lock (_lock) drop = _clients.FindAll(c => !c.FromUsb && !c.IsHttp);
            foreach (var c in drop) c.Close();
            if (drop.Count > 0) Log("Video over Wi-Fi switched off: " + drop.Count + " Wi-Fi viewer(s) disconnected");
        }

        /// <summary>If the oldest unsent video for a viewer is older than this, skip to the next keyframe.</summary>
        public double MaxBacklogSeconds = 1.5;

        // Limits, counted separately for the USB cable and for Wi-Fi, so a busy network never locks the PC out over USB.
        private const int MaxViewers = 4;                         // video viewers
        private const int MaxViewersPerAddress = 2;               // video viewers from one Wi-Fi address
        private const int MaxHttp = 8;                            // web requests answered at once
        private const int MaxConnections = MaxViewers + MaxHttp;  // accepted before it is known what they are
        private readonly object _limitLogLock = new object();
        private DateTime _lastLimitLog = DateTime.MinValue;

        /// <summary>At most one line per 10 s: a client that keeps reconnecting must not flood the log.</summary>
        private void LogLimited(string m)
        {
            lock (_limitLogLock)
            {
                if ((DateTime.UtcNow - _lastLimitLog).TotalSeconds < 10) return;
                _lastLimitLog = DateTime.UtcNow;
            }
            Log(m);
        }

        public StreamServer(params int[] ports) { _ports = ports; }

        /// <summary>Viewers currently receiving video (web requests and viewers still waiting for a keyframe don't count).</summary>
        public int ClientCount { get { lock (_lock) { int n = 0; foreach (var c in _clients) if (c.Synced && !c.IsClosed) n++; return n; } } }
        /// <summary>Viewers on one transport: the USB cable, or Wi-Fi (for the phone's service pills).</summary>
        public int ViewerCount(bool fromUsb) { lock (_lock) { int n = 0; foreach (var c in _clients) if (c.Synced && !c.IsClosed && c.FromUsb == fromUsb) n++; return n; } }

        public async Task StartAsync()
        {
            if (_listeners.Count > 0) return;
            foreach (int port in _ports)
            {
                var l = new StreamSocketListener();
                l.Control.NoDelay = true;
                l.ConnectionReceived += OnConnection;
                await l.BindServiceNameAsync(port.ToString());
                _listeners.Add(l);
                Log("Video listening on TCP port " + port);
            }
        }

        public void Stop()
        {
            lock (_lock)
            {
                foreach (var c in _clients) c.Close();
                _clients.Clear();
            }
            foreach (var l in _listeners) l.Dispose();
            _listeners.Clear();
        }

        private void OnConnection(StreamSocketListener sender, StreamSocketListenerConnectionReceivedEventArgs args)
        {
            string address = args.Socket.Information.RemoteAddress?.DisplayName ?? "?";
            bool fromUsb = address == "127.0.0.1";
            Client c;
            lock (_lock)
            {
                int open = 0;
                foreach (var x in _clients) if (x.FromUsb == fromUsb && !x.IsClosed) open++;
                if (open >= MaxConnections)
                {
                    try { args.Socket.Dispose(); } catch { }
                    c = null;
                }
                else
                {
                    c = new Client(args.Socket, Log) { Who = fromUsb ? "PC over USB" : address + " over Wi-Fi", FromUsb = fromUsb, Address = address };
                    _clients.Add(c);
                }
            }
            if (c == null) { LogLimited("Too many connections - refused one from " + address); return; }
            // Nothing is logged yet: the settings page polls over this port every few seconds, and those requests
            // would flood the log. A viewer is logged when its video starts flowing (see Broadcast).
            var _ = c.RunAsync(() =>
            {
                lock (_lock) _clients.Remove(c);
                if (c.Synced) Log("Viewer left: " + c.Who);
            });
            var __ = WatchInputAsync(c, args.Socket);
            if (!c.FromUsb && !WifiVideoEnabled) { var ___ = RefuseWifiViewerAsync(c); }
        }

        /// <summary>Video over Wi-Fi is off: a client that hasn't asked for a web page within 2 s is a viewer, so close it.</summary>
        private async Task RefuseWifiViewerAsync(Client c)
        {
            await Task.Delay(2000);
            if (c.IsHttp || c.IsClosed) return;
            Log("Video over Wi-Fi is off: refused a viewer from " + c.Who);
            c.Close();
        }

        /// <summary>
        /// Video viewers never send anything. A client that sends an HTTP request gets the web interface.
        /// If the read ends, the viewer has disconnected (catches dead USB connections quickly).
        /// </summary>
        private async Task WatchInputAsync(Client c, StreamSocket socket)
        {
            try
            {
                var buf = new Windows.Storage.Streams.Buffer(2048);
                var got = await socket.InputStream.ReadAsync(buf, 2048, Windows.Storage.Streams.InputStreamOptions.Partial);
                if (got.Length == 0) { c.Close(); return; }
                byte[] data = got.ToArray();
                if (HttpApi.LooksLikeHttp(data) && Http != null)
                {
                    c.MarkHttp(); // no video for this client from now on
                    int busy = 0;
                    lock (_lock) foreach (var x in _clients) if (x.IsHttp && !x.IsClosed && x.FromUsb == c.FromUsb) busy++;
                    if (busy > MaxHttp)
                    {
                        LogLimited("Too many web requests at once - refused one from " + c.Who);
                        c.SendFinal(HttpApi.Reply(503, "Service Unavailable", "text/plain", "Busy, try again\n"));
                        return;
                    }
                    var request = await HttpApi.ReadAsync(socket.InputStream, data, c.FromUsb, socket.Information.RemoteAddress?.DisplayName);
                    if (request == null) { c.Close(); return; }
                    c.SendFinal(await Http.HandleAsync(request));
                }
            }
            catch (Exception ex)
            {
                // Usually the viewer went away (its read fails); not logged when this side closed it on purpose.
                if (!c.IsClosed) LogDrop((c.IsHttp ? "Web request from " : "Viewer ") + c.Who + " dropped: " + ex.Message.Trim());
                c.Close();
            }
        }

        private DateTime _lastDropLog = DateTime.MinValue;

        /// <summary>At most one line per 10 s, kept apart from LogLimited so dropped viewers never hide a refused one.</summary>
        private void LogDrop(string m)
        {
            lock (_limitLogLock)
            {
                if ((DateTime.UtcNow - _lastDropLog).TotalSeconds < 10) return;
                _lastDropLog = DateTime.UtcNow;
            }
            Log(m);
        }

        /// <summary>Called for single NAL units: the decoder setup at the start, and every NAL unit of the MP4 engine.</summary>
        public void Broadcast(byte[] nal, int type)
        {
            if (type == 7) _sps = nal;
            else if (type == 8) _pps = nal;

            bool firstSliceOfIdr = type == 5 && nal.Length > 1 && (nal[1] & 0x80) != 0;
            lock (_lock) if (_clients.Count == 0) return;
            Deliver(WithStartCode(nal), firstSliceOfIdr);
        }

        /// <summary>
        /// Called for every frame of the low-latency engine: the frame's NAL units go out as one buffer per viewer
        /// (start codes included), so each viewer gets one socket write per frame instead of one per NAL unit.
        /// </summary>
        public void BroadcastAccessUnit(List<byte[]> nals)
        {
            bool idr = false;
            int size = 0;
            foreach (var nal in nals)
            {
                if (nal.Length == 0) continue;
                int type = nal[0] & 0x1F;
                if (type == 7) _sps = nal;
                else if (type == 8) _pps = nal;
                else if (type == 5 && nal.Length > 1 && (nal[1] & 0x80) != 0) idr = true;
                size += 4 + nal.Length;
            }
            if (size == 0) return;
            lock (_lock) if (_clients.Count == 0) return;
            var packet = new byte[size];
            int at = 0;
            foreach (var nal in nals)
            {
                if (nal.Length == 0) continue;
                Buffer.BlockCopy(StartCode, 0, packet, at, 4);
                Buffer.BlockCopy(nal, 0, packet, at + 4, nal.Length);
                at += 4 + nal.Length;
            }
            Deliver(packet, idr);
        }

        /// <summary>
        /// Queues one buffer for every viewer. A viewer waiting for a picture starts at a keyframe (keyframeStart:
        /// the buffer holds the first slice of an IDR frame), after the decoder setup.
        /// </summary>
        private void Deliver(byte[] packet, bool keyframeStart)
        {
            List<Client> dead = null, refused = null;
            List<string> notes = null; // logged once the lock is released

            lock (_lock)
            {
                if (_clients.Count == 0) return;
                foreach (var c in _clients)
                {
                    if (c.IsClosed || c.IsHttp) continue;
                    if (!c.FromUsb && !WifiVideoEnabled) continue; // being refused (RefuseWifiViewerAsync)
                    if (c.SecondsSinceLastSend > 6 && c.QueuedBytes > 0)
                    {
                        (dead ?? (dead = new List<Client>())).Add(c);
                        continue;
                    }
                    if (c.WaitingForKeyframe)
                    {
                        if (keyframeStart && _sps != null && _pps != null)
                        {
                            if (!c.Synced && TooManyViewers(c)) { (refused ?? (refused = new List<Client>())).Add(c); continue; }
                            c.Enqueue(WithStartCode(_sps));
                            c.Enqueue(WithStartCode(_pps));
                            c.Enqueue(packet);
                            c.WaitingForKeyframe = false;
                            if (!c.Synced) { c.Synced = true; (notes ?? (notes = new List<string>())).Add("Viewer connected: " + c.Who + " - video flowing"); }
                        }
                    }
                    else if (c.OldestAgeSeconds > MaxBacklogSeconds)
                    {
                        c.ClearQueue();
                        c.WaitingForKeyframe = true;
                        (notes ?? (notes = new List<string>())).Add("Viewer fell behind - skipping to next keyframe");
                    }
                    else
                    {
                        c.Enqueue(packet);
                    }
                }
            }
            if (notes != null) foreach (var m in notes) Log(m);
            if (dead != null)
                foreach (var c in dead)
                {
                    Log("Viewer not responding - disconnecting it");
                    c.Close();
                }
            if (refused != null)
                foreach (var c in refused)
                {
                    LogLimited("Too many viewers (" + MaxViewers + ", " + MaxViewersPerAddress + " per Wi-Fi address) - refused " + c.Who);
                    c.Close();
                }
        }

        /// <summary>A new viewer would go over the limits (called under _lock, just before its video would start).</summary>
        private bool TooManyViewers(Client c)
        {
            int all = 0, same = 0;
            foreach (var x in _clients)
            {
                if (!x.Synced || x.IsClosed || x.IsHttp || x.FromUsb != c.FromUsb) continue;
                all++;
                if (x.Address == c.Address) same++;
            }
            return all >= MaxViewers || (!c.FromUsb && same >= MaxViewersPerAddress);
        }

        private static byte[] WithStartCode(byte[] nal)
        {
            var b = new byte[nal.Length + 4];
            Buffer.BlockCopy(StartCode, 0, b, 0, 4);
            Buffer.BlockCopy(nal, 0, b, 4, nal.Length);
            return b;
        }

        private sealed class Client
        {
            private readonly StreamSocket _socket;
            private readonly Action<string> _log;
            private readonly Queue<byte[]> _queue = new Queue<byte[]>();
            private readonly Queue<long> _times = new Queue<long>();
            private long _delaySumTicks, _delayCount, _delayMaxTicks, _statStartTicks;
            private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();
            private readonly SemaphoreSlim _signal = new SemaphoreSlim(0);
            private long _queuedBytes;
            private volatile bool _closed;
            public bool IsClosed => _closed;
            /// <summary>This client asked for a web page, not video.</summary>
            public volatile bool IsHttp;
            /// <summary>Video has started flowing to this viewer at least once.</summary>
            public volatile bool Synced;
            public string Who = "?";
            public string Address = "?";
            public bool FromUsb;
            private volatile bool _closeWhenDrained;

            public void MarkHttp()
            {
                IsHttp = true;
                ClearQueue();
            }

            public void SendFinal(byte[] b)
            {
                IsHttp = true;
                ClearQueue();
                _closeWhenDrained = true;
                Enqueue(b);
            }

            public volatile bool WaitingForKeyframe = true;

            public Client(StreamSocket socket, Action<string> log)
            {
                _socket = socket;
                _log = log;
                try { _socket.Control.NoDelay = true; } catch { }
            }

            public long QueuedBytes { get { lock (_queue) return _queuedBytes; } }

            private long _lastSendTicks = Clock.ElapsedTicks;
            public double SecondsSinceLastSend =>
                (Clock.ElapsedTicks - System.Threading.Interlocked.Read(ref _lastSendTicks)) / (double)System.Diagnostics.Stopwatch.Frequency;

            public double OldestAgeSeconds
            {
                get
                {
                    lock (_queue)
                        return _times.Count == 0 ? 0 : (Clock.ElapsedTicks - _times.Peek()) / (double)System.Diagnostics.Stopwatch.Frequency;
                }
            }

            public void Enqueue(byte[] b)
            {
                lock (_queue) { _queue.Enqueue(b); _times.Enqueue(Clock.ElapsedTicks); _queuedBytes += b.Length; }
                _signal.Release();
            }

            public void ClearQueue()
            {
                lock (_queue) { _queue.Clear(); _times.Clear(); _queuedBytes = 0; }
            }

            public void Close()
            {
                _closed = true;
                try { _socket.Dispose(); } catch { } // aborts a write that is stuck on a dead connection
                _signal.Release();
            }

            public async Task RunAsync(Action onClosed)
            {
                try
                {
                    var output = _socket.OutputStream;
                    while (!_closed)
                    {
                        await _signal.WaitAsync().ConfigureAwait(false);
                        byte[] b = null;
                        lock (_queue)
                        {
                            if (_queue.Count > 0)
                            {
                                b = _queue.Dequeue();
                                long queuedAt = _times.Dequeue();
                                long waited = Clock.ElapsedTicks - queuedAt;
                                _delaySumTicks += waited; _delayCount++; if (waited > _delayMaxTicks) _delayMaxTicks = waited;
                                _queuedBytes -= b.Length;
                            }
                        }
                        if (b != null)
                        {
                            await output.WriteAsync(b.AsBuffer());
                            System.Threading.Interlocked.Exchange(ref _lastSendTicks, Clock.ElapsedTicks);
                            if (_statStartTicks == 0) _statStartTicks = Clock.ElapsedTicks;
                            double secs = (Clock.ElapsedTicks - _statStartTicks) / (double)System.Diagnostics.Stopwatch.Frequency;
                            if (secs >= 30 && _delayCount > 0 && !IsHttp)
                            {
                                double f = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                                _log(string.Format("Viewer send queue (last {0:0}s): avg wait {1:0} ms, max {2:0} ms",
                                    secs, _delaySumTicks / (double)_delayCount * f, _delayMaxTicks * f));
                                _statStartTicks = Clock.ElapsedTicks; _delaySumTicks = 0; _delayCount = 0; _delayMaxTicks = 0;
                            }
                        }
                        if (_closeWhenDrained && QueuedBytes == 0)
                        {
                            await output.FlushAsync();
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (!IsHttp) _log("Viewer connection ended: " + ex.Message);
                }
                finally
                {
                    _closed = true;
                    try { _socket.Dispose(); } catch { }
                    onClosed();
                }
            }
        }
    }
}
