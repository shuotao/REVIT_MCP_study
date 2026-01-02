/**
 * Wall Fire Rating - Output to file for better visibility
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

const PARAM_NAMES = ["s_CW_防火防煙性能"];

let viewId = null;
let walls = [];
let wallData = [];
let idx = 0;
let stage = 'get_view';
let dist = {};

function getColor(val) {
    if (!val || val === "") return COLOR_MAP.UNSET;
    if (val.includes("2") && val.includes("小時")) return COLOR_MAP["2HR"];
    if (val.includes("1") && val.includes("小時")) return COLOR_MAP["1HR"];
    if (val.includes("無") || val === "0" || val === "-") return COLOR_MAP.NONE;
    return COLOR_MAP["1HR"]; // Has value, use yellow
}

function send(cmd, params) {
    ws.send(JSON.stringify({ CommandName: cmd, Parameters: params, RequestId: cmd + '_' + Date.now() }));
}

function output(msg) {
    console.log(msg);
    log.push(msg);
}

ws.on('open', () => {
    output('=== Fire Rating Visualization ===');
    output('Using parameter: s_CW_防火防煙性能');
    send('get_active_view', {});
});

ws.on('message', (data) => {
    const res = JSON.parse(data.toString());
    if (!res.Success) { output('Error: ' + res.Error); finish(); return; }

    if (stage === 'get_view') {
        viewId = res.Data.Id;
        output('View: ' + res.Data.Name);
        stage = 'get_walls';
        send('query_elements', { category: 'Walls', viewId: viewId });
    }
    else if (stage === 'get_walls') {
        walls = res.Data.Elements || [];
        output('Walls found: ' + walls.length);
        if (walls.length === 0) { finish(); return; }
        stage = 'get_info';
        idx = 0;
        send('get_element_info', { elementId: walls[idx].ElementId });
    }
    else if (stage === 'get_info') {
        let val = "";
        if (res.Data.Parameters) {
            const p = res.Data.Parameters.find(x => x.Name === "s_CW_防火防煙性能");
            if (p && p.Value) val = p.Value.trim();
        }

        wallData.push({ id: walls[idx].ElementId, fire: val });
        const key = val || "(empty)";
        dist[key] = (dist[key] || 0) + 1;

        idx++;
        if (idx < walls.length) {
            send('get_element_info', { elementId: walls[idx].ElementId });
        } else {
            output('');
            output('=== DISTRIBUTION ===');
            for (const [k, v] of Object.entries(dist).sort((a, b) => b[1] - a[1])) {
                output('  ' + k + ': ' + v + ' walls');
            }
            output('');
            stage = 'override';
            idx = 0;
            applyNext();
        }
    }
    else if (stage === 'override') {
        idx++;
        if (idx < wallData.length) {
            applyNext();
        } else {
            output('Applied colors to ' + wallData.length + ' walls');
            output('');
            output('=== COLOR LEGEND ===');
            output('  GREEN  = 2HR');
            output('  YELLOW = 1HR or has value');
            output('  BLUE   = No rating');
            output('  PURPLE = Empty');
            output('');
            output('=== DONE ===');
            finish();
        }
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
    output('Results saved to fire_rating_result.txt');
    ws.close();
}

ws.on('error', (e) => console.error('Error:', e.message));
ws.on('close', () => process.exit(0));
setTimeout(() => { output('Timeout'); finish(); }, 300000);
