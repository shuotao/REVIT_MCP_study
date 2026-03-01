import { RevitSocketClient } from '../build/socket.js';

// Dump raw room-window data to diagnose missing windows
const MISSING_IDS = [256482, 257143, 257293, 258010];

async function debugDaylightRaw() {
    const client = new RevitSocketClient('localhost', 8964);
    try {
        await client.connect();
        const res = await client.sendCommand('get_room_daylight_info', {});
        if (!res.success) throw new Error(res.error);

        const rooms = res.data.Rooms;
        console.log(`Total rooms: ${rooms.length}\n`);

        // Track all window IDs across all rooms
        const allWindowIds = new Set();

        for (const room of rooms) {
            const openings = room.Openings || [];
            const windowIds = openings.map(o => o.Id);
            windowIds.forEach(id => allWindowIds.add(id));

            console.log(`Room: ${room.Name} (ID:${room.ElementId}) - ${openings.length} windows`);
            for (const op of openings) {
                const mark = MISSING_IDS.includes(op.Id) ? ' <<<< FOUND!' : '';
                console.log(`  Window ${op.Id} [${op.FamilyName}] W=${op.Width} H=${op.Height} Sill=${op.SillHeight} Ext=${op.IsExterior}${mark}`);
            }
        }

        console.log(`\n--- Missing window check ---`);
        for (const id of MISSING_IDS) {
            console.log(`Window ${id}: ${allWindowIds.has(id) ? 'FOUND in data' : 'NOT IN ANY ROOM'}`);
        }

    } catch (err) {
        console.error('Error:', err.message);
    } finally {
        client.disconnect();
    }
}

debugDaylightRaw();
