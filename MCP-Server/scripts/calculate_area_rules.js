import { RevitSocketClient } from '../build/socket.js';

/**
 * 面積計算規則
 * - 樓梯間: 0%
 * - 陽台: 50%
 * - 其他: 100%
 */
const RULES = [
    { name: '樓梯間 (Staircase)', keywords: ['樓梯', 'Stair', '梯間'], factor: 0.0 },
    { name: '陽台 (Balcony)', keywords: ['陽台', 'Balcony', '露台', 'Terrace'], factor: 0.5 },
    { name: '一般空間 (General)', keywords: [], factor: 1.0 } // Default
];

function getRuleForRoom(roomName) {
    if (!roomName) return RULES[2]; // Default

    for (const rule of RULES) {
        if (rule.keywords.length > 0) {
            if (rule.keywords.some(k => roomName.includes(k))) {
                return rule;
            }
        }
    }
    return RULES[2]; // Default
}

async function calculateAreaWithRules() {
    const client = new RevitSocketClient('localhost', 8966);

    try {
        console.log('🔌 Connecting to Revit...');
        await client.connect();

        // 1. Get all levels
        const levelsRes = await client.sendCommand('get_all_levels', {});
        if (!levelsRes.success) {
            throw new Error(`Failed to get levels: ${levelsRes.error}`);
        }

        const levels = levelsRes.data.Levels;
        let grandTotalEffectiveArea = 0;

        // 2. Process each level
        for (const level of levels) {
            const roomRes = await client.sendCommand('get_rooms_by_level', {
                level: level.Name,
                includeUnnamed: true
            });

            if (roomRes.success && roomRes.data.TotalRooms > 0) {
                console.log(`\n🏗️  Level: ${level.Name}`);
                console.log('   ------------------------------------------------------------------------------------------');
                console.log('   | Number | Name                | Area (m²) | Type       | Factor | Eff. Area (m²) |');
                console.log('   ------------------------------------------------------------------------------------------');

                let levelEffectiveArea = 0;

                roomRes.data.Rooms.sort((a, b) => a.Number.localeCompare(b.Number)).forEach(room => {
                    const rule = getRuleForRoom(room.Name);
                    const effectiveArea = room.Area * rule.factor;
                    levelEffectiveArea += effectiveArea;

                    console.log(
                        `   | ${pad(room.Number, 6)} ` +
                        `| ${pad(room.Name, 19)} ` +
                        `| ${pad(room.Area.toFixed(2), 9)} ` +
                        `| ${pad(rule.name.split(' ')[0], 10)} ` +
                        `| ${pad(rule.factor * 100 + '%', 6)} ` +
                        `| ${pad(effectiveArea.toFixed(2), 14)} |`
                    );
                });

                console.log('   ------------------------------------------------------------------------------------------');
                console.log(`   📊 Level Total Effective Area: ${levelEffectiveArea.toFixed(2)} m²\n`);
                grandTotalEffectiveArea += levelEffectiveArea;
            }
        }

        console.log('==========================================================================================');
        console.log(`🏆 Grand Total Effective Area: ${grandTotalEffectiveArea.toFixed(2)} m²`);
        console.log('==========================================================================================');

    } catch (error) {
        console.error('❌ Error:', error);
    } finally {
        client.disconnect();
        console.log('\n🔌 Disconnected');
    }
}

function pad(str, len) {
    if (str === null || str === undefined) return ''.padEnd(len);
    // Simple padding (Note: Chinese characters might misalign in simple consoles, but readable enough)
    let output = str.toString();
    // Rudimentary check for non-ascii to adjust padding length slightly (optional optimization)
    // For now, standard padEnd is sufficient for logic verification
    return output.padEnd(len);
}

calculateAreaWithRules();
