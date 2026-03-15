import WebSocket from 'ws';
import fs from 'fs';

const REPORT_PATH = 'C:\\Users\\david\\.gemini\\antigravity\\brain\\cbe6d689-b5d7-4aac-8262-959083dd8c3b\\exterior_wall_check.json';
const PORT = 11111;

function sendCommand(ws, name, args) {
    return new Promise((resolve, reject) => {
        const reqId = 'req_' + Date.now() + '_' + Math.random().toString(36).substr(2, 9);
        const cmd = {
            CommandName: name,
            Parameters: args,
            RequestId: reqId
        };

        const listener = (data) => {
            try {
                const msg = JSON.parse(data.toString());
                if (msg.RequestId === reqId) {
                    ws.off('message', listener);
                    if (msg.Success) {
                        resolve(msg.Data || { success: true });
                    } else {
                        resolve({ success: false, error: msg.Message || "Unknown Error" });
                    }
                }
            } catch (e) { }
        };

        ws.on('message', listener);
        ws.send(JSON.stringify(cmd));

        setTimeout(() => {
            ws.off('message', listener);
            resolve({ success: false, error: "Timeout" });
        }, 10000);
    });
}

async function markDimensions() {
    console.log('Reading report...');
    let report;
    try {
        report = JSON.parse(fs.readFileSync(REPORT_PATH, 'utf8'));
        console.log(`Read report: ${report.details.length} openings.`);
    } catch (e) { return; }

    const ws = new WebSocket(`ws://localhost:${PORT}`);

    ws.on('open', async () => {
        console.log('Connected to Revit.');
        try {
            let count = 0;
            console.log('Writing dimensions to "?™è¨»" parameter...');

            // Limit for testing
            const LIMIT = 20;

            for (const detail of report.details) {
                const id = detail.openingId;
                const isWarning45 = detail.article45 && detail.article45.BuildingStatus !== 0;
                const isWarning110 = detail.article110 && detail.article110.OverallStatus !== 0;

                if (isWarning45 || isWarning110) {
                    const distPL = detail.article45 ? detail.article45.DistanceToBoundary : -1;
                    const distBldg = detail.article45 ? detail.article45.DistanceToBuilding : -1;

                    const msgParts = [];
                    if (distPL >= 0) msgParts.push(`PL:${distPL.toFixed(2)}m`);
                    if (distBldg >= 0) msgParts.push(`Bldg:${distBldg.toFixed(2)}m`);
                    const comment = `[MCP] ${msgParts.join(', ')}`;

                    console.log(`Marking ID: ${id} with "${comment}"`);

                    // Try '?™è¨»'
                    const res = await sendCommand(ws, 'modify_element_parameter', {
                        elementId: id,
                        parameterName: '?™è¨»',
                        value: comment
                    });

                    if (res && res.success === false) {
                        console.error(`   Failed ID: ${id} - ${res.error}`);
                    } else {
                        count++;
                        console.log(`   Success.`);
                        if (count >= LIMIT) break;
                    }
                }
            }

            console.log(`Done. Marked ${count} elements.`);
            ws.close();
        } catch (e) {
            console.error('Error:', e);
            ws.close();
        }
    });
}

markDimensions();

