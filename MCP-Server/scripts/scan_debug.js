import WebSocket from 'ws';
const ws = new WebSocket('ws://localhost:8964');
ws.on('open', async () => {
    ws.once('message', data => {
        console.log('SCAN_DEBUG_RESULT:', data.toString());
        ws.close();
        process.exit(0);
    });
    ws.send(JSON.stringify({CommandName: 'scan_penetrated_beams_in_view', Parameters: {}, RequestId: 'dbg-' + Date.now()}));
});
setTimeout(() => process.exit(1), 10000);
