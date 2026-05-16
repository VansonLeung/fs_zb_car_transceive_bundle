#pragma once

#include "FsCoreTypes.h"
#include "RcFeatureTypes.h"

constexpr char DEFAULT_RTSP_URL[] = "rtsp://192.168.4.2/live";

struct RtspSettings {
  String url = DEFAULT_RTSP_URL;
};

struct BrowserControlSettings {
  uint32_t resendIntervalMs = 90;
  int keyboardSteeringSpan = 25;
  int keyboardThrottleSpan = 25;
  int joystickSteeringSpan = 25;
  int joystickThrottleSpan = 25;
};

struct AppSettings {
  WifiSettings wifi;
  UdpSettings udp;
  SecuritySettings security;
  RtspSettings rtsp;
  RcControlParams rc;
  BrowserControlSettings browser;

  static AppSettings defaults() {
    return AppSettings();
  }
};