/**
 * Complete Fire Rating Visualization with Join Management
 * Fixed viewId issue
 */
import WebSocket from 'ws';
import fs from 'fs';

const ws = new WebSocket('ws://localhost:8964');
const log = [];

const COLOR_MAP = {
    "2HR": { r: 0, g: 180, b: 0, t: 20 },
    "1HR": { r: 255, g: 255, b: 0, t: 30 },
    "NONE": { r: 100, g: 150, b: 255, t: 40 },
    "UNSET": { r: 200, g: 0, b: 200, t: 50 }
};

const PARAM_NAME = "s_CW_防火防煙性能";

let viewId = null;
let wallIds = [];
let wallData = [];
let idx = 0;
let stage = 'get_view';
let dist = {};

function getColor(val) {
    if (!val || val === "") return COLOR_MAP.UNSET;
    if (val === "2" || (val.includes("2") && val.includes("小時"))) return COLOR_MAP["2HR"];
    if (val === "1" || (val.includes("1") && val.includes("小時"))) return COLOR_MAP["1HR"];
    if (val.includes("無") || val === "0") return COLOR_MAP.NONE;
    return COLOR_MAP["1HR"];
}

function send(cmd, params) {
    ws.send(JSON.stringify({ CommandName: cmd, Parameters: params, RequestId: cmd + '_' + Date.now() }));
}

function output(msg) {
    console.log(msg);
    log.push(msg);
}

ws.on('open', () => {
    output('=== Fire Rating Visualization (v2) ===');
    output('Step 1: Get walls from current view...');
    send('query_elements', { category: 'Walls', maxCount: 200 });
});

ws.on('message', (data) => {
    const res = JSON.parse(data.toString());
    if (!res.Success) { output('Error: ' + res.Error); finish(); return; }

    switch (stage) {
        case 'get_view':
            // Get viewId from query result
            viewId = res.Data.ViewId;
            wallIds = (res.Data.Elements || []).map(e => e.ElementId);
            output('View ID: ' + viewId);
            output('Walls found: ' + wallIds.length);

            if (wallIds.length === 0) { finish(); return; }

            // Step 2: Unjoin wall-column geometry
            stage = 'unjoin';
            output('Step 2: Unjoin wall-column geometry...');
            send('unjoin_wall_joins', { wallIds: wallIds, viewId: viewId });
            break;

        case 'unjoin':
            output('  Unjoined: ' + (res.Data.UnjoinedCount || 0) + ' joins');
            stage = 'get_info';
            idx = 0;
            output('Step 3: Analyze parameters...');
            send('get_element_info', { elementId: wallIds[idx] });
            break;

        case 'get_info':
            let val = "";
            if (res.Data.Parameters) {
                const p = res.Data.Parameters.find(x => x.Name === PARAM_NAME);
                if (p && p.Value) val = p.Value.trim();
            }
            wallData.push({ id: wallIds[idx], fire: val });
            const key = val || "(empty)";
            dist[key] = (dist[key] || 0) + 1;

            idx++;
            if (idx < wallIds.length) {
                send('get_element_info', { elementId: wallIds[idx] });
            } else {
                output('');
                output('=== DISTRIBUTION ===');
                for (const [k, v] of Object.entries(dist).sort((a, b) => b[1] - a[1])) {
                    output('  ' + k + ': ' + v + ' walls');
                }
                output('');
                stage = 'override';
                idx = 0;
                output('Step 4: Apply colors...');
                applyNext();
            }
            break;

        case 'override':
            idx++;
            if (idx < wallData.length) {
                applyNext();
            } else {
                output('Applied colors to ' + wallData.length + ' walls');
                // Step 5: Rejoin
                stage = 'rejoin';
                output('Step 5: Rejoin wall-column geometry...');
                send('rejoin_wall_joins', {});
            }
            break;

        case 'rejoin':
            output('  Rejoined: ' + (res.Data.RejoinedCount || 0) + ' joins');
            output('');
            output('=== COMPLETE ===');
            output('GREEN  = 2 (2HR)');
            output('YELLOW = 1 (1HR)');
            output('PURPLE = Empty');
            finish();
            break;
    }
});

function applyNext() {
    const w = wallData[idx];
    const c = getColor(w.fire);
    send('override_element_graphics', {
        elementId: w.id,
        viewId: viewId,
        surfaceFillColor: { r: c.r, g: c.g, b: c.b },
        transparency: c.t
    });
}

function finish() {
    fs.writeFileSync('fire_rating_result.txt', log.join('\n'), 'utf8');
    output('Saved to fire_rating_result.txt');
    ws.close();
}

ws.on('error', (e) => console.error('Error:', e.message));
ws.on('close', () => process.exit(0));
setTimeout(() => { output('Timeout'); finish(); }, 300000);
