/**
 * 帷幕牆面板排列測試腳本
 * 
 * 使用方式：
 * 1. 在 Revit 中選取一個帷幕牆
 * 2. 執行此腳本：node scratch/test_curtain_wall.js
 */

import WebSocket from 'ws';

const SOCKET_URL = 'ws://localhost:8964';

async function sendCommand(ws, commandName, parameters = {}) {
    return new Promise((resolve, reject) => {
        const requestId = `req_${Date.now()}`;
        const command = {
            CommandName: commandName,
            Parameters: parameters,
            RequestId: requestId
        };

        const timeout = setTimeout(() => {
            reject(new Error('命令執行逾時'));
        }, 30000);

        const handler = (message) => {
            try {
                const response = JSON.parse(message.toString());
                if (response.RequestId === requestId) {
                    clearTimeout(timeout);
                    ws.off('message', handler);
                    if (response.Success) {
                        resolve(response.Data);
                    } else {
                        reject(new Error(response.Error || '命令執行失敗'));
                    }
                }
            } catch (err) {
                // 忽略非 JSON 訊息
            }
        };

        ws.on('message', handler);
        ws.send(JSON.stringify(command));
    });
}

async function main() {
    console.log('🏢 帷幕牆面板排列測試');
    console.log('='.repeat(50));

    const ws = new WebSocket(SOCKET_URL);

    ws.on('error', (err) => {
        console.error('❌ WebSocket 連線錯誤:', err.message);
        console.log('請確認 Revit 已開啟並載入 RevitMCP Add-in');
        process.exit(1);
    });

    await new Promise((resolve) => ws.on('open', resolve));
    console.log('✅ 已連接到 Revit\n');

    try {
        // 1. 取得帷幕牆資訊
        console.log('📋 取得帷幕牆資訊...');
        const wallInfo = await sendCommand(ws, 'get_curtain_wall_info');
        console.log(`   Element ID: ${wallInfo.ElementId}`);
        console.log(`   牆類型: ${wallInfo.WallType}`);
        console.log(`   Grid: ${wallInfo.Columns} 列 x ${wallInfo.Rows} 行`);
        console.log(`   面板尺寸: ${wallInfo.PanelWidth}mm x ${wallInfo.PanelHeight}mm`);
        console.log(`   總面板數: ${wallInfo.TotalPanels}`);
        console.log(`   現有面板類型:`);
        wallInfo.PanelTypes.forEach(pt => {
            console.log(`     - ${pt.TypeName} (ID: ${pt.TypeId}): ${pt.Count} 個`);
        });
        console.log();

        // 2. 取得可用的面板類型
        console.log('🎨 取得可用面板類型...');
        const panelTypes = await sendCommand(ws, 'get_curtain_panel_types');
        console.log(`   共 ${panelTypes.Count} 種面板類型:`);
        panelTypes.PanelTypes.slice(0, 10).forEach(pt => {
            console.log(`     - ${pt.TypeName} (${pt.Family}) ID: ${pt.TypeId}`);
        });
        if (panelTypes.Count > 10) {
            console.log(`     ... 還有 ${panelTypes.Count - 10} 種`);
        }
        console.log();

        // 輸出 JSON 供預覽工具使用
        const previewData = {
            elementId: wallInfo.ElementId,
            columns: wallInfo.Columns,
            rows: wallInfo.Rows,
            panelWidth: wallInfo.PanelWidth,
            panelHeight: wallInfo.PanelHeight,
            panelTypes: wallInfo.PanelTypes.map((pt, i) => ({
                id: String.fromCharCode(65 + i),
                name: pt.TypeName,
                color: pt.MaterialColor || ['#5C4033', '#C0C0C0', '#6082B6', '#DEB887'][i % 4],
                revitTypeId: pt.TypeId,
                materialName: pt.MaterialName
            })),
            revitPanelTypes: wallInfo.PanelTypes.map(pt => ({
                TypeId: pt.TypeId,
                TypeName: pt.TypeName,
                MaterialName: pt.MaterialName,
                MaterialColor: pt.MaterialColor,
                Transparency: pt.Transparency,
                Count: pt.Count
            }))
        };

        console.log('📦 預覽工具資料:');
        console.log(JSON.stringify(previewData, null, 2));

    } catch (err) {
        console.error('❌ 錯誤:', err.message);
    } finally {
        ws.close();
    }
}

main();
