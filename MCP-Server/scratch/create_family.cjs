
const WebSocket = require('ws');

const url = 'ws://localhost:11111/';
console.log(`Connecting to ${url}...`);

const ws = new WebSocket(url);

ws.on('open', function open() {
    console.log('Connected!');
    const request = {
        CommandName: 'create_family_document',
        Parameters: {
            templateName: '公制通用模型'
        },
        RequestId: 'create_family_' + Date.now()
    };
    ws.send(JSON.stringify(request));
});

ws.on('message', function message(data) {
    const response = JSON.parse(data.toString());
    console.log('--- DATA ---');
    console.log(JSON.stringify(response, null, 2));
    ws.close();
});

ws.on('error', function error(err) {
    console.error('Error:', err.message);
});

ws.on('close', function close() {
    console.log('Done.');
});

setTimeout(() => { ws.terminate(); process.exit(1); }, 10000);
