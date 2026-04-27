import WebSocket from 'ws';

const url = 'ws://localhost:8964';
console.log(`Connecting to ${url}...`);

const ws = new WebSocket(url);

ws.on('open', () => {
    console.log('✅ Connected to Revit Plugin!');

    const testCommand = {
        CommandName: 'get_project_info',
        Parameters: {},
        RequestId: 'test_ping'
    };

    console.log('Sending test command: get_project_info');
    ws.send(JSON.stringify(testCommand));
});

ws.on('message', (data) => {
    console.log('📩 Received response from Revit:');
    console.log(JSON.stringify(JSON.parse(data.toString()), null, 2));
    ws.close();
    process.exit(0);
});

ws.on('error', (err) => {
    console.error('❌ Connection error:', err.message);
    process.exit(1);
});

setTimeout(() => {
    console.error('⌛ Connection timed out (is Revit MCP service ON?)');
    process.exit(1);
}, 5000);
