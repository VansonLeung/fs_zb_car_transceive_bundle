#pragma once

#include <Arduino.h>
#include <IPAddress.h>

#if !defined(ARDUINO_ARCH_ESP32)
#error "Select an ESP32-C3 board (ESP32 Arduino core)."
#endif

constexpr char SETTINGS_NAMESPACE[] = "fsrcudp";

constexpr char DEFAULT_AP_SSID[] = "FS-RC-Air-C3";
constexpr char DEFAULT_AP_PASSWORD[] = "fsrc1234";
constexpr char DEFAULT_HOST_NAME[] = "fs-rc-air";
constexpr char DEFAULT_ADMIN_PASSWORD[] = "admin1234";
constexpr char DEFAULT_UDP_TEST_HOST[] = "192.168.4.2";
constexpr uint16_t DEFAULT_UDP_PORT = 5000;

struct WifiSettings {
  bool staEnabled = false;
  bool apAlwaysOn = true;
  String apSsid = DEFAULT_AP_SSID;
  String apPassword = DEFAULT_AP_PASSWORD;
  String staSsid;
  String staPassword;
  String hostName = DEFAULT_HOST_NAME;
};

struct UdpSettings {
  uint16_t listenPort = DEFAULT_UDP_PORT;
  String testHost = DEFAULT_UDP_TEST_HOST;
  uint16_t testPort = DEFAULT_UDP_PORT;
};

struct SecuritySettings {
  String adminPassword = DEFAULT_ADMIN_PASSWORD;
};

struct BootDiagnostics {
  String resetReasonCode = "ESP_RST_UNKNOWN";
  String resetReasonText = "Unknown reset reason";
  bool brownoutDetected = false;
  String brownoutHint;
  uint32_t bootFreeHeapBytes = 0;
};

struct UdpPacketInfo {
  bool seen = false;
  bool binary = false;
  bool discovery = false;
  bool validControl = false;
  IPAddress remoteIp;
  uint16_t remotePort = 0;
  uint16_t sequence = 0;
  uint32_t receivedAtMs = 0;
  String preview;
};