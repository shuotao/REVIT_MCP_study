import { RevitSocketClient } from '../build/socket.js';

/**
 * 採光偵錯整合工具 (Daylight Debug Toolkit)
 * 
 * 整合自三個原始偵錯腳本：
 *   - debug_daylight_raw.js   → mode: 'raw'
 *   - diagnose_dimensions.js  → mode: 'dimensions'  
 *   - color_daylight_fail.js  → mode: 'color'
 * 
 * 用法：
 *   node scratch/debug_daylight_toolkit.js raw          # 傾印所有房間-窗戶採光資料
 *   node scratch/debug_daylight_toolkit.js dimensions   # 檢查開口尺寸參數
 *   node scratch/debug_daylight_toolkit.js color <id1> <id2> ...  # 將不合格房間上色
 * 
 * 相關 domain：domain/daylight-area-check.md
 */

const mode = process.argv[2] || 'raw';

async function main() {
    const client = new RevitSocketClient('localhost', 8964);

    try {
        await client.connect();

        switch (mode) {
            case 'raw':
                await dumpRawDaylightData(client);
                break;
            case 'dimensions':
                await diagnoseDimensions(client);
                break;
            case 'color':
                await colorFailRooms(client);
                break;
            default:
                console.error(`未知模式: ${mode}\n可用: raw | dimensions | color`);
        }
    } catch (err) {
        console.error('❌ Error:', err.message);
    } finally {
        client.disconnect();
    }
}

// --- Mode 1: 傾印原始房間-窗戶採光資料 ---
async function dumpRawDaylightData(client) {
    const res = await client.sendCommand('get_room_daylight_info', {});
    if (!res.success) throw new Error(res.error);

    const rooms = res.data.Rooms;
    console.log(`Total rooms: ${rooms.length}\n`);

    const allWindowIds = new Set();

    for (const room of rooms) {
        const openings = room.Openings || [];
        openings.forEach(o => allWindowIds.add(o.Id));

        console.log(`Room: ${room.Name} (ID:${room.ElementId}) - ${openings.length} windows`);
        for (const op of openings) {
            console.log(`  Window ${op.Id} [${op.FamilyName}] W=${op.Width} H=${op.Height} Sill=${op.SillHeight} Ext=${op.IsExterior}`);
        }
    }

    console.log(`\nTotal unique windows across all rooms: ${allWindowIds.size}`);
}

// --- Mode 2: 診斷開口尺寸參數 ---
async function diagnoseDimensions(client) {
    const res = await client.sendCommand('get_room_daylight_info', {});
    if (!res.success) throw new Error(res.error);

    for (const room of res.data.Rooms) {
        if (room.Openings && room.Openings.length > 0) {
            console.log(`房間: ${room.Name} (ID: ${room.Id})`);
            console.log(`開口數量: ${room.Openings.length}`);

            for (const opening of room.Openings.slice(0, 3)) {
                console.log(`\n  開口 ID: ${opening.Id}`);
                console.log(`  族群: ${opening.FamilyName}`);
                console.log(`  Width: ${opening.Width} mm`);
                console.log(`  Height: ${opening.Height} mm`);
                console.log(`  SillHeight: ${opening.SillHeight} mm`);
                console.log(`  HeadHeight: ${opening.HeadHeight} mm`);
            }
            break; // 只顯示第一個有開口的房間
        }
    }
}

// --- Mode 3: 將不合格房間上色為紅色 ---
async function colorFailRooms(client) {
    const roomIds = process.argv.slice(3).map(Number).filter(n => !isNaN(n));
    
    if (roomIds.length === 0) {
        console.error('用法: node debug_daylight_toolkit.js color <roomId1> <roomId2> ...');
        console.error('範例: node debug_daylight_toolkit.js color 258443 258446 258449');
        return;
    }

    console.log(`將 ${roomIds.length} 個不合格房間上色為紅色...`);

    for (const id of roomIds) {
        const res = await client.sendCommand('override_element_graphics', {
            elementId: id,
            surfaceFillColor: { r: 255, g: 0, b: 0 },
            transparency: 50,
            patternMode: 'surface'
        });

        if (res.success) {
            console.log(`  ✓ Room ${id} colored`);
        } else {
            console.log(`  ✗ Room ${id} failed: ${res.error}`);
        }
    }

    console.log('✅ Done.');
}

main();
