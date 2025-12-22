#include <ESP8266WiFi.h>
#include <Servo.h>
#include <EEPROM.h>
extern "C" {
#include <user_interface.h>
}

// Pin definitions
#define SERVO_PIN D1    // GPIO1 (D1) for steering servo
#define ESC_PIN D2      // GPIO2 (D2) for ESC throttle

// Communication settings
#define BAUD_RATE 9600
#define BUFFER_SIZE 64

// MAC index persistence
constexpr uint16_t EEPROM_MAGIC = 0xA55A;
constexpr uint16_t EEPROM_ADDR_MAGIC = 0;
constexpr uint16_t EEPROM_ADDR_INDEX = 2;

// Servo and ESC objects
Servo steeringServo;
Servo escMotor;

// Communication variables
char buffer[BUFFER_SIZE];
int bufferIndex = 0;
bool commandComplete = false;
int macIndex = 0;

// Default values
int steeringValue = 90;  // Center position (0-180)
int throttleValue = 90;  // Neutral position (1-180, mapped to 0-180 for servo lib)

int targetSteeringValue = 90;
int targetThrottleValue = 90;

void setup() {
  // Initialize serial communication
  Serial.begin(BAUD_RATE);
  Serial.println("RC Car Controller Initialized");

  // Load MAC index and set MAC
  EEPROM.begin(8);
  loadMacIndex();
  applyMacIndex();

  // Attach servos
  steeringServo.attach(SERVO_PIN);
  escMotor.attach(ESC_PIN);

  // Set initial positions
  steeringServo.write(steeringValue);
  escMotor.write(throttleValue);

  Serial.println("Servos attached and initialized");
}

void loop() {
  // Read serial data
  while (Serial.available() > 0) {
    char incomingByte = Serial.read();

    if (incomingByte == '\n' || incomingByte == '\r') {
      if (bufferIndex > 0) {
        buffer[bufferIndex] = '\0';  // Null terminate
        commandComplete = true;
      }
    } else if (bufferIndex < BUFFER_SIZE - 1) {
      buffer[bufferIndex++] = incomingByte;
    }
  }

  // Process complete command
  if (commandComplete) {
    processCommand(buffer);
    bufferIndex = 0;
    commandComplete = false;
  }
}

void processCommand(char* command) {
  // Expected format: "S<steering>T<throttle>"
  // Example: "S090T090" for center steering and neutral throttle

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

  int sIndex = -1;
  int tIndex = -1;

  // Find positions of S and T
  for (int i = 0; command[i] != '\0'; i++) {
    if (command[i] == 'S') sIndex = i;
    if (command[i] == 'T') tIndex = i;
  }

  if (sIndex >= 0 && tIndex == sIndex + 4) {
    // Extract steering value (3 digits after S)
    char steeringStr[4];
    steeringStr[0] = command[sIndex + 1];
    steeringStr[1] = command[sIndex + 2];
    steeringStr[2] = command[sIndex + 3];
    steeringStr[3] = '\0';

    // Extract throttle value (3 digits after T)
    char throttleStr[4];
    throttleStr[0] = command[tIndex + 1];
    throttleStr[1] = command[tIndex + 2];
    throttleStr[2] = command[tIndex + 3];
    throttleStr[3] = '\0';

    // Convert to integers
    targetSteeringValue = atoi(steeringStr);
    targetThrottleValue = atoi(throttleStr);

    // Validate ranges
    if (targetSteeringValue >= 5 && targetSteeringValue <= 175) {
      // Limit max change to 20
      int diff = targetSteeringValue - steeringValue;
      if (abs(diff) > 20) {
        steeringValue += (diff > 0) ? 20 : -20;
      } else {
        steeringValue = targetSteeringValue;
      }
      steeringServo.write(steeringValue);
      // Serial.print("Steering set to: ");
      // Serial.println(steeringValue);
    }

    if (targetThrottleValue >= 15 && targetThrottleValue <= 160) {
      // Limit max change to 3
      int diff = targetThrottleValue - throttleValue;
      if (abs(diff) > 5) {
        throttleValue += (diff > 0) ? 5 : -5;
      } else {
        throttleValue = targetThrottleValue;
      }
      escMotor.write(throttleValue);
      // Serial.print("Throttle set to: ");
      // Serial.println(throttleValue);
    }

    // Send confirmation
    // Serial.print("OK:S");
    // Serial.print(steeringValue);
    // Serial.print("T");
    // Serial.println(throttleValue);

  } else {
    // Serial.println("ERROR: Invalid command format. Use SxxxTyyy");
  }
}

// ---------- MAC helpers ----------
void setMacAddress(uint8_t idx) {
  uint8_t mac[6] = {0x66, 0x33, 0x9F, 0x00, 0x00, idx};
  wifi_set_macaddr(STATION_IF, mac);
  char buf[24];
  snprintf(buf, sizeof(buf), "%02X:%02X:%02X:%02X:%02X:%02X", mac[0], mac[1], mac[2], mac[3], mac[4], mac[5]);
  Serial.print("MAC set to ");
  Serial.println(buf);
}

void applyMacIndex() {
  setMacAddress((uint8_t)macIndex);
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
  uint8_t idx = (uint8_t)macIndex;
  EEPROM.put(EEPROM_ADDR_INDEX, idx);
  EEPROM.commit();
}
