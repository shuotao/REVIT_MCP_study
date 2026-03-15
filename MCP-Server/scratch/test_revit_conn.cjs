
const WebSocket = require('ws');

const url = 'ws://localhost:12345/';
console.log(`Connecting to ${url}...`);

const ws = new WebSocket(url);

ws.on('open', function open() {
    console.log('Connected to Revit WebSocket server!');
    const ping = {
        command: 'get_project_info',
        args: {}
    };
    ws.send(JSON.stringify(ping));
});

ws.on('message', function message(data) {
    console.log('Received response from Revit:');
    console.log(data.toString());
    ws.close();
});

ws.on('error', function error(err) {
    console.error('WebSocket Error:', err.message);
});

ws.on('close', function close() {
    console.log('Disconnected.');
});

setTimeout(() => {
    console.log('Timeout - closing connection attempt.');
    ws.terminate();
    process.exit(1);
}, 5000);
