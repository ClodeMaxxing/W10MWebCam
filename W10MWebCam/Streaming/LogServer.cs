using System;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Networking.Sockets;

namespace W10MWebCam.Streaming
{
    /// <summary>Tiny HTTP server on http://PHONE-IP:5001 for Wi-Fi: the log, /status and the settings page (see HttpApi).</summary>
    internal sealed class LogServer
    {
        private readonly HttpApi _api;
        private StreamSocketListener _listener;

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
            try
            {
                using (var socket = args.Socket)
                {
                    bool fromUsb = socket.Information.RemoteAddress?.DisplayName == "127.0.0.1";
                    var request = await HttpApi.ReadAsync(socket.InputStream, null, fromUsb, socket.Information.RemoteAddress?.DisplayName);
                    if (request == null) return;
                    byte[] reply = await _api.HandleAsync(request);
                    await socket.OutputStream.WriteAsync(reply.AsBuffer());
                    await socket.OutputStream.FlushAsync();
                }
            }
            catch { }
        }
    }
}
