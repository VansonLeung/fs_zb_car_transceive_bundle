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
  bool enableNeutralGear = true;
  bool enableReverseGear = true;
  uint8_t maxForwardGear = 3;
  uint8_t drive1Percent = 40;
  uint8_t drive2Percent = 70;
  uint8_t drive3Percent = 100;
  uint8_t drive4Percent = 100;
  uint8_t drive5Percent = 100;
  uint8_t drive6Percent = 100;
  uint8_t reversePercent = 40;
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