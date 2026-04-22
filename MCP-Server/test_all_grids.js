import WebSocket from 'ws';

const PORT = 11111;
const ws = new WebSocket(`ws://localhost:${PORT}/`);

ws.on('open', () => {
    const payload = JSON.stringify({ 
        RequestId: 'req-all-grids', 
        CommandName: 'get_all_grids', 
        Parameters: {} 
    });
    ws.send(payload);
    setTimeout(() => { ws.close(); process.exit(); }, 8000);
});

ws.on('message', (data) => {
    const response = JSON.parse(data.toString());
    if (response.Success) {
        console.log('All Grids Data:');
        console.log(JSON.stringify(response.Data, null, 2));
    } else {
        console.log('Error:', response.Error);
    }
});
