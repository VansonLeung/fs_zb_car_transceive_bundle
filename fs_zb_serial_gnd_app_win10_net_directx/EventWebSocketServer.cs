using System;
using System.Collections.Generic;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RCCarController
{
    public class EventWebSocketServer : IDisposable
    {
        private readonly HttpListener listener = new HttpListener();
        private readonly List<WebSocket> clients = new List<WebSocket>();
        private CancellationTokenSource? cts;
        private Task? acceptLoop;
        private readonly object syncRoot = new object();
        private bool disposed;

        public int Port { get; }
        public string Path { get; }

        public EventWebSocketServer(int port = 9091, string path = "/events/")
        {
            Port = port;
            Path = path.StartsWith("/") ? path : "/" + path;
        }

        public void Start()
        {
            if (disposed)
                return;
            if (cts != null)
                return;

            cts = new CancellationTokenSource();
            try
            {
                // Try broad binding; fall back to localhost if necessary.
                listener.Prefixes.Add($"http://+:{Port}{Path}");
            }
            catch
            {
                // Ignore and rely on localhost registration below
            }

            if (listener.Prefixes.Count == 0)
            {
                listener.Prefixes.Add($"http://localhost:{Port}{Path}");
            }

            listener.Start();
            acceptLoop = Task.Run(() => AcceptLoopAsync(cts.Token));
        }

        public void Stop()
        {
            try
            {
                cts?.Cancel();
                listener.Stop();
            }
            catch
            {
            }

            lock (syncRoot)
            {
                foreach (var ws in clients)
                {
                    try { ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "server stopping", CancellationToken.None).Wait(200); } catch { }
                    try { ws.Dispose(); } catch { }
                }
                clients.Clear();
            }

            try { acceptLoop?.Wait(500); } catch { }
            acceptLoop = null;
            cts?.Dispose();
            cts = null;
        }

        public async Task BroadcastAsync(object payload)
        {
            if (disposed) return;
            string json;
            try
            {
                json = JsonSerializer.Serialize(payload);
            }
            catch
            {
                return;
            }

            List<WebSocket> snapshot;
            lock (syncRoot)
            {
                snapshot = new List<WebSocket>(clients);
            }

            var buffer = Encoding.UTF8.GetBytes(json);
            var segment = new ArraySegment<byte>(buffer);
            foreach (var ws in snapshot)
            {
                if (ws.State != WebSocketState.Open)
                {
                    RemoveClient(ws);
                    continue;
                }

                try
                {
                    await ws.SendAsync(segment, WebSocketMessageType.Text, true, CancellationToken.None);
                }
                catch
                {
                    RemoveClient(ws);
                }
            }
        }

        private async Task AcceptLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                HttpListenerContext? context = null;
                try
                {
                    context = await listener.GetContextAsync();
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch
                {
                    if (token.IsCancellationRequested) break;
                    continue;
                }

                if (context == null)
                    continue;

                if (!context.Request.IsWebSocketRequest)
                {
                    context.Response.StatusCode = 400;
                    context.Response.Close();
                    continue;
                }

                try
                {
                    var wsContext = await context.AcceptWebSocketAsync(null);
                    var socket = wsContext.WebSocket;
                    lock (syncRoot)
                    {
                        clients.Add(socket);
                    }
                }
                catch
                {
                    // ignore failed accept
                }
            }
        }

        private void RemoveClient(WebSocket ws)
        {
            lock (syncRoot)
            {
                if (clients.Remove(ws))
                {
                    try { ws.Dispose(); } catch { }
                }
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Stop();
            listener.Close();
        }
    }
}
