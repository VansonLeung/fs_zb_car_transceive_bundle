#pragma once

#include <ESP32Servo.h>

#include "RcFeatureTypes.h"

class RcController {
public:
  enum class MotionTestType {
    None,
    Servo,
    Esc,
  };

  void begin(const RcControlParams& newParams);
  void updateParams(const RcControlParams& newParams);
  void acceptControl(int steering, int throttle);
  void tick();
  void servoSweep();
  void escSweep();
  MotionTestType getActiveMotionTest() const;

  int getCurrentSteering() const;
  int getCurrentThrottle() const;
  int getTargetSteering() const;
  int getTargetThrottle() const;
  uint32_t getLastControlAgeMs() const;
  bool isFailsafeEngaged() const;

private:
  Servo steeringServo;
  Servo escServo;
  RcControlParams params;
  int currentSteering = DEFAULT_STEERING;
  int currentThrottle = DEFAULT_THROTTLE;
  int targetSteering = DEFAULT_STEERING;
  int targetThrottle = DEFAULT_THROTTLE;
  uint32_t lastControlMs = 0;
  uint32_t lastApplyMs = 0;
  bool failsafeEngaged = false;

  MotionTestType activeMotionTest = MotionTestType::None;
  uint8_t motionTestPhase = 0;
  uint32_t motionTestAtMs = 0;
  int motionTestValue = DEFAULT_STEERING;

  static int constrainValue(int value, int minValue, int maxValue);
  static int applySlew(int currentValue, int desiredValue, int maxStep);
  void cancelMotionTest();
  void tickServoTest(uint32_t now);
  void tickEscTest(uint32_t now);
  bool stepValueToward(int& value, int targetValue, int step);
};