using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace RCCarController;

/// <summary>
/// Handles receiving control data from a WebSocket server and exposing mapped values.
/// </summary>
public class WebSocketInputManager : IDisposable
{
    private const string WebSocketUrl = "ws://localhost:8080/";
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);

    private ClientWebSocket? client;
    private CancellationTokenSource? cts;
    private Task? workerTask;
    private bool disposed;

    public event Action<int, int>? ControlValuesChanged; // steering, throttle (0-180)
    public event Action<string>? StatusChanged;

    public int? LastRawSteering { get; private set; }
    public int? LastRawThrottle { get; private set; }
    public int? LastRawBrake { get; private set; }

    public bool IsRunning => workerTask != null && !workerTask.IsCompleted;

    public void Start()
    {
        if (disposed || IsRunning)
            return;

        cts = new CancellationTokenSource();
        workerTask = Task.Run(() => RunAsync(cts.Token));
    }

    public void Stop()
    {
        if (cts == null && client == null && workerTask == null)
            return;

        cts?.Cancel();
        try
        {
            workerTask?.Wait(TimeSpan.FromSeconds(1));
        }
        catch
        {
            // Ignore shutdown errors.
        }
        try
        {
            client?.CloseAsync(WebSocketCloseStatus.NormalClosure, "Stopping", CancellationToken.None)
                .Wait(TimeSpan.FromSeconds(1));
        }
        catch
        {
            // Ignore shutdown errors.
        }
        client?.Dispose();
        client = null;
        cts?.Dispose();
        cts = null;
        workerTask = null;
    }

    private async Task RunAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                StatusChanged?.Invoke("...");
                client = new ClientWebSocket();
                await client.ConnectAsync(new Uri(WebSocketUrl), token);
                StatusChanged?.Invoke("ENGINE: ON");

                await ReceiveLoopAsync(client, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Error: {ex.Message}");
            }
            finally
            {
                client?.Dispose();
                client = null;
            }

            if (token.IsCancellationRequested)
                break;

            StatusChanged?.Invoke($"Reconnecting in {ReconnectDelay.TotalSeconds:0} seconds...");
            try
            {
                await Task.Delay(ReconnectDelay, token);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken token)
    {
        var buffer = new byte[4096];
        using var ms = new MemoryStream();

        while (!token.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            var result = await socket.ReceiveAsync(buffer.AsMemory(0, buffer.Length), token);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                break;
            }

            ms.Write(buffer, 0, result.Count);

            if (result.EndOfMessage)
            {
                var message = Encoding.UTF8.GetString(ms.ToArray());
                ms.SetLength(0);
                ProcessMessage(message);
            }
        }
    }

    private void ProcessMessage(string message)
    {
        try
        {
            using var doc = JsonDocument.Parse(message);
            var root = doc.RootElement;

            int steeringRaw = root.TryGetProperty("steering", out var steeringElement)
                ? steeringElement.GetInt32()
                : 32767;
            int throttleRaw = root.TryGetProperty("throttle", out var throttleElement)
                ? throttleElement.GetInt32()
                : 0;
            int brakeRaw = root.TryGetProperty("brake", out var brakeElement)
                ? brakeElement.GetInt32()
                : 0;

            LastRawSteering = steeringRaw;
            LastRawThrottle = throttleRaw;
            LastRawBrake = brakeRaw;

            // Combine throttle and brake around neutral (90) so more throttle moves below 90 and more brake moves above 90.
            int netInput = Math.Clamp(throttleRaw - brakeRaw, -65535, 65535);
            double normalized = netInput / 65535.0; // -1 .. 1
            double centeredThrottle = 90 - normalized * 90; // 0..180 with 90 as neutral

            int steering = MapToRange(steeringRaw, 0, 65535, 0, 180);
            int throttle = (int)Math.Round(Math.Clamp(centeredThrottle, 0, 180));

            ControlValuesChanged?.Invoke(steering, throttle);
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke($"Parse error: {ex.Message}");
        }
    }

    private int MapToRange(int value, int fromMin, int fromMax, int toMin, int toMax)
    {
        double clamped = Math.Clamp(value, fromMin, fromMax);
        double scaled = (clamped - fromMin) * (toMax - toMin) / (fromMax - fromMin) + toMin;
        return (int)Math.Round(Math.Clamp(scaled, toMin, toMax));
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        Stop();
        cts?.Dispose();
    }
}
