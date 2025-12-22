const WS_ENDPOINTS = [
  { url: "ws://localhost:9091/events/", label: "events" },
  { url: "ws://localhost:8080/", label: "legacy" },
];
const RECONNECT_DELAY_MS = 4000;

const statusEl = document.getElementById("connectionStatus");
const speedNeedle = document.querySelector("#speedGauge .needle");
const throttleBar = document.getElementById("throttleBar");
const brakeBar = document.getElementById("brakeBar");
const speedValueEl = document.getElementById("speedValue");
const steeringValueEl = document.getElementById("steeringValue");
const rawThrottleEl = document.getElementById("rawThrottleValue");
const rawBrakeEl = document.getElementById("rawBrakeValue");
const videoEl = document.getElementById("cameraFeed");
const fullscreenBtn = document.getElementById("fullscreenToggle");

let socket;
let reconnectHandle;
let currentEndpointIndex = 0;

function setStatus(text, isOnline = false) {
  statusEl.textContent = text;
  statusEl.classList.toggle("online", isOnline);
}

function connectWebSocket() {
  clearTimeout(reconnectHandle);
  const endpoint = WS_ENDPOINTS[currentEndpointIndex];
  setStatus(`Connecting (${endpoint.label})...`);

  try {
    socket = new WebSocket(endpoint.url);
  } catch (err) {
    setStatus(`WebSocket error: ${err.message}`);
    scheduleReconnect();
    return;
  }

  socket.addEventListener("open", () => setStatus(`ENGINE: ON (${endpoint.label})`, true));

  socket.addEventListener("message", (event) => {
    try {
      const payload = JSON.parse(event.data);
      const normalized = normalizeTelemetryPayload(payload);
      if (normalized) {
        updateTelemetry(normalized);
      }
    } catch (err) {
      setStatus(`Data error: ${err.message}`);
    }
  });

  socket.addEventListener("close", () => {
    setStatus("ENGINE: OFF");
    scheduleReconnect();
  });

  socket.addEventListener("error", (err) => {
    console.error("WebSocket error", err);
    setStatus("Socket error");
    socket.close();
  });
}

function scheduleReconnect() {
  clearTimeout(reconnectHandle);
  currentEndpointIndex = (currentEndpointIndex + 1) % WS_ENDPOINTS.length;
  reconnectHandle = setTimeout(connectWebSocket, RECONNECT_DELAY_MS);
}

function clamp(value, min, max) {
  return Math.max(min, Math.min(max, value));
}

function mapRange(value, fromMin, fromMax, toMin, toMax) {
  const clamped = clamp(value, fromMin, fromMax);
  return (
    ((clamped - fromMin) * (toMax - toMin)) / (fromMax - fromMin) + toMin
  );
}

function setNeedleRotation(needle, percent) {
  // -135deg is 0%, +135deg is 100%. Total sweep 270deg.
  // Must preserve translateX(-50%) for centering.
  const rotation = -135 + clamp(percent, 0, 1) * 270;
  needle.style.transform = `translateX(-50%) rotate(${rotation}deg)`;
}

function toNumber(value, fallback = null) {
  return Number.isFinite(value) ? Number(value) : fallback;
}

function normalizeTelemetryPayload(payload) {
  if (!payload || typeof payload !== "object") return null;

  if (payload.type === "control" && payload.version) {
    return {
      steering180: toNumber(payload.steering),
      throttle180: toNumber(payload.throttle),
      brake180: toNumber(payload.brake),
      steeringRaw: toNumber(payload.steeringRaw),
      throttleRaw: toNumber(payload.throttleRaw),
      brakeRaw: toNumber(payload.brakeRaw),
    };
  }

  if (
    Number.isFinite(payload.steering) ||
    Number.isFinite(payload.throttle) ||
    Number.isFinite(payload.brake)
  ) {
    return {
      steeringRaw: toNumber(payload.steering, 32767),
      throttleRaw: toNumber(payload.throttle, 0),
      brakeRaw: toNumber(payload.brake, 0),
    };
  }

  return null;
}

function updateTelemetry(data) {
  if (!data) return;

  const steeringRawResolved = Number.isFinite(data.steeringRaw)
    ? data.steeringRaw
    : Math.round(mapRange(clamp(data.steering180 ?? 90, 0, 180), 0, 180, 0, 65535));

  const throttleRawResolved = Number.isFinite(data.throttleRaw)
    ? data.throttleRaw
    : Math.round(mapRange(clamp(data.throttle180 ?? 90, 0, 180), 0, 180, 0, 65535));

  const brakeRawResolved = Number.isFinite(data.brakeRaw)
    ? data.brakeRaw
    : Math.round(mapRange(clamp(data.brake180 ?? 0, 0, 180), 0, 180, 0, 65535));

  const netInput = clamp(throttleRawResolved - brakeRawResolved, -65535, 65535);
  const normalized = netInput / 65535;
  const throttleValueFromRaw = Math.round(clamp(90 - normalized * 90, 0, 180));

  const throttleValue = Number.isFinite(data.throttle180)
    ? Math.round(clamp(data.throttle180, 0, 180))
    : throttleValueFromRaw;

  const steeringValue = Number.isFinite(data.steering180)
    ? Math.round(clamp(data.steering180, 0, 180))
    : Math.round(mapRange(steeringRawResolved, 0, 65535, 0, 180));

  const forwardPercent = clamp((90 - throttleValue) / 90, 0, 1);
  const speedKph = Math.round(forwardPercent * 150);

  // Update Speed Gauge
  if (speedNeedle) {
    setNeedleRotation(speedNeedle, forwardPercent);
  }
  if (speedValueEl) {
    speedValueEl.textContent = speedKph;
  }

  // Update Throttle Bar
  if (throttleBar) {
    const throttlePercent = clamp(throttleRawResolved / 65535, 0, 1);
    throttleBar.style.height = `${throttlePercent * 100}%`;
  }

  // Update Brake Bar
  if (brakeBar) {
    const brakePercent = clamp(brakeRawResolved / 65535, 0, 1);
    brakeBar.style.height = `${brakePercent * 100}%`;
  }

  if (steeringValueEl) steeringValueEl.textContent = `${steeringValue}°`;
  if (rawThrottleEl) rawThrottleEl.textContent = `${throttleRawResolved}`;
  if (rawBrakeEl) rawBrakeEl.textContent = `${brakeRawResolved}`;
}

let currentDeviceIndex = 0;
let videoDevices = [];

async function getVideoDevices() {
  if (!navigator.mediaDevices?.enumerateDevices) return [];
  const devices = await navigator.mediaDevices.enumerateDevices();
  return devices.filter((device) => device.kind === "videoinput");
}

async function startCamera(deviceId = null) {
  if (!navigator.mediaDevices?.getUserMedia) {
    setStatus("Camera unavailable");
    return;
  }

  // Stop existing stream tracks if any
  if (videoEl.srcObject) {
    videoEl.srcObject.getTracks().forEach(track => track.stop());
  }

  const constraints = {
    video: deviceId ? { deviceId: { exact: deviceId } } : { facingMode: "environment" },
    audio: false,
  };

  try {
    const stream = await navigator.mediaDevices.getUserMedia(constraints);
    videoEl.srcObject = stream;
    // setStatus("Camera active", true);
  } catch (err) {
    console.warn("Camera permission denied or error", err);
    // setStatus("Camera error");
  }
}

async function initCamera() {
  videoDevices = await getVideoDevices();
  if (videoDevices.length > 0) {
    // Try to find back camera first if we haven't selected one
    const backCamera = videoDevices.find(d => d.label.toLowerCase().includes('back') || d.label.toLowerCase().includes('environment'));
    const initialDevice = backCamera || videoDevices[0];
    currentDeviceIndex = videoDevices.indexOf(initialDevice);
    await startCamera(initialDevice.deviceId);
  } else {
    // Fallback if enumeration fails or returns empty (some browsers hide labels until permission granted)
    await startCamera();
    // Try to enumerate again after permission granted
    videoDevices = await getVideoDevices();
  }
}

async function switchCamera() {
  if (videoDevices.length < 2) {
    // Refresh list just in case
    videoDevices = await getVideoDevices();
    if (videoDevices.length < 2) return;
  }

  currentDeviceIndex = (currentDeviceIndex + 1) % videoDevices.length;
  const nextDevice = videoDevices[currentDeviceIndex];
  await startCamera(nextDevice.deviceId);
  setStatus(`Cam: ${nextDevice.label || 'Device ' + (currentDeviceIndex + 1)}`, true);
}

document.getElementById("cameraTrigger").addEventListener("click", switchCamera);
if (fullscreenBtn) {
  fullscreenBtn.addEventListener("click", async () => {
    try {
      if (!document.fullscreenElement) {
        await document.documentElement.requestFullscreen({ navigationUI: "hide" });
      } else {
        await document.exitFullscreen();
      }
    } catch (err) {
      console.warn("Fullscreen toggle failed", err);
    }
  });
}

window.addEventListener("DOMContentLoaded", () => {
  initCamera();
  connectWebSocket();
});