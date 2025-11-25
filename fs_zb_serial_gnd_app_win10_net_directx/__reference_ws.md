<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>Simagic Input WebSocket Client</title>
    <style>
        body { 
            font-family: Arial, sans-serif; 
            margin: 20px; 
            background: #1e1e1e; 
            color: #e0e0e0;
        }
        h1 { color: #4CAF50; }
        h2 { color: #03A9F4; margin-bottom: 5px; }
        #status { 
            color: #4CAF50; 
            font-weight: bold; 
            padding: 10px; 
            background: #2d2d2d; 
            border-radius: 5px; 
            margin-bottom: 20px;
        }
        #server-info {
            background: #2d2d2d;
            padding: 15px;
            border-radius: 8px;
            border: 1px solid #444;
            margin-bottom: 20px;
        }
        .info-row {
            display: flex;
            justify-content: space-between;
            padding: 5px 0;
            border-bottom: 1px solid #444;
        }
        .info-row:last-child {
            border-bottom: none;
        }
        .info-label {
            color: #03A9F4;
            font-weight: bold;
        }
        .info-value {
            color: #4CAF50;
        }
        .device-list {
            margin-top: 10px;
            padding-left: 20px;
        }
        .device-item {
            padding: 5px 0;
            color: #a0a0a0;
        }
        .device-item.connected {
            color: #4CAF50;
        }
        #data { 
            background: #2d2d2d; 
            padding: 10px; 
            border: 1px solid #444; 
            white-space: pre-wrap; 
            border-radius: 5px;
            color: #a0a0a0;
        }
        .input-display { 
            margin: 20px 0; 
            padding: 15px;
            background: #2d2d2d;
            border-radius: 8px;
            border: 1px solid #444;
        }
        .value-container {
            display: flex;
            align-items: center;
            gap: 10px;
            margin-top: 10px;
        }
        .value-text {
            min-width: 80px;
            font-size: 18px;
            font-weight: bold;
            color: #4CAF50;
        }
        .bar-container {
            flex: 1;
            height: 30px;
            background: #1a1a1a;
            border: 2px solid #444;
            border-radius: 5px;
            overflow: hidden;
            position: relative;
        }
        .bar {
            height: 100%;
            transition: width 0.05s linear;
            display: flex;
            align-items: center;
            justify-content: flex-end;
            padding-right: 5px;
            color: white;
            font-weight: bold;
            font-size: 12px;
        }
        .bar-steering {
            background: linear-gradient(90deg, #FF5722 0%, #4CAF50 50%, #2196F3 100%);
            position: relative;
        }
        .bar-throttle {
            background: linear-gradient(90deg, #4CAF50, #8BC34A);
        }
        .bar-brake {
            background: linear-gradient(90deg, #F44336, #E91E63);
        }
        .center-line {
            position: absolute;
            left: 50%;
            top: 0;
            bottom: 0;
            width: 2px;
            background: white;
            z-index: 1;
        }
        .buttons-grid {
            display: grid;
            grid-template-columns: repeat(auto-fill, minmax(60px, 1fr));
            gap: 5px;
            margin-top: 10px;
        }
        .button-indicator {
            padding: 8px;
            background: #1a1a1a;
            border: 2px solid #444;
            border-radius: 5px;
            text-align: center;
            font-weight: bold;
            transition: all 0.1s;
        }
        .button-indicator.active {
            background: #4CAF50;
            border-color: #4CAF50;
            color: white;
        }
    </style>
</head>
<body>
    <h1>🏎️ Simagic Input WebSocket Client</h1>
    <div id="status">Connecting...</div>
    
    <div id="server-info">
        <h2>🖥️ Server Status</h2>
        <div class="info-row">
            <span class="info-label">Input Mode:</span>
            <span class="info-value" id="input-mode">Loading...</span>
        </div>
        <div class="info-row">
            <span class="info-label">Device Filter:</span>
            <span class="info-value" id="device-filter">Loading...</span>
        </div>
        <div class="info-row">
            <span class="info-label">Damping:</span>
            <span class="info-value" id="damping-status">Loading...</span>
        </div>
        <div class="info-row">
            <span class="info-label">Force Feedback:</span>
            <span class="info-value" id="ffb-status">Loading...</span>
        </div>
        <div class="info-row">
            <span class="info-label">Connected Devices:</span>
            <span class="info-value" id="connected-count">Loading...</span>
        </div>
        <div class="device-list" id="device-list"></div>
    </div>
    
    <div class="input-display">
        <h2>🎯 Steering</h2>
        <div class="value-container">
            <div class="value-text" id="steering">32767</div>
            <div class="bar-container">
                <div class="center-line"></div>
                <div class="bar bar-steering" id="steering-bar"></div>
            </div>
        </div>
    </div>
    
    <div class="input-display">
        <h2>⚡ Throttle</h2>
        <div class="value-container">
            <div class="value-text" id="throttle">0</div>
            <div class="bar-container">
                <div class="bar bar-throttle" id="throttle-bar"></div>
            </div>
        </div>
    </div>
    
    <div class="input-display">
        <h2>🛑 Brake</h2>
        <div class="value-container">
            <div class="value-text" id="brake">0</div>
            <div class="bar-container">
                <div class="bar bar-brake" id="brake-bar"></div>
            </div>
        </div>
    </div>
    
    <div class="input-display">
        <h2>🎮 Wheel Buttons</h2>
        <div class="buttons-grid" id="buttons-grid"></div>
    </div>
    
    <div class="input-display">
        <h2>📊 Raw JSON</h2>
        <div id="data">No data received</div>
    </div>

    <script>
        const ws = new WebSocket('ws://localhost:8080/');
        const status = document.getElementById('status');
        const dataDiv = document.getElementById('data');
        const steeringDiv = document.getElementById('steering');
        const throttleDiv = document.getElementById('throttle');
        const brakeDiv = document.getElementById('brake');
        const steeringBar = document.getElementById('steering-bar');
        const throttleBar = document.getElementById('throttle-bar');
        const brakeBar = document.getElementById('brake-bar');
        const buttonsGrid = document.getElementById('buttons-grid');

        // Initialize button indicators
        for (let i = 0; i < 10; i++) {
            const btn = document.createElement('div');
            btn.className = 'button-indicator';
            btn.id = `button-${i}`;
            btn.textContent = `${i + 1}`;
            buttonsGrid.appendChild(btn);
        }

        // Fetch server status on load
        async function loadServerStatus() {
            try {
                const response = await fetch('/api/status');
                const statusData = await response.json();
                
                // Update server info
                document.getElementById('input-mode').textContent = statusData.settings.input_mode;
                document.getElementById('device-filter').textContent = statusData.settings.device_filter;
                document.getElementById('damping-status').textContent = statusData.settings.damping_enabled ? 'Enabled' : 'Disabled';
                document.getElementById('ffb-status').textContent = statusData.settings.force_feedback_enabled ? 'Enabled' : 'Disabled';
                
                // Update connected devices
                const connectedDevices = statusData.connected_devices || [];
                const keyboardDevice = statusData.keyboard_device;
                let totalConnected = connectedDevices.length;
                if (keyboardDevice) totalConnected++;
                
                document.getElementById('connected-count').textContent = totalConnected;
                
                // Build device list
                const deviceList = document.getElementById('device-list');
                deviceList.innerHTML = '';
                
                if (keyboardDevice) {
                    const div = document.createElement('div');
                    div.className = 'device-item connected';
                    div.textContent = `✓ ${keyboardDevice.name} (${keyboardDevice.status})`;
                    deviceList.appendChild(div);
                }
                
                connectedDevices.forEach(device => {
                    const div = document.createElement('div');
                    div.className = 'device-item connected';
                    div.textContent = `✓ ${device.name} (${device.status})`;
                    deviceList.appendChild(div);
                });
                
                if (totalConnected === 0) {
                    deviceList.innerHTML = '<div class="device-item">No devices connected</div>';
                }
                
            } catch (error) {
                console.error('Failed to load server status:', error);
                document.getElementById('input-mode').textContent = 'Error loading status';
            }
        }

        // Load status on page load
        loadServerStatus();

        ws.onopen = () => {
            status.textContent = '✅ Connected to WebSocket server';
            status.style.color = '#4CAF50';
        };

        ws.onmessage = (event) => {
            const data = JSON.parse(event.data);
            dataDiv.textContent = JSON.stringify(data, null, 2);
            
            // Update steering (0-65535, center at 32767)
            const steering = data.steering !== undefined ? data.steering : 32767;
            steeringDiv.textContent = steering;
            const steeringPercent = (steering / 65535) * 100;
            steeringBar.style.width = steeringPercent + '%';
            
            // Update throttle (0-65535)
            const throttle = data.throttle || 0;
            throttleDiv.textContent = throttle;
            const throttlePercent = (throttle / 65535) * 100;
            throttleBar.style.width = throttlePercent + '%';
            throttleBar.textContent = throttlePercent.toFixed(0) + '%';
            
            // Update brake (0-65535)
            const brake = data.brake || 0;
            brakeDiv.textContent = brake;
            const brakePercent = (brake / 65535) * 100;
            brakeBar.style.width = brakePercent + '%';
            brakeBar.textContent = brakePercent.toFixed(0) + '%';
            
            // Update buttons
            const buttons = data.wheel_buttons || [];
            for (let i = 0; i < 10; i++) {
                const btn = document.getElementById(`button-${i}`);
                if (buttons[i]) {
                    btn.classList.add('active');
                } else {
                    btn.classList.remove('active');
                }
            }
        };

        ws.onclose = () => {
            status.textContent = '❌ Disconnected from WebSocket server';
            status.style.color = '#F44336';
        };

        ws.onerror = (error) => {
            status.textContent = '⚠️ WebSocket error: ' + error;
            status.style.color = '#FF9800';
        };
    </script>
</body>
</html>