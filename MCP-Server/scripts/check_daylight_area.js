import { RevitSocketClient } from '../build/socket.js';
import path from 'path';
import fs from 'fs';

// Article 41 Requirements
const REQ_RATIO = {
    SCHOOL: 0.20, // 1/5
    RESIDENTIAL: 0.125 // 1/8
};

// Keywords for room types
const ROOM_TYPES = {
    SCHOOL: ["?™å®¤", "Classroom", "å¹¼ç???, "Kindergarten"],
    EXCLUDE: ["å»æ?", "æµ´å®¤", "èµ°å?", "æ¢¯å»³", "æ©Ÿæˆ¿", "Toilet", "Bath", "Corridor", "Lobby", "Mechanical", "Storage", "?²è?"]
};

// Calculate effective area based on regulation
function calculateEffectiveArea(width, height, sillHeight, familyName) {
    // 1. Exclude area below 75cm (750mm)
    let effectiveHeight = 0;
    const sill = sillHeight || 0;
    const h = height || 0;
    const head = sill + h;

    if (sill >= 750) {
        effectiveHeight = h;
    } else if (head > 750) {
        effectiveHeight = head - 750;
    } else {
        effectiveHeight = 0; // Entire window below 75cm
    }

    if (effectiveHeight < 0) effectiveHeight = 0;

    // 2. Apply factors
    let factor = 1.0;
    if (familyName && (familyName.includes("å¤©ç?") || familyName.includes("Skylight"))) {
        factor = 3.0;
    }
    // Balcony factor (0.7) requires manual check or advanced geo analysis

    // Area in mÂ² (dimensions are in mm)
    return (width / 1000.0) * (effectiveHeight / 1000.0) * factor;
}

async function checkDaylightArea() {
    const client = new RevitSocketClient('localhost', 11111);

    try {
        console.log('CWD:', process.cwd());
        fs.writeFileSync('debug_cwd.txt', process.cwd());
        console.log('?? Connecting to Revit...');
        await client.connect();

        // 1. Get Room Daylight Info
        console.log('?? Querying room and window data...');
        // Note: This command is new and requires C# rebuild
        const res = await client.sendCommand('get_room_daylight_info', {});

        if (!res.success) {
            throw new Error(`Failed to get room info: ${res.error}`);
        }

        const rooms = res.data.Rooms;
        fs.writeFileSync('debug_log.txt', `Analyzed ${rooms.length} rooms\n`);
        console.log(`??Analyzed ${rooms.length} rooms.`);
        // Initialize detailed results array
        const detailedResults = [];


        console.log('\n?? Daylight Compliance Report (Article 41)');
        console.log('===================================================================================================');
        console.log(`| ${'Room Name'.padEnd(20)} | ${'Area (mÂ²)'.padEnd(10)} | ${'Req. (mÂ²)'.padEnd(10)} | ${'Eff. Win'.padEnd(10)} | ${'Ratio'.padEnd(8)} | ${'Result'.padEnd(8)} |`);
        console.log('---------------------------------------------------------------------------------------------------');

        const violations = [];
        const passedIds = [];

        for (const room of rooms) {
            // Check if room should be excluded
            const name = room.Name || "";
            if (ROOM_TYPES.EXCLUDE.some(k => name.includes(k))) {
                continue;
            }

            // Determine requirement
            let reqRatio = REQ_RATIO.RESIDENTIAL;
            if (ROOM_TYPES.SCHOOL.some(k => name.includes(k))) {
                reqRatio = REQ_RATIO.SCHOOL;
            }

            // Calculate Total Effective Area
            let totalEffArea = 0;
            if (room.Openings && room.Openings.length > 0) {
                for (const op of room.Openings) {
                    if (op.IsExterior) { // Only count exterior windows
                        totalEffArea += calculateEffectiveArea(op.Width, op.Height, op.SillHeight, op.FamilyName);
                    }
                }
            }

            const reqArea = room.Area * reqRatio;
            const currentRatio = room.Area > 0 ? (totalEffArea / room.Area) : 0;
            const passed = currentRatio >= reqRatio;

            const status = passed ? "??PASS" : "??FAIL";
            const ratioPct = (currentRatio * 100).toFixed(1) + "%";

            // Output row
            const subName = name.length > 20 ? name.substring(0, 17) + "..." : name;
            console.log(`| ${subName.padEnd(20)} | ${room.Area.toFixed(2).padEnd(10)} | ${reqArea.toFixed(2).padEnd(10)} | ${totalEffArea.toFixed(2).padEnd(10)} | ${ratioPct.padEnd(8)} | ${status.padEnd(8)} |`);

            if (passed) {
                passedIds.push(room.ElementId);
            } else {
                violations.push(room.ElementId);
            }

            // Store detailed results
            detailedResults.push({
                name: room.Name,
                id: room.ElementId,
                area: room.Area,
                reqRatio: reqRatio,
                reqArea: reqArea,
                effArea: totalEffArea,
                passed: passed,
                rawOpenings: room.Openings
            });
        }
        console.log('===================================================================================================');
        console.log(`Summary: ${violations.length} violations found.`);
        console.log(`         ${passedIds.length} passed.`);

        // Generate Text Report
        let reportLines = [];
        reportLines.push("å±…å®¤?¡å?æª¢è?è¨ˆç???(Daylight Calculation Report)");
        reportLines.push("===============================================================================");
        reportLines.push(`Date: ${new Date().toLocaleString()}`);
        reportLines.push("æ³•è?ä¾æ? (Regulation): å»ºç??€è¡“è??‡å»ºç¯‰è¨­è¨ˆæ–½å·¥ç·¨ ç¬?1æ¢?(Building Technical Regulations, Article 41)");
        reportLines.push("æ¨™æ? (Standard): å­¸æ ¡/å¹¼å???(School/Kindergarten) 1/5, ?¶ä?å±…å®¤ (Others) 1/8");
        reportLines.push("===============================================================================\n");

        for (const room of detailedResults) {
            let statusIcon = room.passed ? "??OK" : "??FAIL";
            reportLines.push(`[${statusIcon}] ?¿å? (Room): ${room.name} (ID: ${room.id})`);
            reportLines.push(`  - æ¨“å±¤?¢ç? (Floor Area): ${room.area.toFixed(2)} mÂ²`);
            reportLines.push(`  - æ³•å?è¦æ? (Required): ${room.area.toFixed(2)} * ${(1 / room.reqRatio).toFixed(1)} = ${room.reqArea.toFixed(2)} mÂ²`);

            reportLines.push(`  - ?‰æ??‹ç?è¨ˆç? (Effective Daylight Calculation):`);
            if (room.rawOpenings && room.rawOpenings.length > 0) {
                for (const op of room.rawOpenings) {
                    if (!op.IsExterior) continue;

                    let h = op.Height;
                    let w = op.Width;
                    let sill = op.SillHeight;
                    let effArea = 0;
                    let formula = "";

                    // Calculate Effective Area (Logic matches script logic)
                    let effectiveHeight = 0;
                    if (sill >= 750) {
                        effectiveHeight = h;
                        formula = `${(w / 1000).toFixed(2)} * ${(h / 1000).toFixed(2)}`;
                    } else if (sill + h > 750) {
                        effectiveHeight = (sill + h) - 750;
                        formula = `${(w / 1000).toFixed(2)} * (${(h / 1000).toFixed(2)} - (0.75 - ${(sill / 1000).toFixed(2)}))`;
                    } else {
                        effectiveHeight = 0;
                        formula = `${(w / 1000).toFixed(2)} * 0 (Below 75cm)`;
                    }

                    effArea = (w * effectiveHeight) / 1000000.0;

                    if (op.FamilyName.includes("å¤©ç?") || op.Name.includes("skylight")) {
                        effArea *= 3.0; // Factor for skylight
                        formula += " * 3.0 (å¤©ç? Skylight)";
                    }
                    else if (op.FamilyName.includes("?½å°") || op.Name.includes("balcony")) {
                        // Potentially apply 0.7 factor if depth logic was here, but standard text says 0.7 for balcony facing
                    }


                    reportLines.push(`    > çª—æˆ¶ (Window) [${op.FamilyName}] (ID:${op.Id}): W=${w.toFixed(2)}, H=${h.toFixed(2)}, Sill=${sill.toFixed(2)}`);
                    reportLines.push(`      ?‰æ??¢ç? (Eff. Area) = ${formula} = ${effArea.toFixed(2)} mÂ²`);
                }
            } else {
                reportLines.push(`    > ?¡æ??ˆå??†é?çª?(No Valid Exterior Windows)`);
            }

            reportLines.push(`  - ç¸½æ??ˆé¢ç©?(Total Eff. Area): ${room.effArea.toFixed(2)} mÂ²`);
            reportLines.push(`  - ?¤å? (Result): ${room.effArea.toFixed(2)} ${room.passed ? ">=" : "<"} ${room.reqArea.toFixed(2)} \n`);
        }

        try {
            const reportPath = path.resolve(process.cwd(), 'daylight_report.txt');
            const reportContent = reportLines.join('\n');
            fs.writeFileSync(reportPath, reportContent, 'utf8');
            console.log(`?? Detailed report saved to '${reportPath}'`);

            // Also print to console for immediate visibility
            console.log('\n' + reportContent + '\n');
        } catch (err) {
            console.error("??Failed to write report:", err);
        }

        // 2. Visualization
        if (violations.length > 0) {
            console.log(`\n?Ž¨ Applying Red Color to ${violations.length} Violations...`);
            for (const id of violations) {
                await client.sendCommand('override_element_graphics', {
                    elementId: id,
                    surfaceFillColor: { r: 255, g: 0, b: 0 }, // Red
                    transparency: 50,
                    patternMode: "surface" // Force Surface Pattern for Rooms
                });

                // Color Room Tags if available
                const room = rooms.find(r => r.ElementId === id);
                if (room && room.TagIds && room.TagIds.length > 0) {
                    for (const tagId of room.TagIds) {
                        await client.sendCommand('override_element_graphics', {
                            elementId: tagId,
                            lineColor: { r: 255, g: 0, b: 0 }, // Red Text/Lines
                            patternMode: "surface" // Force Surface/Projection Pattern for Tags
                        });
                    }
                }
            }
        }

        if (passedIds.length > 0) {
            console.log(`\n?Ž¨ Applying Green Color to ${passedIds.length} Passed Rooms...`);
            for (const id of passedIds) {
                await client.sendCommand('override_element_graphics', {
                    elementId: id,
                    surfaceFillColor: { r: 0, g: 255, b: 0 }, // Green
                    transparency: 80,
                    patternMode: "surface" // Force Surface Pattern for Rooms
                });

                // Color Room Tags if available
                const room = rooms.find(r => r.ElementId === id);
                if (room && room.TagIds && room.TagIds.length > 0) {
                    for (const tagId of room.TagIds) {
                        await client.sendCommand('override_element_graphics', {
                            elementId: tagId,
                            lineColor: { r: 0, g: 128, b: 0 }, // Dark Green Text
                            patternMode: "surface" // Force Surface/Projection Pattern for Tags
                        });
                    }
                }
            }
        }

        console.log('??Visualization updated.');

    } catch (error) {
        console.error('??Error:', error.message);
        console.error('\n? ï?  If you see "unknown command", please REBUILD the Revit Add-in and Restart Revit.');
    } finally {
        // Disconnect
        client.disconnect();
    }
}

checkDaylightArea();

