using System;
using System.IO.Ports;

namespace RCCarController
{
    public class SerialManager
    {
        private SerialPort? serialPort;
        private bool isConnected = false;

        public event Action<string>? DataReceived;
        public event Action<string>? TransmissionError;

        public bool IsConnected => isConnected;

        public SerialManager()
        {
        }

        public void SendCommand(int steering, int throttle)
        {
            if (isConnected && serialPort != null)
            {
                try
                {
                    string command = $"S{steering:D3}T{throttle:D3}\n";
                    serialPort.Write(command);
                    LatestMessage = $"S{steering:D3}T{throttle:D3}";
                }
                catch (Exception ex)
                {
                    TransmissionError?.Invoke(ex.Message);
                    Disconnect();
                }
            }
        }

        public void SendMacRange(int startIndex, int endIndex)
        {
            if (!isConnected || serialPort == null)
                return;

            startIndex = Math.Clamp(startIndex, 0, 255);
            endIndex = Math.Clamp(endIndex, 0, 255);

            if (startIndex > endIndex)
                return;

            serialPort.Write($"MACLIST {startIndex} {endIndex}\n");
        }

        public void RequestActiveMac()
        {
            if (!isConnected || serialPort == null)
                return;

            serialPort.Write("MACACTIVE?\n");
        }

        public void SendMacSelect(int index)
        {
            if (!isConnected || serialPort == null)
                return;

            serialPort.Write($"MACSELECT {index}\n");
        }

        public string LatestMessage { get; private set; } = "";

        public async Task<bool> Connect(string portName, int baudRate)
        {
            try
            {
                serialPort = new SerialPort(portName, baudRate);
                serialPort.DataReceived += SerialPort_DataReceived;
                serialPort.Open();
                isConnected = true;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public void Disconnect()
        {
            if (serialPort != null && serialPort.IsOpen)
            {
                serialPort.Close();
            }
            isConnected = false;
            LatestMessage = "(disconnected)";
        }

        private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                string data = serialPort.ReadLine().Trim();
                DataReceived?.Invoke(data);
            }
            catch (Exception ex)
            {
                TransmissionError?.Invoke(ex.Message);
            }
        }

        public string[] GetAvailablePorts()
        {
            return SerialPort.GetPortNames();
        }
    }
}