#pragma once

#include <WiFiUdp.h>

#include "FsCoreTypes.h"
#include "RcFeatureTypes.h"

class UdpControlService {
public:
  typedef void (*ControlCallback)(int steering, int throttle, uint8_t flags, uint16_t sequence);
  typedef String (*DiscoveryCallback)();

  void begin(const UdpSettings& newSettings, ControlCallback newControlCallback, DiscoveryCallback newDiscoveryCallback);
  void applySettings(const UdpSettings& newSettings);
  void poll();
  bool sendText(const String& host, uint16_t port, const String& payload);
  const UdpPacketInfo& getLastPacket() const;

private:
  WiFiUDP udp;
  UdpSettings settings;
  ControlCallback controlCallback = nullptr;
  DiscoveryCallback discoveryCallback = nullptr;
  UdpPacketInfo lastPacket;

  static bool parseTextControl(const String& payload, int& steering, int& throttle);
  void respondToDiscovery();
};