# FS ESP32-C3 UDP Air Unit Feature Spec

## Goal

Create a new ESP32-C3 air-unit sketch named `fs_zb_serial_air_arduino_esp32c3_udp.ino` that replaces ESP-NOW with WiFi networking and uses:

- UDP as the main real-time RC transport
- HTTP for settings and browser UI
- WebSocket as a planned next-step transport for browser/mobile live control and telemetry
- mDNS and UDP discovery for device discovery

This folder contains the first implementation slice and the agreed full-scope contract.

## Delivery Slices

### Slice 1: implemented now

- ESP32-C3 sketch with modular classes
- AP-first networking
- AP+STA capable runtime with AP kept alive for recovery
- persisted settings via `Preferences`
- authenticated HTTP UI
- UDP control receiver
- UDP test sender
- UDP discovery reply
- telemetry JSON endpoint
- RC parameter editor and reset
- RTSP URL editor and reset
- placeholder pages for later richer RC and UDP tooling

### Slice 2: planned next

- WebSocket live control and telemetry push
- richer browser RC page with smoother control loop
- richer mobile-friendly joystick UI
- full WiFi mode workflow polish and status history
- stronger admin auth options

## Runtime Model

### WiFi behavior

- Device starts AP mode by default.
- AP remains enabled when STA is also enabled.
- If STA credentials are configured, device also attempts STA connection.
- If STA connection fails, AP stays available so the device remains recoverable.
- STA retry policy:
  - initial connect window: 15 seconds
  - background retry interval: 30 seconds
- mDNS host name is configurable.

### Suggested STA failure behavior

- Keep AP running at all times during STA attempts.
- Show STA failure state in web telemetry and WiFi page.
- Retry STA in background.
- Do not erase saved STA credentials automatically.
- Provide explicit factory reset and WiFi reset actions.

## Web UI Areas

### Dashboard

- summary of WiFi state
- summary of control state
- summary of last UDP packet
- links to feature pages

### WiFi page

- enable or disable STA
- keep AP enabled while STA runs
- edit AP SSID and password
- edit STA SSID and password
- edit mDNS host name
- edit admin password
- reset WiFi settings to factory defaults

### UDP page

- edit UDP listen port
- edit UDP test target host and port
- send UDP test payload from ESP32
- inspect last received UDP packet summary
- inspect discovery behavior summary

### RC page

- first slice: basic browser control stub and simple manual control
- next slice: keyboard + touch + joystick over WebSocket

### RC params page

- edit steering neutral/min/max/step
- edit throttle neutral/min/max/step
- edit failsafe timeout
- reset RC params to factory defaults

### RTSP page

- edit RTSP URL
- launch RTSP URL in client app or browser handler
- reset RTSP URL to factory default

## Authentication

- First slice uses HTTP Basic Auth.
- Username is fixed as `admin`.
- Password is configurable and persisted.
- Default password: `admin1234`

## Persisted Settings Model

```text
wifi.staEnabled
wifi.apAlwaysOn
wifi.apSsid
wifi.apPassword
wifi.staSsid
wifi.staPassword
wifi.hostName
security.adminPassword
udp.listenPort
udp.testHost
udp.testPort
rtsp.url
rc.steeringNeutral
rc.throttleNeutral
rc.steeringMin
rc.steeringMax
rc.throttleMin
rc.throttleMax
rc.steeringStep
rc.throttleStep
rc.failsafeTimeoutMs
```

## UDP Control Protocol

### Primary binary packet

Binary packet is little-endian and fixed-size.

```c
struct UdpControlPacketV1 {
  uint16_t magic;        // 0xF5A5
  uint8_t version;       // 1
  uint8_t flags;         // bit field, reserved for now
  uint16_t sequence;     // client sequence number
  uint16_t steering;     // expected range 0..180, constrained by RC params
  uint16_t throttle;     // expected range 0..180, constrained by RC params
  uint32_t clientMillis; // sender timestamp in milliseconds
};
```

### Binary semantics

- `magic` rejects random traffic.
- `version` allows future expansion.
- `sequence` helps clients detect drops or stale frames.
- `flags` is reserved for future actions such as lights, horn, arm/disarm, or mode bits.
- `clientMillis` is advisory telemetry only.

### Debug text packet

First slice also accepts a text command for manual testing:

```text
S090T090
```

Format:

- `S` followed by 3 digits for steering
- `T` followed by 3 digits for throttle

Example:

- `S120T100`

## UDP Discovery Protocol

### Request

```text
FSRC_DISCOVER_V1
```

### Response

JSON reply over UDP to the sender:

```json
{
  "device":"fs-rc-air",
  "version":1,
  "host":"fs-rc-air",
  "apIp":"192.168.4.1",
  "staConnected":false,
  "staIp":"",
  "udpPort":5000
}
```

## HTTP API Contract

### `GET /api/telemetry`

Returns JSON including:

- WiFi mode and connection status
- AP SSID and AP IP
- STA status and STA IP
- host name
- UDP listen port
- last UDP sender IP and port
- age of last control packet
- current steering and throttle
- target steering and throttle
- failsafe state
- RTSP URL

### `POST /api/control`

Form fields or query values:

- `steering`
- `throttle`

Applies the same control path as UDP input.

### `POST /udp/send`

Form fields:

- `host`
- `port`
- `payload`

Sends a UDP datagram from the ESP32.

## Default Values

- AP SSID: `FS-RC-Air-C3`
- AP password: `fsrc1234`
- mDNS host: `fs-rc-air`
- admin password: `admin1234`
- UDP listen port: `5000`
- UDP test target host: `192.168.4.2`
- UDP test target port: `5000`
- RTSP URL: `rtsp://192.168.4.2/live`
- steering neutral: `90`
- throttle neutral: `90`
- steering min/max: `5 / 175`
- throttle min/max: `15 / 160`
- steering step: `20`
- throttle step: `5`
- failsafe timeout: `500 ms`

## Reuse Direction

The code is intentionally split into reusable concerns:

- `SettingsStore`
- `WifiService`
- `RcController`
- `UdpControlService`
- `WebUiServer`

These can be extracted later into shared foundational libraries for other IoT apps such as routers, sensors, or camera helpers.