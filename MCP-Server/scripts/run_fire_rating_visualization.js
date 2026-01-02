/**
 * 牆體防火防煙性能視覺化
 * 透過 WebSocket 直接連接 Revit MCP Server
 */

import WebSocket from 'ws';

const ws = new WebSocket('ws://localhost:8964');

// 顏色映射配置
const COLOR_MAP = {
    "2小時": { r: 0, g: 180, b: 0, transparency: 20, label: "🟢 2小時防火" },
    "1.5小時": { r: 100, g: 220, b: 100, transparency: 30, label: "🟢 1.5小時防火" },
    "1小時": { r: 255, g: 255, b: 0, transparency: 30, label: "🟡 1小時防火" },
    "0.5小時": { r: 255, g: 165, b: 0, transparency: 30, label: "🟠 0.5小時防火" },
    "無防火": { r: 100, g: 150, b: 255, transparency: 40, label: "🔵 無防火" },
    "未設定": { r: 200, g: 0, b: 200, transparency: 50, label: "🟣 未設定" }
};

const PARAMETER_NAMES = ["防火防煙性能", "防火時效", "Fire Rating", "FireRating", "防火性能"];

let currentView = null;
let allWalls = [];
let wallDataList = [];
let currentWallIndex = 0;
let distribution = {};
let stage = 'get_view';

function sendCommand(commandName, parameters) {
    const command = {
        CommandName: commandName,
        Parameters: parameters,
        RequestId: `${commandName}_${Date.now()}`
    };
    console.log(`[發送] ${commandName}`);
    ws.send(JSON.stringify(command));
}

function getColorForValue(value) {
    for (const [key, config] of Object.entries(COLOR_MAP)) {
        if (value && value.includes(key)) {
            return config;
        }
    }
    return COLOR_MAP["未設定"];
}

ws.on('open', function () {
    console.log('='.repeat(60));
    console.log('牆體防火防煙性能視覺化');
    console.log('='.repeat(60));
    console.log('\n步驟 1: 取得當前視圖...');
    sendCommand('get_active_view', {});
});

ws.on('message', function (data) {
    const response = JSON.parse(data.toString());

    if (!response.Success) {
        console.log('❌ 錯誤:', response.Error);
        ws.close();
        return;
    }

    switch (stage) {
        case 'get_view':
            currentView = response.Data;
            console.log(`✓ 當前視圖: ${currentView.Name} (ID: ${currentView.Id})`);

            console.log('\n步驟 2: 查詢所有牆體...');
            stage = 'get_walls';
            sendCommand('query_elements', { category: 'Walls', viewId: currentView.Id });
            break;

        case 'get_walls':
            allWalls = response.Data.Elements || [];
            console.log(`✓ 找到 ${allWalls.length} 面牆`);

            if (allWalls.length === 0) {
                console.log('❌ 當前視圖中沒有牆體');
                ws.close();
                return;
            }

            console.log('\n步驟 3: 分析防火防煙性能參數...');
            stage = 'get_wall_info';
            currentWallIndex = 0;
            sendCommand('get_element_info', { elementId: allWalls[currentWallIndex].ElementId });
            break;

        case 'get_wall_info':
            const wallInfo = response.Data;
            let fireRatingValue = "未設定";

            // 查找防火參數
            if (wallInfo.Parameters) {
                for (const paramName of PARAMETER_NAMES) {
                    const param = wallInfo.Parameters.find(p => p.Name === paramName);
                    if (param && param.Value) {
                        fireRatingValue = param.Value.trim();
                        break;
                    }
                }
            }

            wallDataList.push({
                elementId: allWalls[currentWallIndex].ElementId,
                name: wallInfo.Name || "未命名",
                fireRating: fireRatingValue
            });

            // 統計分布
            if (!distribution[fireRatingValue]) {
                distribution[fireRatingValue] = 0;
            }
            distribution[fireRatingValue]++;

            currentWallIndex++;
            if (currentWallIndex < allWalls.length) {
                // 繼續處理下一面牆
                if (currentWallIndex % 10 === 0) {
                    console.log(`  處理中... ${currentWallIndex}/${allWalls.length}`);
                }
                sendCommand('get_element_info', { elementId: allWalls[currentWallIndex].ElementId });
            } else {
                // 所有牆體分析完成
                console.log(`✓ 分析完成 ${allWalls.length} 面牆`);
                console.log('\n參數值分布:');
                for (const [value, count] of Object.entries(distribution)) {
                    const config = getColorForValue(value);
                    console.log(`  ${config.label}: ${count} 面`);
                }

                console.log('\n步驟 4: 應用顏色覆寫...');
                stage = 'apply_override';
                currentWallIndex = 0;
                applyNextOverride();
            }
            break;

        case 'apply_override':
            currentWallIndex++;
            if (currentWallIndex < wallDataList.length) {
                if (currentWallIndex % 10 === 0) {
                    console.log(`  覆寫中... ${currentWallIndex}/${wallDataList.length}`);
                }
                applyNextOverride();
            } else {
                // 所有覆寫完成
                console.log(`✓ 覆寫完成 ${wallDataList.length} 面牆`);
                printFinalReport();
                ws.close();
            }
            break;
    }
});

function applyNextOverride() {
    const wall = wallDataList[currentWallIndex];
    const colorConfig = getColorForValue(wall.fireRating);

    sendCommand('override_element_graphics', {
        elementId: wall.elementId,
        viewId: currentView.Id,
        surfaceFillColor: { r: colorConfig.r, g: colorConfig.g, b: colorConfig.b },
        transparency: colorConfig.transparency
    });
}

function printFinalReport() {
    console.log('\n' + '='.repeat(60));
    console.log('牆體防火防煙性能視覺化報告');
    console.log('='.repeat(60));

    console.log(`\n視圖: ${currentView.Name} (ID: ${currentView.Id})`);
    console.log(`總牆體數量: ${wallDataList.length} 面`);

    console.log('\n防火性能分布:');
    for (const [value, count] of Object.entries(distribution)) {
        const config = getColorForValue(value);
        const percentage = ((count / wallDataList.length) * 100).toFixed(1);
        console.log(`  ${config.label}: ${count} 面 (${percentage}%)`);
    }

    console.log('\n顏色映射表:');
    for (const [value, config] of Object.entries(COLOR_MAP)) {
        console.log(`  ${config.label}: RGB(${config.r}, ${config.g}, ${config.b}) 透明度 ${config.transparency}%`);
    }

    const allIds = wallDataList.map(w => w.elementId);
    console.log('\n清除顏色覆寫指令:');
    console.log(`node -e "...clear_element_override({ elementIds: [${allIds.slice(0, 5).join(', ')}...], viewId: ${currentView.Id} })"`);

    console.log('\n' + '='.repeat(60));
    console.log('✓ 執行完成！請檢查 Revit 視圖中的顏色標記。');
    console.log('='.repeat(60));
}

ws.on('error', function (error) {
    console.error('❌ 連線錯誤:', error.message);
    console.log('請確認 Revit 已啟動且 MCP 服務已開啟');
});

ws.on('close', function () {
    process.exit(0);
});

setTimeout(() => {
    console.log('⚠️ 執行超時');
    ws.close();
    process.exit(1);
}, 120000);
