import { RevitSocketClient } from '../build/socket.js';

async function verify() {
    const client = new RevitSocketClient('localhost', 8964);
    try {
        console.log('Connecting...');
        await client.connect();
        console.log('Connected!');

        console.log('Sending get_project_info...');
        const result = await client.sendCommand('get_project_info', {});
        console.log('Result:', JSON.stringify(result, null, 2));

        process.exit(0);
    } catch (err) {
        console.error('Error:', err.message);
        process.exit(1);
    } finally {
        client.disconnect();
    }
}

verify();
