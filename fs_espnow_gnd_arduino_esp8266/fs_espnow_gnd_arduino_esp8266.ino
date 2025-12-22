#include <ESP8266WiFi.h>
extern "C" {
#include <espnow.h>
}
#include <Wire.h>
#include <EEPROM.h>
#include <Adafruit_GFX.h>
#include <Adafruit_SSD1306.h>
#include <cstring>
#include <cctype>
#include <cstdio>
#include <strings.h>

// ---------------- Serial <-> ESP-NOW Ground Bridge ----------------
// Modular bridge that accepts Zigbee-style commands (SxxxTyyy) from
// USB serial, relays them to the currently-selected ESP-NOW peer, and
// manages a configurable MAC list with persistence + OLED feedback.

constexpr uint32_t SERIAL_BAUD = 115200;
constexpr size_t SERIAL_BUFFER_SIZE = 512;

constexpr int STEERING_MIN = 5;
constexpr int STEERING_MAX = 175;
constexpr int THROTTLE_MIN = 15;
constexpr int THROTTLE_MAX = 160;

constexpr uint16_t CONTROL_MAGIC = 0xF5A5;

constexpr uint8_t MAX_INDEX = 255;
constexpr uint16_t EEPROM_MAGIC = 0xCAFE;
constexpr size_t EEPROM_BYTES = 8; // magic (2) + start (1) + end (1) + active (1) + padding

constexpr uint8_t BUTTON_PIN = D5;
constexpr uint8_t I2C_SDA_PIN = D2;
constexpr uint8_t I2C_SCL_PIN = D1;

constexpr uint8_t OLED_WIDTH = 128;
constexpr uint8_t OLED_HEIGHT = 32;
constexpr uint8_t OLED_ADDR = 0x3C;

struct ControlPacket {
  uint16_t magic;
  uint8_t steering;
  uint8_t throttle;
  uint8_t flags;
};

struct MacAddress {
  uint8_t bytes[6];

  void toString(char* dest, size_t len) const {
    if (len < 18) {
      if (len > 0) dest[0] = '\0';
      return;
    }
    snprintf(dest, len, "%02X:%02X:%02X:%02X:%02X:%02X",
             bytes[0], bytes[1], bytes[2], bytes[3], bytes[4], bytes[5]);
  }
};

struct EepromLayout {
  uint16_t magic;
  uint8_t startIndex;
  uint8_t endIndex;
  uint8_t activeIndex;
  uint8_t reserved;
};

class MacRegistry {
public:
  void begin() {
    EEPROM.begin(EEPROM_BYTES);
    load();
  }

  bool hasEntries() const { return startIndex <= endIndex; }
  uint8_t getCount() const { return hasEntries() ? (endIndex - startIndex + 1) : 0; }
  uint8_t getActiveIndex() const { return activeIndex; }

  const MacAddress* getActive() {
    if (!hasEntries() || activeIndex < startIndex || activeIndex > endIndex) {
      return nullptr;
    }
    computeMac(activeIndex, activeMac);
    return &activeMac;
  }

  bool setRange(uint8_t start, uint8_t end) {
    if (start > end || end > MAX_INDEX) return false;
    startIndex = start;
    endIndex = end;
    activeIndex = startIndex;
    persist();
    return true;
  }

  bool setActive(uint8_t index) {
    if (!hasEntries() || index < startIndex || index > endIndex) return false;
    activeIndex = index;
    persist();
    return true;
  }

  bool advanceActive() {
    if (!hasEntries()) return false;
    if (activeIndex >= endIndex) {
      activeIndex = startIndex;
    } else {
      activeIndex++;
    }
    persist();
    return true;
  }

  void getMacString(uint8_t index, char* dest, size_t len) {
    if (!hasEntries() || index < startIndex || index > endIndex || dest == nullptr) {
      if (dest && len) dest[0] = '\0';
      return;
    }
    MacAddress tmp;
    computeMac(index, tmp);
    tmp.toString(dest, len);
  }

  uint8_t getStart() const { return startIndex; }
  uint8_t getEnd() const { return endIndex; }

private:
  uint8_t startIndex = 0;
  uint8_t endIndex = 0;
  uint8_t activeIndex = 0;
  MacAddress activeMac{};

  static void computeMac(uint8_t idx, MacAddress& out) {
    out.bytes[0] = 0x66;
    out.bytes[1] = 0x33;
    out.bytes[2] = 0x9F;
    out.bytes[3] = 0x00;
    out.bytes[4] = 0x00;
    out.bytes[5] = idx;
  }

  void load() {
    EepromLayout layout;
    EEPROM.get(0, layout);
    if (layout.magic != EEPROM_MAGIC || layout.startIndex > layout.endIndex) {
      startIndex = 0;
      endIndex = 0;
      activeIndex = 0;
      return;
    }
    startIndex = layout.startIndex;
    endIndex = layout.endIndex;
    activeIndex = (layout.activeIndex >= startIndex && layout.activeIndex <= endIndex) ? layout.activeIndex : startIndex;
  }

  void persist() {
    EepromLayout layout;
    layout.magic = EEPROM_MAGIC;
    layout.startIndex = startIndex;
    layout.endIndex = endIndex;
    layout.activeIndex = activeIndex;
    layout.reserved = 0;
    EEPROM.put(0, layout);
    EEPROM.commit();
  }
};

class DisplayManager {
public:
  void begin() {
    Wire.begin(I2C_SDA_PIN, I2C_SCL_PIN);
    if (!display.begin(SSD1306_SWITCHCAPVCC, OLED_ADDR)) {
      ready = false;
      Serial.println(F("WARN: SSD1306 init failed"));
      return;
    }
    ready = true;
    display.clearDisplay();
    display.setTextSize(1);
    display.setTextColor(SSD1306_WHITE);
    display.setCursor(0, 0);
    display.println(F("ESP-NOW Ground"));
    display.display();
  }

  void showActive(uint8_t index, uint8_t total, const MacAddress* mac) {
    if (!ready) return;
    display.clearDisplay();
    display.setTextSize(1);
    display.setCursor(0, 0);
    display.println(F("Active MAC"));
    display.setCursor(0, 10);
    if (mac != nullptr && total > 0) {
      char macStr[18];
      mac->toString(macStr, sizeof(macStr));
      display.println(macStr);
      display.setCursor(0, 22);
      display.print(F("Index: "));
      display.print(index + 1);
      display.print(F("/"));
      display.print(total);
    } else {
      display.println(F("(none)"));
    }
    display.display();
  }

private:
  Adafruit_SSD1306 display = Adafruit_SSD1306(OLED_WIDTH, OLED_HEIGHT, &Wire, -1);
  bool ready = false;
};

class EspNowBridge {
public:
  bool begin() {
    WiFi.mode(WIFI_STA);
    WiFi.disconnect();

    if (esp_now_init() != 0) {
      Serial.println(F("ERR: ESP-NOW init failed"));
      return false;
    }

    esp_now_set_self_role(ESP_NOW_ROLE_CONTROLLER);
    esp_now_register_send_cb(onEspNowSendStatic);
    esp_now_register_recv_cb(onEspNowReceiveStatic);
    instance = this;
    return true;
  }

  bool ensurePeer(const MacAddress* mac) {
    if (mac == nullptr) {
      peerReady = false;
      return false;
    }

    if (peerReady && memcmp(mac->bytes, currentTarget.bytes, sizeof(currentTarget.bytes)) == 0) {
      return true;
    }

    esp_now_del_peer(const_cast<uint8_t*>(currentTarget.bytes));
    if (esp_now_add_peer(const_cast<uint8_t*>(mac->bytes), ESP_NOW_ROLE_SLAVE, 1, nullptr, 0) == 0) {
      currentTarget = *mac;
      peerReady = true;
    } else {
      peerReady = false;
    }
    return peerReady;
  }

  bool sendControl(int steering, int throttle) {
    if (!peerReady) {
      return false;
    }
    ControlPacket packet;
    packet.magic = CONTROL_MAGIC;
    packet.steering = static_cast<uint8_t>(steering);
    packet.throttle = static_cast<uint8_t>(throttle);
    packet.flags = 0;
    int status = esp_now_send(currentTarget.bytes, reinterpret_cast<uint8_t*>(&packet), sizeof(packet));
    return status == 0;
  }

  static void onSend(uint8_t status) {
    if (status != 0) {
      Serial.print(F("ERR:ESP-NOW send status "));
      Serial.println(status);
    }
  }

  static void onReceive(uint8_t* data, uint8_t len) {
    Serial.print(F("RX:"));
    for (uint8_t i = 0; i < len; ++i) {
      char c = static_cast<char>(data[i]);
      if (isprint(c)) {
        Serial.print(c);
      } else {
        Serial.print("<");
        Serial.print(data[i]);
        Serial.print(">");
      }
    }
    Serial.println();
  }

private:
  MacAddress currentTarget{};
  bool peerReady = false;

  static EspNowBridge* instance;

  static void onEspNowSendStatic(uint8_t* /*mac*/, uint8_t status) {
    if (instance != nullptr) {
      instance->onSend(status);
    }
  }

  static void onEspNowReceiveStatic(uint8_t* /*mac*/, uint8_t* data, uint8_t len) {
    onReceive(data, len);
  }
};

EspNowBridge* EspNowBridge::instance = nullptr;

class ButtonController {
public:
  explicit ButtonController(uint8_t pin) : pin(pin) {}

  void begin() {
    pinMode(pin, INPUT_PULLUP);
    lastState = digitalRead(pin);
  }

  bool wasPressed() {
    bool reading = digitalRead(pin);
    if (reading != lastState) {
      lastDebounce = millis();
      lastState = reading;
    }
    if ((millis() - lastDebounce) > debounceMs) {
      if (!reading && !pressedFlag) {
        pressedFlag = true;
        return true;
      }
      if (reading) {
        pressedFlag = false;
      }
    }
    return false;
  }

private:
  uint8_t pin;
  bool lastState = HIGH;
  bool pressedFlag = false;
  uint32_t lastDebounce = 0;
  const uint32_t debounceMs = 50;
};

// Forward declarations for cross-module calls
void notifyActiveMac();
void handleControlCommand(int steering, int throttle);
extern MacRegistry macRegistry;
extern EspNowBridge espNow;

class SerialCommandRouter {
public:
  void begin() {
    Serial.begin(SERIAL_BAUD);
    Serial.println();
    Serial.println(F("ESP-NOW Ground Station"));
    flush();
  }

  void poll() {
    while (Serial.available() > 0) {
      char incoming = Serial.read();
      if (incoming == '\n' || incoming == '\r') {
        if (index > 0) {
          buffer[index] = '\0';
          processLine(buffer);
        }
        flush();
      } else if (index < SERIAL_BUFFER_SIZE - 1) {
        buffer[index++] = incoming;
      } else {
        Serial.println(F("ERR:Command too long"));
        flush();
      }
    }
  }

private:
  char buffer[SERIAL_BUFFER_SIZE];
  size_t index = 0;

  static bool parseControl(const char* line, int& steering, int& throttle) {
    if (line == nullptr || line[0] != 'S') return false;
    int sIndex = -1;
    int tIndex = -1;
    size_t len = strlen(line);
    for (size_t i = 0; i < len; ++i) {
      if (line[i] == 'S') sIndex = static_cast<int>(i);
      if (line[i] == 'T') tIndex = static_cast<int>(i);
    }
    if (sIndex < 0 || tIndex < 0 || tIndex - sIndex != 4) {
      return false;
    }
    char sBuf[4];
    char tBuf[4];
    strncpy(sBuf, line + sIndex + 1, 3);
    strncpy(tBuf, line + tIndex + 1, 3);
    sBuf[3] = '\0';
    tBuf[3] = '\0';
    steering = constrain(atoi(sBuf), STEERING_MIN, STEERING_MAX);
    throttle = constrain(atoi(tBuf), THROTTLE_MIN, THROTTLE_MAX);
    return true;
  }

  static void processMacList(const char* payload) {
    if (payload == nullptr) {
      Serial.println(F("ERR:MACLIST missing payload"));
      return;
    }
    char scratch[SERIAL_BUFFER_SIZE];
    strncpy(scratch, payload, sizeof(scratch));
    scratch[sizeof(scratch) - 1] = '\0';
    char* token = strtok(scratch, ",; \t");
    if (token == nullptr) { Serial.println(F("ERR:MACLIST needs start,end")); return; }
    int start = atoi(token);
    token = strtok(nullptr, ",; \t");
    if (token == nullptr) { Serial.println(F("ERR:MACLIST needs end index")); return; }
    int end = atoi(token);

    if (start < 0 || start > MAX_INDEX || end < 0 || end > MAX_INDEX || start > end) {
      Serial.println(F("ERR:MACLIST index range invalid"));
      return;
    }

    if (macRegistry.setRange((uint8_t)start, (uint8_t)end)) {
      espNow.ensurePeer(macRegistry.getActive());
      notifyActiveMac();
      Serial.print(F("MACRANGE-ACK "));
      Serial.print(start);
      Serial.print('-');
      Serial.println(end);
    }
  }

  static void processMacSelect(const char* payload) {
    if (payload == nullptr) return;
    int idx = atoi(payload);
    if (idx < 0) return;
    if (macRegistry.setActive(static_cast<uint8_t>(idx))) {
      espNow.ensurePeer(macRegistry.getActive());
      notifyActiveMac();
    } else {
      Serial.println(F("ERR:MACSELECT out of range"));
    }
  }

  static void processLine(const char* line) {
    if (line == nullptr || line[0] == '\0') return;
    if (strncasecmp(line, "MACLIST", 7) == 0) {
      const char* payload = line + 7;
      while (*payload == ' ' || *payload == ':' || *payload == '=') ++payload;
      processMacList(payload);
      return;
    }
    if (strncasecmp(line, "MACSELECT", 9) == 0) {
      const char* payload = line + 9;
      while (*payload == ' ' || *payload == ':' || *payload == '=') ++payload;
      processMacSelect(payload);
      return;
    }
    if (strcasecmp(line, "MACACTIVE?") == 0) {
      notifyActiveMac();
      return;
    }

    int steering = 0;
    int throttle = 0;
    if (parseControl(line, steering, throttle)) {
      handleControlCommand(steering, throttle);
    } else {
      Serial.println(F("ERR:Unknown command"));
    }
  }

  void flush() {
    memset(buffer, 0, sizeof(buffer));
    index = 0;
  }
};

// ----------- Global singletons -----------
MacRegistry macRegistry;
DisplayManager displayManager;
EspNowBridge espNow;
ButtonController macButton(BUTTON_PIN);
SerialCommandRouter serialRouter;

// ----------- Forward declarations -----------
void notifyActiveMac();
void handleControlCommand(int steering, int throttle);
void handleButtonSelection();

void setup() {
  serialRouter.begin();
  macRegistry.begin();
  displayManager.begin();
  macButton.begin();

  if (espNow.begin()) {
    espNow.ensurePeer(macRegistry.getActive());
  }

  notifyActiveMac();
}

void loop() {
  serialRouter.poll();
  handleButtonSelection();
}

void handleButtonSelection() {
  if (macButton.wasPressed() && macRegistry.advanceActive()) {
    espNow.ensurePeer(macRegistry.getActive());
    notifyActiveMac();
  }
}

void handleControlCommand(int steering, int throttle) {
  if (!macRegistry.hasEntries()) {
    Serial.println(F("ERR:No MAC entries configured"));
    return;
  }
  if (!espNow.ensurePeer(macRegistry.getActive())) {
    Serial.println(F("ERR:Peer not ready"));
    return;
  }
  if (espNow.sendControl(steering, throttle)) {
    char ack[24];
    snprintf(ack, sizeof(ack), "OK:S%03dT%03d", steering, throttle);
    Serial.println(ack);
  } else {
    Serial.println(F("ERR:Send failed"));
  }
}

void notifyActiveMac() {
  const MacAddress* mac = macRegistry.getActive();
  char macStr[18];
  if (mac != nullptr) {
    mac->toString(macStr, sizeof(macStr));
  } else {
    strncpy(macStr, "NONE", sizeof(macStr));
    macStr[sizeof(macStr) - 1] = '\0';
  }
  Serial.print(F("MACACTIVE "));
  Serial.print(mac != nullptr ? macRegistry.getActiveIndex() : 0);
  Serial.print(' ');
  Serial.print(macStr);
  Serial.print(' ');
  Serial.println(macRegistry.getCount());
  displayManager.showActive(macRegistry.getActiveIndex(), macRegistry.getCount(), mac);
}

