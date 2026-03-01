import WebSocket from 'ws';

const port = 8966;
console.log(`Testing WebSocket connection to localhost:${port}...`);

const ws = new WebSocket(`ws://localhost:${port}`);

ws.on('open', () => {
    console.log(`✅ WebSocket OPENED successfully on ${port}!`);
    process.exit(0);
});

ws.on('error', (error) => {
    console.error(`❌ WebSocket error on ${port}:`, error.message);
    process.exit(1);
});

setTimeout(() => {
    console.error('⌛ Connection timeout');
    process.exit(1);
}, 2000);
