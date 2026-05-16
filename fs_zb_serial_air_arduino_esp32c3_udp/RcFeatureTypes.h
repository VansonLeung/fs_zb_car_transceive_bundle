#pragma once

#include <Arduino.h>

#ifndef LED_BUILTIN
#define LED_BUILTIN 8
#endif

constexpr uint8_t SERVO_PIN = 0;
constexpr uint8_t ESC_PIN = 1;
constexpr uint8_t STATUS_LED_PIN = 8;

constexpr uint16_t CONTROL_MAGIC = 0xF5A5;
constexpr uint8_t CONTROL_VERSION = 1;
constexpr char DISCOVERY_REQUEST[] = "FSRC_DISCOVER_V1";

constexpr int DEFAULT_STEERING = 90;
constexpr int DEFAULT_THROTTLE = 90;
constexpr int DEFAULT_STEERING_MIN = 5;
constexpr int DEFAULT_STEERING_MAX = 175;
constexpr int DEFAULT_THROTTLE_MIN = 15;
constexpr int DEFAULT_THROTTLE_MAX = 160;
constexpr int DEFAULT_STEERING_STEP = 20;
constexpr int DEFAULT_THROTTLE_STEP = 5;
constexpr uint32_t DEFAULT_FAILSAFE_TIMEOUT_MS = 500;

struct RcControlParams {
  int steeringNeutral = DEFAULT_STEERING;
  int throttleNeutral = DEFAULT_THROTTLE;
  int steeringMin = DEFAULT_STEERING_MIN;
  int steeringMax = DEFAULT_STEERING_MAX;
  int throttleMin = DEFAULT_THROTTLE_MIN;
  int throttleMax = DEFAULT_THROTTLE_MAX;
  int steeringStep = DEFAULT_STEERING_STEP;
  int throttleStep = DEFAULT_THROTTLE_STEP;
  uint32_t failsafeTimeoutMs = DEFAULT_FAILSAFE_TIMEOUT_MS;
};

struct __attribute__((packed)) UdpControlPacket {
  uint16_t magic;
  uint8_t version;
  uint8_t flags;
  uint16_t sequence;
  uint16_t steering;
  uint16_t throttle;
  uint32_t clientMillis;
};