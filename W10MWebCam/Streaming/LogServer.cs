using System;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.Networking.Sockets;

namespace W10MWebCam.Streaming
{
    /// <summary>Tiny HTTP server on http://PHONE-IP:5001 for Wi-Fi: the log, /status and the settings page (see HttpApi).</summary>
    internal sealed class LogServer
    {
        private const int MaxConnections = 16; // more at once get a quick 503
        private static readonly TimeSpan WriteTimeout = TimeSpan.FromSeconds(30);

        private readonly HttpApi _api;
        private StreamSocketListener _listener;
        private int _active;
        private DateTime _lastErrorLog = DateTime.MinValue;
        private int _quietErrors;
        private readonly object _logLock = new object();

        public Action<string> Log = _ => { };

        public LogServer(HttpApi api) { _api = api; }

        public async Task StartAsync(int port)
        {
            if (_listener != null) return;
            _listener = new StreamSocketListener();
            _listener.ConnectionReceived += OnConnection;
            await _listener.BindServiceNameAsync(port.ToString());
        }

        public void Stop()
        {
            _listener?.Dispose();
            _listener = null;
        }

        private async void OnConnection(StreamSocketListener sender, StreamSocketListenerConnectionReceivedEventArgs args)
        {
            int active = Interlocked.Increment(ref _active);
            try
            {
                using (var socket = args.Socket)
                {
                    byte[] reply;
                    if (active > MaxConnections)
                        reply = HttpApi.Reply(503, "Service Unavailable", "text/plain", "Busy, try again\n");
                    else
                    {
                        bool fromUsb = socket.Information.RemoteAddress?.DisplayName == "127.0.0.1";
                        var request = await HttpApi.ReadAsync(socket.InputStream, null, fromUsb, socket.Information.RemoteAddress?.DisplayName);
                        if (request == null) return; // closed, too large, or not complete within 10 s
                        reply = await _api.HandleAsync(request);
                    }
                    using (var cts = new CancellationTokenSource(WriteTimeout))
                    {
                        await socket.OutputStream.WriteAsync(reply.AsBuffer()).AsTask(cts.Token);
                        await socket.OutputStream.FlushAsync().AsTask(cts.Token);
                    }
                }
            }
            catch (Exception ex) { LogError(ex); }
            finally { Interlocked.Decrement(ref _active); }
        }

        /// <summary>At most one line per 30 s, so a client that keeps failing cannot flood the log.</summary>
        private void LogError(Exception ex)
        {
            string line;
            lock (_logLock)
            {
                var now = DateTime.UtcNow;
                if ((now - _lastErrorLog).TotalSeconds < 30) { _quietErrors++; return; }
                line = "Web port: request failed (" + (ex is OperationCanceledException ? "timed out" : ex.Message) + ")" +
                       (_quietErrors > 0 ? " - " + _quietErrors + " more since the last message" : "");
                _lastErrorLog = now;
                _quietErrors = 0;
            }
            Log(line);
        }
    }
}
