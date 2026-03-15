import WebSocket from 'ws';

// Connect to Revit Plugin Socket
const ws = new WebSocket('ws://localhost:11111');

console.log('Attempting to connect to Revit at ws://localhost:11111...');

ws.on('open', () => {
    console.log('Connected to Revit Plugin');
    const requestId = `req_${Date.now()}`;
    const command = {
        CommandName: 'get_project_info',
        Parameters: {},
        RequestId: requestId
    };
    console.log('Sending command:', JSON.stringify(command));
    ws.send(JSON.stringify(command));
});

ws.on('message', (data) => {
    console.log('Raw Response:', data.toString());
    try {
        const response = JSON.parse(data.toString());
        if (response.Success) {
            console.log('Project Info:', JSON.stringify(response.Data, null, 2));
        } else {
            console.error('Command Failed:', response.Error);
        }
    } catch (e) {
        console.error('Failed to parse response:', e);
    }
    ws.close();
});

ws.on('error', (err) => {
    console.error('Connection Error. Is Revit running and the Add-in loaded?');
    console.error('Details:', err.message);
    process.exit(1);
});

