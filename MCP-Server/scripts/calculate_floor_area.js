/**
 * 計算樓地板面積並檢查陽台
 * 用法: node calculate_floor_area.js
 */

import { executeRevitTool } from '../build/tools/revit-tools.js';
import { RevitSocketClient } from '../build/socket.js';

async function main() {
    const client = new RevitSocketClient(8964);

    try {
        await client.connect();

        console.log("正在擷取樓層資訊...");
        const levels = await executeRevitTool('get_all_levels', {}, client);

        console.log(`找到 ${levels.length} 個樓層。開始分析房間...`);
        console.log("┌────────────────────────────────────────────────────────┐");
        console.log("│                  容積檢討報告 (含陽台)                 │");
        console.log("├────────────────────────────────────────────────────────┤");

        let grandTotalArea = 0;
        let grandTotalBalcony = 0;

        for (const level of levels) {
            // 略過沒有高程的樓層 (如果有)
            if (level.elevation === undefined) continue;

            const rooms = await executeRevitTool('get_rooms_by_level', {
                level: level.name
            }, client);

            let floorTotalArea = 0;
            let floorBalconyArea = 0;

            for (const room of rooms) {
                // 面積單位通常是平方米，視 API 回傳而定，假設回傳已是數值
                // 如果是字串需轉型
                const area = parseFloat(room.area) || 0;

                // 判斷是否為陽台
                const isBalcony = /陽台|Balcony|露台/i.test(room.name) || /陽台|Balcony|露台/i.test(room.department);

                if (isBalcony) {
                    floorBalconyArea += area;
                }

                // 假設所有房間都先加總
                floorTotalArea += area;
            }

            grandTotalArea += floorTotalArea;
            grandTotalBalcony += floorBalconyArea;

            if (floorTotalArea > 0) {
                const balconyStr = floorBalconyArea > 0 ? `(含陽台: ${floorBalconyArea.toFixed(2)}㎡)` : "";
                console.log(`│ ${level.name.padEnd(10)}: ${floorTotalArea.toFixed(2).padStart(8)}㎡ ${balconyStr.padEnd(20)} │`);
            }
        }

        console.log("├────────────────────────────────────────────────────────┤");
        console.log(`│ 總樓地板面積 : ${grandTotalArea.toFixed(2).padStart(8)}㎡                               │`);
        console.log(`│ 總陽台面積   : ${grandTotalBalcony.toFixed(2).padStart(8)}㎡ (需檢討 10% 上限)          │`);
        console.log("└────────────────────────────────────────────────────────┘");

    } catch (error) {
        console.error("執行失敗:", error);
    } finally {
        client.disconnect();
    }
}

main();
