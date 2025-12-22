using System;
using System.IO.Ports;
using System.Text;
using System.Timers;

namespace RCCarController
{
    public class QrScannerManager : IDisposable
    {
        private SerialPort? serialPort;
        private readonly StringBuilder buffer = new StringBuilder();
        private bool disposed;
        private readonly System.Timers.Timer flushTimer = new System.Timers.Timer(150) { AutoReset = false };
        private string? lastScanPayload;
        private DateTime lastScanAt = DateTime.MinValue;

        public event Action<string>? QrScanned;
        public event Action<string>? StatusChanged;

        public bool IsConnected => serialPort?.IsOpen == true;

        /// <summary>
        /// Debounce window in milliseconds for identical payloads to avoid double-trigger.
        /// </summary>
        public int DebounceWindowMs { get; set; } = 1500;

        public string[] GetAvailablePorts() => SerialPort.GetPortNames();

        public bool Connect(string portName, int baud = 115200)
        {
            if (disposed)
                return false;

            try
            {
                Disconnect();
                serialPort = new SerialPort(portName, baud)
                {
                    NewLine = "\n",
                    Encoding = Encoding.UTF8
                };
                serialPort.DataReceived += SerialPort_DataReceived;
                flushTimer.Elapsed += FlushTimer_Elapsed;
                serialPort.Open();
                StatusChanged?.Invoke($"Scanner connected: {portName}");
                return true;
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Scanner error: {ex.Message}");
                Disconnect();
                return false;
            }
        }

        public void Disconnect()
        {
            if (serialPort != null)
            {
                try
                {
                    serialPort.DataReceived -= SerialPort_DataReceived;
                    if (serialPort.IsOpen)
                    {
                        serialPort.Close();
                    }
                }
                catch
                {
                }
            }
            serialPort = null;
            buffer.Clear();
            flushTimer.Stop();
            flushTimer.Elapsed -= FlushTimer_Elapsed;
        }

        private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                if (serialPort == null)
                    return;

                string incoming = serialPort.ReadExisting();
                buffer.Append(incoming);

                // Normalize to newline; handle devices sending \r, \n, or \r\n
                string current = buffer.ToString();
                int splitIndex;
                while ((splitIndex = IndexOfLineBreak(current)) >= 0)
                {
                    var line = current.Substring(0, splitIndex).Trim();
                    current = current.Substring(splitIndex + 1);
                    if (line.Length > 0)
                    {
                        EmitScan(line);
                    }
                }

                buffer.Clear();
                buffer.Append(current);

                // If no terminator arrives, emit after a short idle
                flushTimer.Stop();
                if (buffer.Length > 0)
                {
                    flushTimer.Start();
                }
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Scanner read error: {ex.Message}");
            }
        }

        private void FlushTimer_Elapsed(object? sender, ElapsedEventArgs e)
        {
            string pending;
            lock (buffer)
            {
                pending = buffer.ToString().Trim();
                buffer.Clear();
            }

            if (!string.IsNullOrEmpty(pending))
            {
                EmitScan(pending);
            }
        }

        private void EmitScan(string payload)
        {
            var now = DateTime.UtcNow;
            var windowMs = Math.Max(0, DebounceWindowMs);

            if (payload == lastScanPayload && (now - lastScanAt).TotalMilliseconds < windowMs)
            {
                return; // duplicate within debounce window; stay silent
            }

            lastScanPayload = payload;
            lastScanAt = now;
            QrScanned?.Invoke(payload);
        }

        private static int IndexOfLineBreak(string text)
        {
            if (string.IsNullOrEmpty(text)) return -1;
            int nIndex = text.IndexOf('\n');
            int rIndex = text.IndexOf('\r');
            if (nIndex == -1) return rIndex;
            if (rIndex == -1) return nIndex;
            return Math.Min(nIndex, rIndex);
        }

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;
            Disconnect();
            flushTimer?.Stop();
            flushTimer?.Dispose();
        }
    }
}
