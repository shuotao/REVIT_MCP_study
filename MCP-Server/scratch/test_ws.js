import WebSocket from 'ws';

console.log('🔍 Testing WebSocket connection to localhost:8964...');

const ws = new WebSocket('ws://localhost:8964');

ws.on('open', () => {
    console.log('✅ Connection SUCCESSFUL!');
    console.log('   Sending ping...');
    ws.send(JSON.stringify({ type: 'ping' }));
});

ws.on('message', (data) => {
    console.log('✅ Received response:', data.toString());
    ws.close();
    process.exit(0);
});

ws.on('error', (error) => {
    console.log('❌ Connection FAILED');
    console.log('   Error:', error.message);
    process.exit(1);
});

ws.on('close', () => {
    console.log('🔌 Connection closed');
});

// Timeout after 5 seconds
setTimeout(() => {
    console.log('❌ Connection timeout (5s)');
    ws.close();
    process.exit(1);
}, 5000);
