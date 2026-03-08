import { RevitSocketClient } from '../build/socket.js';

async function inspectPropertyLineGeometry() {
    const client = new RevitSocketClient('localhost', 8964);

    try {
        await client.connect();

        // 1. Find Property Lines
        const res = await client.sendCommand('query_elements', {
            category: 'OST_SiteProperty',
            maxCount: 1
        });

        if (res.success && res.data.Elements && res.data.Elements.length > 0) {
            const pl = res.data.Elements[0];
            console.log(`Found Property Line: ${pl.Name} (ID: ${pl.ElementId})`);

            // 2. Get Geometry/Info
            const infoRes = await client.sendCommand('get_element_info', {
                elementId: pl.ElementId
            });

            if (infoRes.success) {
                console.log(JSON.stringify(infoRes.data, null, 2));
            } else {
                console.error('Failed to get info:', infoRes.error);
            }

        } else {
            console.error('No Property Lines found.');
        }

    } catch (error) {
        console.error('Error:', error);
    } finally {
        client.disconnect();
        process.exit(0);
    }
}

inspectPropertyLineGeometry();
