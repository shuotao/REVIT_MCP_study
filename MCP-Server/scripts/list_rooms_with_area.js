/**
 * 列出每層樓的房間清單與面積
 * 用法: node list_rooms_with_area.js
 */

import { executeRevitTool } from '../build/tools/revit-tools.js';
import { RevitSocketClient } from '../build/socket.js';

async function main() {
    const client = new RevitSocketClient('localhost', 8964);

    try {
        await client.connect();

        console.log("正在擷取樓層資訊...");
        const response = await executeRevitTool('get_all_levels', {}, client);
        const levels = response.Levels || response;

        console.log(`找到 ${levels.length} 個樓層。開始列出房間清單...`);

        for (const level of levels) {
            // 略過沒有高程的樓層 (如果有)
            if (level.elevation === undefined) continue;

            const rooms = await executeRevitTool('get_rooms_by_level', {
                level: level.name
            }, client);

            if (rooms.length === 0) continue;

            console.log(`\n=== 樓層: ${level.name} (房間數: ${rooms.length}) ===`);
            console.log("┌──────────────────────────────┬─────────────┐");
            console.log("│ 房間名稱                     │ 面積 (㎡)   │");
            console.log("├──────────────────────────────┼─────────────┤");

            let levelTotalArea = 0;

            // 排序房間名稱
            rooms.sort((a, b) => a.name.localeCompare(b.name));

            for (const room of rooms) {
                const area = parseFloat(room.area) || 0;
                levelTotalArea += area;

                // 截斷過長的名稱
                const nameDisplay = room.name.length > 28 ? room.name.substring(0, 25) + "..." : room.name;

                console.log(`│ ${nameDisplay.padEnd(28)} │ ${area.toFixed(2).padStart(11)} │`);
            }

            console.log("├──────────────────────────────┼─────────────┤");
            console.log(`│ 總計                         │ ${levelTotalArea.toFixed(2).padStart(11)} │`);
            console.log("└──────────────────────────────┴─────────────┘");
        }

    } catch (error) {
        console.error("執行失敗:", error);
    } finally {
        client.disconnect();
    }
}

main();
