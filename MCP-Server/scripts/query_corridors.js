/**
 * 查詢所有走廊房間及其相關牆體防火資訊
 */

import WebSocket from 'ws';

const ws = new WebSocket('ws://localhost:8964');

ws.on('open', function () {
    console.log('=== 查詢走廊及防火規範資訊 ===\n');

    // 先查詢所有房間
    const command = {
        CommandName: 'get_rooms',
        Parameters: {},
        RequestId: 'get_rooms_' + Date.now()
    };

    ws.send(JSON.stringify(command));
});

let step = 1;
let corridors = [];

ws.on('message', function (data) {
    const response = JSON.parse(data.toString());

    if (step === 1) {
        // 處理房間查詢結果
        if (response.Success && response.Data && response.Data.Rooms) {
            console.log('找到', response.Data.Rooms.length, '個房間\n');

            // 篩選走廊
            corridors = response.Data.Rooms.filter(room =>
                room.Name && (
                    room.Name.includes('走廊') ||
                    room.Name.toLowerCase().includes('corridor') ||
                    room.Name.includes('廊道')
                )
            );

            console.log('=== 走廊列表 ===');
            if (corridors.length > 0) {
                corridors.forEach((room, index) => {
                    console.log(`\n[${index + 1}] ${room.Name}`);
                    console.log(`    ID: ${room.ElementId}`);
                    console.log(`    樓層: ${room.Level || 'N/A'}`);
                    console.log(`    面積: ${room.Area ? (room.Area / 1000000).toFixed(2) + ' m²' : 'N/A'}`);
                    if (room.BoundingBox) {
                        console.log(`    邊界: (${room.BoundingBox.MinX?.toFixed(0)}, ${room.BoundingBox.MinY?.toFixed(0)}) - (${room.BoundingBox.MaxX?.toFixed(0)}, ${room.BoundingBox.MaxY?.toFixed(0)})`);
                    }
                });

                // 查詢第一個走廊的邊界牆
                step = 2;
                const firstCorridor = corridors[0];
                console.log('\n\n=== 查詢「' + firstCorridor.Name + '」周圍牆體防火資訊 ===\n');

                const wallCommand = {
                    CommandName: 'get_room_boundaries',
                    Parameters: {
                        roomId: firstCorridor.ElementId
                    },
                    RequestId: 'get_boundaries_' + Date.now()
                };
                ws.send(JSON.stringify(wallCommand));
            } else {
                console.log('未找到走廊房間');

                // 列出所有房間名稱供參考
                console.log('\n所有房間名稱:');
                response.Data.Rooms.forEach(room => {
                    console.log(`  - ${room.Name} (Level: ${room.Level || 'N/A'})`);
                });
                ws.close();
            }
        } else {
            console.log('查詢房間失敗:', response.Error || '未知錯誤');
            ws.close();
        }
    } else if (step === 2) {
        // 處理邊界牆查詢結果
        if (response.Success && response.Data) {
            console.log('找到邊界元素:');

            if (response.Data.Boundaries && response.Data.Boundaries.length > 0) {
                console.log('\n=== 邊界牆防火資訊 ===');
                response.Data.Boundaries.forEach((boundary, index) => {
                    console.log(`\n[${index + 1}] ${boundary.Name || 'Wall'}`);
                    console.log(`    ID: ${boundary.ElementId}`);
                    console.log(`    類型: ${boundary.Category || boundary.WallType || 'N/A'}`);

                    // 防火相關參數
                    if (boundary.FireRating) {
                        console.log(`    🔥 防火時效: ${boundary.FireRating}`);
                    }
                    if (boundary.Parameters) {
                        const fireParam = boundary.Parameters.find(p =>
                            p.Name && (
                                p.Name.includes('防火') ||
                                p.Name.includes('Fire') ||
                                p.Name.includes('防煙')
                            )
                        );
                        if (fireParam) {
                            console.log(`    🔥 ${fireParam.Name}: ${fireParam.Value}`);
                        }
                    }
                });
            }

            if (response.Data.Walls && response.Data.Walls.length > 0) {
                console.log('\n=== 牆體詳細資訊 ===');
                response.Data.Walls.forEach((wall, index) => {
                    console.log(`\n[${index + 1}] ${wall.Name || wall.WallType || 'Wall'}`);
                    console.log(`    ID: ${wall.ElementId}`);
                    console.log(`    厚度: ${wall.Thickness ? wall.Thickness + ' mm' : 'N/A'}`);
                    console.log(`    長度: ${wall.Length ? wall.Length + ' mm' : 'N/A'}`);
                    if (wall.FireRating) {
                        console.log(`    🔥 防火時效: ${wall.FireRating}`);
                    }
                });
            }
        } else {
            console.log('查詢邊界失敗:', response.Error || '嘗試其他方法...');

            // 嘗試直接查詢牆體
            step = 3;
            const queryCommand = {
                CommandName: 'query_elements',
                Parameters: {
                    category: 'Walls',
                    includeParameters: true
                },
                RequestId: 'query_walls_' + Date.now()
            };
            ws.send(JSON.stringify(queryCommand));
        }
    } else if (step === 3) {
        // 處理牆體查詢結果
        if (response.Success && response.Data) {
            console.log('\n=== 所有牆體防火資訊 ===');
            const walls = response.Data.Elements || response.Data.Walls || [];

            walls.forEach((wall, index) => {
                // 檢查是否有防火相關參數
                let fireInfo = 'N/A';
                if (wall.Parameters) {
                    for (const param of wall.Parameters) {
                        if (param.Name && (
                            param.Name.includes('防火') ||
                            param.Name.includes('Fire') ||
                            param.Name.includes('防煙') ||
                            param.Name.includes('s_CW')
                        )) {
                            fireInfo = `${param.Name}: ${param.Value}`;
                            break;
                        }
                    }
                }

                if (fireInfo !== 'N/A' || index < 10) {
                    console.log(`\n[${wall.ElementId}] ${wall.Name || wall.WallType || 'Wall'}`);
                    console.log(`    🔥 防火資訊: ${fireInfo}`);
                }
            });
        }
        ws.close();
    }
});

ws.on('error', function (error) {
    console.error('連線錯誤:', error.message);
    console.error('\n請確認:');
    console.error('1. Revit 已開啟並載入專案');
    console.error('2. 已點擊 Add-ins > MCP Tools > 「MCP 服務 (開/關)」啟動服務');
});

ws.on('close', function () {
    process.exit(0);
});

setTimeout(() => {
    console.log('\n⏱️  查詢超時（30秒）');
    process.exit(1);
}, 30000);
