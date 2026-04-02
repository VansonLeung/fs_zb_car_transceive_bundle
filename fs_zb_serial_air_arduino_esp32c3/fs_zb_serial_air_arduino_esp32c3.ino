#if !defined(ARDUINO_ARCH_ESP32)
#error "Select an ESP32-C3 board (ESP32 Arduino core)."
#endif

#include <WiFi.h>
#include <esp_now.h>
#include <esp_wifi.h>
#include <esp_mac.h>
#include <ESP32Servo.h>
#include <EEPROM.h>
#include <cstring>

#ifndef LED_BUILTIN
#define LED_BUILTIN 8
#endif

// Pin definitions
constexpr uint8_t SERVO_PIN = 0;      // Steering servo (IO0)
constexpr uint8_t ESC_PIN = 1;        // ESC / throttle output (IO1)
constexpr uint8_t STATUS_LED_PIN = 8;

// Control parameters
constexpr int DEFAULT_STEERING = 90;
constexpr int DEFAULT_THROTTLE = 90;
constexpr int STEERING_MIN = 5;
constexpr int STEERING_MAX = 175;
constexpr int THROTTLE_MIN = 15;
constexpr int THROTTLE_MAX = 160;
constexpr int STEERING_STEP = 20;
constexpr int THROTTLE_STEP = 5;
constexpr uint32_t FAILSAFE_TIMEOUT_MS = 500;  // Neutral if no packets within 0.5s

// MAC index persistence
constexpr uint16_t EEPROM_MAGIC = 0xA55A;
constexpr uint16_t EEPROM_ADDR_MAGIC = 0;
constexpr uint16_t EEPROM_ADDR_INDEX = 2;

// ESP-NOW payload signature to reject random noise
constexpr uint16_t CONTROL_MAGIC = 0xF5A5;

struct ControlPacket {
  uint16_t magic;
  uint8_t steering;  // 0-180 expected
  uint8_t throttle;  // 0-180 expected
  uint8_t flags;     // Reserved for future use
};

Servo steeringServo;
Servo escMotor;

// Serial command buffer (for AT+INDEX)
constexpr size_t CMD_BUFFER_SIZE = 64;
char cmdBuffer[CMD_BUFFER_SIZE];
size_t cmdBufferIndex = 0;
bool cmdComplete = false;

int macIndex = 0;

volatile int targetSteeringValue = DEFAULT_STEERING;
volatile int targetThrottleValue = DEFAULT_THROTTLE;
volatile bool targetDirty = false;
volatile uint32_t lastPacketMillis = 0;
volatile uint32_t lastBlinkMillis = 0;
volatile uint32_t blinkOnMillis = 0;
volatile bool blinkActive = false;

int currentSteeringValue = DEFAULT_STEERING;
int currentThrottleValue = DEFAULT_THROTTLE;
bool failsafeEngaged = false;

void applyTargets(int desiredSteering, int desiredThrottle);
void engageFailsafe();
void setTargetsSafely(int steering, int throttle);
void onDataRecv(const esp_now_recv_info* info, const uint8_t* incomingData, int len);
void processCommand(char* command);
void runServoTestSweep();
void runEscTestSweep();
void setMacAddress(uint8_t idx);
void applyMacIndex();
void loadMacIndex();
void saveMacIndex();

void setup() {
  Serial.begin(115200);
  Serial.println("ESP-NOW RC Controller (ESP32-C3) initializing...");

  steeringServo.attach(SERVO_PIN);
  escMotor.attach(ESC_PIN);
  steeringServo.write(DEFAULT_STEERING);
  escMotor.write(DEFAULT_THROTTLE);

  pinMode(STATUS_LED_PIN, OUTPUT);
  digitalWrite(STATUS_LED_PIN, HIGH);

  EEPROM.begin(8);
  loadMacIndex();
  applyMacIndex();

  wifi_init_config_t cfg = WIFI_INIT_CONFIG_DEFAULT();
  esp_wifi_init(&cfg);
  esp_wifi_start();
  esp_wifi_set_mode(WIFI_MODE_STA);

  WiFi.mode(WIFI_STA);
  Serial.print("Local MAC: ");
  Serial.println(WiFi.macAddress());

  if (esp_now_init() != ESP_OK) {
    Serial.println("ESP-NOW init failed, restarting...");
    delay(1000);
    ESP.restart();
  }

  esp_now_register_recv_cb(onDataRecv);

  lastPacketMillis = millis();
  lastBlinkMillis = lastPacketMillis;
  Serial.println("Ready to receive ESP-NOW packets");
}

void loop() {
  // Non-blocking serial command handling for AT+INDEX=n
  while (Serial.available() > 0) {
    char incoming = Serial.read();
    if (incoming == '\n' || incoming == '\r') {
      if (cmdBufferIndex > 0) {
        cmdBuffer[cmdBufferIndex] = '\0';
        cmdComplete = true;
      }
    } else if (cmdBufferIndex < CMD_BUFFER_SIZE - 1) {
      cmdBuffer[cmdBufferIndex++] = incoming;
    }
  }

  if (cmdComplete) {
    processCommand(cmdBuffer);
    cmdBufferIndex = 0;
    cmdComplete = false;
  }

  bool localDirty = false;
  int desiredSteering = currentSteeringValue;
  int desiredThrottle = currentThrottleValue;
  uint32_t lastRx = 0;

  noInterrupts();
  localDirty = targetDirty;
  if (localDirty) {
    desiredSteering = targetSteeringValue;
    desiredThrottle = targetThrottleValue;
    targetDirty = false;
    failsafeEngaged = false;
  }
  lastRx = lastPacketMillis;
  interrupts();

  if (localDirty) {
    applyTargets(desiredSteering, desiredThrottle);
  }

  if (!failsafeEngaged && (millis() - lastRx) > FAILSAFE_TIMEOUT_MS) {
    engageFailsafe();
  }

  // Blink status LED briefly every 1.5s when no ESP-NOW data is received.
  uint32_t now = millis();
  bool noRecentPackets = (now - lastRx) > FAILSAFE_TIMEOUT_MS;
  if (noRecentPackets) {
    if (!blinkActive && (now - lastBlinkMillis) >= 1000) {
      digitalWrite(STATUS_LED_PIN, LOW);
      blinkActive = true;
      blinkOnMillis = now;
    }
    if (blinkActive && (now - blinkOnMillis) >= 100) {
      digitalWrite(STATUS_LED_PIN, HIGH);
      blinkActive = false;
      lastBlinkMillis = now;
    }
  } else {
    blinkActive = false;
    lastBlinkMillis = now;
  }
}

void onDataRecv(const esp_now_recv_info* info, const uint8_t* incomingData, int len) {
  (void)info;  // Unused metadata from esp_now_recv_info
  if (len < static_cast<int>(sizeof(ControlPacket))) {
    return;
  }

  ControlPacket packet;
  memcpy(&packet, incomingData, sizeof(ControlPacket));

  if (packet.magic != CONTROL_MAGIC) {
    return;
  }

  int steering = constrain(packet.steering, STEERING_MIN, STEERING_MAX);
  int throttle = constrain(packet.throttle, THROTTLE_MIN, THROTTLE_MAX);

  noInterrupts();
  targetSteeringValue = steering;
  targetThrottleValue = throttle;
  targetDirty = true;
  lastPacketMillis = millis();
  blinkActive = false;
  lastBlinkMillis = lastPacketMillis;
  interrupts();

  digitalWrite(STATUS_LED_PIN, LOW);
}

int applySlew(int currentValue, int desiredValue, int maxStep) {
  int diff = desiredValue - currentValue;
  if (abs(diff) <= maxStep) {
    return desiredValue;
  }
  return currentValue + (diff > 0 ? maxStep : -maxStep);
}

void applyTargets(int desiredSteering, int desiredThrottle) {
  desiredSteering = constrain(desiredSteering, STEERING_MIN, STEERING_MAX);
  desiredThrottle = constrain(desiredThrottle, THROTTLE_MIN, THROTTLE_MAX);

  currentSteeringValue = applySlew(currentSteeringValue, desiredSteering, STEERING_STEP);
  currentThrottleValue = applySlew(currentThrottleValue, desiredThrottle, THROTTLE_STEP);

  steeringServo.write(currentSteeringValue);
  escMotor.write(currentThrottleValue);

  // Serial debug available if needed
  // Serial.printf("S%d T%d\n", desiredSteering, desiredThrottle);
}

void engageFailsafe() {
  if (!failsafeEngaged) {
    failsafeEngaged = true;
  }
  setTargetsSafely(DEFAULT_STEERING, DEFAULT_THROTTLE);
}

void processCommand(char* command) {
  // AT command: AT+INDEX=<n>
  if (strncmp(command, "AT+INDEX=", 9) == 0) {
    int idx = atoi(command + 9);
    if (idx >= 0 && idx <= 255) {
      macIndex = idx;
      saveMacIndex();
      // applyMacIndex();
      Serial.print("OK:INDEX ");
      Serial.println(macIndex);
    } else {
      Serial.println("ERR:INDEX_RANGE");
    }
    return;
  }

  if (strcmp(command, "AT+SVOTEST") == 0) {
    runServoTestSweep();
    Serial.println("OK:SVOTEST");
    return;
  }

  if (strcmp(command, "AT+ESCTEST") == 0) {
    runEscTestSweep();
    Serial.println("OK:ESCTEST");
    return;
  }
}

void runServoTestSweep() {
  const int step = 5;
  const int dwellMs = 15;

  // Sweep 90 -> 0
  for (int pos = 90; pos >= 0; pos -= step) {
    steeringServo.write(pos);
    delay(dwellMs);
  }

  // Sweep 0 -> 180
  for (int pos = 0; pos <= 180; pos += step) {
    steeringServo.write(pos);
    delay(dwellMs);
  }

  // Sweep 180 -> 90
  for (int pos = 180; pos >= 90; pos -= step) {
    steeringServo.write(pos);
    delay(dwellMs);
  }

  // Restore neutral and state tracking
  steeringServo.write(DEFAULT_STEERING);
  currentSteeringValue = DEFAULT_STEERING;
  setTargetsSafely(DEFAULT_STEERING, targetThrottleValue);
}

void runEscTestSweep() {
  const int step = 5;
  const int dwellMs = 15;

  // Sweep 90 -> 50
  for (int pos = 90; pos >= 50; pos -= step) {
    escMotor.write(pos);
    delay(dwellMs);
  }

  // Sweep 50 -> 130
  for (int pos = 50; pos <= 130; pos += step) {
    escMotor.write(pos);
    delay(dwellMs);
  }

  // Sweep 130 -> 90
  for (int pos = 130; pos >= 90; pos -= step) {
    escMotor.write(pos);
    delay(dwellMs);
  }

  // Restore neutral and state tracking
  escMotor.write(DEFAULT_THROTTLE);
  currentThrottleValue = DEFAULT_THROTTLE;
  setTargetsSafely(targetSteeringValue, DEFAULT_THROTTLE);
}

void setMacAddress(uint8_t idx) {
  uint8_t mac[6] = {0x66, 0x33, 0x9F, 0x00, 0x00, idx};
  char buf[24];
  snprintf(buf, sizeof(buf), "%02X:%02X:%02X:%02X:%02X:%02X", mac[0], mac[1], mac[2], mac[3], mac[4], mac[5]);
  esp_err_t res = esp_base_mac_addr_set(mac);
  if (res == ESP_OK) {
    Serial.print("MAC set to ");
    Serial.println(buf);
  } else {
    Serial.print("Failed to set MAC: ");
    Serial.println(res);
  }
  esp_err_t res2 = esp_wifi_set_mac(WIFI_IF_STA, mac);
  if (res2 == ESP_OK) {
    Serial.print("MAC set to ");
    Serial.println(buf);
  } else {
    Serial.print("Failed to set MAC: ");
    Serial.println(res2);
  }

  uint8_t baseMac[6];

  esp_read_mac(baseMac, ESP_MAC_WIFI_STA);
  Serial.print("Station MAC: ");
  for (int i = 0; i < 5; i++) {
    Serial.printf("%02X:", baseMac[i]);
  }
  Serial.printf("%02X\n", baseMac[5]);
}

void applyMacIndex() {
  setMacAddress(static_cast<uint8_t>(macIndex));
}

void loadMacIndex() {
  uint16_t magic = 0;
  EEPROM.get(EEPROM_ADDR_MAGIC, magic);
  if (magic == EEPROM_MAGIC) {
    uint8_t idx = 0;
    EEPROM.get(EEPROM_ADDR_INDEX, idx);
    macIndex = idx;
  }
}

void saveMacIndex() {
  EEPROM.put(EEPROM_ADDR_MAGIC, EEPROM_MAGIC);
  uint8_t idx = static_cast<uint8_t>(macIndex);
  EEPROM.put(EEPROM_ADDR_INDEX, idx);
  EEPROM.commit();
}

void setTargetsSafely(int steering, int throttle) {
  noInterrupts();
  targetSteeringValue = steering;
  targetThrottleValue = throttle;
  targetDirty = true;
  interrupts();
}
