import { RevitSocketClient } from '../build/socket.js';

async function listLevels() {
    // Correct usage: host, port
    const client = new RevitSocketClient('localhost', 11111);

    try {
        console.log('?? Connecting to Revit MCP Server...');
        await client.connect();
        console.log('??Connected successfully');

        console.log('\n?? Querying Levels...');
        const result = await client.sendCommand('get_all_levels', {});

        if (result.success) {
            const data = result.data;
            console.log(`\n??Found ${data.Count} Levels:\n`);

            // Format output as a nice table
            console.log('--------------------------------------------------');
            console.log(pad('ID', 10) + pad('Name', 25) + pad('Elevation (mm)', 15));
            console.log('--------------------------------------------------');

            data.Levels.forEach(level => {
                console.log(
                    pad(level.ElementId.toString(), 10) +
                    pad(level.Name, 25) +
                    pad(level.Elevation.toFixed(2), 15)
                );
            });
            console.log('--------------------------------------------------');
        } else {
            console.error('??Error querying levels:', result.error);
        }

    } catch (error) {
        console.error('??Execution failed:', error);
    } finally {
        client.disconnect();
        console.log('\n?? Disconnected');
    }
}

function pad(str, len) {
    return str.toString().padEnd(len);
}

listLevels();

