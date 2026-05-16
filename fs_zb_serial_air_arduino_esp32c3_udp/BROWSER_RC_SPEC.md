# Browser RC Spec

This document specifies the browser-side RC control system implemented in the RC page.

Scope:
- Browser RC page logic and behavior.
- Browser settings and how they map to outgoing control commands.
- Gear and braking behavior.
- Live status bar behavior for control channel and websocket monitor.

Out of scope:
- Firmware internal servo/ESC output implementation details.
- UDP packet ingestion path.

## 1. Architecture Summary

The browser RC control flow is:
1. UI input state is collected from keyboard, touch pads, and dual joysticks.
2. Input state is transformed into logical steering and throttle requests.
3. Browser-only gear logic scales and signs throttle request.
4. Final command is posted to `/api/control` as `application/x-www-form-urlencoded`.
5. Active non-neutral state is refreshed at a short interval (resend heartbeat model).

Transport:
- Control transport uses HTTP POST to `/api/control`.
- WebSocket is monitor-only in current implementation (attempts connection to `/ws` and reports status).

## 2. Browser Settings Model

Browser settings are persisted with app settings and loaded on RC page render.

### 2.1 Core browser control settings
- `resendIntervalMs`
- `keyboardSteeringSpan`
- `keyboardThrottleSpan`
- `joystickSteeringSpan`
- `joystickThrottleSpan`

### 2.2 Browser gear settings
- `enableNeutralGear` (N toggle)
- `enableReverseGear` (R toggle)
- `maxForwardGear` in range 1..6
- `drive1Percent` .. `drive6Percent` in range 0..100
- `reversePercent` in range 0..100

Default profile:
- D1=40%
- D2=70%
- D3=100%
- D4..D6=100%
- R=40%
- startup gear in browser session is D1

Non-persistent runtime state:
- Current selected gear is not persisted; page load starts with D1.

## 3. Input Mapping

### 3.1 Keyboard mapping
- `W` / `ArrowUp`: throttle request active.
- `S` / `ArrowDown`: brake active (forces throttle to neutral).
- `A` / `ArrowLeft`: steer left.
- `D` / `ArrowRight`: steer right.
- `Space`: immediate neutral/stop helper.

Gear hotkeys:
- `1..6`: direct select D1..D6 (subject to `maxForwardGear`).
- `0`: select N (only when N enabled).
- `R`: select reverse (only when R enabled).
- `[` shift down.
- `]` shift up.

### 3.2 Touch pads
- Throttle pad: activates throttle request.
- Brake pad: activates brake request.
- Left/Right pads: steering request.

### 3.3 Joysticks
- Left joystick:
  - Up direction (`dy < 0`) increases throttle magnitude.
  - Down direction (`dy > 0`) engages brake if beyond threshold.
- Right joystick:
  - Horizontal axis controls steering.

## 4. Gear Path Rules

Available gears are dynamically built as ordered list:
1. Include `R` if `enableReverseGear` is true.
2. Include `N` if `enableNeutralGear` is true.
3. Include `D1..Dmax` where `Dmax=maxForwardGear`.

Shift behavior:
- Shift up/down moves within this list by index.
- If both R and N are disabled, list starts at D1 and shifting below D1 is prohibited.
- If R enabled and N disabled, shift between R and D1 is direct.
- If R disabled and N enabled, shift to R is impossible.

## 5. Steering/Throttle Computation

### 5.1 Steering
Steering precedence:
1. Right joystick steering when active.
2. Touch pad steer left/right when active.
3. Keyboard A/D.
4. Neutral steering.

Steering is clamped to `[steeringMin, steeringMax]`.

### 5.2 Throttle and brake
Throttle precedence:
1. Brake override: if any brake source active, output throttle neutral immediately.
2. If current gear is N, output throttle neutral.
3. Otherwise use throttle magnitude from joystick/pad/keyboard request.

Magnitude source order:
1. Throttle joystick magnitude.
2. Touch throttle pad (fixed configured span).
3. Keyboard throttle key (fixed configured span).

Effective throttle is computed as:
1. Determine direction sign from gear:
   - Reverse gear -> sign positive in servo space.
   - Drive gear -> sign negative in servo space.
2. Determine gear percentage:
   - `reversePercent` for R
   - `driveXPercent` for DX
3. Compute allowed directional range from neutral to min/max bound.
4. Apply `delta = range * inputMagnitude * (gearPercent / 100)`.
5. `throttle = neutral + sign * delta`.
6. Clamp to `[throttleMin, throttleMax]`.

Notes:
- Gear is browser-only scaling/sign logic.
- Firmware receives final steering/throttle values and does not need gear semantics.

## 6. Command Transmit Model

Endpoint:
- POST `/api/control`
- Body: `steering=<int>&throttle=<int>`

Refresh logic:
- Send immediately when payload changed.
- If payload is unchanged, resend only when:
  - state is non-neutral, and
  - elapsed time since last send >= `resendIntervalMs`.

This heartbeat behavior keeps firmware failsafe satisfied while holding a control.

## 7. Live Status Bar

The RC page renders a bottom live status bar with:
- Control status
- WebSocket status
- Last TX timestamp
- Control RTT

### 7.1 Control status states
- `idle`: no active send in progress and no recent activity.
- `sending`: a control POST is in-flight.
- `connected`: most recent control POST succeeded (HTTP 2xx).
- `error`: most recent control POST failed or non-2xx response.

### 7.2 WebSocket status states
WebSocket monitor attempts connection to `/ws`:
- `connecting`
- `connected`
- `disconnected`
- `error`

If no websocket endpoint is implemented server-side, monitor will typically cycle between `error`/`disconnected` and reconnect attempts.

### 7.3 Last TX and RTT
- Last TX displays local wall clock of last successful control POST.
- RTT displays request round-trip time in ms for the latest successful control POST.

## 8. Timing and Looping

Tick loop interval:
- `max(25, min(resendIntervalMs, max(25, failsafeTimeoutMs - 20)))`

Purpose:
- Stay below failsafe timeout margin.
- Avoid overloading browser/network with excessively fast loops.

## 9. Params Page Integration

Browser RC behavior is configurable from Params page:
- Browser control tuning fields.
- Browser gear settings fields.

Save behavior:
- Values are normalized and persisted.
- RC page uses saved values on next load.

Reset behavior:
- RC params and browser settings reset to defaults.

## 10. Replication Checklist

To replicate implementation behavior in another frontend:
1. Implement identical input state model (keyboard, pad, joysticks).
2. Implement gear list construction and shift navigation rules.
3. Apply steering/throttle precedence and brake override exactly.
4. Apply gear percentage scaling and direction sign before sending.
5. Implement command refresh heartbeat for active non-neutral state.
6. Implement live channel status and websocket monitor status bar.
7. Keep startup gear non-persistent (D1 on page load).
8. Keep firmware interface unchanged: send only steering/throttle integers.
