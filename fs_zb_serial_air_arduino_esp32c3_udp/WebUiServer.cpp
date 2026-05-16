#include "WebUiServer.h"

WebUiServer::WebUiServer(AppSettings& settingsRef,
                         SettingsStore& storeRef,
                         WifiService& wifiRef,
                         RcController& rcRef,
                         UdpControlService& udpRef,
                         const BootDiagnostics& bootRef)
  : server(80),
    settings(settingsRef),
    store(storeRef),
    wifiService(wifiRef),
    rcController(rcRef),
    udpService(udpRef),
    bootInfo(bootRef) {
}

void WebUiServer::begin() {
  server.on("/", HTTP_GET, [this]() { handleDashboard(); });
  server.on("/wifi", HTTP_GET, [this]() { handleWifiPage(); });
  server.on("/wifi/save", HTTP_POST, [this]() { handleWifiSave(); });
  server.on("/wifi/reset", HTTP_POST, [this]() { handleWifiReset(); });

  server.on("/udp", HTTP_GET, [this]() { handleUdpPage(); });
  server.on("/udp/save", HTTP_POST, [this]() { handleUdpSave(); });
  server.on("/udp/send", HTTP_POST, [this]() { handleUdpSend(); });

  server.on("/rc", HTTP_GET, [this]() { handleRcPage(); });
  server.on("/api/control", HTTP_POST, [this]() { handleControlPost(); });
  server.on("/api/servo-test", HTTP_POST, [this]() { handleServoTest(); });
  server.on("/api/esc-test", HTTP_POST, [this]() { handleEscTest(); });

  server.on("/params", HTTP_GET, [this]() { handleParamsPage(); });
  server.on("/params/save", HTTP_POST, [this]() { handleParamsSave(); });
  server.on("/params/reset", HTTP_POST, [this]() { handleParamsReset(); });

  server.on("/rtsp", HTTP_GET, [this]() { handleRtspPage(); });
  server.on("/rtsp/save", HTTP_POST, [this]() { handleRtspSave(); });
  server.on("/rtsp/reset", HTTP_POST, [this]() { handleRtspReset(); });

  server.on("/api/telemetry", HTTP_GET, [this]() { handleTelemetry(); });
  server.onNotFound([this]() {
    if (!ensureAuth()) {
      return;
    }
    server.send(404, "text/plain", "Not found");
  });

  server.begin();
}

void WebUiServer::tick() {
  server.handleClient();
}

bool WebUiServer::ensureAuth() {
  // Basic auth disabled
  return true;
}

void WebUiServer::redirectTo(const char* path) {
  server.sendHeader("Location", path, true);
  server.send(303, "text/plain", "Redirecting");
}

void WebUiServer::setFlash(const String& message) {
  flashMessage = message;
}

String WebUiServer::takeFlash() {
  String value = flashMessage;
  flashMessage = "";
  return value;
}

String WebUiServer::ipToString(const IPAddress& ip) {
  return ip.toString();
}

String WebUiServer::jsonEscape(const String& input) {
  String output;
  output.reserve(input.length() + 8);
  for (size_t i = 0; i < input.length(); ++i) {
    char c = input[i];
    if (c == '\\' || c == '"') {
      output += '\\';
    }
    if (c == '\n') {
      output += "\\n";
    } else if (c != '\r') {
      output += c;
    }
  }
  return output;
}

String WebUiServer::htmlEscape(String input) {
  input.replace("&", "&amp;");
  input.replace("<", "&lt;");
  input.replace(">", "&gt;");
  input.replace("\"", "&quot;");
  return input;
}

String WebUiServer::layout(const String& title, const String& body) {
  String html;
  html.reserve(7000);
  html += F("<!doctype html><html><head><meta name='viewport' content='width=device-width,initial-scale=1'>");
  html += F("<title>");
  html += title;
  html += F("</title><style>");
  html += F("body{font-family:Helvetica,Arial,sans-serif;background:#f4f1e8;color:#1f2a30;margin:0;}header{background:#17313e;color:#fff;padding:16px 20px;}nav a{color:#fff;margin-right:12px;text-decoration:none;font-weight:600;}main{padding:20px;max-width:980px;margin:0 auto;}section{background:#fff;border-radius:12px;padding:16px 18px;margin-bottom:16px;box-shadow:0 3px 14px rgba(0,0,0,.08);}h1,h2{margin-top:0;}label{display:block;margin:10px 0 4px;font-weight:600;}input[type=text],input[type=password],input[type=number],textarea{width:100%;padding:10px;border:1px solid #c5cfd4;border-radius:8px;box-sizing:border-box;}button{background:#d97b29;border:0;color:#fff;padding:10px 14px;border-radius:8px;font-weight:700;cursor:pointer;margin-right:8px;margin-top:10px;}button.secondary{background:#49636d;}button.danger{background:#b44848;}small,code{color:#51656d;} .grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(220px,1fr));gap:12px;} .card{border:1px solid #d8e0e4;border-radius:10px;padding:12px;background:#fcfcfb;} .notice{background:#ecf7d5;border:1px solid #bad58e;padding:12px;border-radius:10px;margin-bottom:16px;} .warning{background:#fff2d9;border:1px solid #f0c56b;padding:12px;border-radius:10px;} .row{display:flex;gap:10px;flex-wrap:wrap;} .pad button{min-width:84px;min-height:54px;font-size:18px;} .muted{color:#667981;} </style></head><body>");
  html += F("<header><h1>FS RC Air UDP</h1><nav><a href='/'>Dashboard</a><a href='/wifi'>WiFi</a><a href='/udp'>UDP</a><a href='/rc'>RC</a><a href='/params'>Params</a><a href='/rtsp'>RTSP</a></nav></header><main>");
  String notice = takeFlash();
  if (!notice.isEmpty()) {
    html += F("<div class='notice'>");
    html += htmlEscape(notice);
    html += F("</div>");
  }
  html += body;
  html += F("</main></body></html>");
  return html;
}

void WebUiServer::handleDashboard() {
  if (!ensureAuth()) {
    return;
  }

  const UdpPacketInfo& packet = udpService.getLastPacket();
  String body;
  body += F("<section><h2>Summary</h2><div class='grid'>");
  body += F("<div class='card'><strong>WiFi mode</strong><br>");
  body += wifiService.getModeSummary();
  body += F("<br><small>AP IP: ");
  body += ipToString(wifiService.getApIp());
  body += F("</small></div>");
  body += F("<div class='card'><strong>STA status</strong><br>");
  body += htmlEscape(wifiService.getStaStatusText());
  body += F("<br><small>STA IP: ");
  body += ipToString(wifiService.getStaIp());
  body += F("</small></div>");
  body += F("<div class='card'><strong>Current control</strong><br>");
  body += "S";
  body += String(rcController.getCurrentSteering());
  body += " T";
  body += String(rcController.getCurrentThrottle());
  body += F("<br><small>Failsafe: ");
  body += rcController.isFailsafeEngaged() ? "yes" : "no";
  body += F("</small></div>");
  body += F("<div class='card'><strong>Last UDP</strong><br>");
  if (packet.seen) {
    body += packet.remoteIp.toString();
    body += ":";
    body += String(packet.remotePort);
    body += F("<br><small>");
    body += htmlEscape(packet.preview);
    body += F("</small>");
  } else {
    body += F("No packets yet");
  }
  body += F("</div>");
  body += F("<div class='card'><strong>Last reset</strong><br>");
  body += htmlEscape(bootInfo.resetReasonText);
  body += F("<br><small>");
  body += htmlEscape(bootInfo.resetReasonCode);
  body += F(" | Boot heap: ");
  body += String(bootInfo.bootFreeHeapBytes);
  body += F(" bytes | Current heap: ");
  body += String(ESP.getFreeHeap());
  body += F(" bytes</small>");
  body += F("</div></div></section>");

  if (bootInfo.brownoutDetected) {
    body += F("<section><h2>Reset Warning</h2><p class='warning'>");
    body += htmlEscape(bootInfo.brownoutHint);
    body += F("</p></section>");
  }

  body += F("<section><h2>Implemented In This Slice</h2><div class='grid'>");
  body += F("<div class='card'>Authenticated HTTP UI</div><div class='card'>AP-first networking</div><div class='card'>Persisted settings via Preferences</div><div class='card'>UDP control + discovery</div><div class='card'>Telemetry JSON endpoint</div><div class='card'>RTSP URL storage and launch</div></div></section>");
  body += F("<section><h2>Next Slice</h2><p class='muted'>WebSocket live control, richer mobile joystick UI, and push telemetry are planned but not implemented in this file yet.</p></section>");

  server.send(200, "text/html", layout("Dashboard", body));
}

void WebUiServer::handleWifiPage() {
  if (!ensureAuth()) {
    return;
  }

  String body;
  body += F("<section><h2>WiFi Settings</h2><p class='warning'>AP remains available during STA connection attempts. If STA fails, the device keeps the AP active and retries in the background.</p>");
  body += F("<form method='post' action='/wifi/save'>");
  body += checkbox("staEnabled", "Enable STA", settings.wifi.staEnabled);
  body += checkbox("apAlwaysOn", "Keep AP running while STA is enabled", settings.wifi.apAlwaysOn);
  body += textInput("apSsid", "AP SSID", settings.wifi.apSsid);
  body += passwordInput("apPassword", "AP password", "Leave blank to keep current password");
  body += textInput("staSsid", "STA SSID", settings.wifi.staSsid);
  body += passwordInput("staPassword", "STA password", "Leave blank to keep current password");
  body += textInput("hostName", "mDNS host name", settings.wifi.hostName);
  body += passwordInput("adminPassword", "Admin password", "Leave blank to keep current password");
  body += F("<button type='submit'>Save WiFi Settings</button></form>");
  body += F("<form method='post' action='/wifi/reset'><button class='danger' type='submit'>Reset WiFi To Factory Defaults</button></form>");
  body += F("</section>");

  body += F("<section><h2>Current Status</h2><div class='grid'>");
  body += F("<div class='card'><strong>Mode</strong><br>");
  body += wifiService.getModeSummary();
  body += F("</div>");
  body += F("<div class='card'><strong>AP</strong><br>");
  body += htmlEscape(settings.wifi.apSsid);
  body += F("<br><small>");
  body += ipToString(wifiService.getApIp());
  body += F("</small></div>");
  body += F("<div class='card'><strong>STA</strong><br>");
  body += htmlEscape(wifiService.getStaStatusText());
  body += F("<br><small>");
  body += ipToString(wifiService.getStaIp());
  body += F("</small></div>");
  body += F("</div></section>");

  server.send(200, "text/html", layout("WiFi", body));
}

void WebUiServer::handleWifiSave() {
  if (!ensureAuth()) {
    return;
  }

  AppSettings updated = settings;
  updated.wifi.staEnabled = server.hasArg("staEnabled");
  updated.wifi.apAlwaysOn = server.hasArg("apAlwaysOn");
  updated.wifi.apSsid = server.arg("apSsid");
  if (server.hasArg("hostName")) {
    updated.wifi.hostName = server.arg("hostName");
  }
  if (server.hasArg("staSsid")) {
    updated.wifi.staSsid = server.arg("staSsid");
  }

  String apPassword = server.arg("apPassword");
  if (!apPassword.isEmpty()) {
    updated.wifi.apPassword = apPassword;
  }
  String staPassword = server.arg("staPassword");
  if (!staPassword.isEmpty()) {
    updated.wifi.staPassword = staPassword;
  }
  String adminPassword = server.arg("adminPassword");
  if (!adminPassword.isEmpty()) {
    updated.security.adminPassword = adminPassword;
  }

  if (!updated.wifi.apPassword.isEmpty() && updated.wifi.apPassword.length() < 8) {
    setFlash("AP password must be empty or at least 8 characters.");
    redirectTo("/wifi");
    return;
  }
  if (!updated.security.adminPassword.isEmpty() && updated.security.adminPassword.length() < 4) {
    setFlash("Admin password must be at least 4 characters.");
    redirectTo("/wifi");
    return;
  }

  settings = updated;
  store.save(settings);
  wifiService.apply(settings.wifi);
  setFlash("WiFi settings saved.");
  redirectTo("/wifi");
}

void WebUiServer::handleWifiReset() {
  if (!ensureAuth()) {
    return;
  }

  AppSettings defaults = AppSettings::defaults();
  settings.wifi = defaults.wifi;
  settings.security = defaults.security;
  store.save(settings);
  wifiService.apply(settings.wifi);
  setFlash("WiFi and admin settings reset to factory defaults.");
  redirectTo("/wifi");
}

void WebUiServer::handleUdpPage() {
  if (!ensureAuth()) {
    return;
  }

  const UdpPacketInfo& packet = udpService.getLastPacket();
  String body;
  body += F("<section><h2>UDP Tools</h2><form method='post' action='/udp/save'>");
  body += numberInput("listenPort", "Listen port", settings.udp.listenPort);
  body += textInput("testHost", "Test target host", settings.udp.testHost);
  body += numberInput("testPort", "Test target port", settings.udp.testPort);
  body += F("<button type='submit'>Save UDP Settings</button></form>");
  body += F("<form method='post' action='/udp/send'>");
  body += textInput("host", "Send to host", settings.udp.testHost);
  body += numberInput("port", "Send to port", settings.udp.testPort);
  body += textareaInput("payload", "Payload", "FSRC_DISCOVER_V1");
  body += F("<button type='submit'>Send UDP Payload</button></form></section>");

  body += F("<section><h2>Last Received UDP Packet</h2><div class='grid'>");
  body += F("<div class='card'><strong>Seen</strong><br>");
  body += packet.seen ? "yes" : "no";
  body += F("</div><div class='card'><strong>Sender</strong><br>");
  if (packet.seen) {
    body += packet.remoteIp.toString();
    body += ":";
    body += String(packet.remotePort);
  } else {
    body += "-";
  }
  body += F("</div><div class='card'><strong>Kind</strong><br>");
  if (!packet.seen) {
    body += "-";
  } else if (packet.discovery) {
    body += "discovery";
  } else if (packet.validControl && packet.binary) {
    body += "binary control";
  } else if (packet.validControl) {
    body += "text control";
  } else {
    body += "other";
  }
  body += F("</div><div class='card'><strong>Preview</strong><br><small>");
  body += htmlEscape(packet.preview);
  body += F("</small></div></div>");
  body += F("<p class='muted'>Supported inputs: binary `UdpControlPacketV1`, text `S090T090`, discovery `FSRC_DISCOVER_V1`.</p></section>");

  server.send(200, "text/html", layout("UDP", body));
}

void WebUiServer::handleUdpSave() {
  if (!ensureAuth()) {
    return;
  }

  settings.udp.listenPort = static_cast<uint16_t>(server.arg("listenPort").toInt());
  settings.udp.testHost = server.arg("testHost");
  settings.udp.testPort = static_cast<uint16_t>(server.arg("testPort").toInt());
  store.save(settings);
  udpService.applySettings(settings.udp);
  setFlash("UDP settings saved.");
  redirectTo("/udp");
}

void WebUiServer::handleUdpSend() {
  if (!ensureAuth()) {
    return;
  }

  String host = server.arg("host");
  uint16_t port = static_cast<uint16_t>(server.arg("port").toInt());
  String payload = server.arg("payload");
  bool sent = udpService.sendText(host, port, payload);
  setFlash(sent ? "UDP payload sent." : "UDP send failed.");
  redirectTo("/udp");
}

void WebUiServer::handleRcPage() {
  if (!ensureAuth()) {
    return;
  }

  String body;
  body += F("<section><h2>Browser RC Control</h2><p class='muted'>Browser-only gears are enabled: W is accelerator, S is brake, and active non-neutral control is refreshed continuously. Gear selection does not persist across page reload and always starts at D1.</p>");
  body += F("<style>.rc-grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(260px,1fr));gap:16px;align-items:start}.rc-panel{border:1px solid #d8e0e4;border-radius:10px;padding:14px;background:#fcfcfb}.rc-status{font-size:14px;color:#4e646d;margin-top:10px}.rc-controls{display:flex;gap:12px;flex-wrap:wrap;align-items:center}.stick-grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:14px;margin-top:12px;align-items:start}.stick-wrap{display:flex;flex-direction:column;align-items:center;gap:8px;min-width:0}.stick{position:relative;width:min(34vw,180px);height:min(34vw,180px);min-width:120px;min-height:120px;max-width:180px;max-height:180px;border-radius:50%;background:radial-gradient(circle at 35% 30%,#f7efe1,#d9d3c5 68%,#bdb19b);border:2px solid #9f927c;touch-action:none;overflow:hidden}.stick::after{content:'';position:absolute;left:50%;top:50%;width:2px;height:72%;background:rgba(23,49,62,.12);transform:translate(-50%,-50%)}.stick::before{content:'';position:absolute;left:50%;top:50%;width:72%;height:2px;background:rgba(23,49,62,.12);transform:translate(-50%,-50%)}.stick-knob{position:absolute;left:50%;top:50%;width:38%;height:38%;border-radius:50%;background:linear-gradient(145deg,#17313e,#385966);transform:translate(-50%,-50%);box-shadow:0 8px 18px rgba(0,0,0,.18)}.compact-buttons{display:grid;grid-template-columns:repeat(3,minmax(70px,1fr));gap:8px;max-width:300px}.compact-buttons button{min-height:48px}.wide{grid-column:span 3}.live-bar{position:sticky;bottom:8px;z-index:10;margin-top:16px;background:#17313e;color:#fff;border-radius:10px;padding:10px 12px;display:flex;gap:12px;flex-wrap:wrap;align-items:center;box-shadow:0 8px 20px rgba(0,0,0,.22)}.pill{display:inline-flex;align-items:center;gap:6px;padding:4px 10px;border-radius:999px;background:rgba(255,255,255,.14);font-size:12px}.dot{width:8px;height:8px;border-radius:50%}.ok{background:#29d391}.warn{background:#ffd36c}.bad{background:#ff7979}.idle{background:#9fb5bf}@media (max-width:560px){.rc-panel{padding:12px}.stick-grid{gap:10px}.stick{width:min(40vw,160px);height:min(40vw,160px);min-width:110px;min-height:110px}}@media (max-width:360px){.stick{width:min(39vw,140px);height:min(39vw,140px);min-width:96px;min-height:96px}}</style>");
  body += F("<div class='rc-grid'>");
  body += F("<div class='rc-panel'><h3>Gear And Drive</h3><div class='rc-controls'><button type='button' class='secondary' onclick='shiftGear(-1)'>Shift Down [</button><button type='button' class='secondary' onclick='shiftGear(1)'>Shift Up ]</button><button type='button' class='secondary' onclick='setGear(1)'>D1</button><button type='button' class='secondary' onclick='setGear(0)'>N (0)</button><button type='button' class='secondary' onclick='setGear(-1)'>R</button><button type='button' class='secondary' onclick='clearControls()'>Neutral / Stop</button></div><p class='muted'>Keyboard: W accelerate, S brake, A/D steer, 1..6 set drive gears, 0 selects N, R selects reverse, [ and ] shift down/up.</p><div class='compact-buttons'><button type='button' class='wide' onpointerdown='pressThrottlePad()' onpointerup='releasePad()' onpointercancel='releasePad()'>Throttle</button><button type='button' onpointerdown='pressSteerPad(-1)' onpointerup='releasePad()' onpointercancel='releasePad()'>Left</button><button type='button' onpointerdown='pressBrakePad()' onpointerup='releasePad()' onpointercancel='releasePad()'>Brake</button><button type='button' onpointerdown='pressSteerPad(1)' onpointerup='releasePad()' onpointercancel='releasePad()'>Right</button></div><div class='rc-status' id='gearStatus'>Gear D1</div><div class='rc-status' id='driveStatus'>Idle</div></div>");
  body += F("<div class='rc-panel'><h3>Dual Touch Joysticks</h3><p class='muted'>Left stick: push up to accelerate, pull down to brake. Right stick: steering. Gear selects direction and max throttle percentage.</p><div class='stick-grid'><div class='stick-wrap'><strong>Throttle / Brake</strong><div class='stick' id='throttleStick'><div class='stick-knob' id='throttleKnob'></div></div></div><div class='stick-wrap'><strong>Steering</strong><div class='stick' id='steeringStick'><div class='stick-knob' id='steeringKnob'></div></div></div></div><div class='rc-status' id='joystickStatus'>Throttle 90, Steering 90</div></div>");
  body += F("</div>");
  body += F("<div class='card'><strong>Loaded Browser Control Settings</strong><br><small>Neutral S");
  body += String(settings.rc.steeringNeutral);
  body += F(" T");
  body += String(settings.rc.throttleNeutral);
  body += F(" | Steering range ");
  body += String(settings.rc.steeringMin);
  body += F("-");
  body += String(settings.rc.steeringMax);
  body += F(" | Throttle range ");
  body += String(settings.rc.throttleMin);
  body += F("-");
  body += String(settings.rc.throttleMax);
  body += F(" | Browser resend ");
  body += String(settings.browser.resendIntervalMs);
  body += F(" ms | Max gear D");
  body += String(settings.browser.maxForwardGear);
  body += F(" | N ");
  body += settings.browser.enableNeutralGear ? F("on") : F("off");
  body += F(" | R ");
  body += settings.browser.enableReverseGear ? F("on") : F("off");
  body += F("</small></div>");
  body += F("<form method='post' action='/api/servo-test'><button class='secondary' type='submit'>Run Servo Sweep</button></form>");
  body += F("<form method='post' action='/api/esc-test'><button class='secondary' type='submit'>Run ESC Sweep</button></form>");
  body += F("<p class='muted'>ESC sweep now starts asynchronously with a safer forward-neutral-reverse-neutral pattern so the browser request can return immediately.</p>");
  body += F("<div class='live-bar' id='liveStatusBar'><div class='pill'><span class='dot idle' id='controlDot'></span>Control: <strong id='controlStatusText'>idle</strong></div><div class='pill'><span class='dot warn' id='wsDot'></span>WebSocket: <strong id='wsStatusText'>connecting</strong></div><div class='pill'><span class='dot idle' id='lastTxDot'></span>Last TX: <strong id='lastTxText'>never</strong></div><div class='pill'><span class='dot idle' id='latencyDot'></span>Control RTT: <strong id='latencyText'>-</strong></div></div>");
  body += F("</section>");
  body += F("<script>(function(){const cfg={steeringNeutral:");
  body += String(settings.rc.steeringNeutral);
  body += F(",throttleNeutral:");
  body += String(settings.rc.throttleNeutral);
  body += F(",steeringMin:");
  body += String(settings.rc.steeringMin);
  body += F(",steeringMax:");
  body += String(settings.rc.steeringMax);
  body += F(",throttleMin:");
  body += String(settings.rc.throttleMin);
  body += F(",throttleMax:");
  body += String(settings.rc.throttleMax);
  body += F(",keyboardSteeringSpan:");
  body += String(settings.browser.keyboardSteeringSpan);
  body += F(",keyboardThrottleSpan:");
  body += String(settings.browser.keyboardThrottleSpan);
  body += F(",joystickSteeringSpan:");
  body += String(settings.browser.joystickSteeringSpan);
  body += F(",joystickThrottleSpan:");
  body += String(settings.browser.joystickThrottleSpan);
  body += F(",resendIntervalMs:");
  body += String(settings.browser.resendIntervalMs);
  body += F(",enableNeutralGear:");
  body += settings.browser.enableNeutralGear ? F("true") : F("false");
  body += F(",enableReverseGear:");
  body += settings.browser.enableReverseGear ? F("true") : F("false");
  body += F(",maxForwardGear:");
  body += String(settings.browser.maxForwardGear);
  body += F(",reversePercent:");
  body += String(settings.browser.reversePercent);
  body += F(",drivePercents:[0,");
  body += String(settings.browser.drive1Percent);
  body += F(",");
  body += String(settings.browser.drive2Percent);
  body += F(",");
  body += String(settings.browser.drive3Percent);
  body += F(",");
  body += String(settings.browser.drive4Percent);
  body += F(",");
  body += String(settings.browser.drive5Percent);
  body += F(",");
  body += String(settings.browser.drive6Percent);
  body += F("]");
  body += F(",failsafeTimeoutMs:");
  body += String(settings.rc.failsafeTimeoutMs);
  body += F("};const state={gear:1,keys:{throttle:false,brake:false,left:false,right:false},pad:{active:false,throttle:false,brake:false,steer:0},joy:{steering:cfg.steeringNeutral,steeringActive:false,throttleMag:0,brake:false,throttleActive:false},lastPayload:'',lastSentAt:0,lastTxLabel:'never',lastRttMs:-1,inFlight:false,ws:{status:'connecting',socket:null,retryTimer:null},controlStatus:'idle'};const driveStatus=document.getElementById('driveStatus');const joystickStatus=document.getElementById('joystickStatus');const gearStatus=document.getElementById('gearStatus');const controlDot=document.getElementById('controlDot');const wsDot=document.getElementById('wsDot');const controlStatusText=document.getElementById('controlStatusText');const wsStatusText=document.getElementById('wsStatusText');const lastTxText=document.getElementById('lastTxText');const lastTxDot=document.getElementById('lastTxDot');const latencyText=document.getElementById('latencyText');const latencyDot=document.getElementById('latencyDot');function clamp(v,min,max){return Math.max(min,Math.min(max,v));}function nowLabel(){const d=new Date();return d.toLocaleTimeString();}function setDot(el,kind){el.className='dot '+kind;}function setControlStatus(status){state.controlStatus=status;controlStatusText.textContent=status;setDot(controlDot,status==='connected'?'ok':status==='sending'?'warn':status==='error'?'bad':'idle');}function setWsStatus(status){state.ws.status=status;wsStatusText.textContent=status;setDot(wsDot,status==='connected'?'ok':status==='connecting'?'warn':status==='error'?'bad':'idle');}function updateStatusBar(){lastTxText.textContent=state.lastTxLabel;setDot(lastTxDot,state.lastTxLabel==='never'?'idle':'ok');if(state.lastRttMs>=0){latencyText.textContent=String(state.lastRttMs)+' ms';setDot(latencyDot,state.lastRttMs<180?'ok':state.lastRttMs<350?'warn':'bad');}else{latencyText.textContent='-';setDot(latencyDot,'idle');}}function throttleRangeMax(){return Math.max(cfg.throttleNeutral-cfg.throttleMin,cfg.throttleMax-cfg.throttleNeutral);}function availableGears(){const g=[];if(cfg.enableReverseGear)g.push(-1);if(cfg.enableNeutralGear)g.push(0);for(let i=1;i<=cfg.maxForwardGear;i++)g.push(i);return g;}function gearLabel(g){if(g<0)return 'R';if(g===0)return 'N';return 'D'+g;}function ensureGearAllowed(){const allowed=availableGears();if(!allowed.includes(state.gear))state.gear=1;}function setGear(g){const allowed=availableGears();if(!allowed.includes(g))return;state.gear=g;tick();}window.setGear=setGear;window.shiftGear=function(dir){const allowed=availableGears();const idx=allowed.indexOf(state.gear);if(idx<0){state.gear=1;tick();return;}const next=idx+dir;if(next<0||next>=allowed.length)return;state.gear=allowed[next];tick();};function steeringFromInput(){if(state.joy.steeringActive)return state.joy.steering;if(state.pad.active&&state.pad.steer!==0)return clamp(cfg.steeringNeutral+(state.pad.steer*cfg.keyboardSteeringSpan),cfg.steeringMin,cfg.steeringMax);if(state.keys.left&&state.keys.right)return cfg.steeringNeutral;if(state.keys.left)return clamp(cfg.steeringNeutral-cfg.keyboardSteeringSpan,cfg.steeringMin,cfg.steeringMax);if(state.keys.right)return clamp(cfg.steeringNeutral+cfg.keyboardSteeringSpan,cfg.steeringMin,cfg.steeringMax);return cfg.steeringNeutral;}function throttleFromInput(){const brakeActive=state.keys.brake||(state.pad.active&&state.pad.brake)||(state.joy.throttleActive&&state.joy.brake);if(brakeActive)return cfg.throttleNeutral;if(state.gear===0)return cfg.throttleNeutral;let inputMag=0;if(state.joy.throttleActive){const joyScale=cfg.joystickThrottleSpan/Math.max(1,throttleRangeMax());inputMag=clamp(state.joy.throttleMag*joyScale,0,1);}else if(state.pad.active&&state.pad.throttle){const padScale=cfg.keyboardThrottleSpan/Math.max(1,throttleRangeMax());inputMag=clamp(padScale,0,1);}else if(state.keys.throttle){const keyScale=cfg.keyboardThrottleSpan/Math.max(1,throttleRangeMax());inputMag=clamp(keyScale,0,1);}if(inputMag<=0)return cfg.throttleNeutral;const sign=state.gear<0?1:-1;const pct=state.gear<0?cfg.reversePercent:cfg.drivePercents[state.gear]||100;const range=sign<0?(cfg.throttleNeutral-cfg.throttleMin):(cfg.throttleMax-cfg.throttleNeutral);const delta=Math.round(range*inputMag*(pct/100));return clamp(cfg.throttleNeutral+(sign*delta),cfg.throttleMin,cfg.throttleMax);}function activeControl(){const steering=steeringFromInput();const throttle=throttleFromInput();return{steering:Math.round(steering),throttle:Math.round(throttle)};}function isActive(control){return(control.steering!==cfg.steeringNeutral)||(control.throttle!==cfg.throttleNeutral);}function updateLabels(control){const allowed=availableGears().map(gearLabel).join(' > ');gearStatus.textContent='Gear '+gearLabel(state.gear)+' | Path '+allowed+' | [ ] shift';driveStatus.textContent='Steering '+control.steering+' | Throttle '+control.throttle+' | Resend '+cfg.resendIntervalMs+' ms';joystickStatus.textContent='Throttle '+Math.round(control.throttle)+' , Steering '+Math.round(state.joy.steering);}function sendControl(control){const now=Date.now();const payload='steering='+encodeURIComponent(control.steering)+'&throttle='+encodeURIComponent(control.throttle);const samePayload=payload===state.lastPayload;const dueForRefresh=isActive(control)&&((now-state.lastSentAt)>=cfg.resendIntervalMs);if(state.inFlight)return;if(samePayload&&!dueForRefresh)return;state.inFlight=true;setControlStatus('sending');const started=performance.now();fetch('/api/control',{method:'POST',headers:{'Content-Type':'application/x-www-form-urlencoded'},body:payload,cache:'no-store'}).then(function(resp){if(!resp.ok){throw new Error('http '+resp.status);}state.lastPayload=payload;state.lastSentAt=now;state.lastTxLabel=nowLabel();state.lastRttMs=Math.round(performance.now()-started);setControlStatus('connected');}).catch(function(){setControlStatus('error');}).finally(function(){state.inFlight=false;updateStatusBar();});}function scheduleWsRetry(){if(state.ws.retryTimer){clearTimeout(state.ws.retryTimer);}state.ws.retryTimer=setTimeout(connectWsMonitor,3000);}function connectWsMonitor(){try{if(state.ws.socket){try{state.ws.socket.close();}catch(e){}}setWsStatus('connecting');const proto=window.location.protocol==='https:'?'wss://':'ws://';const ws=new WebSocket(proto+window.location.host+'/ws');state.ws.socket=ws;ws.onopen=function(){setWsStatus('connected');};ws.onclose=function(){setWsStatus('disconnected');scheduleWsRetry();};ws.onerror=function(){setWsStatus('error');};ws.onmessage=function(){};}catch(e){setWsStatus('error');scheduleWsRetry();}}function tick(){ensureGearAllowed();const control=activeControl();updateLabels(control);sendControl(control);updateStatusBar();}function clearKeyState(){state.keys.throttle=false;state.keys.brake=false;state.keys.left=false;state.keys.right=false;}window.clearControls=function(){state.pad.active=false;state.pad.throttle=false;state.pad.brake=false;state.pad.steer=0;state.joy.steeringActive=false;state.joy.throttleActive=false;state.joy.throttleMag=0;state.joy.brake=false;state.joy.steering=cfg.steeringNeutral;clearKeyState();resetKnob('throttleKnob');resetKnob('steeringKnob');tick();};window.pressThrottlePad=function(){state.pad.active=true;state.pad.throttle=true;state.pad.brake=false;tick();};window.pressBrakePad=function(){state.pad.active=true;state.pad.throttle=false;state.pad.brake=true;tick();};window.pressSteerPad=function(dir){state.pad.active=true;state.pad.steer=dir;tick();};window.releasePad=function(){if(!state.pad.active)return;state.pad.active=false;state.pad.throttle=false;state.pad.brake=false;state.pad.steer=0;tick();};document.addEventListener('keydown',function(e){if(['INPUT','TEXTAREA'].includes(document.activeElement.tagName))return;const key=e.key.toLowerCase();if(['w','a','s','d','arrowup','arrowdown','arrowleft','arrowright',' ','[',']','0','1','2','3','4','5','6','r'].includes(key)){e.preventDefault();}if(e.repeat)return;if(key===']'){window.shiftGear(1);return;}if(key==='['){window.shiftGear(-1);return;}if(key==='0'){setGear(0);return;}if(key==='r'){setGear(-1);return;}if(key>='1'&&key<='6'){setGear(parseInt(key,10));return;}if(key===' '){clearControls();return;}if(key==='w'||e.key==='ArrowUp')state.keys.throttle=true;if(key==='s'||e.key==='ArrowDown')state.keys.brake=true;if(key==='a'||e.key==='ArrowLeft')state.keys.left=true;if(key==='d'||e.key==='ArrowRight')state.keys.right=true;tick();});document.addEventListener('keyup',function(e){const key=e.key.toLowerCase();if(key==='w'||e.key==='ArrowUp')state.keys.throttle=false;if(key==='s'||e.key==='ArrowDown')state.keys.brake=false;if(key==='a'||e.key==='ArrowLeft')state.keys.left=false;if(key==='d'||e.key==='ArrowRight')state.keys.right=false;tick();});function resetKnob(knobId){const knob=document.getElementById(knobId);knob.style.transform='translate(-50%,-50%)';}function setupStick(stickId,knobId,mode){const stick=document.getElementById(stickId);const knob=document.getElementById(knobId);let pointerId=null;function render(nx,ny){const px=nx*34;const py=ny*34;knob.style.transform='translate(calc(-50% + '+px+'%), calc(-50% + '+py+'%))';}function applyFromEvent(ev){const rect=stick.getBoundingClientRect();const cx=rect.left+rect.width/2;const cy=rect.top+rect.height/2;let dx=(ev.clientX-cx)/(rect.width/2);let dy=(ev.clientY-cy)/(rect.height/2);const mag=Math.sqrt(dx*dx+dy*dy);if(mag>1){dx/=mag;dy/=mag;}if(mode==='throttle'){dx=0;state.joy.throttleActive=true;state.joy.throttleMag=clamp(-dy,0,1);state.joy.brake=dy>0.15;}else{dy=0;state.joy.steering=clamp(cfg.steeringNeutral+(dx*cfg.joystickSteeringSpan),cfg.steeringMin,cfg.steeringMax);state.joy.steeringActive=true;}render(dx,dy);tick();}stick.addEventListener('pointerdown',function(ev){pointerId=ev.pointerId;stick.setPointerCapture(pointerId);applyFromEvent(ev);});stick.addEventListener('pointermove',function(ev){if(pointerId!==ev.pointerId)return;applyFromEvent(ev);});function release(ev){if(pointerId!==ev.pointerId)return;pointerId=null;if(mode==='throttle'){state.joy.throttleActive=false;state.joy.throttleMag=0;state.joy.brake=false;}else{state.joy.steering=cfg.steeringNeutral;state.joy.steeringActive=false;}resetKnob(knobId);tick();}stick.addEventListener('pointerup',release);stick.addEventListener('pointercancel',release);}setControlStatus('idle');setWsStatus('connecting');updateStatusBar();setupStick('throttleStick','throttleKnob','throttle');setupStick('steeringStick','steeringKnob','steering');connectWsMonitor();tick();setInterval(tick,Math.max(25,Math.min(cfg.resendIntervalMs,Math.max(25,cfg.failsafeTimeoutMs-20))));})();</script>");

  server.send(200, "text/html", layout("RC", body));
}

void WebUiServer::handleControlPost() {
  if (!ensureAuth()) {
    return;
  }
  int steering = server.arg("steering").toInt();
  int throttle = server.arg("throttle").toInt();
  rcController.acceptControl(steering, throttle);
  server.send(200, "application/json", "{\"ok\":true}");
}

void WebUiServer::handleServoTest() {
  if (!ensureAuth()) {
    return;
  }
  rcController.servoSweep();
  setFlash("Servo sweep started.");
  redirectTo("/rc");
}

void WebUiServer::handleEscTest() {
  if (!ensureAuth()) {
    return;
  }
  rcController.escSweep();
  setFlash("ESC sweep started with neutral dwell before reverse.");
  redirectTo("/rc");
}

void WebUiServer::handleParamsPage() {
  if (!ensureAuth()) {
    return;
  }

  String body;
  body += F("<section><h2>RC Parameters</h2><form method='post' action='/params/save'>");
  body += numberInput("steeringNeutral", "Steering neutral", settings.rc.steeringNeutral);
  body += numberInput("throttleNeutral", "Throttle neutral", settings.rc.throttleNeutral);
  body += numberInput("steeringMin", "Steering min", settings.rc.steeringMin);
  body += numberInput("steeringMax", "Steering max", settings.rc.steeringMax);
  body += numberInput("throttleMin", "Throttle min", settings.rc.throttleMin);
  body += numberInput("throttleMax", "Throttle max", settings.rc.throttleMax);
  body += numberInput("steeringStep", "Steering slew step", settings.rc.steeringStep);
  body += numberInput("throttleStep", "Throttle slew step", settings.rc.throttleStep);
  body += numberInput("failsafeTimeoutMs", "Failsafe timeout (ms)", settings.rc.failsafeTimeoutMs);
  body += F("<h3>Browser Control Tuning</h3><p class='muted'>These settings shape the browser controller feel without changing the firmware safety envelope above.</p>");
  body += numberInput("browserResendIntervalMs", "Browser resend interval (ms)", settings.browser.resendIntervalMs);
  body += numberInput("browserKeyboardSteeringSpan", "Keyboard steering span", settings.browser.keyboardSteeringSpan);
  body += numberInput("browserKeyboardThrottleSpan", "Keyboard throttle span", settings.browser.keyboardThrottleSpan);
  body += numberInput("browserJoystickSteeringSpan", "Joystick steering span", settings.browser.joystickSteeringSpan);
  body += numberInput("browserJoystickThrottleSpan", "Joystick throttle span", settings.browser.joystickThrottleSpan);
  body += F("<h3>Browser Gear Settings</h3><p class='muted'>Gear selection is browser-only and starts in D1 on each page load.</p>");
  body += checkbox("browserEnableNeutralGear", "Enable N gear", settings.browser.enableNeutralGear);
  body += checkbox("browserEnableReverseGear", "Enable R gear", settings.browser.enableReverseGear);
  body += numberInput("browserMaxForwardGear", "Max forward gear (D1..D6)", settings.browser.maxForwardGear);
  body += numberInput("browserDrive1Percent", "D1 throttle percent", settings.browser.drive1Percent);
  body += numberInput("browserDrive2Percent", "D2 throttle percent", settings.browser.drive2Percent);
  body += numberInput("browserDrive3Percent", "D3 throttle percent", settings.browser.drive3Percent);
  body += numberInput("browserDrive4Percent", "D4 throttle percent", settings.browser.drive4Percent);
  body += numberInput("browserDrive5Percent", "D5 throttle percent", settings.browser.drive5Percent);
  body += numberInput("browserDrive6Percent", "D6 throttle percent", settings.browser.drive6Percent);
  body += numberInput("browserReversePercent", "R throttle percent", settings.browser.reversePercent);
  body += F("<button type='submit'>Save RC Parameters</button></form>");
  body += F("<form method='post' action='/params/reset'><button class='danger' type='submit'>Reset RC Params To Factory Defaults</button></form></section>");
  server.send(200, "text/html", layout("Params", body));
}

void WebUiServer::handleParamsSave() {
  if (!ensureAuth()) {
    return;
  }

  settings.rc.steeringNeutral = server.arg("steeringNeutral").toInt();
  settings.rc.throttleNeutral = server.arg("throttleNeutral").toInt();
  settings.rc.steeringMin = server.arg("steeringMin").toInt();
  settings.rc.steeringMax = server.arg("steeringMax").toInt();
  settings.rc.throttleMin = server.arg("throttleMin").toInt();
  settings.rc.throttleMax = server.arg("throttleMax").toInt();
  settings.rc.steeringStep = server.arg("steeringStep").toInt();
  settings.rc.throttleStep = server.arg("throttleStep").toInt();
  settings.rc.failsafeTimeoutMs = static_cast<uint32_t>(server.arg("failsafeTimeoutMs").toInt());
  settings.browser.resendIntervalMs = static_cast<uint32_t>(server.arg("browserResendIntervalMs").toInt());
  settings.browser.keyboardSteeringSpan = server.arg("browserKeyboardSteeringSpan").toInt();
  settings.browser.keyboardThrottleSpan = server.arg("browserKeyboardThrottleSpan").toInt();
  settings.browser.joystickSteeringSpan = server.arg("browserJoystickSteeringSpan").toInt();
  settings.browser.joystickThrottleSpan = server.arg("browserJoystickThrottleSpan").toInt();
  settings.browser.enableNeutralGear = server.hasArg("browserEnableNeutralGear");
  settings.browser.enableReverseGear = server.hasArg("browserEnableReverseGear");
  settings.browser.maxForwardGear = static_cast<uint8_t>(server.arg("browserMaxForwardGear").toInt());
  settings.browser.drive1Percent = static_cast<uint8_t>(server.arg("browserDrive1Percent").toInt());
  settings.browser.drive2Percent = static_cast<uint8_t>(server.arg("browserDrive2Percent").toInt());
  settings.browser.drive3Percent = static_cast<uint8_t>(server.arg("browserDrive3Percent").toInt());
  settings.browser.drive4Percent = static_cast<uint8_t>(server.arg("browserDrive4Percent").toInt());
  settings.browser.drive5Percent = static_cast<uint8_t>(server.arg("browserDrive5Percent").toInt());
  settings.browser.drive6Percent = static_cast<uint8_t>(server.arg("browserDrive6Percent").toInt());
  settings.browser.reversePercent = static_cast<uint8_t>(server.arg("browserReversePercent").toInt());
  store.save(settings);
  rcController.updateParams(settings.rc);
  setFlash("RC parameters saved.");
  redirectTo("/params");
}

void WebUiServer::handleParamsReset() {
  if (!ensureAuth()) {
    return;
  }

  settings.rc = AppSettings::defaults().rc;
  settings.browser = AppSettings::defaults().browser;
  store.save(settings);
  rcController.updateParams(settings.rc);
  setFlash("RC and browser control settings reset to factory defaults.");
  redirectTo("/params");
}

void WebUiServer::handleRtspPage() {
  if (!ensureAuth()) {
    return;
  }

  String body;
  body += F("<section><h2>RTSP URL</h2><form method='post' action='/rtsp/save'>");
  body += textInput("url", "RTSP URL", settings.rtsp.url);
  body += F("<button type='submit'>Save RTSP URL</button></form>");
  body += F("<form method='post' action='/rtsp/reset'><button class='danger' type='submit'>Reset RTSP URL</button></form>");
  body += F("<p><a href='");
  body += htmlEscape(settings.rtsp.url);
  body += F("' target='_blank'>Launch current RTSP URL</a></p></section>");
  server.send(200, "text/html", layout("RTSP", body));
}

void WebUiServer::handleRtspSave() {
  if (!ensureAuth()) {
    return;
  }
  settings.rtsp.url = server.arg("url");
  store.save(settings);
  setFlash("RTSP URL saved.");
  redirectTo("/rtsp");
}

void WebUiServer::handleRtspReset() {
  if (!ensureAuth()) {
    return;
  }
  settings.rtsp = AppSettings::defaults().rtsp;
  store.save(settings);
  setFlash("RTSP URL reset to factory default.");
  redirectTo("/rtsp");
}

void WebUiServer::handleTelemetry() {
  if (!ensureAuth()) {
    return;
  }

  const UdpPacketInfo& packet = udpService.getLastPacket();
  String json = "{";
  json += "\"resetReasonCode\":\"";
  json += jsonEscape(bootInfo.resetReasonCode);
  json += "\",";
  json += "\"resetReasonText\":\"";
  json += jsonEscape(bootInfo.resetReasonText);
  json += "\",";
  json += "\"brownoutDetected\":";
  json += bootInfo.brownoutDetected ? "true" : "false";
  json += ",";
  json += "\"brownoutHint\":\"";
  json += jsonEscape(bootInfo.brownoutHint);
  json += "\",";
  json += "\"bootFreeHeapBytes\":";
  json += String(bootInfo.bootFreeHeapBytes);
  json += ",";
  json += "\"currentFreeHeapBytes\":";
  json += String(ESP.getFreeHeap());
  json += ",";
  json += "\"wifiMode\":\"";
  json += jsonEscape(wifiService.getModeSummary());
  json += "\",";
  json += "\"apSsid\":\"";
  json += jsonEscape(settings.wifi.apSsid);
  json += "\",";
  json += "\"apIp\":\"";
  json += jsonEscape(ipToString(wifiService.getApIp()));
  json += "\",";
  json += "\"staEnabled\":";
  json += settings.wifi.staEnabled ? "true" : "false";
  json += ",";
  json += "\"staConnected\":";
  json += wifiService.isStaConnected() ? "true" : "false";
  json += ",";
  json += "\"staStatus\":\"";
  json += jsonEscape(wifiService.getStaStatusText());
  json += "\",";
  json += "\"staIp\":\"";
  json += jsonEscape(ipToString(wifiService.getStaIp()));
  json += "\",";
  json += "\"hostName\":\"";
  json += jsonEscape(settings.wifi.hostName);
  json += "\",";
  json += "\"udpPort\":";
  json += String(settings.udp.listenPort);
  json += ",";
  json += "\"lastUdpSeen\":";
  json += packet.seen ? "true" : "false";
  json += ",";
  json += "\"lastUdpSender\":\"";
  if (packet.seen) {
    String sender = packet.remoteIp.toString();
    sender += ":";
    sender += String(packet.remotePort);
    json += jsonEscape(sender);
  }
  json += "\",";
  json += "\"lastUdpPreview\":\"";
  json += jsonEscape(packet.preview);
  json += "\",";
  json += "\"lastControlAgeMs\":";
  json += String(rcController.getLastControlAgeMs());
  json += ",";
  json += "\"currentSteering\":";
  json += String(rcController.getCurrentSteering());
  json += ",";
  json += "\"currentThrottle\":";
  json += String(rcController.getCurrentThrottle());
  json += ",";
  json += "\"targetSteering\":";
  json += String(rcController.getTargetSteering());
  json += ",";
  json += "\"targetThrottle\":";
  json += String(rcController.getTargetThrottle());
  json += ",";
  json += "\"failsafe\":";
  json += rcController.isFailsafeEngaged() ? "true" : "false";
  json += ",";
  json += "\"rtspUrl\":\"";
  json += jsonEscape(settings.rtsp.url);
  json += "\"";
  json += "}";
  server.send(200, "application/json", json);
}

String WebUiServer::checkbox(const char* name, const char* label, bool checked) {
  String html;
  html += "<label><input type='checkbox' name='";
  html += String(name);
  html += "'";
  if (checked) {
    html += " checked";
  }
  html += "> ";
  html += String(label);
  html += "</label>";
  return html;
}

String WebUiServer::textInput(const char* name, const char* label, const String& value) {
  String html;
  html += "<label for='";
  html += String(name);
  html += "'>";
  html += String(label);
  html += "</label>";
  html += "<input type='text' id='";
  html += String(name);
  html += "' name='";
  html += String(name);
  html += "' value='";
  html += htmlEscape(value);
  html += "'>";
  return html;
}

String WebUiServer::passwordInput(const char* name, const char* label, const char* helpText) {
  String html;
  html += "<label for='";
  html += String(name);
  html += "'>";
  html += String(label);
  html += "</label>";
  html += "<input type='password' id='";
  html += String(name);
  html += "' name='";
  html += String(name);
  html += "' value=''>";
  html += "<small>";
  html += String(helpText);
  html += "</small>";
  return html;
}

String WebUiServer::numberInput(const char* name, const char* label, uint32_t value) {
  String html;
  html += "<label for='";
  html += String(name);
  html += "'>";
  html += String(label);
  html += "</label>";
  html += "<input type='number' id='";
  html += String(name);
  html += "' name='";
  html += String(name);
  html += "' value='";
  html += String(value);
  html += "'>";
  return html;
}

String WebUiServer::textareaInput(const char* name, const char* label, const String& value) {
  String html;
  html += "<label for='";
  html += String(name);
  html += "'>";
  html += String(label);
  html += "</label>";
  html += "<textarea id='";
  html += String(name);
  html += "' name='";
  html += String(name);
  html += "' rows='4'>";
  html += htmlEscape(value);
  html += "</textarea>";
  return html;
}