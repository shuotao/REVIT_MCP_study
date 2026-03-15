import { RevitSocketClient } from '../build/socket.js';

async function listRoomsByLevel() {
    const client = new RevitSocketClient('localhost', 11111);

    try {
        console.log('?? Connecting to Revit...');
        await client.connect();

        // 1. Get all levels first
        const levelsRes = await client.sendCommand('get_all_levels', {});
        if (!levelsRes.success) {
            throw new Error(`Failed to get levels: ${levelsRes.error}`);
        }

        const levels = levelsRes.data.Levels;
        console.log(`??Found ${levels.length} levels. detailed Querying rooms...`);

        // 2. Query rooms for each level
        for (const level of levels) {
            console.log(`\n??ï¸? Level: ${level.Name} (Elev: ${level.Elevation}mm)`);

            const roomRes = await client.sendCommand('get_rooms_by_level', {
                level: level.Name,
                includeUnnamed: true
            });

            if (roomRes.success) {
                const data = roomRes.data;

                if (data.TotalRooms === 0) {
                    console.log('   ?¹ï?  No rooms found.');
                } else {
                    console.log(`   ?? Total: ${data.TotalRooms} rooms, ${data.TotalArea} mÂ²`);
                    console.log('   ---------------------------------------------------------');
                    console.log('   | Number | Name                | Area (mÂ²) | Status      |');
                    console.log('   ---------------------------------------------------------');

                    data.Rooms.forEach(room => {
                        const status = room.Area > 0 ? "OK" : "Not Enclosed";
                        console.log(
                            `   | ${pad(room.Number, 6)} ` +
                            `| ${pad(room.Name, 19)} ` +
                            `| ${pad(room.Area.toFixed(2), 9)} ` +
                            `| ${pad(status, 11)} |`
                        );
                    });
                    console.log('   ---------------------------------------------------------');
                }
            } else {
                console.error(`   ??Failed to query rooms: ${roomRes.error}`);
            }
        }

    } catch (error) {
        console.error('??Error:', error);
    } finally {
        client.disconnect();
        console.log('\n?? Disconnected');
        process.exit(0);
    }
}

function pad(str, len) {
    if (str === null || str === undefined) return ''.padEnd(len);
    // Handle Chinese characters width if possible, but for simple alignment in console:
    // Simple padding (might be slightly off with wide chars but good enough for now)
    return str.toString().padEnd(len);
}

listRoomsByLevel();

