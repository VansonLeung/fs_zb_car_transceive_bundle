# FS ESP32-C3 UDP Air Unit

This folder contains a WiFi-based ESP32-C3 air-unit firmware for RC control over UDP, with a browser UI for setup, testing, and control.

The code is intentionally organized so you can reuse the non-RC foundation for another IoT app, or swap the RC-car-specific parts for an RC robot, boat, turret, crawler, or a different actuator layout.

## Design Goal

The main architectural rule is:

- keep transport, settings, WiFi, and generic device plumbing on the core side
- keep steering, throttle, actuator behavior, and RC-facing UI on the RC side

That separation matters because it lets you reuse the same base stack for:

- another RC vehicle with a different control model
- an RC robot app with tracks, arm joints, or a camera gimbal
- a non-RC IoT app that still needs WiFi setup, UDP messaging, discovery, and a web admin UI

## File Structure

### Composition root

- [fs_zb_serial_air_arduino_esp32c3_udp.ino](./fs_zb_serial_air_arduino_esp32c3_udp.ino)

This should stay small. It wires the modules together, owns the top-level globals, and runs `setup()` and `loop()`.

It should not become a place for feature logic.

### Core basic modules

- [FsCoreTypes.h](./FsCoreTypes.h)
- [AppSettings.h](./AppSettings.h)
- [SettingsStore.h](./SettingsStore.h)
- [SettingsStore.cpp](./SettingsStore.cpp)
- [WifiService.h](./WifiService.h)
- [WifiService.cpp](./WifiService.cpp)
- [UdpControlService.h](./UdpControlService.h)
- [UdpControlService.cpp](./UdpControlService.cpp)

These are the most reusable parts.

#### `FsCoreTypes.h`

Contains reusable configuration and transport-side types:

- WiFi settings
- UDP settings
- admin/security settings
- last-packet telemetry shape

This header is intended to be reusable even if you remove RC control entirely.

#### `AppSettings.h`

Defines the aggregate application settings object.

This is the place where core settings and feature settings are assembled into one saved runtime configuration.

If you build another app, this file is where you decide which feature settings belong to that app.

#### `SettingsStore`

Owns loading, saving, defaults, and normalization through `Preferences`.

This is a reusable persistence boundary. If a future app has different features, update the stored fields here rather than spreading `Preferences` calls across the codebase.

#### `WifiService`

Owns AP/STA setup, retry policy, and mDNS.

This is a base device-connectivity module and should remain independent from steering, throttle, ESC, or UI-specific behavior.

#### `UdpControlService`

Owns UDP receive/send, discovery response dispatch, and packet parsing.

Today it accepts the RC control packet, but the transport service itself is still conceptually core infrastructure. In another app, you could keep the same skeleton and replace only the payload parser and callback contract.

### RC-control and RC-settings-specific modules

- [RcFeatureTypes.h](./RcFeatureTypes.h)
- [RcController.h](./RcController.h)
- [RcController.cpp](./RcController.cpp)

These are the most domain-specific parts.

#### `RcFeatureTypes.h`

Contains the RC-domain constants and payload shape:

- steering and throttle defaults
- control limits and slew defaults
- servo and ESC pin assignments
- RC UDP packet format

These are not generic IoT concerns. They belong to the RC feature side.

If you build a robot with left/right tracks or arm joints instead of car steering/throttle, this is one of the first files you would redesign.

#### `RcController`

Owns actuator behavior:

- servo and ESC attachment
- control acceptance
- slew limiting
- failsafe behavior
- non-blocking test motions

This module should stay focused on motion behavior only.

It should not know about:

- WiFi credentials
- browser authentication
- HTTP routes
- persistence details

### App-facing UI module

- [WebUiServer.h](./WebUiServer.h)
- [WebUiServer.cpp](./WebUiServer.cpp)

This module is the app-facing presentation layer.

It currently contains both:

- generic device admin pages such as WiFi and UDP tools
- RC-specific pages such as browser drive control and RC parameter editing

This is acceptable for the current size, but it is the next natural place to split further if the app grows.

Recommended future split:

- `WebUiCorePages` for dashboard, WiFi, UDP, telemetry, auth helpers
- `WebUiRcPages` for RC control, RC params, motion tests
- `WebUiMediaPages` for RTSP settings

### Compatibility umbrella

- [FsRcTypes.h](./FsRcTypes.h)

This now exists only as a thin compatibility include.

New code should prefer the narrower headers directly rather than reintroducing a single mixed types file.

## Dependency Rules

Use these rules when extending the project.

### Allowed direction

- `.ino` may depend on all modules
- UI modules may depend on core modules and RC modules
- RC modules may depend on RC feature types
- core modules may depend on core types and app settings

### Avoid

- `WifiService` depending on `RcController`
- `SettingsStore` depending on web route logic
- `RcController` depending on `WebServer`
- core type headers importing RC actuator types unless the app aggregate truly needs them

## What Was Refined In This Review

The main separation issue found during review was that [FsRcTypes.h](./FsRcTypes.h) mixed reusable core settings/transport types with RC-domain actuator and control types.

That has now been split into:

- [FsCoreTypes.h](./FsCoreTypes.h) for reusable base concerns
- [RcFeatureTypes.h](./RcFeatureTypes.h) for RC-domain concerns
- [AppSettings.h](./AppSettings.h) for app-level aggregation

This makes the reuse boundary clearer and reduces the chance that a future non-RC app accidentally drags in car-specific constants and protocol assumptions.

## How To Reuse This For Another App

### For another RC vehicle or RC robot

Usually keep:

- `SettingsStore`
- `WifiService`
- most of `UdpControlService`
- parts of `WebUiServer` related to WiFi, UDP, auth, and telemetry

Usually replace or heavily edit:

- `RcFeatureTypes.h`
- `RcController`
- RC parts of `WebUiServer`

Example: tracked robot

- replace `steering` and `throttle` with `leftTrack` and `rightTrack`
- replace servo/ESC output logic with dual motor-driver output logic
- keep the same WiFi setup, admin password, AP/STA retry policy, and discovery transport

### For a non-RC IoT app

Usually keep:

- `FsCoreTypes.h`
- `AppSettings.h` structure pattern
- `SettingsStore`
- `WifiService`
- `UdpControlService` transport shell

Usually remove:

- `RcFeatureTypes.h`
- `RcController`
- RC pages in `WebUiServer`

Example: smart temperature sensor

- replace `RcController` with a `SensorService`
- keep WiFi AP/STA setup and admin UI
- keep UDP discovery for mobile app finding the device
- repurpose the web UI to show sensor values and calibration settings

## Build Notes

Current verified compile target:

- `esp32:esp32:nologo_esp32c3_super_mini`

Example compile command:

```sh
"/Applications/Arduino IDE.app/Contents/Resources/app/lib/backend/resources/arduino-cli" compile --fqbn esp32:esp32:nologo_esp32c3_super_mini /Users/van/Documents/projects/fs/fs_zb_car_transceive_bundle/fs_zb_serial_air_arduino_esp32c3_udp
```

## Next Recommended Refactor

The next highest-value separation is inside [WebUiServer.cpp](./WebUiServer.cpp).

If the app continues to grow, split it by feature area rather than by technical helper function:

- core admin pages
- RC control pages
- media or RTSP pages

That will keep the same architecture pattern consistent:

- reusable base modules on one side
- domain-specific feature modules on the other