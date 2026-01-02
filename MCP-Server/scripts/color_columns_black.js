/**
 * Color Agile角柱 (columns) BLACK
 * Query structural columns and filter by name containing "Agile"
 */
import WebSocket from 'ws';

const ws = new WebSocket('ws://localhost:8964');

let viewId = null;
let allColumns = [];
let columnIds = [];
let idx = 0;
let stage = 'get_struct_columns';

ws.on('open', () => {
    console.log('=== Color Agile角柱 BLACK ===');
    console.log('Step 1: Get structural columns...');
    ws.send(JSON.stringify({
        CommandName: 'query_elements',
        Parameters: { category: 'StructuralColumns', maxCount: 500 },
        RequestId: 'step1'
    }));
});

ws.on('message', (data) => {
    const res = JSON.parse(data.toString());

    if (stage === 'get_struct_columns') {
        if (res.Success && res.Data.Elements) {
            viewId = res.Data.ViewId;
            allColumns = res.Data.Elements;
            console.log('Structural columns found:', allColumns.length);
        }

        // Also try architectural columns
        stage = 'get_arch_columns';
        console.log('Step 2: Get architectural columns...');
        ws.send(JSON.stringify({
            CommandName: 'query_elements',
            Parameters: { category: 'Columns', maxCount: 500 },
            RequestId: 'step2'
        }));
        return;
    }

    if (stage === 'get_arch_columns') {
        if (res.Success && res.Data.Elements) {
            if (!viewId) viewId = res.Data.ViewId;
            allColumns = allColumns.concat(res.Data.Elements);
            console.log('Total columns after arch:', allColumns.length);
        }

        // Filter by name containing "Agile" or "角柱"
        columnIds = allColumns
            .filter(c => c.Name && (c.Name.includes('Agile') || c.Name.includes('角柱')))
            .map(c => c.ElementId);

        console.log('Columns matching Agile/角柱:', columnIds.length);

        if (columnIds.length === 0) {
            // If not found by name, color ALL columns
            console.log('No Agile columns found by name, coloring all columns...');
            columnIds = allColumns.map(c => c.ElementId);
        }

        if (columnIds.length === 0) {
            console.log('No columns found');
            ws.close();
            return;
        }

        stage = 'override';
        idx = 0;
        console.log('Step 3: Apply BLACK color to', columnIds.length, 'columns...');
        applyNext();
        return;
    }

    if (stage === 'override') {
        idx++;
        if (idx < columnIds.length) {
            applyNext();
        } else {
            console.log('Applied BLACK to', columnIds.length, 'columns');
            console.log('=== DONE ===');
            ws.close();
        }
    }
});

function applyNext() {
    ws.send(JSON.stringify({
        CommandName: 'override_element_graphics',
        Parameters: {
            elementId: columnIds[idx],
            viewId: viewId,
            surfaceFillColor: { r: 30, g: 30, b: 30 },  // Dark gray/black
            transparency: 0
        },
        RequestId: 'override_' + idx
    }));
}

ws.on('error', (e) => console.error('Error:', e.message));
ws.on('close', () => process.exit(0));
setTimeout(() => { ws.close(); }, 120000);
