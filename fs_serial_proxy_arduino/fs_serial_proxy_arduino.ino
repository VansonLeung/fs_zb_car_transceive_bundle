// Serial Proxy for ESP8266
// Forwards serial data between hardware serial (USB) and software serial (pins D1/D2)
// Useful for bridging serial devices or debugging serial communication

#include <SoftwareSerial.h>

// Software serial pins (D1 = GPIO5 RX, D2 = GPIO4 TX)
#define RX_PIN 5  // D1
#define TX_PIN 4  // D2

// Baud rates
#define HW_SERIAL_BAUD 115200  // USB serial (for debugging/programming)
#define SW_SERIAL_BAUD 9600    // Device serial

SoftwareSerial swSerial(RX_PIN, TX_PIN);

void setup() {
  // Initialize hardware serial (USB)
  Serial.begin(HW_SERIAL_BAUD);
  Serial.println("Serial Proxy Started");
  Serial.print("Hardware Serial: ");
  Serial.print(HW_SERIAL_BAUD);
  Serial.println(" baud");
  Serial.print("Software Serial: ");
  Serial.print(SW_SERIAL_BAUD);
  Serial.println(" baud");

  // Initialize software serial (device)
  swSerial.begin(SW_SERIAL_BAUD);

  Serial.println("Ready to proxy serial data...");
}

void loop() {
  // Forward from hardware serial to software serial
  if (Serial.available()) {
    char c = Serial.read();
    swSerial.write(c);
    // Echo to hardware serial for debugging (optional)
    // Serial.write(c);
  }

  // Forward from software serial to hardware serial
  if (swSerial.available()) {
    char c = swSerial.read();
    Serial.write(c);
  }

  // Small delay to prevent overwhelming the serial buffers
  delay(1);
}
