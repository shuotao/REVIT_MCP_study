import { RevitSocketClient } from '../build/socket.js';

async function diagnose() {
    const client = new RevitSocketClient('localhost', 8964);

    try {
        await client.connect();
        console.log('🔍 Fetching room data...');

        const res = await client.sendCommand('get_room_daylight_info', {});

        if (!res.success) {
            throw new Error(res.error);
        }

        // Find first opening with Sill Height > 0
        for (const room of res.data.Rooms) {
            console.log(`\n房間: ${room.Name} (ID: ${room.Id})`);

            if (room.Openings && room.Openings.length > 0) {
                console.log(`開口數量: ${room.Openings.length}`);

                for (const opening of room.Openings.slice(0, 3)) {  // First 3 only
                    console.log(`\n  開口 ID: ${opening.Id}`);
                    console.log(`  族群: ${opening.FamilyName}`);
                    console.log(`  名稱: ${opening.Name}`);
                    console.log(`  Width: ${opening.Width} mm`);
                    console.log(`  Height: ${opening.Height} mm`);
                    console.log(`  SillHeight: ${opening.SillHeight} mm`);
                    console.log(`  Head Height: ${opening.HeadHeight} mm`);
                }

                break;  // Only first room
            }
        }

    } catch (error) {
        console.error('❌ Error:', error.message);
    } finally {
        client.disconnect();
    }
}

diagnose();
