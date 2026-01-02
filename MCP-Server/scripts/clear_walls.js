/**
 * Clear all wall color overrides
 */
import WebSocket from 'ws';

const ws = new WebSocket('ws://localhost:8964');

let wallIds = [];
let viewId = null;

ws.on('open', () => {
    console.log('Step 1: Getting walls...');
    ws.send(JSON.stringify({
        CommandName: 'query_elements',
        Parameters: { category: 'Walls', maxCount: 200 },
        RequestId: 'step1'
    }));
});

ws.on('message', (data) => {
    const res = JSON.parse(data.toString());
    if (!res.Success) { console.log('Error:', res.Error); ws.close(); return; }

    if (!viewId) {
        viewId = res.Data.ViewId;
        wallIds = res.Data.Elements.map(e => e.ElementId);
        console.log('Found', wallIds.length, 'walls in view', viewId);
        console.log('Step 2: Clearing overrides...');

        ws.send(JSON.stringify({
            CommandName: 'clear_element_override',
            Parameters: { elementIds: wallIds, viewId: viewId },
            RequestId: 'step2'
        }));
    } else {
        console.log('Result:', JSON.stringify(res.Data, null, 2));
        console.log('=== DONE: Cleared wall overrides ===');
        ws.close();
    }
});

ws.on('error', (e) => console.error('Error:', e.message));
ws.on('close', () => process.exit(0));
setTimeout(() => { ws.close(); }, 30000);
