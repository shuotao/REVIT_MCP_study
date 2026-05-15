import WebSocket from 'ws';
const ws = new WebSocket('ws://localhost:8964');
ws.on('open', async () => {
    const send = (cmd, params = {}) => new Promise(res => {
        const id = 'req-' + Date.now();
        ws.once('message', data => res(JSON.parse(data.toString())));
        ws.send(JSON.stringify({CommandName: cmd, Parameters: params, RequestId: id}));
    });

    console.log('--- LINK INSTANCE DIAGNOSTIC ---');
    // Using query_elements to find RevitLinkInstance
    const resp = await send('query_elements', { category: 'RVT 連結', limit: 10 });
    console.log('LINK_INSTANCES:', JSON.stringify(resp, null, 2));
    
    ws.close();
    process.exit(0);
});
