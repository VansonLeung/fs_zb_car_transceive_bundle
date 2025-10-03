# RC Car Remote Control System - Development Guide

## Overview

This project implements a remote control system for an RC car using ESP8266 Arduino MCU, controlled via PC application through Zigbee-powered serial UART bridge. The system allows wireless control of steering servo and ESC throttle motor.

## System Architecture

```
PC (Cross-platform) <---UART---> ESP8266 Serial Proxy <---UART---> Device
PC (Cross-platform) <---UART (9600 baud)---> Zigbee Bridge <---UART---> ESP8266/ESP32/ESP32-C3 Arduino <---> Servo & ESC
```

### Components

1. **ESP8266/ESP32/ESP32-C3 Arduino MCU** - Main controller board (ESP8266, ESP32, or ESP32-C3)
2. **Steering Servo** - Controls car steering (0-180 degrees)
3. **ESC (Electronic Speed Controller)** - Controls car throttle motor
4. **Zigbee UART Bridge Pair** - Wireless serial communication bridge
5. **Serial Proxy ESP8266** - Serial data forwarding bridge for debugging/testing
6. **Cross-Platform PC Application** - Control interface with keyboard and game controller support

## Hardware Setup

### ESP8266 Arduino Pin Connections

| Component | ESP8266 Pin | GPIO Number | Purpose |
|-----------|-------------|-------------|---------|
| Steering Servo | D5 | GPIO14 | PWM signal for servo |
| ESC Throttle | D6 | GPIO12 | PWM signal for ESC |
| Zigbee RX | RX | GPIO3 | Serial receive from Zigbee |
| Zigbee TX | TX | GPIO1 | Serial transmit to Zigbee |

### Power Requirements

- **ESP8266**: 3.3V (regulated)
- **Servo**: 5-6V (typically 5V)
- **ESC**: According to motor specifications (usually 6-12V)
- **Zigbee Modules**: 3.3V

**Important**: Ensure proper power isolation between control signals and motor power to prevent electrical noise.

## Communication Protocol

### UART Settings
- **Baud Rate**: 9600
- **Data Bits**: 8
- **Stop Bits**: 1
- **Parity**: None

### Command Format
Commands are sent as ASCII strings terminated with newline (`\n`):

```
SxxxTyyy\n
```

Where:
- `S` = Steering command prefix
- `xxx` = Steering value (000-180)
- `T` = Throttle command prefix
- `yyy` = Throttle value (001-180)

**Examples:**
- `S090T090\n` - Center steering, neutral throttle
- `S000T120\n` - Full left steering, forward throttle
- `S180T060\n` - Full right steering, reverse throttle

### Response Format
Arduino acknowledges each command with:

```
OK:SxxxTyyy
```

Where:
- `OK:` = Acknowledgement prefix
- `Sxxx` = Current steering value (000-180)
- `Tyyy` = Current throttle value (001-180)

## Software Components

### 1. Arduino ESP8266 Firmware

**File**: `fs_zb_serial_air_arduino_esp8266/fs_zb_serial_air_arduino.ino`

**Features:**
- UART communication at 9600 baud
- Servo control (GPIO14/D5)
- ESC control (GPIO12/D6)
- Command parsing and validation
- Debug output via serial

**Setup:**
1. Install Arduino IDE
2. Install ESP8266 board support
3. Open the `.ino` file
4. Select "NodeMCU 1.0 (ESP-12E Module)" board
5. Set CPU Frequency to 80MHz
6. Upload to ESP8266

### 1.5. Arduino ESP32 Firmware

**File**: `fs_zb_serial_air_arduino_esp32/fs_zb_serial_air_arduino.ino`

**Features:**
- UART communication at 9600 baud
- Servo control (GPIO12)
- ESC control (GPIO13)
- Command parsing and validation
- Debug output via serial

**Setup:**
1. Install Arduino IDE
2. Install ESP32 board support
3. Open the `.ino` file
4. Select "ESP32 Dev Module" board
5. Set Upload Speed to 115200
6. Set CPU Frequency to 240MHz
7. Upload to ESP32

### 1.6. Arduino ESP32-C3 Firmware

**File**: `fs_zb_serial_air_arduino_esp32c3/fs_zb_serial_air_arduino.ino`

**Features:**
- UART communication at 9600 baud
- Servo control (GPIO2)
- ESC control (GPIO3)
- Command parsing and validation
- Debug output via serial

**Setup:**
1. Install Arduino IDE
2. Install ESP32 board support
3. Open the `.ino` file
4. Select "ESP32C3 Dev Module" board
5. Set Upload Speed to 115200
6. Set CPU Frequency to 160MHz
7. Set Flash Size to 4MB
8. Upload to ESP32-C3

### 1.5. Serial Proxy Arduino Firmware

**File**: `fs_serial_proxy_arduino/fs_serial_proxy_arduino.ino`

**Features:**
- Serial data forwarding between hardware and software serial ports
- Hardware serial: 115200 baud (USB connection)
- Software serial: 9600 baud (pins D1/D2)
- Bidirectional data forwarding
- Debug output via hardware serial

**Pin Connections:**
- **D1 (GPIO5)**: Software Serial RX (connect to device's TX)
- **D2 (GPIO4)**: Software Serial TX (connect to device's RX)

**Setup:**
1. Install Arduino IDE
2. Install ESP8266 board support
3. Open the `.ino` file
4. Select "NodeMCU 1.0 (ESP-12E Module)" board
5. Set CPU Frequency to 80MHz
6. Upload to ESP8266

**Usage:**
- Connect the device to D1/D2 pins
- Use USB serial for monitoring/debugging
- All data sent to USB serial is forwarded to the device
- All data received from device is forwarded to USB serial

### 2. Cross-Platform PC Control Application

**Project**: `fs_zb_serial_gnd_app_win10_net_directx/`

**Requirements:**
- .NET 8.0 or later
- Windows, macOS, or Linux

**Features:**
- Manual control via UI sliders
- Keyboard control (WASD or arrow keys)
- Game controller support on Windows (DirectX-compatible)
- Serial port auto-detection
- Settings auto-save
- Real-time data transmission (10ms intervals)
- Command acknowledgement display
- Debug logging

**Building:**
```bash
cd fs_zb_car_transcieve_bundle/fs_zb_serial_gnd_app_win10_net_directx
dotnet build
```

**Running:**
```bash
dotnet run
```

## Usage Instructions

### Initial Setup

1. **Hardware Assembly:**
   - Connect servo to ESP8266 GPIO14 (D5)
   - Connect ESC to ESP8266 GPIO12 (D6)
   - Connect Zigbee modules to ESP8266 TX/RX pins
   - Power all components appropriately

2. **Zigbee Configuration:**
   - Configure Zigbee modules for bridge mode
   - Set both modules to same channel and PAN ID
   - Ensure proper UART baud rate (9600)

3. **Arduino Programming:**
   - Upload firmware to ESP8266
   - Verify serial output in Arduino IDE

4. **PC Application:**
   - Build and run the Windows application
   - Select correct serial port (Zigbee receiver)
   - Set baud rate to 9600
   - Click "Connect"

### Control Methods

#### Manual Control (UI Sliders)
1. Use steering slider (0-180) for steering angle
2. Use throttle slider (1-180) for motor speed/direction
3. Values update in real-time

#### Keyboard Control
1. Steering: A/D keys or Left/Right arrow keys
2. Throttle: W/S keys or Up/Down arrow keys
3. Values automatically adjust in 5-unit increments

#### Game Controller Control (Windows only)
1. Connect DirectX-compatible controller
2. Application auto-detects controller on Windows
3. Steering: X-axis (left stick or wheel)
4. Throttle: Y-axis (right stick or pedals)
5. Values automatically map to 0-180 range

### Operation

1. Ensure all hardware is powered and connected
2. Launch PC application
3. Select serial port and connect
4. Use sliders or game controller to control car
5. Monitor debug log for connection status
6. Values are transmitted every 10ms when connected

## Troubleshooting

### Common Issues

1. **No Serial Connection:**
   - Check Zigbee module power and connections
   - Verify Zigbee channel/PAN ID configuration
   - Ensure correct COM port selection

2. **Servo Not Responding:**
   - Check servo power supply (5-6V)
   - Verify GPIO14 connection
   - Confirm servo signal wire polarity

3. **ESC Not Responding:**
   - Check ESC power supply
   - Verify GPIO12 connection
   - Ensure ESC is properly calibrated

4. **Game Controller Not Detected (Windows only):**
   - Install latest DirectX
   - Test controller in Windows Game Controllers
   - Check USB connection
   - On macOS/Linux, use keyboard controls instead

### Debug Information

- Arduino serial output shows received commands and responses
- PC application debug log shows connection status and sent commands
- Use Arduino IDE Serial Monitor to debug ESP8266

## Development Notes

### Code Structure

- **Arduino**: Simple command parser with servo control
- **PC App**: Avalonia UI with keyboard input, DirectX input (Windows), and serial communication
- **Protocol**: ASCII-based command/response system

### Future Enhancements

- Add telemetry feedback (battery voltage, motor RPM)
- Implement PID control for better motor response
- Add camera streaming capability
- Support multiple RC cars on same network
- Add mobile app support (Flutter)

### Safety Considerations

- Always test with low throttle values first
- Ensure proper power isolation between control and motor circuits
- Use appropriate fuses and protection circuits
- Test in open areas away from people and obstacles

## License

This project is open source. Use at your own risk.
