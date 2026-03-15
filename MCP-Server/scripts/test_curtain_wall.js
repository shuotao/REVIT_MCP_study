/**
 * Â∏∑Â??ÜÈù¢?øÊ??óÊ∏¨Ë©¶ËÖ≥?? * 
 * ‰ΩøÁî®?πÂ?Ôº? * 1. ??Revit ‰∏≠ÈÅ∏?ñ‰??ãÂ∏∑ÂπïÁ?
 * 2. ?∑Ë?Ê≠§ËÖ≥?¨Ô?node scratch/test_curtain_wall.js
 */

import WebSocket from 'ws';

const SOCKET_URL = 'ws://localhost:11111';

async function sendCommand(ws, commandName, parameters = {}) {
    return new Promise((resolve, reject) => {
        const requestId = `req_${Date.now()}`;
        const command = {
            CommandName: commandName,
            Parameters: parameters,
            RequestId: requestId
        };

        const timeout = setTimeout(() => {
            reject(new Error('?Ω‰ª§?∑Ë??æÊ?'));
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
                        reject(new Error(response.Error || '?Ω‰ª§?∑Ë?Â§±Ê?'));
                    }
                }
            } catch (err) {
                // ÂøΩÁï•??JSON Ë®äÊÅØ
            }
        };

        ws.on('message', handler);
        ws.send(JSON.stringify(command));
    });
}

async function main() {
    console.log('?è¢ Â∏∑Â??ÜÈù¢?øÊ??óÊ∏¨Ë©?);
    console.log('='.repeat(50));

    const ws = new WebSocket(SOCKET_URL);

    ws.on('error', (err) => {
        console.error('??WebSocket ????ØË™§:', err.message);
        console.log('Ë´ãÁ¢∫Ë™?Revit Â∑≤È??ü‰∏¶ËºâÂÖ• RevitMCP Add-in');
        process.exit(1);
    });

    await new Promise((resolve) => ws.on('open', resolve));
    console.log('??Â∑≤ÈÄ?é•??Revit\n');

    try {
        // 1. ?ñÂ?Â∏∑Â??ÜË?Ë®?        console.log('?? ?ñÂ?Â∏∑Â??ÜË?Ë®?..');
        const wallInfo = await sendCommand(ws, 'get_curtain_wall_info');
        console.log(`   Element ID: ${wallInfo.ElementId}`);
        console.log(`   ?ÜÈ??? ${wallInfo.WallType}`);
        console.log(`   Grid: ${wallInfo.Columns} ??x ${wallInfo.Rows} Ë°å`);
        console.log(`   ?¢ÊùøÂ∞∫ÂØ∏: ${wallInfo.PanelWidth}mm x ${wallInfo.PanelHeight}mm`);
        console.log(`   Á∏ΩÈù¢?øÊï∏: ${wallInfo.TotalPanels}`);
        console.log(`   ?æÊ??¢ÊùøÈ°ûÂ?:`);
        wallInfo.PanelTypes.forEach(pt => {
            console.log(`     - ${pt.TypeName} (ID: ${pt.TypeId}): ${pt.Count} ?ã`);
        });
        console.log();

        // 2. ?ñÂ??ØÁî®?ÑÈù¢?øÈ???        console.log('?é® ?ñÂ??ØÁî®?¢ÊùøÈ°ûÂ?...');
        const panelTypes = await sendCommand(ws, 'get_curtain_panel_types');
        console.log(`   ??${panelTypes.Count} Á®ÆÈù¢?øÈ???`);
        panelTypes.PanelTypes.slice(0, 10).forEach(pt => {
            console.log(`     - ${pt.TypeName} (${pt.Family}) ID: ${pt.TypeId}`);
        });
        if (panelTypes.Count > 10) {
            console.log(`     ... ?ÑÊ? ${panelTypes.Count - 10} Á®Æ`);
        }
        console.log();

        // Ëº∏Âá∫ JSON ‰æõÈ?Ë¶ΩÂ∑•?∑‰Ωø??        const previewData = {
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

        console.log('?ì¶ ?êË¶ΩÂ∑•ÂÖ∑Ë≥áÊ?:');
        console.log(JSON.stringify(previewData, null, 2));

    } catch (err) {
        console.error('???ØË™§:', err.message);
    } finally {
        ws.close();
    }
}

main();

