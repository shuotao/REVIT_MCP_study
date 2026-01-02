/**
 * 1FL 走廊法規檢討與自動標註腳本
 */

import WebSocket from 'ws';

const ws = new WebSocket('ws://localhost:8964');
let step = 0;
let activeViewId = null;

// 待處理的走廊清單 (從之前的查詢結果得知)
const corridors = [
    { name: '廊下1', number: '121' },
    { name: '廊下2', number: '29' }
];

let currentCorridorIndex = 0;

ws.on('open', function () {
    console.log('=== 1FL 走廊法規檢討與自動標註 ===\n');
    nextStep();
});

function nextStep() {
    step++;

    // 步驟 1: 取得目前視圖
    if (step === 1) {
        console.log('1. 確認目前視圖...');
        ws.send(JSON.stringify({ CommandName: 'get_active_view', Parameters: {}, RequestId: 'step1' }));
    }
    // 步驟 2: 查詢目前走廊資訊
    else if (step === 2) {
        if (currentCorridorIndex >= corridors.length) {
            console.log('\n=== 所有走廊處理完成 ===');
            ws.close();
            return;
        }

        const corridor = corridors[currentCorridorIndex];
        console.log(`\n=== 處理走廊: ${corridor.name} [${corridor.number}] ===`);

        // 先用 query_elements 找房間 ID (因為之前的 ID 可能是動態的或需要確認)
        // 這裡直接用名字找比較保險，或者如果之前 ID 是固定的話... 
        // 為了保險，先 query 所有 1FL 房間再 filter
        ws.send(JSON.stringify({
            CommandName: 'get_rooms_by_level',
            Parameters: { level: '1FL' },
            RequestId: 'step2_find_room'
        }));
    }
}

ws.on('message', function (data) {
    const response = JSON.parse(data.toString());

    // 處理視圖回應
    if (response.RequestId === 'step1') {
        if (response.Success) {
            activeViewId = response.Data.ElementId;
            console.log(`   使用視圖: ${response.Data.Name} (ID: ${activeViewId})`);
            // 檢查視圖名稱是否包含 1F 或 level 1 (非強制，僅提示)
            if (!response.Data.Name.includes('1') && !response.Data.LevelName?.includes('1')) {
                console.log('   ⚠️ 警告: 目前視圖似乎不是一樓平面圖，標註可能無法顯示。');
            }
            nextStep();
        } else {
            console.log('無法取得視圖，終止。');
            ws.close();
        }
    }

    // 處理房間搜尋
    else if (response.RequestId === 'step2_find_room') {
        if (response.Success) {
            const targetName = corridors[currentCorridorIndex].name;
            const room = response.Data.Rooms.find(r => r.Name === targetName);

            if (room) {
                console.log(`   找到房間: ID ${room.ElementId}, 面積 ${room.Area} m²`);
                console.log(`   中心點: (${room.CenterX}, ${room.CenterY})`);

                // 儲存房間資訊供後續使用
                corridors[currentCorridorIndex].info = room;

                // 下一步: 查詢牆體
                queryWalls(room);
            } else {
                console.log(`   ❌ 找不到房間 ${targetName}`);
                currentCorridorIndex++;
                step = 1; // 重置步驟標記以繼續迴圈
                nextStep();
            }
        }
    }

    // 處理牆體查詢
    else if (response.RequestId.startsWith('step3_walls')) {
        const index = parseInt(response.RequestId.split('_')[2]);
        processWallsAndDimension(response.Data, index);
    }

    // 處理標註建立
    else if (response.RequestId.startsWith('step4_dim')) {
        if (response.Success) {
            console.log(`   ✅ 標註建立成功 (${response.Data.Value} mm)`);
        } else {
            console.log(`   ❌ 標註建立失敗: ${response.Error}`);
        }

        // 檢查是否還有待處理的標註 (例如每個走廊有 2 個標註)
        // 這裡簡化流程：收到標註回應後，繼續下一個走廊
        // 但我們發送了兩個標註請求，所以需要計數器或等待機制
        // 簡單起見，我們假設這是一個非同步操作，繼續處理下一個
        // 更好的方式是用 Promise chain，但這裡用 ws callback 結構
    }
});

function queryWalls(room) {
    console.log('   查詢周邊牆體...');
    const radius = 5000; // 5m 搜尋半徑

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
        console.log('   ❌ 找不到牆體，無法標註。');
        finishCorridor();
        return;
    }

    // 判斷走廊方向 (水平或垂直)
    // 簡單邏輯：看最近的兩面牆是平行於 X 還是 Y
    // 或者看 BoundingBox 比例，但這裡我們只有中心點和牆
    // 我們分析牆的 Orientation 分佈

    const hWalls = wallData.Walls.filter(w => w.Orientation === 'Horizontal');
    const vWalls = wallData.Walls.filter(w => w.Orientation === 'Vertical');

    let boundaryWalls = [];
    let direction = ''; // 標註線的方向 (Horizontal: 標註 X 軸, Vertical: 標註 Y 軸... 等等，需釐清)

    // 如果水平牆比較近且成對，則走廊是東西向(水平)，寬度在 Y 方向 --> 需要 Vertical 標註線 (量測 Y 距)
    // 修正：走廊是水平長條 -> 牆在上下側 -> 牆是 Horizontal -> 量測 Y 距離

    // 找出最近的牆
    const nearestWall = wallData.Walls[0];
    const orientation = nearestWall.Orientation; // Horizontal or Vertical

    if (orientation === 'Horizontal') {
        console.log('   判定走廊為東西向 (水平)，測量南北 (Y) 寬度');
        boundaryWalls = hWalls;
        // 找最近的兩面牆 (一個在中心上方，一個在下方)
    } else {
        console.log('   判定走廊為南北向 (垂直)，測量東西 (X) 寬度');
        boundaryWalls = vWalls;
    }

    // 尋找兩側面牆
    const center = corridors[index].info;
    const centerCoordinate = orientation === 'Horizontal' ? center.CenterY : center.CenterX;

    // 分類：大於中心與小於中心
    // 對於 Horizontal 牆，比較 Y 座標 (Face1.Y)
    // 對於 Vertical 牆，比較 X 座標 (Face1.X)

    let side1Walls = [];
    let side2Walls = [];

    boundaryWalls.forEach(w => {
        // 取牆面座標的平均值或 Face1 作為判斷
        const wallCoord = orientation === 'Horizontal' ? w.Face1.Y : w.Face1.X;
        if (wallCoord > centerCoordinate) side2Walls.push(w);
        else side1Walls.push(w);
    });

    if (side1Walls.length === 0 || side2Walls.length === 0) {
        console.log('   ❌ 無法找到兩側邊界牆 (可能單側是開放或柱列)');
        finishCorridor();
        return;
    }

    // 取最近的牆
    side1Walls.sort((a, b) => b.DistanceToCenter - a.DistanceToCenter); // 錯誤：Distance是正數，應該找最小的DistanceToCenter
    // 其實 query_walls 已經按距離排序了。
    // 所以 side1Walls 的最後一個可能不是最近的? 不，原始列表是 sorted by distance.
    // 所以我們只需要在原始 sorted list 中找到第一個 side1 和第一個 side2

    const wall1 = side1Walls.find(w => true); // 在 sorted list 中找第一個 side1 (已是最接近的)
    const wall2 = side2Walls.find(w => true); // 在 sorted list 中找第一個 side2 (已是最接近的)

    // 為了安全，重新在 boundaryWalls (已排序) 中找
    const w1 = boundaryWalls.find(w => (orientation === 'Horizontal' ? w.Face1.Y : w.Face1.X) < centerCoordinate);
    const w2 = boundaryWalls.find(w => (orientation === 'Horizontal' ? w.Face1.Y : w.Face1.X) > centerCoordinate);

    if (!w1 || !w2) {
        console.log('   ❌ 邊界牆判定失敗');
        finishCorridor();
        return;
    }

    // 計算坐標
    let dimStart, dimEnd, centerStart, centerEnd;

    if (orientation === 'Horizontal') {
        // 牆是水平的 -> 測量 Y
        // 牆內緣 (Net)
        // w1 在下方 (Y小), w2 在上方 (Y大)
        // w1 的 Face 應該是 Y 較大的那個? (Face1/Face2 哪個大?)
        // 讓我們假設 Face1, Face2 是牆的兩個面。
        // 下方牆(w1)需取上方由 (Max Y among faces)
        const w1MaxY = Math.max(w1.Face1.Y, w1.Face2.Y); // 下牆的上緣
        const w2MinY = Math.min(w2.Face1.Y, w2.Face2.Y); // 上牆的下緣

        dimStart = { x: center.CenterX, y: w1MaxY };
        dimEnd = { x: center.CenterX, y: w2MinY };

        // 中心線
        centerStart = { x: center.CenterX, y: w1.LocationLine.StartY };
        centerEnd = { x: center.CenterX, y: w2.LocationLine.StartY };

    } else {
        // 牆是垂直的 -> 測量 X
        // w1 在左方 (X小), w2 在右方 (X大)
        const w1MaxX = Math.max(w1.Face1.X, w1.Face2.X); // 左牆的右緣
        const w2MinX = Math.min(w2.Face1.X, w2.Face2.X); // 右牆的左緣

        dimStart = { x: w1MaxX, y: center.CenterY };
        dimEnd = { x: w2MinX, y: center.CenterY };

        // 中心線
        centerStart = { x: w1.LocationLine.StartX, y: center.CenterY };
        centerEnd = { x: w2.LocationLine.StartX, y: center.CenterY };
    }

    const netWidth = orientation === 'Horizontal'
        ? Math.abs(dimEnd.y - dimStart.y)
        : Math.abs(dimEnd.x - dimStart.x);

    console.log(`   淨寬: ${netWidth.toFixed(1)} mm`);

    // 法規檢討
    checkCompliance(netWidth);

    // 建立標註
    createDimensions(dimStart, dimEnd, centerStart, centerEnd, orientation);

    // 這裡我們需要一個延遲，確保標註命令發送後再進下一走廊
    setTimeout(finishCorridor, 1000);
}

function checkCompliance(width) {
    console.log('   [法規檢討]');
    const w = width; // mm

    // 台灣法規
    if (w >= 1600) console.log('   ✅ 符合雙側居室標準 (>=1.6m)');
    else if (w >= 1200) console.log('   ⚠️ 符合單側居室標準 (>=1.2m), 但不符雙側要求');
    else console.log('   ❌ 不符合走廊寬度標準 (<1.2m)');
}

function createDimensions(p1, p2, c1, c2, orientation) {
    // 內緣標註
    ws.send(JSON.stringify({
        CommandName: 'create_dimension',
        Parameters: {
            viewId: activeViewId,
            startX: p1.x, startY: p1.y,
            endX: p2.x, endY: p2.y,
            offset: 1200 // 內側
        },
        RequestId: `step4_dim_net_${currentCorridorIndex}`
    }));

    // 中心標註
    ws.send(JSON.stringify({
        CommandName: 'create_dimension',
        Parameters: {
            viewId: activeViewId,
            startX: c1.x, startY: c1.y,
            endX: c2.x, endY: c2.y,
            offset: 2000 // 外側
        },
        RequestId: `step4_dim_center_${currentCorridorIndex}`
    }));
}

function finishCorridor() {
    currentCorridorIndex++;
    step = 1; // 重置步驟
    nextStep();
}

ws.on('error', function (error) {
    console.error('連線錯誤:', error.message);
});

ws.on('close', function () {
    process.exit(0);
});
