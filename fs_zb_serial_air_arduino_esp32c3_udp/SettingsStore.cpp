#include "SettingsStore.h"

AppSettings SettingsStore::load() {
  AppSettings settings = AppSettings::defaults();

  Preferences prefs;
  if (!prefs.begin(SETTINGS_NAMESPACE, true)) {
    return settings;
  }

  settings.wifi.staEnabled = prefs.getBool("staEnable", settings.wifi.staEnabled);
  settings.wifi.apAlwaysOn = prefs.getBool("apAlways", settings.wifi.apAlwaysOn);
  settings.wifi.apSsid = prefs.getString("apSsid", settings.wifi.apSsid);
  settings.wifi.apPassword = prefs.getString("apPass", settings.wifi.apPassword);
  settings.wifi.staSsid = prefs.getString("staSsid", settings.wifi.staSsid);
  settings.wifi.staPassword = prefs.getString("staPass", settings.wifi.staPassword);
  settings.wifi.hostName = prefs.getString("hostName", settings.wifi.hostName);

  settings.udp.listenPort = prefs.getUShort("udpPort", settings.udp.listenPort);
  settings.udp.testHost = prefs.getString("udpHost", settings.udp.testHost);
  settings.udp.testPort = prefs.getUShort("udpTgtPort", settings.udp.testPort);

  settings.security.adminPassword = prefs.getString("adminPass", settings.security.adminPassword);
  settings.rtsp.url = prefs.getString("rtspUrl", settings.rtsp.url);

  settings.rc.steeringNeutral = prefs.getInt("steerN", settings.rc.steeringNeutral);
  settings.rc.throttleNeutral = prefs.getInt("throtN", settings.rc.throttleNeutral);
  settings.rc.steeringMin = prefs.getInt("steerMin", settings.rc.steeringMin);
  settings.rc.steeringMax = prefs.getInt("steerMax", settings.rc.steeringMax);
  settings.rc.throttleMin = prefs.getInt("throtMin", settings.rc.throttleMin);
  settings.rc.throttleMax = prefs.getInt("throtMax", settings.rc.throttleMax);
  settings.rc.steeringStep = prefs.getInt("steerStep", settings.rc.steeringStep);
  settings.rc.throttleStep = prefs.getInt("throtStep", settings.rc.throttleStep);
  settings.rc.failsafeTimeoutMs = prefs.getUInt("failsafe", settings.rc.failsafeTimeoutMs);
  settings.browser.resendIntervalMs = prefs.getUInt("webTxMs", settings.browser.resendIntervalMs);
  settings.browser.keyboardSteeringSpan = prefs.getInt("webKeySteer", settings.browser.keyboardSteeringSpan);
  settings.browser.keyboardThrottleSpan = prefs.getInt("webKeyThrot", settings.browser.keyboardThrottleSpan);
  settings.browser.joystickSteeringSpan = prefs.getInt("webJoySteer", settings.browser.joystickSteeringSpan);
  settings.browser.joystickThrottleSpan = prefs.getInt("webJoyThrot", settings.browser.joystickThrottleSpan);
  settings.browser.enableNeutralGear = prefs.getBool("webGearN", settings.browser.enableNeutralGear);
  settings.browser.enableReverseGear = prefs.getBool("webGearR", settings.browser.enableReverseGear);
  settings.browser.maxForwardGear = prefs.getUChar("webGearMax", settings.browser.maxForwardGear);
  settings.browser.drive1Percent = prefs.getUChar("webD1Pct", settings.browser.drive1Percent);
  settings.browser.drive2Percent = prefs.getUChar("webD2Pct", settings.browser.drive2Percent);
  settings.browser.drive3Percent = prefs.getUChar("webD3Pct", settings.browser.drive3Percent);
  settings.browser.drive4Percent = prefs.getUChar("webD4Pct", settings.browser.drive4Percent);
  settings.browser.drive5Percent = prefs.getUChar("webD5Pct", settings.browser.drive5Percent);
  settings.browser.drive6Percent = prefs.getUChar("webD6Pct", settings.browser.drive6Percent);
  settings.browser.reversePercent = prefs.getUChar("webRevPct", settings.browser.reversePercent);

  prefs.end();
  normalize(settings);
  return settings;
}

void SettingsStore::save(AppSettings& settings) {
  normalize(settings);

  Preferences prefs;
  if (!prefs.begin(SETTINGS_NAMESPACE, false)) {
    return;
  }

  prefs.putBool("staEnable", settings.wifi.staEnabled);
  prefs.putBool("apAlways", settings.wifi.apAlwaysOn);
  prefs.putString("apSsid", settings.wifi.apSsid);
  prefs.putString("apPass", settings.wifi.apPassword);
  prefs.putString("staSsid", settings.wifi.staSsid);
  prefs.putString("staPass", settings.wifi.staPassword);
  prefs.putString("hostName", settings.wifi.hostName);

  prefs.putUShort("udpPort", settings.udp.listenPort);
  prefs.putString("udpHost", settings.udp.testHost);
  prefs.putUShort("udpTgtPort", settings.udp.testPort);

  prefs.putString("adminPass", settings.security.adminPassword);
  prefs.putString("rtspUrl", settings.rtsp.url);

  prefs.putInt("steerN", settings.rc.steeringNeutral);
  prefs.putInt("throtN", settings.rc.throttleNeutral);
  prefs.putInt("steerMin", settings.rc.steeringMin);
  prefs.putInt("steerMax", settings.rc.steeringMax);
  prefs.putInt("throtMin", settings.rc.throttleMin);
  prefs.putInt("throtMax", settings.rc.throttleMax);
  prefs.putInt("steerStep", settings.rc.steeringStep);
  prefs.putInt("throtStep", settings.rc.throttleStep);
  prefs.putUInt("failsafe", settings.rc.failsafeTimeoutMs);
  prefs.putUInt("webTxMs", settings.browser.resendIntervalMs);
  prefs.putInt("webKeySteer", settings.browser.keyboardSteeringSpan);
  prefs.putInt("webKeyThrot", settings.browser.keyboardThrottleSpan);
  prefs.putInt("webJoySteer", settings.browser.joystickSteeringSpan);
  prefs.putInt("webJoyThrot", settings.browser.joystickThrottleSpan);
  prefs.putBool("webGearN", settings.browser.enableNeutralGear);
  prefs.putBool("webGearR", settings.browser.enableReverseGear);
  prefs.putUChar("webGearMax", settings.browser.maxForwardGear);
  prefs.putUChar("webD1Pct", settings.browser.drive1Percent);
  prefs.putUChar("webD2Pct", settings.browser.drive2Percent);
  prefs.putUChar("webD3Pct", settings.browser.drive3Percent);
  prefs.putUChar("webD4Pct", settings.browser.drive4Percent);
  prefs.putUChar("webD5Pct", settings.browser.drive5Percent);
  prefs.putUChar("webD6Pct", settings.browser.drive6Percent);
  prefs.putUChar("webRevPct", settings.browser.reversePercent);

  prefs.end();
}

AppSettings SettingsStore::resetAll() {
  AppSettings defaults = AppSettings::defaults();
  save(defaults);
  return defaults;
}

void SettingsStore::normalize(AppSettings& settings) {
  settings.wifi.apSsid.trim();
  settings.wifi.apPassword.trim();
  settings.wifi.staSsid.trim();
  settings.wifi.staPassword.trim();
  settings.wifi.hostName.trim();
  settings.udp.testHost.trim();
  settings.security.adminPassword.trim();
  settings.rtsp.url.trim();

  if (settings.wifi.apSsid.isEmpty()) {
    settings.wifi.apSsid = DEFAULT_AP_SSID;
  }
  if (!settings.wifi.apPassword.isEmpty() && settings.wifi.apPassword.length() < 8) {
    settings.wifi.apPassword = DEFAULT_AP_PASSWORD;
  }
  if (settings.wifi.hostName.isEmpty()) {
    settings.wifi.hostName = DEFAULT_HOST_NAME;
  }
  if (settings.udp.listenPort == 0) {
    settings.udp.listenPort = DEFAULT_UDP_PORT;
  }
  if (settings.udp.testHost.isEmpty()) {
    settings.udp.testHost = DEFAULT_UDP_TEST_HOST;
  }
  if (settings.udp.testPort == 0) {
    settings.udp.testPort = DEFAULT_UDP_PORT;
  }
  // Allow empty admin password
  // if (settings.security.adminPassword.isEmpty()) {
  //   settings.security.adminPassword = DEFAULT_ADMIN_PASSWORD;
  // }
  if (settings.rtsp.url.isEmpty()) {
    settings.rtsp.url = DEFAULT_RTSP_URL;
  }

  settings.rc.steeringNeutral = constrain(settings.rc.steeringNeutral, 0, 180);
  settings.rc.throttleNeutral = constrain(settings.rc.throttleNeutral, 0, 180);
  settings.rc.steeringMin = constrain(settings.rc.steeringMin, 0, 180);
  settings.rc.steeringMax = constrain(settings.rc.steeringMax, settings.rc.steeringMin, 180);
  settings.rc.throttleMin = constrain(settings.rc.throttleMin, 0, 180);
  settings.rc.throttleMax = constrain(settings.rc.throttleMax, settings.rc.throttleMin, 180);
  settings.rc.steeringStep = constrain(settings.rc.steeringStep, 1, 90);
  settings.rc.throttleStep = constrain(settings.rc.throttleStep, 1, 90);
  settings.rc.failsafeTimeoutMs = max(static_cast<uint32_t>(100), settings.rc.failsafeTimeoutMs);

  const int steeringLeftSpan = settings.rc.steeringNeutral - settings.rc.steeringMin;
  const int steeringRightSpan = settings.rc.steeringMax - settings.rc.steeringNeutral;
  const int throttleReverseSpan = settings.rc.throttleNeutral - settings.rc.throttleMin;
  const int throttleForwardSpan = settings.rc.throttleMax - settings.rc.throttleNeutral;
  const int steeringSpanLimit = max(1, min(90, max(steeringLeftSpan, steeringRightSpan)));
  const int throttleSpanLimit = max(1, min(90, max(throttleReverseSpan, throttleForwardSpan)));

  settings.browser.resendIntervalMs = constrain(settings.browser.resendIntervalMs, static_cast<uint32_t>(30), settings.rc.failsafeTimeoutMs);
  settings.browser.keyboardSteeringSpan = constrain(settings.browser.keyboardSteeringSpan, 1, steeringSpanLimit);
  settings.browser.keyboardThrottleSpan = constrain(settings.browser.keyboardThrottleSpan, 1, throttleSpanLimit);
  settings.browser.joystickSteeringSpan = constrain(settings.browser.joystickSteeringSpan, 1, steeringSpanLimit);
  settings.browser.joystickThrottleSpan = constrain(settings.browser.joystickThrottleSpan, 1, throttleSpanLimit);
  settings.browser.maxForwardGear = static_cast<uint8_t>(constrain(static_cast<int>(settings.browser.maxForwardGear), 1, 6));
  settings.browser.drive1Percent = static_cast<uint8_t>(constrain(static_cast<int>(settings.browser.drive1Percent), 0, 100));
  settings.browser.drive2Percent = static_cast<uint8_t>(constrain(static_cast<int>(settings.browser.drive2Percent), 0, 100));
  settings.browser.drive3Percent = static_cast<uint8_t>(constrain(static_cast<int>(settings.browser.drive3Percent), 0, 100));
  settings.browser.drive4Percent = static_cast<uint8_t>(constrain(static_cast<int>(settings.browser.drive4Percent), 0, 100));
  settings.browser.drive5Percent = static_cast<uint8_t>(constrain(static_cast<int>(settings.browser.drive5Percent), 0, 100));
  settings.browser.drive6Percent = static_cast<uint8_t>(constrain(static_cast<int>(settings.browser.drive6Percent), 0, 100));
  settings.browser.reversePercent = static_cast<uint8_t>(constrain(static_cast<int>(settings.browser.reversePercent), 0, 100));
}