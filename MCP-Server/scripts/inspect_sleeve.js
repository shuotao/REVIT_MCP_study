import WebSocket from 'ws';

const ws = new WebSocket('ws://localhost:8964');
const targetId = process.argv[2] || 14344038;

ws.on('open', async () => {
    console.log(`--- INSPECTING ELEMENT ${targetId} ---`);
    
    const send = (cmd, params = {}) => new Promise(res => {
        const id = 'req-' + Date.now();
        ws.once('message', data => {
            const resp = JSON.parse(data.toString());
            if (resp.RequestId === id) res(resp);
        });
        ws.send(JSON.stringify({CommandName: cmd, Parameters: params, RequestId: id}));
    });

    try {
        const info = await send('get_element_info', { elementId: targetId });
        if (!info.Success) {
            console.error('Error:', info.Error);
            process.exit(1);
        }

        console.log('ELEMENT INFO:', JSON.stringify(info.Data, null, 2));
        
        // Also check if it hits any walls specifically
        const wallClash = await send('detect_clashes', {
            mepSource: { categories: ["PipeAccessory", "GenericModel"], filters: [{ field: "Id", operator: "equals", value: targetId.toString() }] },
            csaSource: { categories: ['Walls'] }
        });
        console.log('WALL CLASHES:', JSON.stringify(wallClash.Data, null, 2));

        ws.close();
        process.exit(0);
    } catch (err) {
        console.error('Error:', err);
        process.exit(1);
    }
});
