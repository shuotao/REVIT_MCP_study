import WebSocket from 'ws';

console.log('Testing WebSocket connection to localhost:8964...');

const ws = new WebSocket('ws://localhost:8964');

ws.on('open', () => {
    console.log('✅ WebSocket OPENED successfully!');

    const testCommand = {
        CommandName: 'get_project_info',
        Parameters: {},
        RequestId: `test_${Date.now()}`
    };

    console.log('Sending test command:', testCommand);
    ws.send(JSON.stringify(testCommand));
});

ws.on('message', (data) => {
    console.log('✅ Received response:', data.toString());
    ws.close();
    process.exit(0);
});

ws.on('error', (error) => {
    console.error('❌ WebSocket error:', error.message);
    process.exit(1);
});

ws.on('close', () => {
    console.log('🔌 Connection closed');
});

// Timeout after 5 seconds
setTimeout(() => {
    console.error('❌ Connection timeout after 5 seconds');
    process.exit(1);
}, 5000);
