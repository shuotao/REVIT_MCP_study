// 一次性查詢腳本：讀取 Revit 當前選取的元件
const WebSocket = require('ws');

const PORT = 11111;
const ws = new WebSocket(`ws://localhost:${PORT}`);

ws.on('open', () => {
    const requestId = 'query_' + Date.now();
    const cmd = {
        CommandName: 'get_selected_elements',
        Parameters: {},
        RequestId: requestId
    };
    ws.send(JSON.stringify(cmd));
    console.error('[sent] get_selected_elements');
});

ws.on('message', (data) => {
    const resp = JSON.parse(data.toString());
    console.log(JSON.stringify(resp, null, 2));
    ws.close();
    process.exit(0);
});

ws.on('error', (err) => {
    console.error('Connection error:', err.message);
    process.exit(1);
});

setTimeout(() => {
    console.error('Timeout!');
    ws.close();
    process.exit(1);
}, 10000);
