/**
 * 為走廊建立尺寸標註
 * 根據走廊邊界盒座標建立寬度和長度的尺寸標註
 */

import WebSocket from 'ws';

const ws = new WebSocket('ws://localhost:8964');

// 廊下1 的邊界盒資訊（從上次查詢結果）
const corridor = {
    name: '廊下1',
    elementId: 52936651,
    boundingBox: {
        minX: -970,
        minY: 13675,
        maxX: 4425,
        maxY: 18925
    }
};

let step = 1;
let activeViewId = null;

ws.on('open', function () {
    console.log('=== 為走廊建立尺寸標註 ===\n');
    console.log(`目標走廊: ${corridor.name} (ID: ${corridor.elementId})`);
    console.log(`邊界盒: (${corridor.boundingBox.minX}, ${corridor.boundingBox.minY}) - (${corridor.boundingBox.maxX}, ${corridor.boundingBox.maxY})`);

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
            console.log(`\n目前視圖: ${response.Data.Name} (ID: ${activeViewId})`);
            console.log(`視圖類型: ${response.Data.ViewType}`);

            // Step 2: 建立寬度標註 (Y 方向)
            step = 2;
            console.log('\n--- 建立寬度標註 (Y 方向) ---');

            // 在走廊左側建立寬度標註
            const widthDimCommand = {
                CommandName: 'create_dimension',
                Parameters: {
                    viewId: activeViewId,
                    startX: corridor.boundingBox.minX - 500,  // 偏移到走廊左邊
                    startY: corridor.boundingBox.minY,
                    endX: corridor.boundingBox.minX - 500,
                    endY: corridor.boundingBox.maxY,
                    offset: 800  // 標註線偏移量
                },
                RequestId: 'dim_width_' + Date.now()
            };

            console.log(`起點: (${widthDimCommand.Parameters.startX}, ${widthDimCommand.Parameters.startY})`);
            console.log(`終點: (${widthDimCommand.Parameters.endX}, ${widthDimCommand.Parameters.endY})`);
            console.log(`預期尺寸: ${Math.abs(corridor.boundingBox.maxY - corridor.boundingBox.minY)} mm`);

            ws.send(JSON.stringify(widthDimCommand));
        } else {
            console.log('取得視圖失敗:', response.Error);
            ws.close();
        }
    } else if (step === 2) {
        // 處理寬度標註結果
        if (response.Success) {
            console.log('✅ 寬度標註建立成功！', response.Data?.DimensionId ? `ID: ${response.Data.DimensionId}` : '');
        } else {
            console.log('❌ 寬度標註建立失敗:', response.Error);
        }

        // Step 3: 建立長度標註 (X 方向)
        step = 3;
        console.log('\n--- 建立長度標註 (X 方向) ---');

        // 在走廊下方建立長度標註
        const lengthDimCommand = {
            CommandName: 'create_dimension',
            Parameters: {
                viewId: activeViewId,
                startX: corridor.boundingBox.minX,
                startY: corridor.boundingBox.minY - 500,  // 偏移到走廊下方
                endX: corridor.boundingBox.maxX,
                endY: corridor.boundingBox.minY - 500,
                offset: 800
            },
            RequestId: 'dim_length_' + Date.now()
        };

        console.log(`起點: (${lengthDimCommand.Parameters.startX}, ${lengthDimCommand.Parameters.startY})`);
        console.log(`終點: (${lengthDimCommand.Parameters.endX}, ${lengthDimCommand.Parameters.endY})`);
        console.log(`預期尺寸: ${Math.abs(corridor.boundingBox.maxX - corridor.boundingBox.minX)} mm`);

        ws.send(JSON.stringify(lengthDimCommand));
    } else if (step === 3) {
        // 處理長度標註結果
        if (response.Success) {
            console.log('✅ 長度標註建立成功！', response.Data?.DimensionId ? `ID: ${response.Data.DimensionId}` : '');
        } else {
            console.log('❌ 長度標註建立失敗:', response.Error);
        }

        // Step 4: 選取走廊元素以便檢視
        step = 4;
        console.log('\n--- 選取並縮放至走廊 ---');

        const zoomCommand = {
            CommandName: 'zoom_to_element',
            Parameters: {
                elementId: corridor.elementId
            },
            RequestId: 'zoom_' + Date.now()
        };
        ws.send(JSON.stringify(zoomCommand));
    } else if (step === 4) {
        // 處理縮放結果
        if (response.Success) {
            console.log('✅ 已縮放至走廊位置');
        } else {
            console.log('⚠️ 縮放失敗:', response.Error);
        }

        console.log('\n=== 標註完成 ===');
        console.log('\n📐 走廊尺寸摘要:');
        console.log(`   寬度: ${Math.abs(corridor.boundingBox.maxY - corridor.boundingBox.minY)} mm (${(Math.abs(corridor.boundingBox.maxY - corridor.boundingBox.minY) / 1000).toFixed(2)} m)`);
        console.log(`   長度: ${Math.abs(corridor.boundingBox.maxX - corridor.boundingBox.minX)} mm (${(Math.abs(corridor.boundingBox.maxX - corridor.boundingBox.minX) / 1000).toFixed(2)} m)`);
        console.log('\n💡 請在 Revit 中查看新建立的尺寸標註');

        ws.close();
    }
});

ws.on('error', function (error) {
    console.error('連線錯誤:', error.message);
    console.error('\n請確認:');
    console.error('1. Revit 已開啟並載入專案');
    console.error('2. 已點擊 Add-ins > MCP Tools > 「MCP 服務 (開/關)」啟動服務');
    console.error('3. 目前視圖為 1FL 平面圖');
});

ws.on('close', function () {
    process.exit(0);
});

setTimeout(() => {
    console.log('\n⏱️  操作超時（30秒）');
    process.exit(1);
}, 30000);
