#include <Servo.h>

// Pin definitions
#define SERVO_PIN 12   // GPIO12 for steering servo
#define ESC_PIN 13     // GPIO13 for ESC throttle

// Communication settings
#define BAUD_RATE 9600
#define BUFFER_SIZE 32

// Servo and ESC objects
Servo steeringServo;
Servo escMotor;

// Communication variables
char buffer[BUFFER_SIZE];
int bufferIndex = 0;
bool commandComplete = false;

// Default values
int steeringValue = 90;  // Center position (0-180)
int throttleValue = 90;  // Neutral position (1-180, mapped to 0-180 for servo lib)

void setup() {
  // Initialize serial communication
  Serial.begin(BAUD_RATE);
  Serial.println("RC Car Controller (ESP32) Initialized");

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

  int sIndex = -1;
  int tIndex = -1;

  // Find positions of S and T
  for (int i = 0; command[i] != '\0'; i++) {
    if (command[i] == 'S') sIndex = i;
    if (command[i] == 'T') tIndex = i;
  }

  if (sIndex >= 0 && tIndex > sIndex) {
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
    int newSteering = atoi(steeringStr);
    int newThrottle = atoi(throttleStr);

    // Validate ranges
    if (newSteering >= 0 && newSteering <= 180) {
      steeringValue = newSteering;
      steeringServo.write(steeringValue);
      // Serial.print("Steering set to: ");
      // Serial.println(steeringValue);
    }

    if (newThrottle >= 1 && newThrottle <= 180) {
      throttleValue = newThrottle;
      escMotor.write(throttleValue);
      // Serial.print("Throttle set to: ");
      // Serial.println(throttleValue);
    }

    // Send confirmation
    Serial.print("OK:S");
    Serial.print(steeringValue);
    Serial.print("T");
    Serial.println(throttleValue);

  } else {
    // Serial.println("ERROR: Invalid command format. Use SxxxTyyy");
  }
}
