import WebSocket from 'ws';

const ws = new WebSocket('ws://localhost:8964');

ws.on('open', () => {
    // First get project info to know the project name
    const command = {
        CommandName: 'get_project_info',
        Parameters: {},
        RequestId: 'project_info'
    };
    ws.send(JSON.stringify(command));
});

ws.on('message', (data) => {
    const response = JSON.parse(data.toString());
    console.log(JSON.stringify(response, null, 2));
    ws.close();
    process.exit(0);
});

ws.on('error', (err) => {
    console.error('連線錯誤:', err.message);
    process.exit(1);
});

setTimeout(() => { process.exit(1); }, 5000);
