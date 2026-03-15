const WebSocket = require('ws');

const ws = new WebSocket('ws://localhost:11111');

ws.on('open', () => {
    console.log('Connected to Revit');
    const requestId = `req_${Date.now()}`;
    const command = {
        CommandName: 'query_elements',
        Parameters: {
            category: 'OST_Sheets' // Try explicit BuiltInCategory name
        },
        RequestId: requestId
    };
    ws.send(JSON.stringify(command));

    // Safety timeout
    setTimeout(() => {
        console.log('Timeout waiting for response');
        ws.close();
    }, 10000);
});

ws.on('message', (data) => {
    try {
        const response = JSON.parse(data.toString());
        console.log('Raw Response received');
        if (response.Success) {
            const data = response.Data;
            // The response might be the result object directly from QueryElements
            const count = data.TotalFound !== undefined ? data.TotalFound : (Array.isArray(data.Elements) ? data.Elements.length : 0);
            console.log(`RESULT: Found ${count} sheets.`);
            if (data.Elements && Array.isArray(data.Elements)) {
                data.Elements.forEach(s => console.log(`- [${s.ElementId}] ${s.Name}`));
            }
        } else {
            console.error('Error:', response.Error);
        }
    } catch (e) {
        console.error('Parse Error:', e.message);
    }
    ws.close();
});

ws.on('error', (err) => {
    console.error('Socket Error:', err.message);
});

