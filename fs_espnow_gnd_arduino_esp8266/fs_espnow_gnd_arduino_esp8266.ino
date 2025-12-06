#include <ESP8266WiFi.h>
extern "C" {
#include <espnow.h>
}
#include <cstring>
#include <cctype>
#include <cstdio>

// ---------------- Serial <-> ESP-NOW Ground Bridge ----------------
// Reads Zigbee-style commands (SxxxTyyy) from USB serial and relays them
// via ESP-NOW to the air unit. Replies/diagnostics are printed back to
// the host PC so the existing ground app can keep the same logic.

// Serial protocol settings
constexpr uint32_t SERIAL_BAUD = 115200;
constexpr size_t BUFFER_SIZE = 32;

// Control limits should mirror the air unit
constexpr int STEERING_MIN = 5;
constexpr int STEERING_MAX = 175;
constexpr int THROTTLE_MIN = 15;
constexpr int THROTTLE_MAX = 160;

// ESP-NOW payload signature (matches air unit)
constexpr uint16_t CONTROL_MAGIC = 0xF5A5;

struct ControlPacket {
  uint16_t magic;
  uint8_t steering;
  uint8_t throttle;
  uint8_t flags;
};

// TODO: Replace with the MAC address of the ESP-NOW air unit.
uint8_t AIR_UNIT_MAC[6] = {0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF};

// Serial command buffer
char buffer[BUFFER_SIZE];
size_t bufferIndex = 0;
bool commandReady = false;

// Last values for optional status echoes
int lastSteering = 90;
int lastThrottle = 90;

bool peerAdded = false;

void onDataSent(uint8_t* mac, uint8_t status);
void onDataRecv(uint8_t* mac, uint8_t* data, uint8_t len);
bool parseCommand(const char* command, int& steering, int& throttle);
void relayToAir(int steering, int throttle);
void flushBuffer();
bool registerPeer();

void setup() {
  Serial.begin(SERIAL_BAUD);
  Serial.println();
  Serial.println(F("ESP-NOW Ground Station v1"));

  WiFi.mode(WIFI_STA);
  WiFi.disconnect();

  if (esp_now_init() != 0) {
    Serial.println(F("ESP-NOW init failed, restarting..."));
    delay(1000);
    ESP.restart();
  }

  esp_now_set_self_role(ESP_NOW_ROLE_CONTROLLER);
  esp_now_register_send_cb(onDataSent);
  esp_now_register_recv_cb(onDataRecv);

  if (!registerPeer()) {
    Serial.println(F("Peer registration failed - update AIR_UNIT_MAC"));
  }

  Serial.println(F("Ready. Waiting for serial commands (SxxxTyyy)"));
}

void loop() {
  while (Serial.available() > 0) {
    char incoming = Serial.read();

    if (incoming == '\n' || incoming == '\r') {
      if (bufferIndex > 0) {
        buffer[bufferIndex] = '\0';
        commandReady = true;
      }
    } else if (bufferIndex < BUFFER_SIZE - 1) {
      buffer[bufferIndex++] = incoming;
    } else {
      // Overflow guard
      flushBuffer();
      Serial.println(F("ERR:Command too long"));
    }
  }

  if (commandReady) {
    int steering = 0;
    int throttle = 0;
    bool ok = parseCommand(buffer, steering, throttle);
    flushBuffer();

    if (ok) {
      relayToAir(steering, throttle);
    } else {
      Serial.println(F("ERR:Invalid format (use SxxxTyyy)"));
    }
  }
}

bool registerPeer() {
  if (peerAdded) {
    return true;
  }

  // Ensure MAC is configured
  bool macValid = false;
  for (uint8_t b : AIR_UNIT_MAC) {
    if (b != 0x00 && b != 0xFF) {
      macValid = true;
      break;
    }
  }
  if (!macValid) {
    Serial.println(F("WARN: AIR_UNIT_MAC not set. Update the MAC before use."));
    return false;
  }

  esp_now_del_peer(AIR_UNIT_MAC);
  if (esp_now_add_peer(AIR_UNIT_MAC, ESP_NOW_ROLE_SLAVE, 1, nullptr, 0) == 0) {
    peerAdded = true;
  }
  return peerAdded;
}

bool parseCommand(const char* command, int& steering, int& throttle) {
  int sIndex = -1;
  int tIndex = -1;
  size_t len = strlen(command);

  for (size_t i = 0; i < len; ++i) {
    if (command[i] == 'S') {
      sIndex = static_cast<int>(i);
    } else if (command[i] == 'T') {
      tIndex = static_cast<int>(i);
    }
  }

  if (sIndex < 0 || tIndex < 0 || tIndex - sIndex != 4) {
    return false;
  }

  char steeringStr[4];
  char throttleStr[4];
  strncpy(steeringStr, command + sIndex + 1, 3);
  strncpy(throttleStr, command + tIndex + 1, 3);
  steeringStr[3] = '\0';
  throttleStr[3] = '\0';

  steering = constrain(atoi(steeringStr), STEERING_MIN, STEERING_MAX);
  throttle = constrain(atoi(throttleStr), THROTTLE_MIN, THROTTLE_MAX);
  return true;
}

void relayToAir(int steering, int throttle) {
  if (!peerAdded && !registerPeer()) {
    Serial.println(F("ERR:No ESP-NOW peer"));
    return;
  }

  ControlPacket packet;
  packet.magic = CONTROL_MAGIC;
  packet.steering = static_cast<uint8_t>(steering);
  packet.throttle = static_cast<uint8_t>(throttle);
  packet.flags = 0;

  int status = esp_now_send(AIR_UNIT_MAC, reinterpret_cast<uint8_t*>(&packet), sizeof(packet));

  if (status == 0) {
    lastSteering = steering;
    lastThrottle = throttle;
    char ack[20];
    snprintf(ack, sizeof(ack), "OK:S%03dT%03d", lastSteering, lastThrottle);
    Serial.println(ack);
  } else {
    Serial.print(F("ERR:Send status "));
    Serial.println(status);
  }
}

void flushBuffer() {
  bufferIndex = 0;
  commandReady = false;
  buffer[0] = '\0';
}

void onDataSent(uint8_t* mac, uint8_t status) {
  if (status != 0) {
    Serial.print(F("ERR:ESP-NOW send cb status "));
    Serial.println(status);
  }
}

void onDataRecv(uint8_t* mac, uint8_t* data, uint8_t len) {
  // Forward any telemetry/raw bytes from the air unit back to the PC.
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
