import WebSocket from 'ws';
import fs from 'fs';
const ws = new WebSocket('ws://localhost:8964');
ws.on('open', async () => {
    ws.once('message', data => {
        fs.writeFileSync('view_check.json', data.toString());
        ws.close();
        process.exit(0);
    });
    ws.send(JSON.stringify({CommandName: 'get_active_view', Parameters: {}, RequestId: 'v-' + Date.now()}));
});
setTimeout(() => process.exit(1), 5000);
