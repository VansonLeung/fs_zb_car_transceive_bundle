const WS_URL = "ws://localhost:8080/";
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

function setStatus(text, isOnline = false) {
  statusEl.textContent = text;
  statusEl.classList.toggle("online", isOnline);
}

function connectWebSocket() {
  clearTimeout(reconnectHandle);
  setStatus("Connecting...");

  try {
    socket = new WebSocket(WS_URL);
  } catch (err) {
    setStatus(`WebSocket error: ${err.message}`);
    scheduleReconnect();
    return;
  }

  socket.addEventListener("open", () => setStatus("ENGINE: ON", true));

  socket.addEventListener("message", (event) => {
    try {
      const payload = JSON.parse(event.data);
      updateTelemetry(payload);
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

function updateTelemetry(data) {
  const steeringRaw = Number.isFinite(data.steering) ? data.steering : 32767;
  const throttleRaw = Number.isFinite(data.throttle) ? data.throttle : 0;
  const brakeRaw = Number.isFinite(data.brake) ? data.brake : 0;

  const netInput = clamp(throttleRaw - brakeRaw, -65535, 65535);
  const normalized = netInput / 65535;
  const throttleValue = Math.round(clamp(90 - normalized * 90, 0, 180));
  const steeringValue = Math.round(mapRange(steeringRaw, 0, 65535, 0, 180));

  const forwardPercent = clamp((90 - throttleValue) / 90, 0, 1);
  const signedThrottlePercent = clamp((90 - throttleValue) / 90, -1, 1);
  const throttleDisplayPercent = Math.round(signedThrottlePercent * 100);
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
    // Show raw throttle input
    const throttlePercent = clamp(throttleRaw / 65535, 0, 1);
    throttleBar.style.height = `${throttlePercent * 100}%`;
  }

  // Update Brake Bar
  if (brakeBar) {
    // Show raw brake input
    const brakePercent = clamp(brakeRaw / 65535, 0, 1);
    brakeBar.style.height = `${brakePercent * 100}%`;
  }

  if (steeringValueEl) steeringValueEl.textContent = `${steeringValue}°`;
  if (rawThrottleEl) rawThrottleEl.textContent = `${throttleRaw}`;
  if (rawBrakeEl) rawBrakeEl.textContent = `${brakeRaw}`;
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