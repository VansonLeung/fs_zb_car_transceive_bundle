#include <ESP8266WiFi.h>
#include <espnow.h>
#include <Servo.h>
#include <cstring>
#include <EEPROM.h>
extern "C" {
#include <user_interface.h>
}

// Pin definitions
constexpr uint8_t SERVO_PIN = D1;  // Steering servo
constexpr uint8_t ESC_PIN = D2;     // ESC / throttle output
constexpr uint8_t STATUS_LED_PIN = LED_BUILTIN;

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

int currentSteeringValue = DEFAULT_STEERING;
int currentThrottleValue = DEFAULT_THROTTLE;
bool failsafeEngaged = false;

void applyTargets(int desiredSteering, int desiredThrottle);
void engageFailsafe();
void setTargetsSafely(int steering, int throttle);
void onDataRecv(uint8_t* mac, uint8_t* incomingData, uint8_t len);
void processCommand(char* command);
void setMacAddress(uint8_t idx);
void applyMacIndex();
void loadMacIndex();
void saveMacIndex();

void setup() {
  Serial.begin(115200);
  Serial.println("ESP-NOW RC Controller initializing...");

  steeringServo.attach(SERVO_PIN);
  escMotor.attach(ESC_PIN);
  steeringServo.write(DEFAULT_STEERING);
  escMotor.write(DEFAULT_THROTTLE);

  pinMode(STATUS_LED_PIN, OUTPUT);
  digitalWrite(STATUS_LED_PIN, HIGH);

  EEPROM.begin(8);
  loadMacIndex();

  WiFi.mode(WIFI_STA);
  WiFi.disconnect();
  applyMacIndex();
  Serial.print("Local MAC: ");
  Serial.println(WiFi.macAddress());

  if (esp_now_init() != 0) {
    Serial.println("ESP-NOW init failed, restarting...");
    delay(1000);
    ESP.restart();
  }

  esp_now_set_self_role(ESP_NOW_ROLE_SLAVE);
  esp_now_register_recv_cb(onDataRecv);

  lastPacketMillis = millis();
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
}

void onDataRecv(uint8_t* mac, uint8_t* incomingData, uint8_t len) {
  if (len < sizeof(ControlPacket)) {
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

  // Serial.print(" S");
  // Serial.print(desiredSteering);
  // Serial.print(" T");
  // Serial.println(desiredThrottle);
}

void engageFailsafe() {
  if (!failsafeEngaged) {
    failsafeEngaged = true;
    // Serial.println("Failsafe engaged - reverting to neutral");
  }
  digitalWrite(STATUS_LED_PIN, HIGH);
  setTargetsSafely(DEFAULT_STEERING, DEFAULT_THROTTLE);
}

void processCommand(char* command) {
  // AT command: AT+INDEX=<n>
  if (strncmp(command, "AT+INDEX=", 9) == 0) {
    int idx = atoi(command + 9);
    if (idx >= 0 && idx <= 255) {
      macIndex = idx;
      saveMacIndex();
      applyMacIndex();
      Serial.print("OK:INDEX ");
      Serial.println(macIndex);
    } else {
      Serial.println("ERR:INDEX_RANGE");
    }
    return;
  }
}

void setMacAddress(uint8_t idx) {
  uint8_t mac[6] = {0x66, 0x33, 0x9F, 0x00, 0x00, idx};
  wifi_set_macaddr(STATION_IF, mac);
  char buf[24];
  snprintf(buf, sizeof(buf), "%02X:%02X:%02X:%02X:%02X:%02X", mac[0], mac[1], mac[2], mac[3], mac[4], mac[5]);
  Serial.print("MAC set to ");
  Serial.println(buf);
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
