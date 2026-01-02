/**
 * 查詢 2FL 走廊附近的牆體
 */

import WebSocket from 'ws';

const ws = new WebSocket('ws://localhost:8964');

ws.on('open', function () {
    console.log('=== 查詢 2FL 走廊周圍牆體 ===');

    // 走廊中心點: (16394.8, 14334.22)
    const command = {
        CommandName: 'query_walls_by_location',
        Parameters: {
            x: 16394.8,
            y: 14334.22,
            searchRadius: 3000,  // 3 公尺搜尋半徑
            level: '2FL'
        },
        RequestId: 'query_corridor_walls_' + Date.now()
    };

    ws.send(JSON.stringify(command));
});

ws.on('message', function (data) {
    const response = JSON.parse(data.toString());

    if (response.Success) {
        console.log('\n找到', response.Data.Count, '面牆體');
        console.log('搜尋中心:', response.Data.SearchCenter);
        console.log('搜尋半徑:', response.Data.SearchRadius, 'mm');

        console.log('\n牆體列表:');
        response.Data.Walls.forEach((wall, index) => {
            console.log(`\n[${index + 1}] 牆 ID: ${wall.ElementId}`);
            console.log(`    名稱: ${wall.Name}`);
            console.log(`    類型: ${wall.WallType}`);
            console.log(`    厚度: ${wall.Thickness} mm`);
            console.log(`    長度: ${wall.Length} mm`);
            console.log(`    距離中心: ${wall.DistanceToCenter} mm`);
            console.log(`    方向: ${wall.Orientation}`);
            console.log(`    位置線: (${wall.LocationLine.StartX}, ${wall.LocationLine.StartY}) → (${wall.LocationLine.EndX}, ${wall.LocationLine.EndY})`);
            console.log(`    內側面1: (${wall.Face1.X}, ${wall.Face1.Y})`);
            console.log(`    內側面2: (${wall.Face2.X}, ${wall.Face2.Y})`);
        });

        // 找出垂直方向（與走廊長度平行的牆）
        const verticalWalls = response.Data.Walls.filter(w => w.Orientation === 'Vertical');
        console.log('\n=== 走廊兩側的垂直牆（可能） ===');
        
        if (verticalWalls.length >= 2) {
            // 按距離排序，取最近的兩面
            verticalWalls.sort((a, b) => a.DistanceToCenter - b.DistanceToCenter);
            
            console.log('\n最接近的兩面牆:');
            verticalWalls.slice(0, 2).forEach((wall, i) => {
                console.log(`\n牆 ${i + 1}: ID ${wall.ElementId}`);
                console.log(`  內側面1 Y座標: ${wall.Face1.Y}`);
                console.log(`  內側面2 Y座標: ${wall.Face2.Y}`);
            });

            // 計算淨寬（使用面向走廊的面）
            const wall1 = verticalWalls[0];
            const wall2 = verticalWalls[1];
            
            // 判斷哪個面朝向走廊
            const centerY = 14334.22;
            const wall1Face = Math.abs(wall1.Face1.Y - centerY) < Math.abs(wall1.Face2.Y - centerY) ? wall1.Face1.Y : wall1.Face2.Y;
            const wall2Face = Math.abs(wall2.Face1.Y - centerY) < Math.abs(wall2.Face2.Y - centerY) ? wall2.Face1.Y : wall2.Face2.Y;
            
            const corridorWidth = Math.abs(wall1Face - wall2Face);
            console.log(`\n\n🎯 計算的走廊淨寬: ${corridorWidth.toFixed(2)} mm`);
            console.log(`   牆1 內表面 Y: ${wall1Face.toFixed(2)}`);
            console.log(`   牆2 內表面 Y: ${wall2Face.toFixed(2)}`);
        }

    } else {
        console.log('查詢失敗:', response.Error);
    }

    ws.close();
});

ws.on('error', function (error) {
    console.error('連線錯誤:', error.message);
    console.error('請確認:');
    console.error('1. Revit 已開啟');
    console.error('2. MCP Plugin 已啟動服務（點擊「MCP 服務 開/關」按鈕）');
});

ws.on('close', function () {
    process.exit(0);
});

setTimeout(() => {
    console.log('\n⏱️  查詢超時（15秒）');
    process.exit(1);
}, 15000);
