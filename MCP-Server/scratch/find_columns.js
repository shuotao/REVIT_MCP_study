/**
 * Debug: List all categories in view
 */
import WebSocket from 'ws';

const ws = new WebSocket('ws://localhost:8964');

// Try different category names for columns
const categoriesToTry = [
    'Columns',
    'StructuralColumns',
    'OST_Columns',
    'OST_StructuralColumns',
    '柱',
    '結構柱'
];

let idx = 0;

ws.on('open', () => {
    console.log('=== Finding Columns ===');
    tryCategory();
});

function tryCategory() {
    if (idx >= categoriesToTry.length) {
        console.log('No columns found with any category name');
        ws.close();
        return;
    }

    console.log('Trying category:', categoriesToTry[idx]);
    ws.send(JSON.stringify({
        CommandName: 'query_elements',
        Parameters: { category: categoriesToTry[idx], maxCount: 50 },
        RequestId: 'try_' + idx
    }));
}

ws.on('message', (data) => {
    const res = JSON.parse(data.toString());

    if (res.Success && res.Data.Elements && res.Data.Elements.length > 0) {
        console.log('FOUND! Category:', categoriesToTry[idx]);
        console.log('Count:', res.Data.Elements.length);
        console.log('Sample:', res.Data.Elements.slice(0, 3).map(e => e.Name));
        ws.close();
        return;
    }

    idx++;
    tryCategory();
});

ws.on('error', (e) => console.error('Error:', e.message));
ws.on('close', () => process.exit(0));
setTimeout(() => { ws.close(); }, 30000);
