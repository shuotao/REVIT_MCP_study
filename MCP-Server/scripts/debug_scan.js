import WebSocket from 'ws';
const ws = new WebSocket('ws://localhost:8964');
ws.on('open', async () => {
    const send = (cmd, params = {}) => new Promise(res => {
        const id = 'req-' + Date.now();
        ws.once('message', data => res(JSON.parse(data.toString())));
        ws.send(JSON.stringify({CommandName: cmd, Parameters: params, RequestId: id}));
    });
    const scanResp = await send('scan_penetrated_beams_in_view');
    console.log('SCAN_RESULT_START');
    console.log(JSON.stringify(scanResp));
    console.log('SCAN_RESULT_END');
    ws.close();
    process.exit(0);
});
