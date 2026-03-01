import WebSocket from 'ws';

const ws = new WebSocket('ws://localhost:8966');

ws.on('open', () => {
    console.log('Connected to Revit on 8966');
    const request = {
        CommandName: 'check_exterior_wall_openings',
        Parameters: {
            colorizeViolations: true,
            checkArticle45: true,
            checkArticle110: true
        },
        RequestId: 'check_' + Date.now()
    };
    ws.send(JSON.stringify(request));
});

ws.on('message', (data) => {
    const res = JSON.parse(data.toString());
    console.log(JSON.stringify(res, null, 2));
    ws.close();
});

ws.on('error', (err) => {
    console.error('Connection failed:', err.message);
    process.exit(1);
});
