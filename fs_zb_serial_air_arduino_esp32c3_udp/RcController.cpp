#include "RcController.h"

namespace {
constexpr int MOTION_TEST_STEP = 5;
constexpr uint32_t MOTION_TEST_INTERVAL_MS = 20;
constexpr uint32_t ESC_TEST_NEUTRAL_DWELL_MS = 300;
constexpr int ESC_TEST_OFFSET = 20;
}

void RcController::begin(const RcControlParams& newParams) {
  params = newParams;
  steeringServo.setPeriodHertz(50);
  escServo.setPeriodHertz(50);
  steeringServo.attach(SERVO_PIN, 500, 2500);
  escServo.attach(ESC_PIN, 500, 2500);

  currentSteering = params.steeringNeutral;
  currentThrottle = params.throttleNeutral;
  targetSteering = currentSteering;
  targetThrottle = currentThrottle;
  lastControlMs = millis();

  steeringServo.write(currentSteering);
  escServo.write(currentThrottle);
}

void RcController::updateParams(const RcControlParams& newParams) {
  params = newParams;
  targetSteering = constrainValue(targetSteering, params.steeringMin, params.steeringMax);
  targetThrottle = constrainValue(targetThrottle, params.throttleMin, params.throttleMax);
  currentSteering = constrainValue(currentSteering, params.steeringMin, params.steeringMax);
  currentThrottle = constrainValue(currentThrottle, params.throttleMin, params.throttleMax);
  steeringServo.write(currentSteering);
  escServo.write(currentThrottle);
}

void RcController::acceptControl(int steering, int throttle) {
  cancelMotionTest();
  targetSteering = constrainValue(steering, params.steeringMin, params.steeringMax);
  targetThrottle = constrainValue(throttle, params.throttleMin, params.throttleMax);
  lastControlMs = millis();
  failsafeEngaged = false;
}

void RcController::tick() {
  const uint32_t now = millis();

  if (activeMotionTest != MotionTestType::None) {
    if (activeMotionTest == MotionTestType::Servo) {
      tickServoTest(now);
    } else if (activeMotionTest == MotionTestType::Esc) {
      tickEscTest(now);
    }
    return;
  }

  if ((now - lastControlMs) > params.failsafeTimeoutMs) {
    targetSteering = params.steeringNeutral;
    targetThrottle = params.throttleNeutral;
    failsafeEngaged = true;
  }

  if ((now - lastApplyMs) < 20) {
    return;
  }
  lastApplyMs = now;

  int nextSteering = applySlew(currentSteering, targetSteering, params.steeringStep);
  int nextThrottle = applySlew(currentThrottle, targetThrottle, params.throttleStep);
  if (nextSteering != currentSteering) {
    currentSteering = nextSteering;
    steeringServo.write(currentSteering);
  }
  if (nextThrottle != currentThrottle) {
    currentThrottle = nextThrottle;
    escServo.write(currentThrottle);
  }
}

void RcController::servoSweep() {
  activeMotionTest = MotionTestType::Servo;
  motionTestPhase = 0;
  motionTestAtMs = 0;
  motionTestValue = params.steeringNeutral;
  steeringServo.write(motionTestValue);
}

void RcController::escSweep() {
  activeMotionTest = MotionTestType::Esc;
  motionTestPhase = 0;
  motionTestAtMs = 0;
  motionTestValue = params.throttleNeutral;
  escServo.write(motionTestValue);
}

RcController::MotionTestType RcController::getActiveMotionTest() const {
  return activeMotionTest;
}

int RcController::getCurrentSteering() const {
  return currentSteering;
}

int RcController::getCurrentThrottle() const {
  return currentThrottle;
}

int RcController::getTargetSteering() const {
  return targetSteering;
}

int RcController::getTargetThrottle() const {
  return targetThrottle;
}

uint32_t RcController::getLastControlAgeMs() const {
  return millis() - lastControlMs;
}

bool RcController::isFailsafeEngaged() const {
  return failsafeEngaged;
}

int RcController::constrainValue(int value, int minValue, int maxValue) {
  return constrain(value, minValue, maxValue);
}

int RcController::applySlew(int currentValue, int desiredValue, int maxStep) {
  int diff = desiredValue - currentValue;
  if (abs(diff) <= maxStep) {
    return desiredValue;
  }
  return currentValue + (diff > 0 ? maxStep : -maxStep);
}

void RcController::cancelMotionTest() {
  if (activeMotionTest == MotionTestType::Servo) {
    steeringServo.write(currentSteering);
  } else if (activeMotionTest == MotionTestType::Esc) {
    escServo.write(currentThrottle);
  }
  activeMotionTest = MotionTestType::None;
}

void RcController::tickServoTest(uint32_t now) {
  if ((now - motionTestAtMs) < MOTION_TEST_INTERVAL_MS) {
    return;
  }
  motionTestAtMs = now;

  int targetValue = params.steeringNeutral;
  switch (motionTestPhase) {
    case 0:
      targetValue = params.steeringMin;
      break;
    case 1:
      targetValue = params.steeringMax;
      break;
    case 2:
      targetValue = params.steeringNeutral;
      break;
    default:
      steeringServo.write(currentSteering);
      activeMotionTest = MotionTestType::None;
      return;
  }

  if (stepValueToward(motionTestValue, targetValue, MOTION_TEST_STEP)) {
    motionTestPhase++;
    if (motionTestPhase > 2) {
      steeringServo.write(currentSteering);
      activeMotionTest = MotionTestType::None;
    }
  }

  if (activeMotionTest == MotionTestType::Servo) {
    steeringServo.write(motionTestValue);
  }
}

void RcController::tickEscTest(uint32_t now) {
  if (motionTestPhase == 1 || motionTestPhase == 3) {
    if ((now - motionTestAtMs) >= ESC_TEST_NEUTRAL_DWELL_MS) {
      motionTestPhase++;
      motionTestAtMs = now;
    }
    return;
  }

  if ((now - motionTestAtMs) < MOTION_TEST_INTERVAL_MS) {
    return;
  }
  motionTestAtMs = now;

  const int forwardTest = constrainValue(params.throttleNeutral - ESC_TEST_OFFSET, params.throttleMin, params.throttleMax);
  const int reverseTest = constrainValue(params.throttleNeutral + ESC_TEST_OFFSET, params.throttleMin, params.throttleMax);

  int targetValue = params.throttleNeutral;
  switch (motionTestPhase) {
    case 0:
      targetValue = forwardTest;
      break;
    case 2:
      targetValue = params.throttleNeutral;
      break;
    case 4:
      targetValue = reverseTest;
      break;
    case 5:
      targetValue = params.throttleNeutral;
      break;
    default:
      escServo.write(currentThrottle);
      activeMotionTest = MotionTestType::None;
      return;
  }

  if (stepValueToward(motionTestValue, targetValue, MOTION_TEST_STEP)) {
    if (motionTestPhase == 0 || motionTestPhase == 2) {
      motionTestPhase++;
    } else if (motionTestPhase == 4) {
      motionTestPhase = 5;
    } else {
      escServo.write(currentThrottle);
      activeMotionTest = MotionTestType::None;
      return;
    }
  }

  if (activeMotionTest == MotionTestType::Esc) {
    escServo.write(motionTestValue);
    if (motionTestPhase == 5 && motionTestValue == params.throttleNeutral) {
      escServo.write(currentThrottle);
      activeMotionTest = MotionTestType::None;
    }
  }
}

bool RcController::stepValueToward(int& value, int targetValue, int step) {
  value = applySlew(value, targetValue, step);
  return value == targetValue;
}