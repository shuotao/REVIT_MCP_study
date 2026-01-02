/**
 * 在目前視圖建立走廊尺寸標註
 * 自動偵測目前視圖的樓層，查詢走廊並建立標註
 */

import WebSocket from 'ws';

const ws = new WebSocket('ws://localhost:8964');

let step = 1;
let activeViewId = null;
let currentLevel = null;
let corridors = [];

ws.on('open', function () {
    console.log('=== 在目前視圖建立走廊尺寸標註 ===\n');

    // Step 1: 取得目前視圖
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
        // 處理視圖資訊
        if (response.Success && response.Data) {
            activeViewId = response.Data.ViewId || response.Data.ElementId;
            currentLevel = response.Data.LevelName || response.Data.Level || '3FL';

            console.log(`📍 目前視圖: ${response.Data.Name}`);
            console.log(`   視圖 ID: ${activeViewId}`);
            console.log(`   視圖類型: ${response.Data.ViewType}`);
            console.log(`   樓層: ${currentLevel}`);

            // Step 2: 查詢該樓層的房間
            step = 2;
            console.log(`\n--- 查詢 ${currentLevel} 樓層的走廊 ---\n`);

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
            console.log('取得視圖失敗:', response.Error);
            ws.close();
        }
    } else if (step === 2) {
        // 處理房間列表
        if (response.Success && response.Data) {
            const rooms = response.Data.Rooms || response.Data;
            console.log(`找到 ${rooms.length} 個房間`);

            // 篩選走廊
            corridors = rooms.filter(room =>
                room.Name && (
                    room.Name.includes('走廊') ||
                    room.Name.toLowerCase().includes('corridor') ||
                    room.Name.includes('廊下') ||
                    room.Name.includes('廊')
                )
            );

            if (corridors.length > 0) {
                console.log(`\n找到 ${corridors.length} 個走廊:`);
                corridors.forEach((c, i) => {
                    console.log(`  [${i + 1}] ${c.Name} (ID: ${c.ElementId})`);
                });

                // 查詢第一個走廊的詳細資訊
                step = 3;
                console.log(`\n--- 取得「${corridors[0].Name}」詳細資訊 ---`);

                const roomInfoCommand = {
                    CommandName: 'get_room_info',
                    Parameters: {
                        roomId: corridors[0].ElementId
                    },
                    RequestId: 'get_room_' + Date.now()
                };
                ws.send(JSON.stringify(roomInfoCommand));
            } else {
                console.log('\n❌ 該樓層沒有找到走廊');
                console.log('所有房間:');
                rooms.forEach(r => console.log(`  - ${r.Name || '(未命名)'}`));
                ws.close();
            }
        } else {
            console.log('查詢房間失敗:', response.Error);
            ws.close();
        }
    } else if (step === 3) {
        // 處理房間詳細資訊
        let boundingBox = null;

        if (response.Success && response.Data && response.Data.BoundingBox) {
            boundingBox = response.Data.BoundingBox;
            console.log(`\n邊界盒:`);
            console.log(`  Min: (${boundingBox.MinX?.toFixed(0)}, ${boundingBox.MinY?.toFixed(0)})`);
            console.log(`  Max: (${boundingBox.MaxX?.toFixed(0)}, ${boundingBox.MaxY?.toFixed(0)})`);
        } else {
            // 如果沒有邊界盒，使用預設座標
            console.log('⚠️ 無法取得邊界盒，嘗試使用查詢牆體...');
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

        // 建立尺寸標註
        if (boundingBox) {
            const width = Math.abs(boundingBox.MaxY - boundingBox.MinY);
            const length = Math.abs(boundingBox.MaxX - boundingBox.MinX);

            console.log(`\n📐 走廊尺寸:`);
            console.log(`   寬度: ${width.toFixed(0)} mm (${(width / 1000).toFixed(2)} m)`);
            console.log(`   長度: ${length.toFixed(0)} mm (${(length / 1000).toFixed(2)} m)`);

            // Step 4: 建立寬度標註
            step = 4;
            console.log('\n--- 建立寬度標註 ---');

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

            // 儲存邊界盒供後續使用
            ws.boundingBox = boundingBox;
            ws.send(JSON.stringify(widthDimCommand));
        }
    } else if (step === 4) {
        // 處理寬度標註結果
        if (response.Success) {
            console.log('✅ 寬度標註建立成功！', response.Data?.DimensionId ? `ID: ${response.Data.DimensionId}` : '');
        } else {
            console.log('❌ 寬度標註失敗:', response.Error);
        }

        // Step 5: 建立長度標註
        if (ws.boundingBox) {
            step = 5;
            console.log('\n--- 建立長度標註 ---');

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
        // 處理長度標註結果
        if (response.Success) {
            console.log('✅ 長度標註建立成功！', response.Data?.DimensionId ? `ID: ${response.Data.DimensionId}` : '');
        } else {
            console.log('❌ 長度標註失敗:', response.Error);
        }

        // 完成
        console.log('\n=== 標註完成 ===');
        console.log('\n💡 請在 Revit 視圖中查看新建立的尺寸標註');

        // 防火規範提醒
        const width = Math.abs(ws.boundingBox.MaxY - ws.boundingBox.MinY);
        console.log('\n🔥 防火規範檢查:');
        if (width >= 1600) {
            console.log(`   ✅ 走廊淨寬 ${(width / 1000).toFixed(2)}m ≥ 1.6m (符合醫院/療養院規定)`);
        } else if (width >= 1200) {
            console.log(`   ✅ 走廊淨寬 ${(width / 1000).toFixed(2)}m ≥ 1.2m (符合一般建築物規定)`);
        } else {
            console.log(`   ❌ 走廊淨寬 ${(width / 1000).toFixed(2)}m < 1.2m (不符合規定)`);
        }

        ws.close();
    }
});

ws.on('error', function (error) {
    console.error('連線錯誤:', error.message);
    console.error('\n請確認 Revit MCP 服務已啟動');
});

ws.on('close', function () {
    process.exit(0);
});

setTimeout(() => {
    console.log('\n⏱️  操作超時（30秒）');
    process.exit(1);
}, 30000);
