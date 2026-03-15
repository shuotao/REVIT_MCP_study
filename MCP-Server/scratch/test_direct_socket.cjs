const WebSocket = require('ws');

async function testConnection() {
    const url = 'ws://localhost:11111/';
    console.log(`Trying to connect to ${url}...`);

    const ws = new WebSocket(url);

    ws.on('open', () => {
        console.log('Connected to Revit!');

        const command = {
            CommandName: 'get_selected_elements',
            Parameters: {},
            RequestId: 'test_req'
        };

        console.log('Sending command...');
        ws.send(JSON.stringify(command));
    });

    ws.on('message', (data) => {
        console.log('Received response:');
        console.log(data.toString());
        process.exit(0);
    });

    ws.on('error', (err) => {
        console.error('Connection error:', err.message);
        process.exit(1);
    });

    setTimeout(() => {
        console.error('Timeout');
        process.exit(1);
    }, 10000);
}

testConnection();
