#pragma once

#include <ESPmDNS.h>
#include <WiFi.h>

#include "FsCoreTypes.h"

class WifiService {
public:
  void begin(const WifiSettings& newSettings);
  void apply(const WifiSettings& newSettings);
  void tick();

  String getModeSummary() const;
  bool isStaConnected() const;
  String getStaStatusText() const;
  IPAddress getApIp() const;
  IPAddress getStaIp() const;
  const WifiSettings& getSettings() const;

private:
  WifiSettings settings;
  bool connectInProgress = false;
  uint32_t lastConnectStartMs = 0;
  uint32_t lastRetryMs = 0;
  String staStatusText = "disabled";

  void startStationConnect();
  void restartMdns();
};