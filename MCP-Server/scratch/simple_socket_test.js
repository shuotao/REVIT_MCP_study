import WebSocket from 'ws';

const ws = new WebSocket('ws://localhost:8964');

ws.on('open', () => {
    console.log('Connected to ws://localhost:8964');
    const reqId = 'req_' + Date.now();
    const command = {
        CommandName: 'get_project_info',
        Parameters: {},
        RequestId: reqId
    };
    console.log('Sending command:', command);
    ws.send(JSON.stringify(command));
});

ws.on('message', (data) => {
    console.log('Received raw data:', data.toString());
    process.exit(0);
});

ws.on('error', (err) => {
    console.error('WebSocket Error:', err);
    process.exit(1);
});

ws.on('close', () => {
    console.log('Connection closed');
});
