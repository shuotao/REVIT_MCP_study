import { RevitSocketClient } from '../build/socket.js';

// Configuration
const SITE_AREA = 500; // m¬≤
const MAX_FAR_PERCENT = 250; // %
const ALLOWABLE_AREA = SITE_AREA * (MAX_FAR_PERCENT / 100);

async function checkFARCompliance() {
    const client = new RevitSocketClient('localhost', 11111);

    try {
        console.log('?? Connecting to Revit...');
        await client.connect();

        // 1. Get all levels
        const levelsRes = await client.sendCommand('get_all_levels', {});
        if (!levelsRes.success) {
            throw new Error(`Failed to get levels: ${levelsRes.error}`);
        }

        const levels = levelsRes.data.Levels;
        console.log(`??Found ${levels.length} levels.`);
        console.log(`TYPE: FAR Check`);
        console.log(`-------------------------------------------`);
        console.log(`?îπ Site Area       : ${SITE_AREA} m¬≤`);
        console.log(`?îπ Max FAR         : ${MAX_FAR_PERCENT}%`);
        console.log(`?îπ Allowable Area  : ${ALLOWABLE_AREA} m¬≤`);
        console.log(`-------------------------------------------\n`);

        let totalWeightedArea = 0;
        let totalExcludingBasement = 0;

        // 2. Query rooms for each level
        for (const level of levels) {
            const roomRes = await client.sendCommand('get_rooms_by_level', {
                level: level.Name,
                includeUnnamed: true
            });

            if (roomRes.success && roomRes.data.TotalRooms > 0) {
                const isBasement = level.Name.toUpperCase().includes("B1") ||
                    level.Name.toUpperCase().includes("B2") ||
                    level.Name.toUpperCase().includes("B3") ||
                    level.Name.toUpperCase().includes("B4");

                let levelWeightedArea = 0;

                roomRes.data.Rooms.forEach(room => {
                    let weight = 1.0;
                    const name = room.Name || "";

                    if (name.includes("?ΩÂè∞")) {
                        weight = 0.5;
                    } else if (name.includes("Ê®ìÊ¢Ø") || name.includes("ÂÆâÂÖ®Ê¢?)) {
                        weight = 0.0;
                    }

                    levelWeightedArea += (room.Area * weight);
                });

                console.log(`??Ô∏? ${level.Name.padEnd(15)} : ${levelWeightedArea.toFixed(2)} m¬≤ ${isBasement ? '(Basement/Parking)' : ''}`);

                totalWeightedArea += levelWeightedArea;
                if (!isBasement) {
                    totalExcludingBasement += levelWeightedArea;
                }
            }
        }

        console.log(`\n===========================================`);
        console.log(`?? CALCULATION RESULTS`);
        console.log(`===========================================`);
        console.log(`1Ô∏è‚É£  Total Weighted Area (All Levels): ${totalWeightedArea.toFixed(2)} m¬≤`);
        console.log(`    Compliance: ${totalWeightedArea <= ALLOWABLE_AREA ? '??PASS' : '??FAIL'}`);
        console.log(`    Diff      : ${(totalWeightedArea - ALLOWABLE_AREA).toFixed(2)} m¬≤`);

        console.log(`\n2Ô∏è‚É£  Total Weighted Area (Excl. Basement): ${totalExcludingBasement.toFixed(2)} m¬≤`);
        console.log(`    Compliance: ${totalExcludingBasement <= ALLOWABLE_AREA ? '??PASS' : '??FAIL'}`);
        console.log(`    Diff      : ${(totalExcludingBasement - ALLOWABLE_AREA).toFixed(2)} m¬≤`);
        console.log(`===========================================`);

    } catch (error) {
        console.error('??Error:', error);
    } finally {
        client.disconnect();
        process.exit(0);
    }
}

checkFARCompliance();

