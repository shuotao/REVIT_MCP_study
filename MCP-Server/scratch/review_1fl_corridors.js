/**
 * 1FL Ëµ∞Â?Ê≥ïË?Ê™¢Ë??áËá™?ïÊ?Ë®ªËÖ≥?? */

import WebSocket from 'ws';

const ws = new WebSocket('ws://localhost:11111');
let step = 0;
let activeViewId = null;

// ÂæÖË??ÜÁ?Ëµ∞Â?Ê∏ÖÂñÆ (Âæû‰??çÁ??•Ë©¢ÁµêÊ?ÂæóÁü•)
const corridors = [
    { name: 'Âªä‰?1', number: '121' },
    { name: 'Âªä‰?2', number: '29' }
];

let currentCorridorIndex = 0;

ws.on('open', function () {
    console.log('=== 1FL Ëµ∞Â?Ê≥ïË?Ê™¢Ë??áËá™?ïÊ?Ë®?===\n');
    nextStep();
});

function nextStep() {
    step++;

    // Ê≠•È? 1: ?ñÂ??ÆÂ?Ë¶ñÂ?
    if (step === 1) {
        console.log('1. Á¢∫Ë??ÆÂ?Ë¶ñÂ?...');
        ws.send(JSON.stringify({ CommandName: 'get_active_view', Parameters: {}, RequestId: 'step1' }));
    }
    // Ê≠•È? 2: ?•Ë©¢?ÆÂ?Ëµ∞Â?Ë≥áË?
    else if (step === 2) {
        if (currentCorridorIndex >= corridors.length) {
            console.log('\n=== ?Ä?âËµ∞ÂªäË??ÜÂ???===');
            ws.close();
            return;
        }

        const corridor = corridors[currentCorridorIndex];
        console.log(`\n=== ?ïÁ?Ëµ∞Â?: ${corridor.name} [${corridor.number}] ===`);

        // ?àÁî® query_elements ?æÊàø??ID (?†ÁÇ∫‰πãÂ???ID ?ØËÉΩ?ØÂ??ãÁ??ñÈ?Ë¶ÅÁ¢∫Ë™?
        // ?ôË£°?¥Êé•?®Â?Â≠óÊâæÊØîË?‰øùÈö™ÔºåÊ??ÖÂ??ú‰???ID ?ØÂõ∫ÂÆöÁ?Ë©?.. 
        // ?∫‰?‰øùÈö™ÔºåÂ? query ?Ä??1FL ?øÈ???filter
        ws.send(JSON.stringify({
            CommandName: 'get_rooms_by_level',
            Parameters: { level: '1FL' },
            RequestId: 'step2_find_room'
        }));
    }
}

ws.on('message', function (data) {
    const response = JSON.parse(data.toString());

    // ?ïÁ?Ë¶ñÂ??ûÊ?
    if (response.RequestId === 'step1') {
        if (response.Success) {
            activeViewId = response.Data.ElementId;
            console.log(`   ‰ΩøÁî®Ë¶ñÂ?: ${response.Data.Name} (ID: ${activeViewId})`);
            // Ê™¢Êü•Ë¶ñÂ??çÁ®±?ØÂê¶?ÖÂê´ 1F ??level 1 (?ûÂº∑?∂Ô??ÖÊ?Á§?
            if (!response.Data.Name.includes('1') && !response.Data.LevelName?.includes('1')) {
                console.log('   ?†Ô? Ë≠¶Â?: ?ÆÂ?Ë¶ñÂ?‰ºº‰?‰∏çÊòØ‰∏ÄÊ®ìÂπ≥?¢Â?ÔºåÊ?Ë®ªÂèØ?ΩÁÑ°Ê≥ïÈ°ØÁ§∫„Ä?);
            }
            nextStep();
        } else {
            console.log('?°Ê??ñÂ?Ë¶ñÂ?ÔºåÁ?Ê≠¢„Ä?);
            ws.close();
        }
    }

    // ?ïÁ??øÈ??úÂ?
    else if (response.RequestId === 'step2_find_room') {
        if (response.Success) {
            const targetName = corridors[currentCorridorIndex].name;
            const room = response.Data.Rooms.find(r => r.Name === targetName);

            if (room) {
                console.log(`   ?æÂà∞?øÈ?: ID ${room.ElementId}, ?¢Á? ${room.Area} m¬≤`);
                console.log(`   ‰∏≠Â?Èª? (${room.CenterX}, ${room.CenterY})`);

                // ?≤Â??øÈ?Ë≥áË?‰æõÂ?Á∫å‰Ωø??                corridors[currentCorridorIndex].info = room;

                // ‰∏ã‰?Ê≠? ?•Ë©¢?ÜÈ?
                queryWalls(room);
            } else {
                console.log(`   ???æ‰??∞Êàø??${targetName}`);
                currentCorridorIndex++;
                step = 1; // ?çÁΩÆÊ≠•È?Ê®ôË?‰ª•ÁπºÁ∫åËø¥??                nextStep();
            }
        }
    }

    // ?ïÁ??ÜÈ??•Ë©¢
    else if (response.RequestId.startsWith('step3_walls')) {
        const index = parseInt(response.RequestId.split('_')[2]);
        processWallsAndDimension(response.Data, index);
    }

    // ?ïÁ?Ê®ôË®ªÂª∫Á?
    else if (response.RequestId.startsWith('step4_dim')) {
        if (response.Success) {
            console.log(`   ??Ê®ôË®ªÂª∫Á??êÂ? (${response.Data.Value} mm)`);
        } else {
            console.log(`   ??Ê®ôË®ªÂª∫Á?Â§±Ê?: ${response.Error}`);
        }

        // Ê™¢Êü•?ØÂê¶?ÑÊ?ÂæÖË??ÜÁ?Ê®ôË®ª (‰æãÂ?ÊØèÂÄãËµ∞ÂªäÊ? 2 ?ãÊ?Ë®?
        // ?ôË£°Á∞°Â?ÊµÅÁ?ÔºöÊî∂?∞Ê?Ë®ªÂ??âÂ?ÔºåÁπºÁ∫å‰?‰∏Ä?ãËµ∞Âª?        // ‰ΩÜÊ??ëÁôº?Å‰??©ÂÄãÊ?Ë®ªË?Ê±ÇÔ??Ä‰ª•È?Ë¶ÅË??∏Âô®?ñÁ?ÂæÖÊ???        // Á∞°ÂñÆËµ∑Ë?ÔºåÊ??ëÂ?Ë®≠ÈÄôÊòØ‰∏Ä?ãÈ??åÊ≠•?ç‰?ÔºåÁπºÁ∫åË??Ü‰?‰∏Ä??        // ?¥Â•Ω?ÑÊñπÂºèÊòØ??Promise chainÔºå‰??ôË£°??ws callback ÁµêÊ?
    }
});

function queryWalls(room) {
    console.log('   ?•Ë©¢?®È??ÜÈ?...');
    const radius = 5000; // 5m ?úÂ??äÂ?

    ws.send(JSON.stringify({
        CommandName: 'query_walls_by_location',
        Parameters: {
            x: room.CenterX,
            y: room.CenterY,
            searchRadius: radius,
            level: '1FL'
        },
        RequestId: `step3_walls_${currentCorridorIndex}`
    }));
}

function processWallsAndDimension(wallData, index) {
    if (!wallData || wallData.Count === 0) {
        console.log('   ???æ‰??∞Á?È´îÔ??°Ê?Ê®ôË®ª??);
        finishCorridor();
        return;
    }

    // ?§Êñ∑Ëµ∞Â??πÂ? (Ê∞¥Âπ≥?ñÂ???
    // Á∞°ÂñÆ?èËºØÔºöÁ??ÄËøëÁ??©Èù¢?ÜÊòØÂπ≥Ë???X ?ÑÊòØ Y
    // ?ñËÄÖÁ? BoundingBox ÊØî‰?Ôºå‰??ôË£°?ëÂÄëÂè™?â‰∏≠ÂøÉÈ??åÁ?
    // ?ëÂÄëÂ??êÁ???Orientation ?Ü‰?

    const hWalls = wallData.Walls.filter(w => w.Orientation === 'Horizontal');
    const vWalls = wallData.Walls.filter(w => w.Orientation === 'Vertical');

    let boundaryWalls = [];
    let direction = ''; // Ê®ôË®ªÁ∑öÁ??πÂ? (Horizontal: Ê®ôË®ª X Ëª? Vertical: Ê®ôË®ª Y Ëª?.. Á≠âÁ?ÔºåÈ??êÊ?)

    // Â¶ÇÊ?Ê∞¥Âπ≥?ÜÊ?ËºÉË?‰∏îÊ?Â∞çÔ??áËµ∞ÂªäÊòØ?±Ë•ø??Ê∞¥Âπ≥)ÔºåÂØ¨Â∫¶Âú® Y ?πÂ? --> ?ÄË¶?Vertical Ê®ôË®ªÁ∑?(?èÊ∏¨ Y Ë∑?
    // ‰øÆÊ≠£ÔºöËµ∞ÂªäÊòØÊ∞¥Âπ≥?∑Ê? -> ?ÜÂú®‰∏ä‰???-> ?ÜÊòØ Horizontal -> ?èÊ∏¨ Y Ë∑ùÈõ¢

    // ?æÂá∫?ÄËøëÁ???    const nearestWall = wallData.Walls[0];
    const orientation = nearestWall.Orientation; // Horizontal or Vertical

    if (orientation === 'Horizontal') {
        console.log('   ?§Â?Ëµ∞Â??∫Êù±Ë•øÂ? (Ê∞¥Âπ≥)ÔºåÊ∏¨?èÂ???(Y) ÂØ¨Â∫¶');
        boundaryWalls = hWalls;
        // ?æÊ?ËøëÁ??©Èù¢??(‰∏Ä?ãÂú®‰∏≠Â?‰∏äÊñπÔºå‰??ãÂú®‰∏ãÊñπ)
    } else {
        console.log('   ?§Â?Ëµ∞Â??∫Â??óÂ? (?ÇÁõ¥)ÔºåÊ∏¨?èÊù±Ë•?(X) ÂØ¨Â∫¶');
        boundaryWalls = vWalls;
    }

    // Â∞ãÊâæ?©ÂÅ¥?¢Á?
    const center = corridors[index].info;
    const centerCoordinate = orientation === 'Horizontal' ? center.CenterY : center.CenterX;

    // ?ÜÈ?ÔºöÂ§ß?º‰∏≠ÂøÉË?Â∞èÊñº‰∏≠Â?
    // Â∞çÊñº Horizontal ?ÜÔ?ÊØîË? Y Â∫ßÊ? (Face1.Y)
    // Â∞çÊñº Vertical ?ÜÔ?ÊØîË? X Â∫ßÊ? (Face1.X)

    let side1Walls = [];
    let side2Walls = [];

    boundaryWalls.forEach(w => {
        // ?ñÁ??¢Â∫ßÊ®ôÁ?Âπ≥Â??ºÊ? Face1 ‰ΩúÁÇ∫?§Êñ∑
        const wallCoord = orientation === 'Horizontal' ? w.Face1.Y : w.Face1.X;
        if (wallCoord > centerCoordinate) side2Walls.push(w);
        else side1Walls.push(w);
    });

    if (side1Walls.length === 0 || side2Walls.length === 0) {
        console.log('   ???°Ê??æÂà∞?©ÂÅ¥?äÁ???(?ØËÉΩ?ÆÂÅ¥?ØÈ??æÊ??±Â?)');
        finishCorridor();
        return;
    }

    // ?ñÊ?ËøëÁ???    side1Walls.sort((a, b) => b.DistanceToCenter - a.DistanceToCenter); // ?ØË™§ÔºöDistance?ØÊ≠£?∏Ô??âË©≤?æÊ?Â∞èÁ?DistanceToCenter
    // ?∂ÂØ¶ query_walls Â∑≤Á??âË??¢Ê?Â∫è‰???    // ?Ä‰ª?side1Walls ?ÑÊ?Âæå‰??ãÂèØ?Ω‰??ØÊ?ËøëÁ?? ‰∏çÔ??üÂ??óË°®??sorted by distance.
    // ?Ä‰ª•Ê??ëÂè™?ÄË¶ÅÂú®?üÂ? sorted list ‰∏≠Êâæ?∞Á¨¨‰∏Ä??side1 ?åÁ¨¨‰∏Ä??side2

    const wall1 = side1Walls.find(w => true); // ??sorted list ‰∏≠ÊâæÁ¨¨‰???side1 (Â∑≤ÊòØ?Ä?•Ë???
    const wall2 = side2Walls.find(w => true); // ??sorted list ‰∏≠ÊâæÁ¨¨‰???side2 (Â∑≤ÊòØ?Ä?•Ë???

    // ?∫‰?ÂÆâÂÖ®ÔºåÈ??∞Âú® boundaryWalls (Â∑≤Ê?Â∫? ‰∏≠Êâæ
    const w1 = boundaryWalls.find(w => (orientation === 'Horizontal' ? w.Face1.Y : w.Face1.X) < centerCoordinate);
    const w2 = boundaryWalls.find(w => (orientation === 'Horizontal' ? w.Face1.Y : w.Face1.X) > centerCoordinate);

    if (!w1 || !w2) {
        console.log('   ???äÁ??ÜÂà§ÂÆöÂ§±??);
        finishCorridor();
        return;
    }

    // Ë®àÁ??êÊ?
    let dimStart, dimEnd, centerStart, centerEnd;

    if (orientation === 'Horizontal') {
        // ?ÜÊòØÊ∞¥Âπ≥??-> Ê∏¨È? Y
        // ?ÜÂÖßÁ∑?(Net)
        // w1 ?®‰???(YÂ∞?, w2 ?®‰???(YÂ§?
        // w1 ??Face ?âË©≤??Y ËºÉÂ§ß?ÑÈÇ£?? (Face1/Face2 ?™ÂÄãÂ§ß?)
        // ËÆìÊ??ëÂ?Ë®?Face1, Face2 ?ØÁ??ÑÂÖ©?ãÈù¢??        // ‰∏ãÊñπ??w1)?Ä?ñ‰??πÁî± (Max Y among faces)
        const w1MaxY = Math.max(w1.Face1.Y, w1.Face2.Y); // ‰∏ãÁ??Ñ‰?Á∑?        const w2MinY = Math.min(w2.Face1.Y, w2.Face2.Y); // ‰∏äÁ??Ñ‰?Á∑?
        dimStart = { x: center.CenterX, y: w1MaxY };
        dimEnd = { x: center.CenterX, y: w2MinY };

        // ‰∏≠Â?Á∑?        centerStart = { x: center.CenterX, y: w1.LocationLine.StartY };
        centerEnd = { x: center.CenterX, y: w2.LocationLine.StartY };

    } else {
        // ?ÜÊòØ?ÇÁõ¥??-> Ê∏¨È? X
        // w1 ?®Â∑¶??(XÂ∞?, w2 ?®Âè≥??(XÂ§?
        const w1MaxX = Math.max(w1.Face1.X, w1.Face2.X); // Â∑¶Á??ÑÂè≥Á∑?        const w2MinX = Math.min(w2.Face1.X, w2.Face2.X); // ?≥Á??ÑÂ∑¶Á∑?
        dimStart = { x: w1MaxX, y: center.CenterY };
        dimEnd = { x: w2MinX, y: center.CenterY };

        // ‰∏≠Â?Á∑?        centerStart = { x: w1.LocationLine.StartX, y: center.CenterY };
        centerEnd = { x: w2.LocationLine.StartX, y: center.CenterY };
    }

    const netWidth = orientation === 'Horizontal'
        ? Math.abs(dimEnd.y - dimStart.y)
        : Math.abs(dimEnd.x - dimStart.x);

    console.log(`   Ê∑®ÂØ¨: ${netWidth.toFixed(1)} mm`);

    // Ê≥ïË?Ê™¢Ë?
    checkCompliance(netWidth);

    // Âª∫Á?Ê®ôË®ª
    createDimensions(dimStart, dimEnd, centerStart, centerEnd, orientation);

    // ?ôË£°?ëÂÄëÈ?Ë¶Å‰??ãÂª∂?≤Ô?Á¢∫‰?Ê®ôË®ª?Ω‰ª§?ºÈÄÅÂ??çÈÄ≤‰?‰∏ÄËµ∞Â?
    setTimeout(finishCorridor, 1000);
}

function checkCompliance(width) {
    console.log('   [Ê≥ïË?Ê™¢Ë?]');
    const w = width; // mm

    // ?∞ÁÅ£Ê≥ïË?
    if (w >= 1600) console.log('   ??Á¨¶Â??ôÂÅ¥Â±ÖÂÆ§Ê®ôÊ? (>=1.6m)');
    else if (w >= 1200) console.log('   ?†Ô? Á¨¶Â??ÆÂÅ¥Â±ÖÂÆ§Ê®ôÊ? (>=1.2m), ‰ΩÜ‰?Á¨¶È??¥Ë?Ê±?);
    else console.log('   ??‰∏çÁ¨¶?àËµ∞ÂªäÂØ¨Â∫¶Ê?Ê∫?(<1.2m)');
}

function createDimensions(p1, p2, c1, c2, orientation) {
    // ?ßÁ∑£Ê®ôË®ª
    ws.send(JSON.stringify({
        CommandName: 'create_dimension',
        Parameters: {
            viewId: activeViewId,
            startX: p1.x, startY: p1.y,
            endX: p2.x, endY: p2.y,
            offset: 1200 // ?ßÂÅ¥
        },
        RequestId: `step4_dim_net_${currentCorridorIndex}`
    }));

    // ‰∏≠Â?Ê®ôË®ª
    ws.send(JSON.stringify({
        CommandName: 'create_dimension',
        Parameters: {
            viewId: activeViewId,
            startX: c1.x, startY: c1.y,
            endX: c2.x, endY: c2.y,
            offset: 2000 // Â§ñÂÅ¥
        },
        RequestId: `step4_dim_center_${currentCorridorIndex}`
    }));
}

function finishCorridor() {
    currentCorridorIndex++;
    step = 1; // ?çÁΩÆÊ≠•È?
    nextStep();
}

ws.on('error', function (error) {
    console.error('????ØË™§:', error.message);
});

ws.on('close', function () {
    process.exit(0);
});

