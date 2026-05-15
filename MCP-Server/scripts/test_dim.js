import WebSocket from 'ws';
const ws = new WebSocket('ws://localhost:8964');
ws.on('open', async () => {
    ws.once('message', data => {
        console.log('DIMENSION_RESULT:', data.toString());
        ws.close();
        process.exit(0);
    });
    ws.send(JSON.stringify({CommandName: 'test_dimension', Parameters: {}, RequestId: 'dim-' + Date.now()}));
});
setTimeout(() => {
    console.log('Timeout waiting for Revit');
    process.exit(1);
}, 10000);
