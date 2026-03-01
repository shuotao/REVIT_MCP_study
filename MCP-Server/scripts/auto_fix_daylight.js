import { RevitSocketClient } from '../build/socket.js';
import fs from 'fs';

const REQ_RATIO = {
    SCHOOL: 0.20,
    RESIDENTIAL: 0.125
};

// Unit Conversion
const MM_TO_FEET = 1 / 304.8;
const TARGET_WIDTH_MM = 1200;
const TARGET_WIDTH_FEET = TARGET_WIDTH_MM * MM_TO_FEET;

async function autoFixDaylight() {
    const client = new RevitSocketClient('localhost', 8964);

    try {
        console.log('🔌 Connecting to Revit...');
        await client.connect();

        console.log('🔍 Getting Room Info...');
        const res = await client.sendCommand('get_room_daylight_info', {});
        if (!res.success) throw new Error(res.error);

        const rooms = res.data.Rooms;
        let modifiedCount = 0;

        for (const room of rooms) {
            // 1. Check Compliance
            // Simple check logic (same as check_daylight_area.js)
            const isSchool = ["教室", "Classroom"].some(k => room.Name.includes(k));
            const reqRatio = isSchool ? REQ_RATIO.SCHOOL : REQ_RATIO.RESIDENTIAL;

            let totalEffArea = 0;
            if (room.Openings) {
                for (const op of room.Openings) {
                    if (op.IsExterior) {
                        // Simplified calc for check
                        let factor = 1.0;
                        if (op.FamilyName.includes("天窗")) factor = 3.0;

                        // Effective height logic (simplified)
                        // Assuming most windows comply with height rule for now or just using raw for quick check
                        // Properly, we should use the exact logic, but here we just want to ID the failing room.
                        // Let's rely on the ratio.
                        let effectiveHeight = 0;
                        const sill = op.SillHeight || 0;
                        const h = op.Height || 0;
                        if (sill >= 750) effectiveHeight = h;
                        else if (sill + h > 750) effectiveHeight = (sill + h) - 750;

                        totalEffArea += (op.Width / 1000.0) * (effectiveHeight / 1000.0) * factor;
                    }
                }
            }

            const reqArea = room.Area * reqRatio;
            const currentRatio = room.Area > 0 ? (totalEffArea / room.Area) : 0;

            if (currentRatio < reqRatio) {
                console.log(`\n❌ Found Failing Room: ${room.Name} (ID: ${room.ElementId})`);
                console.log(`   Deficit: ${(reqArea - totalEffArea).toFixed(2)} m²`);
                console.log(`   Attempting to widen windows to ${TARGET_WIDTH_MM}mm...`);

                if (!room.Openings || room.Openings.length === 0) {
                    console.log("   No openings to modify.");
                    continue;
                }

                for (const op of room.Openings) {
                    if (!op.IsExterior) continue;

                    // Skip if already large enough
                    if (op.Width >= TARGET_WIDTH_MM) {
                        console.log(`   - Window ${op.Id} width ${op.Width}mm >= 1200mm. Skipping.`);
                        continue;
                    }

                    console.log(`   - Modifying Window ${op.Id} (${op.FamilyName})...`);

                    // Try setting "Width"
                    // We try both "Width" and "寬度" just in case
                    try {
                        const paramRes = await client.sendCommand('modify_element_parameter', {
                            elementId: op.Id,
                            parameterName: 'Width',
                            value: TARGET_WIDTH_FEET.toString()
                        });

                        if (paramRes.success) {
                            console.log(`     ✅ Changed 'Width' to ${TARGET_WIDTH_MM}mm`);
                            modifiedCount++;
                        } else {
                            // Try "寬度"
                            const paramRes2 = await client.sendCommand('modify_element_parameter', {
                                elementId: op.Id,
                                parameterName: '寬度',
                                value: TARGET_WIDTH_FEET.toString()
                            });
                            if (paramRes2.success) {
                                console.log(`     ✅ Changed '寬度' to ${TARGET_WIDTH_MM}mm`);
                                modifiedCount++;
                            } else {
                                console.log(`     ⚠️ Failed to modify width. Parameter might be Read-Only (Type Parameter?)`);
                            }
                        }
                    } catch (err) {
                        console.log(`     ❌ Error modifying window: ${err.message}`);
                    }
                }
            }
        }

        console.log(`\n✨ Auto-fix complete. Modified ${modifiedCount} windows.`);
        console.log(`   Please re-run 'node scripts/check_daylight_area.js' to verify.`);

    } catch (err) {
        console.error('Error:', err);
    } finally {
        client.disconnect();
    }
}

autoFixDaylight();
