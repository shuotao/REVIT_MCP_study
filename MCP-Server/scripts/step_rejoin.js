/**
 * Step: Rejoin wall-column geometry
 */
import WebSocket from 'ws';

const ws = new WebSocket('ws://localhost:8964');

ws.on('open', () => {
    console.log('Step: Rejoin wall-column geometry...');
    ws.send(JSON.stringify({
        CommandName: 'rejoin_wall_joins',
        Parameters: {},
        RequestId: 'rejoin'
    }));
});

ws.on('message', (data) => {
    const res = JSON.parse(data.toString());
    console.log('Result:', JSON.stringify(res.Data, null, 2));
    console.log('=== Rejoin complete ===');
    ws.close();
});

ws.on('error', (e) => console.error('Error:', e.message));
ws.on('close', () => process.exit(0));
setTimeout(() => { ws.close(); }, 30000);
