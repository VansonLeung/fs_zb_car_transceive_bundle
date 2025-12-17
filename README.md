# FS ZB Car Transceive Bundle

This repository contains a collection of Arduino sketches and a .NET application for controlling an RC car via serial communication. The system supports various ESP boards (ESP32, ESP32C3, ESP8266) as air units and a Windows .NET app as the ground station.

## Components

### Arduino Sketches (Air Units)
- `fs_serial_proxy_arduino/` - Basic serial proxy for Arduino Uno
- `fs_zb_serial_air_arduino_esp32/` - ESP32-based air unit with servo control
- `fs_zb_serial_air_arduino_esp32c3/` - ESP32-C3 variant
- `fs_zb_serial_air_arduino_esp8266/` - ESP8266-based air unit with limited throttle range (40-140)

### .NET Ground Station
- `fs_zb_serial_gnd_app_win10_net_directx/` - Avalonia-based Windows app with DirectX gamepad support

## Features

- Serial communication protocol: "S<steering>T<throttle>" (e.g., "S090T090")
- Steering: 0-180 degrees
- Throttle: Varies by board (1-180 for ESP32, 40-140 for ESP8266)
- Gamepad support (Windows only)
- Keyboard controls (WASD)
- Real-time UI updates

## Build for Windows

```
cd /Users/van/Documents/projects/fs/fs_zb_car_transceive_bundle/fs_zb_serial_gnd_app_win10_net_directx && dotnet publish -c Release -r win-x64 --self-contained true
```


## Setup

1. Upload the appropriate Arduino sketch to your ESP board
2. Build and run the .NET app on Windows
3. Connect via serial port
4. Use gamepad or keyboard to control the car

## Protocol

Commands are sent as strings over serial:
- Steering: 3 digits (000-180)
- Throttle: 3 digits (board-dependent range)

Example: "S090T090" (center steering, neutral throttle)

## Development

- Arduino sketches use Servo library for PWM control
- .NET app uses Avalonia UI framework
- DirectX integration for game controllers

## License

[Add license information here]
