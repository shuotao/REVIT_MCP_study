/**
 * Simple test for override_element_graphics
 */
import WebSocket from 'ws';

const ws = new WebSocket('ws://localhost:8964');

let stage = 'get_view';
let viewId = null;
let walls = [];

ws.on('open', () => {
    console.log('Connected to Revit MCP');
    console.log('Step 1: Getting active view...');
    ws.send(JSON.stringify({
        CommandName: 'get_active_view',
        Parameters: {},
        RequestId: 'step1'
    }));
});

ws.on('message', (data) => {
    const response = JSON.parse(data.toString());

    if (!response.Success) {
        console.log('ERROR:', response.Error);
        ws.close();
        return;
    }

    if (stage === 'get_view') {
        viewId = response.Data.Id;
        console.log('View:', response.Data.Name, 'ID:', viewId);

        console.log('Step 2: Getting walls...');
        stage = 'get_walls';
        ws.send(JSON.stringify({
            CommandName: 'query_elements',
            Parameters: { category: 'Walls', viewId: viewId },
            RequestId: 'step2'
        }));
    }
    else if (stage === 'get_walls') {
        walls = response.Data.Elements || [];
        console.log('Found', walls.length, 'walls');

        if (walls.length === 0) {
            console.log('No walls found');
            ws.close();
            return;
        }

        // Test override on first wall
        console.log('Step 3: Testing override on first wall (ID:', walls[0].ElementId, ')');
        stage = 'override';
        ws.send(JSON.stringify({
            CommandName: 'override_element_graphics',
            Parameters: {
                elementId: walls[0].ElementId,
                viewId: viewId,
                surfaceFillColor: { r: 255, g: 0, b: 0 },
                transparency: 20
            },
            RequestId: 'step3'
        }));
    }
    else if (stage === 'override') {
        console.log('Override result:', JSON.stringify(response.Data, null, 2));
        console.log('SUCCESS! Check Revit - first wall should be RED');
        ws.close();
    }
});

ws.on('error', (err) => {
    console.error('Connection error:', err.message);
});

ws.on('close', () => {
    process.exit(0);
});

setTimeout(() => {
    console.log('Timeout');
    ws.close();
}, 30000);
