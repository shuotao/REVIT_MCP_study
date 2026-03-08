import { RevitSocketClient } from '../build/socket.js';

async function listRoomsByLevelJson() {
    const client = new RevitSocketClient('localhost', 8964);

    try {
        await client.connect();

        const levelsRes = await client.sendCommand('get_all_levels', {});
        if (!levelsRes.success) {
            console.error(JSON.stringify({ error: levelsRes.error }));
            return;
        }

        const levels = levelsRes.data.Levels;
        const result = [];

        for (const level of levels) {
            const levelData = {
                name: level.Name,
                elevation: level.Elevation,
                rooms: []
            };

            const roomRes = await client.sendCommand('get_rooms_by_level', {
                level: level.Name,
                includeUnnamed: true
            });

            if (roomRes.success) {
                levelData.rooms = roomRes.data.Rooms.map(r => ({
                    name: r.Name,
                    number: r.Number,
                    area: r.Area
                }));
            }
            result.push(levelData);
        }

        const fs = await import('fs');
        fs.writeFileSync('rooms_utf8.json', JSON.stringify(result, null, 2), 'utf8');
        console.log('JSON written to rooms_utf8.json');

    } catch (error) {
        console.error(JSON.stringify({ error: error.message }));
    } finally {
        client.disconnect();
        process.exit(0);
    }
}

listRoomsByLevelJson();
