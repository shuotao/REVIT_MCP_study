/**
 * å»ºç??°é??‹ä¸¦å¥—ç”¨?’å?æ¨¡å?
 */

import WebSocket from 'ws';
import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

const SOCKET_URL = 'ws://localhost:11111';

async function sendCommand(ws, commandName, parameters = {}) {
    return new Promise((resolve, reject) => {
        const requestId = `req_${Date.now()}_${Math.random().toString(36).substr(2, 9)}`;
        const command = {
            CommandName: commandName,
            Parameters: parameters,
            RequestId: requestId
        };

        const timeout = setTimeout(() => {
            reject(new Error('?½ä»¤?·è??¾æ?'));
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
                        reject(new Error(response.Error || '?½ä»¤?·è?å¤±æ?'));
                    }
                }
            } catch (err) {
                // å¿½ç•¥??JSON è¨Šæ¯
            }
        };

        ws.on('message', handler);
        ws.send(JSON.stringify(command));
    });
}

async function main() {
    console.log('?Ž¨ å»ºç??°é??‹ä¸¦å¥—ç”¨?’å?æ¨¡å?');
    console.log('='.repeat(50));

    // è®€?–è¨­å®šæ?
    const resultPath = path.join(__dirname, 'curtain_pattern_result.json');
    const config = JSON.parse(fs.readFileSync(resultPath, 'utf-8'));

    console.log(`?? ?’å?æ¨¡å?: ${config.pattern}`);
    console.log(`?? Grid: ${config.gridConfig.columns} ??x ${config.gridConfig.rows} è¡Œ`);
    console.log(`?Ž¨ é¡žå??¸é?: ${Object.keys(config.typeMapping).length}\n`);

    const ws = new WebSocket(SOCKET_URL);

    ws.on('error', (err) => {
        console.error('??WebSocket ????¯èª¤:', err.message);
        process.exit(1);
    });

    await new Promise((resolve) => ws.on('open', resolve));
    console.log('??å·²é€?Ž¥??Revit\n');

    try {
        // æ­¥é? 1: ?ºæ??‹é??‹å»ºç«‹æ–°??Panel Type
        console.log('?“¦ æ­¥é? 1: å»ºç??°ç? Panel Types...');
        const typeIdMapping = {};

        for (const [key, typeInfo] of Object.entries(config.typeMapping)) {
            console.log(`   å»ºç? ${key}: ${typeInfo.name} (${typeInfo.color})...`);

            const result = await sendCommand(ws, 'create_curtain_panel_type', {
                typeName: typeInfo.name,
                color: typeInfo.color
            });

            typeIdMapping[key] = result.TypeId;
            console.log(`   ???å?! Type ID: ${result.TypeId}, ?æ?: ${result.MaterialName}`);
        }

        console.log('\n?? é¡žå?? å?è¡?');
        for (const [key, typeId] of Object.entries(typeIdMapping)) {
            console.log(`   ${key} ??${typeId}`);
        }

        // æ­¥é? 2: å¥—ç”¨?’å?æ¨¡å?
        console.log('\n?”§ æ­¥é? 2: å¥—ç”¨?’å?æ¨¡å??°å¸·å¹•ç?...');

        const applyResult = await sendCommand(ws, 'apply_panel_pattern', {
            elementId: 316906,  // å¸·å??†ç? Element ID
            typeMapping: typeIdMapping,
            matrix: config.matrix
        });

        console.log(`\n??å¥—ç”¨å®Œæ?!`);
        console.log(`   ç¸½é¢?¿æ•¸: ${applyResult.TotalPanels}`);
        console.log(`   ?´æ”¹?¢æ¿?? ${applyResult.ChangedPanels}`);

        if (applyResult.FailedCount > 0) {
            console.log(`   ? ï? å¤±æ??¢æ¿?? ${applyResult.FailedCount}`);
            console.log('   å¤±æ??Ÿå?:');
            applyResult.FailedPanels.slice(0, 5).forEach(fp => {
                console.log(`     - Panel ${fp.PanelId} [${fp.Row},${fp.Col}]: ${fp.Reason}`);
            });
        }

        console.log(`\n${applyResult.Message}`);

    } catch (err) {
        console.error('???¯èª¤:', err.message);
    } finally {
        ws.close();
    }
}

main();

