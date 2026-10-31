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
        public volatile bool WifiVideoEnabled = true;

        /// <summary>Disconnects every video viewer that is not on the USB cable (called when Wi-Fi video is switched off).</summary>
        public void CloseWifiViewers()
        {
            List<Client> drop;
            lock (_lock) drop = _clients.FindAll(c => !c.FromUsb && !c.IsHttp);
            foreach (var c in drop) c.Close();
            if (drop.Count > 0) Log("Video over Wi-Fi switched off: " + drop.Count + " Wi-Fi viewer(s) disconnected");
        }

        /// <summary>Kept for compatibility; lag is now judged by time (see MaxBacklogSeconds).</summary>
        public long MaxQueueBytes = 4000000;
        /// <summary>If the oldest unsent video for a viewer is older than this, skip to the next keyframe.</summary>
        public double MaxBacklogSeconds = 1.5;

        public StreamServer(params int[] ports) { _ports = ports; }

        /// <summary>Viewers currently receiving video (web requests and viewers still waiting for a keyframe don't count).</summary>
        public int ClientCount { get { lock (_lock) { int n = 0; foreach (var c in _clients) if (c.Synced && !c.IsClosed) n++; return n; } } }

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
            var c = new Client(args.Socket, Log);
            string address = args.Socket.Information.RemoteAddress?.DisplayName ?? "?";
            c.Who = address == "127.0.0.1" ? "PC over USB" : address + " over Wi-Fi";
            c.FromUsb = address == "127.0.0.1";
            lock (_lock) _clients.Add(c);
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
                    var request = await HttpApi.ReadAsync(socket.InputStream, data, c.FromUsb);
                    if (request == null) { c.Close(); return; }
                    c.SendFinal(await Http.HandleAsync(request));
                }
            }
            catch { c.Close(); }
        }

        /// <summary>Called for every H.264 NAL unit coming out of the encoder.</summary>
        public void Broadcast(byte[] nal, int type)
        {
            if (type == 7) _sps = nal;
            else if (type == 8) _pps = nal;

            bool firstSliceOfIdr = type == 5 && nal.Length > 1 && (nal[1] & 0x80) != 0;
            byte[] packet = null;
            List<Client> dead = null;

            lock (_lock)
            {
                if (_clients.Count == 0) return;
                packet = WithStartCode(nal);
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
                        if (firstSliceOfIdr && _sps != null && _pps != null)
                        {
                            c.Enqueue(WithStartCode(_sps));
                            c.Enqueue(WithStartCode(_pps));
                            c.Enqueue(packet);
                            c.WaitingForKeyframe = false;
                            if (!c.Synced) { c.Synced = true; Log("Viewer connected: " + c.Who + " - video flowing"); }
                        }
                    }
                    else if (c.OldestAgeSeconds > MaxBacklogSeconds)
                    {
                        c.ClearQueue();
                        c.WaitingForKeyframe = true;
                        Log("Viewer fell behind - skipping to next keyframe");
                    }
                    else
                    {
                        c.Enqueue(packet);
                    }
                }
            }
            if (dead != null)
                foreach (var c in dead)
                {
                    Log("Viewer not responding - disconnecting it");
                    c.Close();
                }
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
