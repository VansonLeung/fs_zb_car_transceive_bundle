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
const partyBadge = document.getElementById("partyBadge");
const lapTimer = document.getElementById("lapTimer");
const tickerEl = document.getElementById("eventTicker");
const toastEl = document.getElementById("qrToast");
const partyGlow = document.getElementById("partyGlow");
const debugElements = document.querySelectorAll(".debug-only");

let socket;
let reconnectHandle;
let currentEndpointIndex = 0;
let audioCtx;
let engineBuffer = null;
let engineSource = null;
let engineGain = null;
let engineLoading = false;
let lastPartyState = {};
let partyDebugEnabled = false;
const engineStartAudio = new Audio("car_ignition.wav");
engineStartAudio.preload = "auto";

function applyDebugVisibility() {
  const hidden = !partyDebugEnabled;
  debugElements.forEach((el) => el.classList.toggle("debug-hidden", hidden));
  if (hidden) {
    if (rawThrottleEl) rawThrottleEl.textContent = "--";
    if (rawBrakeEl) rawBrakeEl.textContent = "--";
  }
}

applyDebugVisibility();

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
      handlePartyDayEvent(payload);
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

function ensureAudio() {
  if (audioCtx) return audioCtx;
  const ctx = new (window.AudioContext || window.webkitAudioContext)();
  audioCtx = ctx;
  return ctx;
}

async function loadEngineBuffer() {
  if (engineBuffer || engineLoading) return engineBuffer;
  engineLoading = true;
  try {
    const resp = await fetch("car_engine.wav");
    const arr = await resp.arrayBuffer();
    const ctx = ensureAudio();
    engineBuffer = await ctx.decodeAudioData(arr);
  } catch (err) {
    console.warn("Engine audio load failed", err);
  } finally {
    engineLoading = false;
  }
  return engineBuffer;
}

async function startEngineLoop() {
  const ctx = ensureAudio();
  if (ctx.state === "suspended") await ctx.resume();
  const buffer = await loadEngineBuffer();
  if (!buffer) return;

  if (engineSource) {
    try { engineSource.stop(); } catch {}
  }

  const source = ctx.createBufferSource();
  source.buffer = buffer;
  source.loop = true;
  source.playbackRate.value = 0.8;

  const gain = ctx.createGain();
  gain.gain.value = 0.06;

  source.connect(gain).connect(ctx.destination);
  source.start();

  engineSource = source;
  engineGain = gain;
}

async function updateEngineSound(drivePercentSigned) {
  // drivePercentSigned: -1..1 where negative is reverse/back-drive
  const effort = clamp(Math.abs(drivePercentSigned), 0, 1);
  const isReverse = drivePercentSigned < -0.001;

  const ctx = ensureAudio();
  if (ctx.state === "suspended") await ctx.resume();

  if (!engineSource) {
    await startEngineLoop();
  }

  if (!engineSource || !engineGain) return;

  const rateBase = isReverse ? 0.65 : 0.7;
  const rateSpan = isReverse ? 1.0 : 1.3;
  const rate = rateBase + effort * rateSpan; // reverse slightly lower pitch

  const gainBase = 0.04;
  const gainSpan = isReverse ? 0.18 : 0.24;
  const gain = gainBase + effort * gainSpan;
  engineSource.playbackRate.value = rate;
  engineGain.gain.value = gain;
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

function handlePartyDayEvent(payload) {
  if (!payload || typeof payload !== "object") return;
  const type = payload.type;
  if (typeof type !== "string") return;
  if (!type.startsWith("partyday")) return;

  if (typeof payload.debugEnabled === "boolean") {
    partyDebugEnabled = payload.debugEnabled;
    applyDebugVisibility();
  }

  try {
    window.dispatchEvent(new CustomEvent("partyday", { detail: payload }));
  } catch (err) {
    console.warn("PartyDay event dispatch failed", err);
  }

  renderPartyUi(payload);
  if (payload.action || payload.reason) {
    pushTicker(`${type} :: ${payload.action || payload.reason}`);
  }
}

function renderPartyUi(payload) {
  const { type, action, reason, remainingMs, modeEnabled, sessionActive, member, qrPayload, source } = payload;

  if (type === "partyday.state") {
    updateBadge(modeEnabled, sessionActive, reason);
    updateTimer(remainingMs);
  }

  if (type === "partyday.session") {
    updateBadge(modeEnabled, sessionActive, action);
    updateTimer(remainingMs);

    if (action === "started") {
      showToast(qrPayload ? `QR VERIFIED (${source || "scanner"})` : "SESSION STARTED", false);
      playEngineStart();
      pulseGlow();
    } else if (action === "ended") {
      showToast("SESSION ENDED", true);
      playEndChirp();
    } else if (action === "tick") {
      // keep timer fresh
    }

    if (member || qrPayload) {
      pushTicker(`Member ${member || "?"} :: ${qrPayload || "QR"}`);
    }
  }

  lastPartyState = { ...lastPartyState, ...payload };
}

function updateBadge(modeEnabled, sessionActive, context) {
  if (!partyBadge) return;
  const active = modeEnabled && sessionActive;
  partyBadge.classList.toggle("active", active);
  partyBadge.classList.toggle("muted", !modeEnabled);
  const stateText = !modeEnabled
    ? "PartyDay: Disabled"
    : active
    ? "PartyDay: Active"
    : "PartyDay: Locked";
  partyBadge.textContent = context ? `${stateText} · ${context}` : stateText;
}

function updateTimer(remainingMs) {
  if (!lapTimer) return;
  if (!Number.isFinite(remainingMs) || remainingMs <= 0) {
    lapTimer.textContent = "Session --:--";
    return;
  }
  const totalSec = Math.round(remainingMs / 1000);
  const m = Math.floor(totalSec / 60)
    .toString()
    .padStart(2, "0");
  const s = (totalSec % 60).toString().padStart(2, "0");
  lapTimer.textContent = `Session ${m}:${s}`;
}

function pushTicker(text) {
  if (!tickerEl || !text || !partyDebugEnabled) return;
  const line = document.createElement("div");
  line.className = "ticker-line";
  line.textContent = `${new Date().toLocaleTimeString()} :: ${text}`;
  tickerEl.prepend(line);
  while (tickerEl.childElementCount > 6) {
    tickerEl.lastChild?.remove();
  }
}

function showToast(text, isError = false) {
  if (!toastEl) return;
  toastEl.textContent = text;
  toastEl.classList.remove("hidden", "error", "show");
  if (isError) toastEl.classList.add("error");
  requestAnimationFrame(() => {
    toastEl.classList.add("show");
    setTimeout(() => toastEl.classList.remove("show"), 1600);
  });
}

function pulseGlow() {
  if (!partyGlow) return;
  partyGlow.style.animation = "none";
  void partyGlow.offsetWidth;
  partyGlow.style.animation = "slow-glow 6s ease-in-out infinite, flash-green 0.6s ease";
}

function ensureAudio() {
  if (audioCtx) return audioCtx;
  const ctx = new (window.AudioContext || window.webkitAudioContext)();
  audioCtx = ctx;
  return ctx;
}

function playEngineStart() {
  try {
    engineStartAudio.currentTime = 0;
    engineStartAudio.play().catch(() => {
      const ctx = ensureAudio();
      if (ctx.state === "suspended") ctx.resume();
    });
  } catch {
    const ctx = ensureAudio();
    if (ctx.state === "suspended") ctx.resume();
  }
}

function playEndChirp() {
  const ctx = ensureAudio();
  if (ctx.state === "suspended") ctx.resume();
  const osc = ctx.createOscillator();
  const gain = ctx.createGain();
  osc.type = "triangle";
  osc.frequency.setValueAtTime(640, ctx.currentTime);
  osc.frequency.exponentialRampToValueAtTime(180, ctx.currentTime + 0.4);
  gain.gain.setValueAtTime(0.001, ctx.currentTime);
  gain.gain.exponentialRampToValueAtTime(0.12, ctx.currentTime + 0.05);
  gain.gain.exponentialRampToValueAtTime(0.001, ctx.currentTime + 0.45);
  osc.connect(gain).connect(ctx.destination);
  osc.start();
  osc.stop(ctx.currentTime + 0.5);
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

  // Update Throttle/Brake Bars and engine sound
  const throttlePercent = clamp(throttleRawResolved / 65535, 0, 1);
  const brakePercent = clamp(brakeRawResolved / 65535, 0, 1);

  if (throttleBar) {
    throttleBar.style.height = `${throttlePercent * 100}%`;
  }

  if (brakeBar) {
    brakeBar.style.height = `${brakePercent * 100}%`;
  }

  updateEngineSound(normalized).catch(() => {});

  if (steeringValueEl) steeringValueEl.textContent = `${steeringValue}°`;
  if (rawThrottleEl) rawThrottleEl.textContent = partyDebugEnabled ? `${throttleRawResolved}` : "--";
  if (rawBrakeEl) rawBrakeEl.textContent = partyDebugEnabled ? `${brakeRawResolved}` : "--";
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