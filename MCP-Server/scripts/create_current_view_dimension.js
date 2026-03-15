/**
 * ?®ÁõÆ?çË??ñÂª∫Á´ãËµ∞ÂªäÂ∞∫ÂØ∏Ê?Ë®? * ?™Â??µÊ∏¨?ÆÂ?Ë¶ñÂ??ÑÊ?Â±§Ô??•Ë©¢Ëµ∞Â?‰∏¶Âª∫Á´ãÊ?Ë®? */

import WebSocket from 'ws';

const ws = new WebSocket('ws://localhost:11111');

let step = 1;
let activeViewId = null;
let currentLevel = null;
let corridors = [];

ws.on('open', function () {
    console.log('=== ?®ÁõÆ?çË??ñÂª∫Á´ãËµ∞ÂªäÂ∞∫ÂØ∏Ê?Ë®?===\n');

    // Step 1: ?ñÂ??ÆÂ?Ë¶ñÂ?
    const command = {
        CommandName: 'get_active_view',
        Parameters: {},
        RequestId: 'get_view_' + Date.now()
    };
    ws.send(JSON.stringify(command));
});

ws.on('message', function (data) {
    const response = JSON.parse(data.toString());

    if (step === 1) {
        // ?ïÁ?Ë¶ñÂ?Ë≥áË?
        if (response.Success && response.Data) {
            activeViewId = response.Data.ViewId || response.Data.ElementId;
            currentLevel = response.Data.LevelName || response.Data.Level || '3FL';

            console.log(`?? ?ÆÂ?Ë¶ñÂ?: ${response.Data.Name}`);
            console.log(`   Ë¶ñÂ? ID: ${activeViewId}`);
            console.log(`   Ë¶ñÂ?È°ûÂ?: ${response.Data.ViewType}`);
            console.log(`   Ê®ìÂ±§: ${currentLevel}`);

            // Step 2: ?•Ë©¢Ë©≤Ê?Â±§Á??øÈ?
            step = 2;
            console.log(`\n--- ?•Ë©¢ ${currentLevel} Ê®ìÂ±§?ÑËµ∞Âª?---\n`);

            const roomsCommand = {
                CommandName: 'get_rooms_by_level',
                Parameters: {
                    level: currentLevel,
                    includeUnnamed: true
                },
                RequestId: 'get_rooms_' + Date.now()
            };
            ws.send(JSON.stringify(roomsCommand));
        } else {
            console.log('?ñÂ?Ë¶ñÂ?Â§±Ê?:', response.Error);
            ws.close();
        }
    } else if (step === 2) {
        // ?ïÁ??øÈ??óË°®
        if (response.Success && response.Data) {
            const rooms = response.Data.Rooms || response.Data;
            console.log(`?æÂà∞ ${rooms.length} ?ãÊàø?ì`);

            // ÁØ©ÈÅ∏Ëµ∞Â?
            corridors = rooms.filter(room =>
                room.Name && (
                    room.Name.includes('Ëµ∞Â?') ||
                    room.Name.toLowerCase().includes('corridor') ||
                    room.Name.includes('Âªä‰?') ||
                    room.Name.includes('Âª?)
                )
            );

            if (corridors.length > 0) {
                console.log(`\n?æÂà∞ ${corridors.length} ?ãËµ∞Âª?`);
                corridors.forEach((c, i) => {
                    console.log(`  [${i + 1}] ${c.Name} (ID: ${c.ElementId})`);
                });

                // ?•Ë©¢Á¨¨‰??ãËµ∞ÂªäÁ?Ë©≥Á¥∞Ë≥áË?
                step = 3;
                console.log(`\n--- ?ñÂ???{corridors[0].Name}?çË©≥Á¥∞Ë?Ë®?---`);

                const roomInfoCommand = {
                    CommandName: 'get_room_info',
                    Parameters: {
                        roomId: corridors[0].ElementId
                    },
                    RequestId: 'get_room_' + Date.now()
                };
                ws.send(JSON.stringify(roomInfoCommand));
            } else {
                console.log('\n??Ë©≤Ê?Â±§Ê??âÊâæ?∞Ëµ∞Âª?);
                console.log('?Ä?âÊàø??');
                rooms.forEach(r => console.log(`  - ${r.Name || '(?™ÂëΩ??'}`));
                ws.close();
            }
        } else {
            console.log('?•Ë©¢?øÈ?Â§±Ê?:', response.Error);
            ws.close();
        }
    } else if (step === 3) {
        // ?ïÁ??øÈ?Ë©≥Á¥∞Ë≥áË?
        let boundingBox = null;

        if (response.Success && response.Data && response.Data.BoundingBox) {
            boundingBox = response.Data.BoundingBox;
            console.log(`\n?äÁ???`);
            console.log(`  Min: (${boundingBox.MinX?.toFixed(0)}, ${boundingBox.MinY?.toFixed(0)})`);
            console.log(`  Max: (${boundingBox.MaxX?.toFixed(0)}, ${boundingBox.MaxY?.toFixed(0)})`);
        } else {
            // Â¶ÇÊ?Ê≤íÊ??äÁ??íÔ?‰ΩøÁî®?êË®≠Â∫ßÊ?
            console.log('?†Ô? ?°Ê??ñÂ??äÁ??íÔ??óË©¶‰ΩøÁî®?•Ë©¢?ÜÈ?...');
            step = 4;
            const wallCommand = {
                CommandName: 'query_walls_by_location',
                Parameters: {
                    x: 0,
                    y: 15000,
                    searchRadius: 10000,
                    level: currentLevel
                },
                RequestId: 'query_walls_' + Date.now()
            };
            ws.send(JSON.stringify(wallCommand));
            return;
        }

        // Âª∫Á?Â∞∫ÂØ∏Ê®ôË®ª
        if (boundingBox) {
            const width = Math.abs(boundingBox.MaxY - boundingBox.MinY);
            const length = Math.abs(boundingBox.MaxX - boundingBox.MinX);

            console.log(`\n?? Ëµ∞Â?Â∞∫ÂØ∏:`);
            console.log(`   ÂØ¨Â∫¶: ${width.toFixed(0)} mm (${(width / 1000).toFixed(2)} m)`);
            console.log(`   ?∑Â∫¶: ${length.toFixed(0)} mm (${(length / 1000).toFixed(2)} m)`);

            // Step 4: Âª∫Á?ÂØ¨Â∫¶Ê®ôË®ª
            step = 4;
            console.log('\n--- Âª∫Á?ÂØ¨Â∫¶Ê®ôË®ª ---');

            const widthDimCommand = {
                CommandName: 'create_dimension',
                Parameters: {
                    viewId: activeViewId,
                    startX: boundingBox.MinX - 500,
                    startY: boundingBox.MinY,
                    endX: boundingBox.MinX - 500,
                    endY: boundingBox.MaxY,
                    offset: 1000
                },
                RequestId: 'dim_width_' + Date.now()
            };

            // ?≤Â??äÁ??í‰?ÂæåÁ?‰ΩøÁî®
            ws.boundingBox = boundingBox;
            ws.send(JSON.stringify(widthDimCommand));
        }
    } else if (step === 4) {
        // ?ïÁ?ÂØ¨Â∫¶Ê®ôË®ªÁµêÊ?
        if (response.Success) {
            console.log('??ÂØ¨Â∫¶Ê®ôË®ªÂª∫Á??êÂ?Ôº?, response.Data?.DimensionId ? `ID: ${response.Data.DimensionId}` : '');
        } else {
            console.log('??ÂØ¨Â∫¶Ê®ôË®ªÂ§±Ê?:', response.Error);
        }

        // Step 5: Âª∫Á??∑Â∫¶Ê®ôË®ª
        if (ws.boundingBox) {
            step = 5;
            console.log('\n--- Âª∫Á??∑Â∫¶Ê®ôË®ª ---');

            const lengthDimCommand = {
                CommandName: 'create_dimension',
                Parameters: {
                    viewId: activeViewId,
                    startX: ws.boundingBox.MinX,
                    startY: ws.boundingBox.MinY - 500,
                    endX: ws.boundingBox.MaxX,
                    endY: ws.boundingBox.MinY - 500,
                    offset: 1000
                },
                RequestId: 'dim_length_' + Date.now()
            };
            ws.send(JSON.stringify(lengthDimCommand));
        } else {
            ws.close();
        }
    } else if (step === 5) {
        // ?ïÁ??∑Â∫¶Ê®ôË®ªÁµêÊ?
        if (response.Success) {
            console.log('???∑Â∫¶Ê®ôË®ªÂª∫Á??êÂ?Ôº?, response.Data?.DimensionId ? `ID: ${response.Data.DimensionId}` : '');
        } else {
            console.log('???∑Â∫¶Ê®ôË®ªÂ§±Ê?:', response.Error);
        }

        // ÂÆåÊ?
        console.log('\n=== Ê®ôË®ªÂÆåÊ? ===');
        console.log('\n?í° Ë´ãÂú® Revit Ë¶ñÂ?‰∏≠Êü•?ãÊñ∞Âª∫Á??ÑÂ∞∫ÂØ∏Ê?Ë®?);

        // ?≤ÁÅ´Ë¶èÁ??êÈ?
        const width = Math.abs(ws.boundingBox.MaxY - ws.boundingBox.MinY);
        console.log('\n?î• ?≤ÁÅ´Ë¶èÁ?Ê™¢Êü•:');
        if (width >= 1600) {
            console.log(`   ??Ëµ∞Â?Ê∑®ÂØ¨ ${(width / 1000).toFixed(2)}m ??1.6m (Á¨¶Â??´Èô¢/?ÇÈ??¢Ë?ÂÆ?`);
        } else if (width >= 1200) {
            console.log(`   ??Ëµ∞Â?Ê∑®ÂØ¨ ${(width / 1000).toFixed(2)}m ??1.2m (Á¨¶Â?‰∏Ä?¨Âª∫ÁØâÁâ©Ë¶èÂ?)`);
        } else {
            console.log(`   ??Ëµ∞Â?Ê∑®ÂØ¨ ${(width / 1000).toFixed(2)}m < 1.2m (‰∏çÁ¨¶?àË?ÂÆ?`);
        }

        ws.close();
    }
});

ws.on('error', function (error) {
    console.error('????ØË™§:', error.message);
    console.error('\nË´ãÁ¢∫Ë™?Revit MCP ?çÂ?Â∑≤Â???);
});

ws.on('close', function () {
    process.exit(0);
});

setTimeout(() => {
    console.log('\n?±Ô?  ?ç‰?Ë∂ÖÊ?Ôº?0ÁßíÔ?');
    process.exit(1);
}, 30000);

