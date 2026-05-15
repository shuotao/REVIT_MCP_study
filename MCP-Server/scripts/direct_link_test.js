import WebSocket from 'ws';
const ws = new WebSocket('ws://localhost:8964');
ws.on('open', async () => {
    const send = (cmd, params = {}) => new Promise(res => {
        const id = 'req-' + Date.now();
        ws.once('message', data => res(JSON.parse(data.toString())));
        ws.send(JSON.stringify({CommandName: cmd, Parameters: params, RequestId: id}));
    });

    console.log('--- DIRECT LINK TEST ---');
    // Try to get info of a known beam in the link
    const beamId = 12703392;
    const linkId = 13671635;
    const resp = await send('get_element_info', { elementId: beamId, linkInstanceId: linkId });
    console.log('RESULT:', JSON.stringify(resp, null, 2));
    
    ws.close();
    process.exit(0);
});
