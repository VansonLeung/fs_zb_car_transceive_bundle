#include "WifiService.h"

void WifiService::begin(const WifiSettings& newSettings) {
  apply(newSettings);
}

void WifiService::apply(const WifiSettings& newSettings) {
  settings = newSettings;
  connectInProgress = false;
  lastRetryMs = 0;
  lastConnectStartMs = 0;
  staStatusText = settings.staEnabled ? "idle" : "disabled";

  WiFi.disconnect(true, false);
  delay(50);

  wifi_mode_t mode = WIFI_AP;
  if (settings.staEnabled && settings.apAlwaysOn) {
    mode = WIFI_AP_STA;
  } else if (settings.staEnabled) {
    mode = WIFI_STA;
  }

  WiFi.mode(mode);

  if (!settings.staEnabled || settings.apAlwaysOn) {
    const char* password = settings.apPassword.c_str();
    if (settings.apPassword.isEmpty()) {
      WiFi.softAP(settings.apSsid.c_str());
    } else {
      WiFi.softAP(settings.apSsid.c_str(), password);
    }
  }

  if (settings.staEnabled && !settings.staSsid.isEmpty()) {
    startStationConnect();
  }

  restartMdns();
}

void WifiService::tick() {
  if (!settings.staEnabled || settings.staSsid.isEmpty()) {
    return;
  }

  wl_status_t status = WiFi.status();
  if (status == WL_CONNECTED) {
    connectInProgress = false;
    staStatusText = "connected";
    return;
  }

  const uint32_t now = millis();
  if (connectInProgress && (now - lastConnectStartMs) > 15000) {
    connectInProgress = false;
    staStatusText = "timeout, AP still available";
  }

  if (!connectInProgress && (lastRetryMs == 0 || (now - lastRetryMs) > 30000)) {
    startStationConnect();
  }
}

String WifiService::getModeSummary() const {
  if (settings.staEnabled && settings.apAlwaysOn) {
    return "AP+STA";
  }
  if (settings.staEnabled) {
    return "STA";
  }
  return "AP";
}

bool WifiService::isStaConnected() const {
  return WiFi.status() == WL_CONNECTED;
}

String WifiService::getStaStatusText() const {
  if (!settings.staEnabled) {
    return "disabled";
  }
  if (settings.staSsid.isEmpty()) {
    return "enabled, credentials missing";
  }
  return staStatusText;
}

IPAddress WifiService::getApIp() const {
  return WiFi.softAPIP();
}

IPAddress WifiService::getStaIp() const {
  return WiFi.localIP();
}

const WifiSettings& WifiService::getSettings() const {
  return settings;
}

void WifiService::startStationConnect() {
  WiFi.begin(settings.staSsid.c_str(), settings.staPassword.c_str());
  connectInProgress = true;
  lastConnectStartMs = millis();
  lastRetryMs = lastConnectStartMs;
  staStatusText = "connecting";
}

void WifiService::restartMdns() {
  if (MDNS.begin(settings.hostName.c_str())) {
    MDNS.addService("http", "tcp", 80);
  }
}