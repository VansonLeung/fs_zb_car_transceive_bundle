#include <ESP8266WiFi.h>
extern "C" {
#include <espnow.h>
}
#include <Servo.h>
#include <cstring>

// Pin definitions
constexpr uint8_t SERVO_PIN = D1;  // Steering servo
constexpr uint8_t ESC_PIN = D2;     // ESC / throttle output

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

void setup() {
  Serial.begin(115200);
  Serial.println("ESP-NOW RC Controller initializing...");

  steeringServo.attach(SERVO_PIN);
  escMotor.attach(ESC_PIN);
  steeringServo.write(DEFAULT_STEERING);
  escMotor.write(DEFAULT_THROTTLE);

  WiFi.mode(WIFI_STA);
  WiFi.disconnect();

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
}

void engageFailsafe() {
  failsafeEngaged = true;
  Serial.println("Failsafe engaged - reverting to neutral");
  setTargetsSafely(DEFAULT_STEERING, DEFAULT_THROTTLE);
}

void setTargetsSafely(int steering, int throttle) {
  noInterrupts();
  targetSteeringValue = steering;
  targetThrottleValue = throttle;
  targetDirty = true;
  interrupts();
}
