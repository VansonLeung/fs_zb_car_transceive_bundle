#pragma once

#include <WebServer.h>

#include "AppSettings.h"
#include "FsCoreTypes.h"
#include "RcController.h"
#include "SettingsStore.h"
#include "UdpControlService.h"
#include "WifiService.h"

class WebUiServer {
public:
  WebUiServer(AppSettings& settingsRef,
              SettingsStore& storeRef,
              WifiService& wifiRef,
              RcController& rcRef,
              UdpControlService& udpRef,
              const BootDiagnostics& bootRef);

  void begin();
  void tick();

private:
  WebServer server;
  AppSettings& settings;
  SettingsStore& store;
  WifiService& wifiService;
  RcController& rcController;
  UdpControlService& udpService;
  const BootDiagnostics& bootInfo;
  String flashMessage;

  bool ensureAuth();
  void redirectTo(const char* path);
  void setFlash(const String& message);
  String takeFlash();

  static String ipToString(const IPAddress& ip);
  static String jsonEscape(const String& input);
  static String htmlEscape(String input);
  String layout(const String& title, const String& body);

  void handleDashboard();
  void handleWifiPage();
  void handleWifiSave();
  void handleWifiReset();
  void handleUdpPage();
  void handleUdpSave();
  void handleUdpSend();
  void handleRcPage();
  void handleControlPost();
  void handleServoTest();
  void handleEscTest();
  void handleParamsPage();
  void handleParamsSave();
  void handleParamsReset();
  void handleRtspPage();
  void handleRtspSave();
  void handleRtspReset();
  void handleTelemetry();

  static String checkbox(const char* name, const char* label, bool checked);
  static String textInput(const char* name, const char* label, const String& value);
  static String passwordInput(const char* name, const char* label, const char* helpText);
  static String numberInput(const char* name, const char* label, uint32_t value);
  static String textareaInput(const char* name, const char* label, const String& value);
};