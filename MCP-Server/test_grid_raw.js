import WebSocket from 'ws';

const PORT = 11111;
const ws = new WebSocket(`ws://localhost:${PORT}/`);

ws.on('open', () => {
    const payload = JSON.stringify({ RequestId: 'req-grid-raw', CommandName: 'query_elements', Parameters: { category: 'Grids', maxCount: 1 } });
    ws.send(payload);
    setTimeout(() => { ws.close(); process.exit(); }, 5000);
});

ws.on('message', (data) => {
    const response = JSON.parse(data.toString());
    console.log(JSON.stringify(response.Data.Elements[0], null, 2));
});
