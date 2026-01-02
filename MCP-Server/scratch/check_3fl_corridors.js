import WebSocket from 'ws';
const ws = new WebSocket('ws://localhost:8964');

// Configuration
const CONFIG = {
    SEARCH_RADIUS: 3000,     // Search radius for walls (mm)
    MIN_WIDTH: 800,          // Minimum plausible corridor width (mm) to ignore pillars/obstacles
    MAX_WIDTH: 4000,         // Maximum plausible corridor width (mm)
    COMPLIANCE_THRESHOLD: 1200, // Standard compliance (mm) - can be adjustable
    LEVEL_NAME: '3FL'        // Target Level
};

let activeViewId = null;
let currentProcessingIndex = 0;
let targetRooms = [];

ws.on('open', () => {
    console.log('=== 3F Corridor Fire Safety & Dimension Check Started ===');
    step1_getActiveView();
});

ws.on('message', (data) => {
    const response = JSON.parse(data.toString());
    handleResponse(response);
});

function sendCommand(name, params, requestId) {
    ws.send(JSON.stringify({
        CommandName: name,
        Parameters: params,
        RequestId: requestId
    }));
}

function step1_getActiveView() {
    console.log('[Step 1] Verifying Active View...');
    sendCommand('get_active_view', {}, 'get_view');
}

function step2_getRooms() {
    console.log(`[Step 2] Finding corridors on ${CONFIG.LEVEL_NAME}...`);
    sendCommand('get_rooms_by_level', { level: CONFIG.LEVEL_NAME }, 'get_rooms');
}

function step3_processNextRoom() {
    if (currentProcessingIndex >= targetRooms.length) {
        console.log('\n=== All Corridors Processed ===');
        ws.close();
        return;
    }

    const room = targetRooms[currentProcessingIndex];
    console.log(`\n[Step 3] Processing Room: ${room.Name} (ID: ${room.ElementId})...`);
    console.log(`   Center: (${room.CenterPoint.X.toFixed(0)}, ${room.CenterPoint.Y.toFixed(0)})`);

    sendCommand('query_walls_by_location', {
        x: room.CenterPoint.X,
        y: room.CenterPoint.Y,
        searchRadius: CONFIG.SEARCH_RADIUS,
        level: CONFIG.LEVEL_NAME
    }, `query_walls_${currentProcessingIndex}`);
}

function analyzeAndDimension(walls, room) {
    // 1. Group by Orientation
    const vWalls = walls.filter(w => w.Orientation === 'Vertical');
    const hWalls = walls.filter(w => w.Orientation === 'Horizontal');

    // 2. Determine Corridor Direction based on wall count/proximity
    // If more vertical walls are close, it's likely a horizontal corridor (walls are above/below)
    // Wait, "Vertical" wall usually means the wall line is vertical (runs Y-axis). 
    // So a Vertical Corridor (running Y-axis) has Vertical Walls on left/right.
    // A Horizontal Corridor (running X-axis) has Horizontal Walls on top/bottom.
    
    let selectedWalls = [];
    let direction = ''; // 'Horizontal' or 'Vertical' (Corridor direction, not wall)

    // Simple heuristic: which pair has a valid width?
    const checkWidth = (wallList) => {
        if (wallList.length < 2) return null;
        // Sort by coordinate (X for Vertical walls, Y for Horizontal walls)
        const sorted = [...wallList].sort((a, b) => {
             const coordA = (a.Orientation === 'Vertical') ? a.LocationLine.StartX : a.LocationLine.StartY;
             const coordB = (b.Orientation === 'Vertical') ? b.LocationLine.StartX : b.LocationLine.StartY;
             return coordA - coordB;
        });
        
        // Find best pair spanning the center
        // For Vertical walls (Vertical Corridor), we need one Left (X < CenterX) and one Right (X > CenterX)
        const centerCoord = (wallList[0].Orientation === 'Vertical') ? room.CenterPoint.X : room.CenterPoint.Y;
        
        const before = sorted.filter(w => {
            const pos = (w.Orientation === 'Vertical') ? w.LocationLine.StartX : w.LocationLine.StartY;
            return pos < centerCoord;
        });
        const after = sorted.filter(w => {
            const pos = (w.Orientation === 'Vertical') ? w.LocationLine.StartX : w.LocationLine.StartY;
            return pos > centerCoord;
        });

        if (before.length > 0 && after.length > 0) {
            // Pick closest to center
            const w1 = before[before.length - 1];
            const w2 = after[0];
            
            // Calculate distance based on Faces facing the center
            // Need to determine which face is inner. 
            // inner face is the one closer to centerCoord
            const getInnerFaceCoord = (w) => {
                 const f1 = (w.Orientation === 'Vertical') ? w.Face1.X : w.Face1.Y;
                 const f2 = (w.Orientation === 'Vertical') ? w.Face2.X : w.Face2.Y;
                 return (Math.abs(f1 - centerCoord) < Math.abs(f2 - centerCoord)) ? f1 : f2;
            };

            const w1Face = getInnerFaceCoord(w1);
            const w2Face = getInnerFaceCoord(w2);
            const width = Math.abs(w2Face - w1Face);

            return { width, w1, w2, w1Face, w2Face };
        }
        return null;
    };

    const vResult = checkWidth(vWalls);
    const hResult = checkWidth(hWalls);

    let result = null;

    // Prefer the dimension that makes sense (e.g. 1.0m - 3.0m)
    if (vResult && vResult.width >= CONFIG.MIN_WIDTH && vResult.width <= CONFIG.MAX_WIDTH) {
        result = vResult;
        direction = 'Vertical Corridor';
    } else if (hResult && hResult.width >= CONFIG.MIN_WIDTH && hResult.width <= CONFIG.MAX_WIDTH) {
        result = hResult;
        direction = 'Horizontal Corridor';
    }

    if (!result) {
        console.log('   ❌ Could not identify valid corridor walls.');
        currentProcessingIndex++;
        step3_processNextRoom();
        return;
    }

    console.log(`   ✓ Identified ${direction}`);
    console.log(`   📏 Measured Net Width: ${result.width.toFixed(2)} mm`);

    // Compliance Check
    const isCompliant = result.width >= CONFIG.COMPLIANCE_THRESHOLD;
    const icon = isCompliant ? '✅' : '⚠️';
    console.log(`   ${icon} Compliance Check (Threshold: ${CONFIG.COMPLIANCE_THRESHOLD}mm): ${isCompliant ? 'PASS' : 'FAIL'}`);

    // Create Dimension
    // For Vertical Corridor (Vertical Walls), dimension line is Horizontal (Y constant)
    // For Horizontal Corridor (Horizontal Walls), dimension line is Vertical (X constant)
    
    const isVertCorridor = direction === 'Vertical Corridor';
    
    const dimStartX = isVertCorridor ? result.w1Face : room.CenterPoint.X;
    const dimStartY = isVertCorridor ? room.CenterPoint.Y : result.w1Face;
    const dimEndX   = isVertCorridor ? result.w2Face : room.CenterPoint.X;
    const dimEndY   = isVertCorridor ? room.CenterPoint.Y : result.w2Face;

    sendCommand('create_dimension', {
        viewId: activeViewId,
        startX: dimStartX,
        startY: dimStartY,
        endX: dimEndX,
        endY: dimEndY,
        offset: 500
    }, `create_dim_${currentProcessingIndex}`);
}

function handleResponse(response) {
    if (response.RequestId === 'get_view') {
        activeViewId = response.Data.ElementId;
        console.log(`   ✓ Active View ID: ${activeViewId} (${response.Data.Name})`);
        step2_getRooms();
    }
    else if (response.RequestId === 'get_rooms') {
        const allRooms = response.Data;
        // Filter for Corridors (names containing '廊', 'Corridor', 'Hall', etc.)
        targetRooms = allRooms.filter(r => 
            r.Name.includes('廊') || 
            r.Name.toLowerCase().includes('corridor') || 
            r.Name.toLowerCase().includes('hall')
        );

        if (targetRooms.length === 0) {
            console.log('   ⚠️ No rooms named "Corridor" or "廊" found.');
            // Fallback: Ask user or try generic rooms? 
            // For now, let's grab the first unnamed room or largest room if specific names fail, 
            // but sticking to name is safer for "finding corridors".
            console.log('   Available rooms:', allRooms.map(r => r.Name).join(', '));
            ws.close();
            return;
        }
        
        console.log(`   ✓ Found ${targetRooms.length} potential corridors.`);
        step3_processNextRoom();
    }
    else if (response.RequestId.startsWith('query_walls_')) {
        const walls = response.Data;
        if (!walls || walls.length === 0) {
            console.log('   ❌ No walls found near center point.');
            currentProcessingIndex++;
            step3_processNextRoom();
            return;
        }
        const room = targetRooms[currentProcessingIndex];
        analyzeAndDimension(walls, room);
    }
    else if (response.RequestId.startsWith('create_dim_')) {
        if (response.Success) {
            console.log(`   ✓ Dimension created (ID: ${response.Data.DimensionId})`);
        } else {
            console.error(`   ❌ Failed to create dimension: ${response.Error}`);
        }
        currentProcessingIndex++;
        step3_processNextRoom();
    }
}
