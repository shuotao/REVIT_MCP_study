
import WebSocket from 'ws';

function generateRequestId() {
    return `req_${Date.now()}_${Math.random().toString(36).substr(2, 9)}`;
}

const host = 'localhost';
const port = 8964;
const wsUrl = `ws://${host}:${port}`;

console.log(`Connecting to Revit Plugin at ${wsUrl}...`);
const ws = new WebSocket(wsUrl);

ws.on('open', () => {
    console.log('Connected to Revit Plugin!');

    const commandName = 'create_wall';
    const parameters = {
        startX: 0,
        startY: 0,
        endX: 5000,
        endY: 0,
        height: 4000
    };

    const requestId = generateRequestId();
    const command = {
        CommandName: commandName,
        Parameters: parameters,
        RequestId: requestId,
    };

    console.log(`Sending command: ${commandName}`, parameters);
    ws.send(JSON.stringify(command));
});

ws.on('message', (data) => {
    try {
        const response = JSON.parse(data.toString());
        console.log('Received response:', JSON.stringify(response, null, 2));

        if (response.Success) {
            console.log('✅ Wall created successfully!');
        } else {
            console.error('❌ Failed to create wall:', response.Error);
        }

        ws.close();
        process.exit(0);
    } catch (error) {
        console.error('Failed to parse response:', error);
    }
});

ws.on('error', (error) => {
    console.error('WebSocket Error:', error);
    process.exit(1);
});
