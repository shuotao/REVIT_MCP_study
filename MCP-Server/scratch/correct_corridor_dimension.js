/**
 * 正確的走廊尺寸標註流程
 * 展示正確的工具調用優先級
 */

import WebSocket from 'ws';

const ws = new WebSocket('ws://localhost:8964');

let currentStep = 0;
let viewId, roomCenter, walls;

ws.on('open', function () {
    console.log('=== 正確的走廊尺寸標註流程 ===\n');
    executeStep1();
});

// Step 1: 取得視圖（用於標註）
function executeStep1() {
    currentStep = 1;
    console.log('[Step 1] 取得目前視圖...');
    
    ws.send(JSON.stringify({
        CommandName: 'get_active_view',
        Parameters: {},
        RequestId: 'step1_' + Date.now()
    }));
}

// Step 2: 取得走廊房間資訊（只為了中心點）
function executeStep2() {
    currentStep = 2;
    console.log('[Step 2] 取得走廊房間中心點（房間ID: 52842719）...');
    console.log('   ⚠️  注意: BoundingBox 不用於尺寸標註！');
    
    ws.send(JSON.stringify({
        CommandName: 'get_room_info',
        Parameters: {
            roomId: 52842719  // 2FL 走廊
        },
        RequestId: 'step2_' + Date.now()
    }));
}

// Step 3: 查詢實際牆體（這是關鍵步驟）
function executeStep3() {
    currentStep = 3;
    console.log('[Step 3] 🎯 查詢實際牆體座標（這是尺寸標註的依據）...');
    console.log(`   搜尋中心: (${roomCenter.x}, ${roomCenter.y})`);
    
    ws.send(JSON.stringify({
        CommandName: 'query_walls_by_location',
        Parameters: {
            x: roomCenter.x,
            y: roomCenter.y,
            searchRadius: 3000,
            level: '2FL'
        },
        RequestId: 'step3_' + Date.now()
    }));
}

// Step 4: 用牆體面座標建立尺寸標註
function executeStep4() {
    currentStep = 4;
    
    // 找出垂直牆（平行於走廊長度）
    const verticalWalls = walls.filter(w => w.Orientation === 'Vertical');
    
    if (verticalWalls.length < 2) {
        console.error('❌ 找不到足夠的垂直牆體');
        ws.close();
        return;
    }
    
    // 按距離排序，取最近的兩面
    verticalWalls.sort((a, b) => a.DistanceToCenter - b.DistanceToCenter);
    
    const wall1 = verticalWalls[0];
    const wall2 = verticalWalls[1];
    
    // 判斷哪個面朝向走廊（選擇較接近走廊中心的面）
    const centerY = roomCenter.y;
    const wall1FaceY = Math.abs(wall1.Face1.Y - centerY) < Math.abs(wall1.Face2.Y - centerY) 
        ? wall1.Face1.Y : wall1.Face2.Y;
    const wall2FaceY = Math.abs(wall2.Face1.Y - centerY) < Math.abs(wall2.Face2.Y - centerY) 
        ? wall2.Face1.Y : wall2.Face2.Y;
    
    const corridorWidth = Math.abs(wall1FaceY - wall2FaceY);
    
    console.log('[Step 4] 建立尺寸標註（使用牆體內表面）...');
    console.log(`   牆1 內表面 Y: ${wall1FaceY.toFixed(2)} mm`);
    console.log(`   牆2 內表面 Y: ${wall2FaceY.toFixed(2)} mm`);
    console.log(`   📏 走廊淨寬: ${corridorWidth.toFixed(2)} mm`);
    console.log('');
    console.log('   ✅ 使用 Wall Face (正確)');
    console.log('   ❌ 不使用 BoundingBox (錯誤)');
    
    // 建立尺寸標註（淨寬）
    ws.send(JSON.stringify({
        CommandName: 'create_dimension',
        Parameters: {
            viewId: viewId,
            startX: roomCenter.x,
            startY: Math.min(wall1FaceY, wall2FaceY),
            endX: roomCenter.x,
            endY: Math.max(wall1FaceY, wall2FaceY),
            offset: 1200  // 較近的標註線（淨寬）
        },
        RequestId: 'step4_' + Date.now()
    }));
}

// Step 5: 建立結構中心線尺寸標註（參考用）
function executeStep5() {
    currentStep = 5;
    
    const verticalWalls = walls.filter(w => w.Orientation === 'Vertical');
    verticalWalls.sort((a, b) => a.DistanceToCenter - b.DistanceToCenter);
    
    const wall1 = verticalWalls[0];
    const wall2 = verticalWalls[1];
    
    // 使用位置線（中心線）
    const wall1CenterY = wall1.ClosestPoint.Y;  // 或 LocationLine 的 Y
    const wall2CenterY = wall2.ClosestPoint.Y;
    
    console.log('[Step 5] 建立參考尺寸標註（結構中心線）...');
    console.log(`   牆1 中心線 Y: ${wall1CenterY.toFixed(2)} mm`);
    console.log(`   牆2 中心線 Y: ${wall2CenterY.toFixed(2)} mm`);
    
    ws.send(JSON.stringify({
        CommandName: 'create_dimension',
        Parameters: {
            viewId: viewId,
            startX: roomCenter.x,
            startY: Math.min(wall1CenterY, wall2CenterY),
            endX: roomCenter.x,
            endY: Math.max(wall1CenterY, wall2CenterY),
            offset: 2000  // 較遠的標註線（結構尺寸）
        },
        RequestId: 'step5_' + Date.now()
    }));
}

ws.on('message', function (data) {
    const response = JSON.parse(data.toString());
    
    if (!response.Success) {
        console.error(`❌ Step ${currentStep} 失敗:`, response.Error);
        ws.close();
        return;
    }
    
    switch (currentStep) {
        case 1:
            viewId = response.Data.ElementId;
            console.log(`   ✓ 視圖: ${response.Data.Name} (ID: ${viewId})\n`);
            executeStep2();
            break;
            
        case 2:
            roomCenter = {
                x: response.Data.CenterX,
                y: response.Data.CenterY
            };
            console.log(`   ✓ 中心點: (${roomCenter.x}, ${roomCenter.y})`);
            console.log(`   ℹ️  BoundingBox: MinY=${response.Data.BoundingBox.MinY}, MaxY=${response.Data.BoundingBox.MaxY}`);
            console.log(`   ℹ️  BoundingBox 寬度: ${response.Data.BoundingBox.MaxY - response.Data.BoundingBox.MinY} mm`);
            console.log(`   ⚠️  注意: 這個寬度不精確，僅供參考！\n`);
            executeStep3();
            break;
            
        case 3:
            walls = response.Data.Walls;
            console.log(`   ✓ 找到 ${walls.length} 面牆體`);
            
            // 顯示牆體資訊
            walls.forEach((wall, i) => {
                if (i < 3) {  // 只顯示前 3 面
                    console.log(`   - 牆 ${i+1}: ${wall.Name}, 距離=${wall.DistanceToCenter.toFixed(0)}mm, 方向=${wall.Orientation}`);
                }
            });
            console.log('');
            executeStep4();
            break;
            
        case 4:
            console.log(`   ✓ 淨寬標註已建立 (ID: ${response.Data.DimensionId})`);
            console.log(`   測量值: ${response.Data.Value} mm\n`);
            executeStep5();
            break;
            
        case 5:
            console.log(`   ✓ 結構中心線標註已建立 (ID: ${response.Data.DimensionId})`);
            console.log(`   測量值: ${response.Data.Value} mm\n`);
            
            console.log('=================================');
            console.log('✅ 所有步驟完成！');
            console.log('=================================');
            console.log('\n📌 重點總結:');
            console.log('1. BoundingBox 只用來找中心點，不用於尺寸');
            console.log('2. query_walls_by_location 是尺寸標註的關鍵');
            console.log('3. 使用 Wall Face 座標才是正確的淨寬');
            console.log('4. 兩條標註線：淨寬（法規）+ 中心線（參考）');
            
            ws.close();
            break;
    }
});

ws.on('error', function (error) {
    console.error('連線錯誤:', error.message);
    console.error('\n請確認:');
    console.error('1. Revit 已開啟 2FL 平面圖');
    console.error('2. MCP Plugin 服務已啟動');
});

ws.on('close', function () {
    process.exit(currentStep === 5 ? 0 : 1);
});

setTimeout(() => {
    console.log('\n⏱️  執行超時（30秒）');
    process.exit(1);
}, 30000);
