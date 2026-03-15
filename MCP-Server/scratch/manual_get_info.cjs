
const WebSocket = require('ws');

const url = 'ws://localhost:11111/';
console.log(`Connecting to ${url}...`);

const ws = new WebSocket(url);

ws.on('open', function open() {
    console.log('Connected!');
    const request = {
        CommandName: 'get_project_info',
        Parameters: {},
        RequestId: 'manual_query_' + Date.now()
    };
    console.log('Sending request:', JSON.stringify(request));
    ws.send(JSON.stringify(request));
});

ws.on('message', function message(data) {
    console.log('--- RESPONSE RECEIVED ---');
    const response = JSON.parse(data.toString());
    console.log(JSON.stringify(response, null, 2));
    ws.close();
});

ws.on('error', function error(err) {
    console.error('WebSocket Error:', err.message);
});

ws.on('close', function close() {
    console.log('Disconnected.');
});

setTimeout(() => {
    console.log('Timeout - closing.');
    ws.terminate();
    process.exit(1);
}, 10000);
