import WebSocket from 'ws';
const ws = new WebSocket('ws://localhost:8964');
ws.on('open', async () => {
    ws.once('message', data => {
        console.log('ADVANCED_ANALYSIS_RESULT:\n', data.toString());
        ws.close();
        process.exit(0);
    });
    console.log("Sending advanced_analyze command...");
    ws.send(JSON.stringify({CommandName: 'advanced_analyze', Parameters: {}, RequestId: 'adv-' + Date.now()}));
});
setTimeout(() => {
    console.log('Timeout waiting for Revit');
    process.exit(1);
}, 60000); // Give it 60 seconds because geometric checking takes time
