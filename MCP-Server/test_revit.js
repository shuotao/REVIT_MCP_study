import WebSocket from 'ws';

const PORT = 11111;
const ws = new WebSocket(`ws://localhost:${PORT}/`);

const reqMap = new Map();

function sendCommand(name, args) {
    const requestId = `req-${Date.now()}-${Math.floor(Math.random() * 1000)}`;
    reqMap.set(requestId, { name, args });
    const payload = JSON.stringify({
       RequestId: requestId,
       CommandName: name,
       Parameters: args
    });
    console.log(`Sending: ${name} (${requestId})`);
    ws.send(payload);
}

ws.on('open', () => {
    console.log(`Diagnostic Link (Port ${PORT})`);
    
    // Command 1: Get active view details
    sendCommand('get_active_view', {});
    
    // Command 2: Get all grids in detail
    sendCommand('query_elements', { 
        category: 'Grids', 
        maxCount: 200
    });
    
    setTimeout(() => {
        ws.close();
        process.exit();
    }, 12000);
});

ws.on('message', (data) => {
    try {
        const response = JSON.parse(data.toString());
        const originalReq = reqMap.get(response.RequestId);
        const cmdName = originalReq ? originalReq.name : 'Unknown';
        
        console.log(`\n--- Response: ${cmdName} ---`);
        if (!response.Success) {
            console.log(`Error: ${response.Error}`);
            return;
        }

        if (cmdName === 'get_active_view') {
            console.log(`Active View Info:`, JSON.stringify(response.Data, null, 2));
        } else if (cmdName === 'query_elements') {
            const elements = response.Data.Elements || [];
            console.log(`Grids List (${elements.length}): ${elements.map(e => e.Name).join(', ')}`);
            // Check for any grid that looks like U or has U in name
            const uGrids = elements.filter(e => e.Name.toUpperCase().includes('U'));
            if (uGrids.length > 0) {
                console.log(`U-Like Grids Found: ${uGrids.map(e => e.Name).join(', ')}`);
            }
        }
    } catch (e) {
        console.log('Parse Error');
    }
});

ws.on('error', (err) => {
    console.error('Socket Error:', err.message);
    process.exit(1);
});
