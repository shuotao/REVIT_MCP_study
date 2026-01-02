/**
 * 查詢走廊尺寸及防火規範資訊 (支援日文命名)
 */

import WebSocket from 'ws';

const ws = new WebSocket('ws://localhost:8964');

ws.on('open', function () {
    console.log('=== 查詢走廊尺寸及防火規範資訊 ===\n');

    // 取得樓層列表
    const command = {
        CommandName: 'get_all_levels',
        Parameters: {},
        RequestId: 'get_levels_' + Date.now()
    };

    ws.send(JSON.stringify(command));
});

let step = 1;
let levels = [];
let selectedLevel = '1FL';
let corridorIds = [];

ws.on('message', function (data) {
    const response = JSON.parse(data.toString());

    if (step === 1) {
        // 處理樓層列表
        if (response.Success && response.Data) {
            levels = response.Data.Levels || response.Data;
            console.log('樓層列表:');
            levels.forEach(level => {
                console.log(`  ${level.Name} (標高: ${level.Elevation} mm)`);
            });

            step = 2;
            const roomsCommand = {
                CommandName: 'get_rooms_by_level',
                Parameters: {
                    level: selectedLevel,
                    includeUnnamed: true
                },
                RequestId: 'get_rooms_' + Date.now()
            };
            ws.send(JSON.stringify(roomsCommand));
        } else {
            console.log('查詢樓層失敗:', response.Error);
            ws.close();
        }
    } else if (step === 2) {
        // 處理房間列表
        if (response.Success && response.Data) {
            const rooms = response.Data.Rooms || response.Data;
            console.log(`\n${selectedLevel} 找到 ${rooms.length} 個房間\n`);

            // 篩選走廊 (包含日文命名)
            const corridors = rooms.filter(room =>
                room.Name && (
                    room.Name.includes('走廊') ||
                    room.Name.toLowerCase().includes('corridor') ||
                    room.Name.includes('廊道') ||
                    room.Name.includes('通道') ||
                    room.Name.includes('廊下') ||  // 日文: 走廊
                    room.Name.includes('廊')      // 通用
                )
            );

            console.log('=== 走廊列表 ===');
            if (corridors.length > 0) {
                corridors.forEach((room, index) => {
                    console.log(`\n[${index + 1}] ${room.Name}`);
                    console.log(`    ID: ${room.ElementId}`);
                    console.log(`    面積: ${room.Area ? (room.Area / 1e6).toFixed(2) + ' m²' : 'N/A'}`);
                    corridorIds.push(room.ElementId);
                });

                // 查詢第一個走廊的詳細資訊
                step = 3;
                console.log('\n\n=== 查詢「' + corridors[0].Name + '」詳細資訊 ===\n');

                const roomInfoCommand = {
                    CommandName: 'get_room_info',
                    Parameters: {
                        roomId: corridors[0].ElementId
                    },
                    RequestId: 'get_room_info_' + Date.now()
                };
                ws.send(JSON.stringify(roomInfoCommand));
            } else {
                console.log('未找到走廊房間');
                ws.close();
            }
        } else {
            console.log('查詢房間失敗:', response.Error);
            ws.close();
        }
    } else if (step === 3) {
        // 處理房間詳細資訊
        if (response.Success && response.Data) {
            const room = response.Data;
            console.log('房間名稱:', room.Name);
            console.log('面積:', room.Area ? (room.Area / 1e6).toFixed(2) + ' m²' : 'N/A');

            if (room.BoundingBox) {
                console.log('\n邊界盒:');
                console.log(`  Min: (${room.BoundingBox.MinX?.toFixed(0)}, ${room.BoundingBox.MinY?.toFixed(0)})`);
                console.log(`  Max: (${room.BoundingBox.MaxX?.toFixed(0)}, ${room.BoundingBox.MaxY?.toFixed(0)})`);

                const width = Math.abs(room.BoundingBox.MaxY - room.BoundingBox.MinY);
                const length = Math.abs(room.BoundingBox.MaxX - room.BoundingBox.MinX);
                const minDim = Math.min(width, length);
                const maxDim = Math.max(width, length);

                console.log(`\n📐 估算尺寸:`);
                console.log(`   寬度: ${minDim.toFixed(0)} mm (${(minDim / 1000).toFixed(2)} m)`);
                console.log(`   長度: ${maxDim.toFixed(0)} mm (${(maxDim / 1000).toFixed(2)} m)`);

                // 防火規範檢查
                console.log('\n\n🔥 === 防火規範檢查 ===');
                console.log('\n【建築技術規則 第93條】走廊淨寬規定:');

                if (minDim >= 1600) {
                    console.log(`✅ 淨寬 ${(minDim / 1000).toFixed(2)}m >= 1.6m`);
                    console.log('   → 符合醫院、療養院走廊規定');
                } else if (minDim >= 1200) {
                    console.log(`✅ 淨寬 ${(minDim / 1000).toFixed(2)}m >= 1.2m`);
                    console.log('   → 符合一般建築物走廊規定');
                    console.log('   → 不足醫院/療養院規定 (需1.6m)');
                } else {
                    console.log(`❌ 淨寬 ${(minDim / 1000).toFixed(2)}m < 1.2m`);
                    console.log('   → 不符合建築技術規則第93條');
                    console.log('   → 需加寬至少至 1200mm');
                }

                // 查詢周圍牆體
                step = 4;
                const centerX = (room.BoundingBox.MaxX + room.BoundingBox.MinX) / 2;
                const centerY = (room.BoundingBox.MaxY + room.BoundingBox.MinY) / 2;

                console.log('\n\n=== 查詢周圍牆體防火資訊 ===');
                console.log(`搜尋中心: (${centerX.toFixed(0)}, ${centerY.toFixed(0)})\n`);

                const wallCommand = {
                    CommandName: 'query_walls_by_location',
                    Parameters: {
                        x: centerX,
                        y: centerY,
                        searchRadius: 5000,
                        level: selectedLevel
                    },
                    RequestId: 'query_walls_' + Date.now()
                };
                ws.send(JSON.stringify(wallCommand));
            } else {
                // 如果沒有邊界盒，直接查詢牆體
                step = 4;
                const wallCommand = {
                    CommandName: 'query_elements',
                    Parameters: {
                        category: 'Walls',
                        level: selectedLevel
                    },
                    RequestId: 'query_walls_' + Date.now()
                };
                ws.send(JSON.stringify(wallCommand));
            }
        } else {
            console.log('查詢房間資訊失敗:', response.Error);
            // 直接查詢牆體參數
            step = 4;
            const wallCommand = {
                CommandName: 'query_elements',
                Parameters: {
                    category: 'Walls'
                },
                RequestId: 'query_walls_' + Date.now()
            };
            ws.send(JSON.stringify(wallCommand));
        }
    } else if (step === 4) {
        // 處理牆體查詢結果
        if (response.Success && response.Data) {
            const walls = response.Data.Walls || response.Data.Elements || [];
            console.log('找到', walls.length, '面牆體');

            // 統計防火等級
            const fireRatings = {};

            console.log('\n=== 牆體防火性能分析 ===\n');

            walls.slice(0, 10).forEach((wall, index) => {
                console.log(`[${index + 1}] ${wall.Name || wall.WallType || 'Wall'} (ID: ${wall.ElementId})`);
                console.log(`    厚度: ${wall.Thickness || 'N/A'} mm`);

                // 查找防火相關參數
                let fireRating = null;
                if (wall.Parameters) {
                    for (const param of wall.Parameters) {
                        if (param.Name && (
                            param.Name.includes('防火') ||
                            param.Name.includes('Fire') ||
                            param.Name.includes('防煙') ||
                            param.Name.includes('s_CW_防火')
                        )) {
                            fireRating = param.Value;
                            console.log(`    🔥 ${param.Name}: ${param.Value}`);
                        }
                    }
                }

                if (wall.FireRating) {
                    fireRating = wall.FireRating;
                    console.log(`    🔥 防火時效: ${wall.FireRating}`);
                }

                // 統計
                if (fireRating) {
                    fireRatings[fireRating] = (fireRatings[fireRating] || 0) + 1;
                }
            });

            if (walls.length > 10) {
                console.log(`\n... 還有 ${walls.length - 10} 面牆體 ...`);
            }

            // 顯示防火等級統計
            const ratingKeys = Object.keys(fireRatings);
            if (ratingKeys.length > 0) {
                console.log('\n=== 防火等級統計 ===');
                ratingKeys.forEach(rating => {
                    console.log(`  ${rating}: ${fireRatings[rating]} 面牆`);
                });
            }

            // 防火規範說明
            console.log('\n\n📋 === 走廊防火規範參考 ===');
            console.log('\n【建築技術規則 第79條】防火區劃:');
            console.log('  - 走廊與居室之間應以防火門窗區隔');
            console.log('  - 防火時效至少 1 小時');
            console.log('\n【建築技術規則 第93條】走廊淨寬:');
            console.log('  - 一般建築物: ≥ 1.2m');
            console.log('  - 醫院/療養院: ≥ 1.6m');
            console.log('  - 兩側有居室: ≥ 1.6m');
            console.log('\n【消防法】避難走廊:');
            console.log('  - 應設置緊急照明');
            console.log('  - 應設置避難方向指示');
        } else {
            console.log('查詢牆體失敗:', response.Error);
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
