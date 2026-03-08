import fs from 'fs';

const BASE_AREA = 500; // m²
const MAX_FAR_PERCENT = 250; // %

try {
    // 1. Calculate Allowable Area
    const maxAllowableArea = BASE_AREA * (MAX_FAR_PERCENT / 100);

    // 2. Get Current Weighted Area
    // We'll recalculate it to be sure, using the same logic as before
    const data = fs.readFileSync('rooms_utf8.json', 'utf8');
    const levels = JSON.parse(data);

    let totalWeightedArea = 0;

    levels.forEach(level => {
        if (level.rooms) {
            level.rooms.forEach(room => {
                let weight = 1.0;
                if (room.name.includes('陽台')) weight = 0.5;
                else if (room.name.includes('樓梯') || room.name.includes('梯間') || room.name.includes('梯廳')) weight = 0.0; // User might want '梯廳' excluded too? Previous turn said '梯廳' was 100%, but usually '梯廳' (Stair Lobby) is part of core. User said "樓梯間不計" (Stairwell not counted).
                // Wait, in previous turn I noted "梯廳" was counted as 100%. The user didn't correct me. 
                // But "樓梯間" usually strictly means the stairwell. "梯廳" is the lobby.
                // I will stick to the previous logic: "樓梯" or "梯間" = 0. "梯廳" = 100%.

                // RE-READING USER PROMPT: "主要空間計100%,陽台計50%,樓梯間不計"
                // "梯廳" (Stair Lobby) is arguably "主要空間" (Main Space) or "公共空間" (Common Area), often counted in FAR unless specifically exempted.
                // I will keep "梯廳" as 100% unless "梯廳" implies "Stairwell" in this specific user's dialect, but usually it's distinct.
                // Let's stick to strict string matching for "樓梯" and "梯間".

                totalWeightedArea += (room.area * weight);
            });
        }
    });

    // 3. Compare
    const currentFARPercent = (totalWeightedArea / BASE_AREA) * 100;
    const isCompliant = totalWeightedArea <= maxAllowableArea;

    console.log('--- Floor Area Ratio (FAR) Check ---');
    console.log(`Base Area: ${BASE_AREA} m²`);
    console.log(`Max FAR: ${MAX_FAR_PERCENT}%`);
    console.log(`Max Allowable Floor Area: ${maxAllowableArea.toFixed(2)} m²`);
    console.log('------------------------------------');
    console.log(`Current Total Weighted Area: ${totalWeightedArea.toFixed(2)} m²`);
    console.log(`Current FAR: ${currentFARPercent.toFixed(2)}%`);
    console.log('------------------------------------');
    console.log(`Result: ${isCompliant ? 'PASS ✅' : 'FAIL ❌'}`);

    if (isCompliant) {
        console.log(`Remaining Allowable Area: ${(maxAllowableArea - totalWeightedArea).toFixed(2)} m²`);
    } else {
        console.log(`Exceeded Area: ${(totalWeightedArea - maxAllowableArea).toFixed(2)} m²`);
    }

} catch (err) {
    console.error('Error:', err);
}
