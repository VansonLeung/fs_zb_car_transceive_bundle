#include "UdpControlService.h"

void UdpControlService::begin(const UdpSettings& newSettings, ControlCallback newControlCallback, DiscoveryCallback newDiscoveryCallback) {
  settings = newSettings;
  controlCallback = newControlCallback;
  discoveryCallback = newDiscoveryCallback;
  udp.stop();
  udp.begin(settings.listenPort);
}

void UdpControlService::applySettings(const UdpSettings& newSettings) {
  begin(newSettings, controlCallback, discoveryCallback);
}

void UdpControlService::poll() {
  int packetSize = udp.parsePacket();
  if (packetSize <= 0) {
    return;
  }

  uint8_t buffer[256];
  int maxBytes = static_cast<int>(sizeof(buffer) - 1);
  int bytesToRead = packetSize < maxBytes ? packetSize : maxBytes;
  int bytesRead = udp.read(buffer, bytesToRead);
  if (bytesRead <= 0) {
    return;
  }
  buffer[bytesRead] = '\0';

  lastPacket.seen = true;
  lastPacket.remoteIp = udp.remoteIP();
  lastPacket.remotePort = udp.remotePort();
  lastPacket.receivedAtMs = millis();
  lastPacket.preview = String(reinterpret_cast<const char*>(buffer)).substring(0, 80);
  lastPacket.binary = false;
  lastPacket.discovery = false;
  lastPacket.validControl = false;
  lastPacket.sequence = 0;

  if (bytesRead == static_cast<int>(sizeof(UdpControlPacket))) {
    UdpControlPacket packet;
    memcpy(&packet, buffer, sizeof(packet));
    if (packet.magic == CONTROL_MAGIC && packet.version == CONTROL_VERSION) {
      lastPacket.binary = true;
      lastPacket.validControl = true;
      lastPacket.sequence = packet.sequence;
      if (controlCallback != nullptr) {
        controlCallback(packet.steering, packet.throttle, packet.flags, packet.sequence);
      }
      return;
    }
  }

  String payload(reinterpret_cast<const char*>(buffer));
  payload.trim();
  lastPacket.preview = payload.substring(0, 80);

  if (payload == DISCOVERY_REQUEST) {
    lastPacket.discovery = true;
    respondToDiscovery();
    return;
  }

  int steering = 0;
  int throttle = 0;
  if (parseTextControl(payload, steering, throttle)) {
    lastPacket.validControl = true;
    if (controlCallback != nullptr) {
      controlCallback(steering, throttle, 0, 0);
    }
  }
}

bool UdpControlService::sendText(const String& host, uint16_t port, const String& payload) {
  if (host.isEmpty() || port == 0) {
    return false;
  }
  if (!udp.beginPacket(host.c_str(), port)) {
    return false;
  }
  udp.write(reinterpret_cast<const uint8_t*>(payload.c_str()), payload.length());
  return udp.endPacket() == 1;
}

const UdpPacketInfo& UdpControlService::getLastPacket() const {
  return lastPacket;
}

bool UdpControlService::parseTextControl(const String& payload, int& steering, int& throttle) {
  if (payload.length() < 8 || payload[0] != 'S') {
    return false;
  }
  int tIndex = payload.indexOf('T');
  if (tIndex != 4) {
    return false;
  }

  steering = payload.substring(1, 4).toInt();
  throttle = payload.substring(5, 8).toInt();
  return true;
}

void UdpControlService::respondToDiscovery() {
  if (discoveryCallback == nullptr) {
    return;
  }

  String response = discoveryCallback();
  if (!udp.beginPacket(udp.remoteIP(), udp.remotePort())) {
    return;
  }
  udp.write(reinterpret_cast<const uint8_t*>(response.c_str()), response.length());
  udp.endPacket();
}