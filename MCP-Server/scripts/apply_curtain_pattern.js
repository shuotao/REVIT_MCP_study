/**
 * 建立新類型並套用排列模式
 */

import WebSocket from 'ws';
import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

const SOCKET_URL = 'ws://localhost:8964';

async function sendCommand(ws, commandName, parameters = {}) {
    return new Promise((resolve, reject) => {
        const requestId = `req_${Date.now()}_${Math.random().toString(36).substr(2, 9)}`;
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
    console.log('🎨 建立新類型並套用排列模式');
    console.log('='.repeat(50));

    // 讀取設定檔
    const resultPath = path.join(__dirname, 'curtain_pattern_result.json');
    const config = JSON.parse(fs.readFileSync(resultPath, 'utf-8'));

    console.log(`📋 排列模式: ${config.pattern}`);
    console.log(`📐 Grid: ${config.gridConfig.columns} 列 x ${config.gridConfig.rows} 行`);
    console.log(`🎨 類型數量: ${Object.keys(config.typeMapping).length}\n`);

    const ws = new WebSocket(SOCKET_URL);

    ws.on('error', (err) => {
        console.error('❌ WebSocket 連線錯誤:', err.message);
        process.exit(1);
    });

    await new Promise((resolve) => ws.on('open', resolve));
    console.log('✅ 已連接到 Revit\n');

    try {
        // 步驟 1: 為每個類型建立新的 Panel Type
        console.log('📦 步驟 1: 建立新的 Panel Types...');
        const typeIdMapping = {};

        for (const [key, typeInfo] of Object.entries(config.typeMapping)) {
            console.log(`   建立 ${key}: ${typeInfo.name} (${typeInfo.color})...`);

            const result = await sendCommand(ws, 'create_curtain_panel_type', {
                typeName: typeInfo.name,
                color: typeInfo.color
            });

            typeIdMapping[key] = result.TypeId;
            console.log(`   ✅ 成功! Type ID: ${result.TypeId}, 材料: ${result.MaterialName}`);
        }

        console.log('\n📊 類型映射表:');
        for (const [key, typeId] of Object.entries(typeIdMapping)) {
            console.log(`   ${key} → ${typeId}`);
        }

        // 步驟 2: 套用排列模式
        console.log('\n🔧 步驟 2: 套用排列模式到帷幕牆...');

        const applyResult = await sendCommand(ws, 'apply_panel_pattern', {
            elementId: 316906,  // 帷幕牆的 Element ID
            typeMapping: typeIdMapping,
            matrix: config.matrix
        });

        console.log(`\n✅ 套用完成!`);
        console.log(`   總面板數: ${applyResult.TotalPanels}`);
        console.log(`   更改面板數: ${applyResult.ChangedPanels}`);

        if (applyResult.FailedCount > 0) {
            console.log(`   ⚠️ 失敗面板數: ${applyResult.FailedCount}`);
            console.log('   失敗原因:');
            applyResult.FailedPanels.slice(0, 5).forEach(fp => {
                console.log(`     - Panel ${fp.PanelId} [${fp.Row},${fp.Col}]: ${fp.Reason}`);
            });
        }

        console.log(`\n${applyResult.Message}`);

    } catch (err) {
        console.error('❌ 錯誤:', err.message);
    } finally {
        ws.close();
    }
}

main();
