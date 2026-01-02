/**
 * Debug: Check what elements are being queried
 */
import WebSocket from 'ws';
import fs from 'fs';

const ws = new WebSocket('ws://localhost:8964');

ws.on('open', () => {
    console.log('Checking query result...');
    ws.send(JSON.stringify({
        CommandName: 'query_elements',
        Parameters: { category: 'Walls', maxCount: 100 },
        RequestId: 'debug1'
    }));
});

ws.on('message', (data) => {
    const res = JSON.parse(data.toString());
    if (!res.Success) { console.log('Error:', res.Error); ws.close(); return; }

    const elements = res.Data.Elements || [];
    console.log('Total elements:', elements.length);

    // Group by category
    const byCategory = {};
    elements.forEach(e => {
        const cat = e.Category || 'Unknown';
        if (!byCategory[cat]) byCategory[cat] = [];
        byCategory[cat].push(e.ElementId);
    });

    console.log('\n=== Elements by Category ===');
    for (const [cat, ids] of Object.entries(byCategory)) {
        console.log(cat + ': ' + ids.length + ' elements');
        console.log('  IDs: ' + ids.slice(0, 5).join(', ') + (ids.length > 5 ? '...' : ''));
    }

    fs.writeFileSync('debug_query_result.json', JSON.stringify(res.Data, null, 2), 'utf8');
    console.log('\nSaved to debug_query_result.json');
    ws.close();
});

ws.on('error', (e) => console.error('Error:', e.message));
ws.on('close', () => process.exit(0));
setTimeout(() => { ws.close(); }, 30000);
