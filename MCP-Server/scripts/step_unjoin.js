/**
 * Step: Unjoin wall-column geometry
 */
import WebSocket from 'ws';

const ws = new WebSocket('ws://localhost:8964');

ws.on('open', () => {
    console.log('Step: Unjoin wall-column geometry...');
    console.log('Getting walls first...');
    ws.send(JSON.stringify({
        CommandName: 'query_elements',
        Parameters: { category: 'Walls', maxCount: 200 },
        RequestId: 'step1'
    }));
});

let stage = 'get_walls';

ws.on('message', (data) => {
    const res = JSON.parse(data.toString());
    if (!res.Success) { console.log('Error:', res.Error); ws.close(); return; }

    if (stage === 'get_walls') {
        const viewId = res.Data.ViewId;
        const wallIds = (res.Data.Elements || []).map(e => e.ElementId);
        console.log('View ID:', viewId);
        console.log('Walls:', wallIds.length);

        stage = 'unjoin';
        console.log('Unjoining...');
        ws.send(JSON.stringify({
            CommandName: 'unjoin_wall_joins',
            Parameters: { wallIds: wallIds, viewId: viewId },
            RequestId: 'step2'
        }));
    } else {
        console.log('Result:', JSON.stringify(res.Data, null, 2));
        console.log('=== Unjoin complete ===');
        ws.close();
    }
});

ws.on('error', (e) => console.error('Error:', e.message));
ws.on('close', () => process.exit(0));
setTimeout(() => { ws.close(); }, 30000);
