import WebSocket from 'ws';
const ws = new WebSocket('ws://localhost:8964');
ws.on('open', async () => {
    ws.once('message', data => {
        console.log('REVIT_VERSION_DATA_START');
        console.log(data.toString());
        console.log('REVIT_VERSION_DATA_END');
        ws.close();
        process.exit(0);
    });
    // Call a command that returns Revit version info
    ws.send(JSON.stringify({CommandName: 'get_active_view', Parameters: {}, RequestId: 'ver-' + Date.now()}));
});
setTimeout(() => process.exit(1), 5000);
