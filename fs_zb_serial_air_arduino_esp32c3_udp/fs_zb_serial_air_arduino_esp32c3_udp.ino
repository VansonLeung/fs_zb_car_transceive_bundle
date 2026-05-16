#include "AppSettings.h"
#include "FsCoreTypes.h"
#include "RcFeatureTypes.h"
#include "RcController.h"
#include "SettingsStore.h"
#include "UdpControlService.h"
#include "WebUiServer.h"
#include "WifiService.h"

#include <esp_system.h>

SettingsStore settingsStore;
AppSettings settings;
RcController rcController;
WifiService wifiService;
UdpControlService udpService;
BootDiagnostics bootDiagnostics;
WebUiServer webUi(settings, settingsStore, wifiService, rcController, udpService, bootDiagnostics);

String resetReasonCode(esp_reset_reason_t reason) {
  switch (reason) {
    case ESP_RST_POWERON:
      return "ESP_RST_POWERON";
    case ESP_RST_EXT:
      return "ESP_RST_EXT";
    case ESP_RST_SW:
      return "ESP_RST_SW";
    case ESP_RST_PANIC:
      return "ESP_RST_PANIC";
    case ESP_RST_INT_WDT:
      return "ESP_RST_INT_WDT";
    case ESP_RST_TASK_WDT:
      return "ESP_RST_TASK_WDT";
    case ESP_RST_WDT:
      return "ESP_RST_WDT";
    case ESP_RST_DEEPSLEEP:
      return "ESP_RST_DEEPSLEEP";
    case ESP_RST_BROWNOUT:
      return "ESP_RST_BROWNOUT";
    case ESP_RST_SDIO:
      return "ESP_RST_SDIO";
    default:
      return "ESP_RST_UNKNOWN";
  }
}

String resetReasonText(esp_reset_reason_t reason) {
  switch (reason) {
    case ESP_RST_POWERON:
      return "Power-on reset";
    case ESP_RST_EXT:
      return "External reset pin";
    case ESP_RST_SW:
      return "Software restart";
    case ESP_RST_PANIC:
      return "Crash or exception panic";
    case ESP_RST_INT_WDT:
      return "Interrupt watchdog reset";
    case ESP_RST_TASK_WDT:
      return "Task watchdog reset";
    case ESP_RST_WDT:
      return "Other watchdog reset";
    case ESP_RST_DEEPSLEEP:
      return "Wake from deep sleep";
    case ESP_RST_BROWNOUT:
      return "Brownout reset from low supply voltage";
    case ESP_RST_SDIO:
      return "SDIO reset";
    default:
      return "Unknown reset reason";
  }
}

BootDiagnostics captureBootDiagnostics() {
  BootDiagnostics info;
  esp_reset_reason_t reason = esp_reset_reason();
  info.resetReasonCode = resetReasonCode(reason);
  info.resetReasonText = resetReasonText(reason);
  info.brownoutDetected = reason == ESP_RST_BROWNOUT;
  info.bootFreeHeapBytes = ESP.getFreeHeap();
  if (info.brownoutDetected) {
    info.brownoutHint = "Brownout reset detected. Check the USB cable, 5V or 3.3V rail stability, and current spikes from the servo or ESC.";
  }
  return info;
}

void logBootDiagnostics() {
  Serial.print("Reset reason: ");
  Serial.print(bootDiagnostics.resetReasonCode);
  Serial.print(" - ");
  Serial.println(bootDiagnostics.resetReasonText);
  Serial.print("Boot free heap: ");
  Serial.println(bootDiagnostics.bootFreeHeapBytes);

  if (bootDiagnostics.brownoutDetected) {
    Serial.println("Brownout hint: supply dipped during operation.");
    Serial.println("Check USB power quality and servo or ESC startup current draw.");
  }
}

void handleUdpControl(int steering, int throttle, uint8_t /*flags*/, uint16_t /*sequence*/) {
  rcController.acceptControl(steering, throttle);
}

String buildDiscoveryResponse() {
  String json = "{";
  json += "\"device\":\"fs-rc-air\",";
  json += "\"version\":1,";
  json += "\"host\":\"";
  json += settings.wifi.hostName;
  json += "\",";
  json += "\"apIp\":\"";
  json += wifiService.getApIp().toString();
  json += "\",";
  json += "\"staConnected\":";
  json += wifiService.isStaConnected() ? "true" : "false";
  json += ",";
  json += "\"staIp\":\"";
  json += wifiService.getStaIp().toString();
  json += "\",";
  json += "\"udpPort\":";
  json += String(settings.udp.listenPort);
  json += "}";
  return json;
}

void updateStatusLed() {
  static uint32_t lastBlinkMs = 0;
  static bool ledLow = false;

  if (!rcController.isFailsafeEngaged()) {
    digitalWrite(STATUS_LED_PIN, LOW);
    return;
  }

  uint32_t now = millis();
  if ((now - lastBlinkMs) >= 250) {
    ledLow = !ledLow;
    digitalWrite(STATUS_LED_PIN, ledLow ? LOW : HIGH);
    lastBlinkMs = now;
  }
}

void setup() {
  Serial.begin(115200);
  Serial.println();
  Serial.println("FS RC Air UDP booting...");
  bootDiagnostics = captureBootDiagnostics();
  logBootDiagnostics();

  pinMode(STATUS_LED_PIN, OUTPUT);
  digitalWrite(STATUS_LED_PIN, HIGH);

  settings = settingsStore.load();
  rcController.begin(settings.rc);
  wifiService.begin(settings.wifi);
  udpService.begin(settings.udp, handleUdpControl, buildDiscoveryResponse);
  webUi.begin();

  Serial.print("AP SSID: ");
  Serial.println(settings.wifi.apSsid);
  Serial.print("AP IP: ");
  Serial.println(wifiService.getApIp());
  Serial.print("UDP port: ");
  Serial.println(settings.udp.listenPort);
}

void loop() {
  wifiService.tick();
  udpService.poll();
  rcController.tick();
  updateStatusLed();
  webUi.tick();
}