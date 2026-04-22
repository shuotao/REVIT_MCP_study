import WebSocket from 'ws';

const PORT = 11111;
const ws = new WebSocket(`ws://localhost:${PORT}/`);

const gridNames = ['1', '3', '5', 'A', 'C', 'E'];

ws.on('open', () => {
    console.log(`Connected (Port ${PORT})`);
    
    sendCommand('query_elements', { 
        category: 'Grids', 
        filters: [
            { field: 'Name', operator: 'contains', value: '' } // All grids
        ]
    });
    
    setTimeout(() => {
        ws.close();
        process.exit();
    }, 5000);
});

function sendCommand(name, args) {
    const requestId = `req-${Date.now()}`;
    const payload = JSON.stringify({ RequestId: requestId, CommandName: name, Parameters: args });
    ws.send(payload);
}

ws.on('message', (data) => {
    const response = JSON.parse(data.toString());
    const elements = response.Data.Elements || [];
    
    gridNames.forEach(name => {
        const g = elements.find(e => e.Name === name);
        if (g) {
            console.log(`MATCH: ${g.Name} -> X=${g.Location?.X || '?'}, Y=${g.Location?.Y || '?'}`);
        } else {
            console.log(`MISSING: ${name}`);
        }
    });
});
