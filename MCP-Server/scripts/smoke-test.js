/**
 * RevitMCP Smoke Test
 *
 * Usage:
 *   node scripts/smoke-test.js
 *   node scripts/smoke-test.js --revit 2021
 *   node scripts/smoke-test.js --port 8964
 *
 * Requires: Revit open with MCP add-in started on the target port
 */

import { WebSocket } from 'ws';

// CLI args: --revit <year>  --port <num>
const args = process.argv.slice(2);
const revitIdx = args.indexOf('--revit');
const portIdx  = args.indexOf('--port');
const revitYear = revitIdx !== -1 ? (args[revitIdx + 1] ?? 'unknown') : 'unknown';
const port      = portIdx  !== -1 ? (args[portIdx  + 1] ?? '8964')    : '8964';
const WS_URL    = `ws://localhost:${port}/`;
const TIMEOUT_MS = 5000;

let passed = 0;
let failed = 0;

function sendCmd(ws, name, params, id) {
    return new Promise((resolve, reject) => {
        const timer = setTimeout(() => reject(new Error(`Timeout: ${name}`)), TIMEOUT_MS);
        const handler = (data) => {
            const r = JSON.parse(data.toString());
            if (r.RequestId === id) {
                clearTimeout(timer);
                ws.removeListener('message', handler);
                resolve(r);
            }
        };
        ws.on('message', handler);
        ws.send(JSON.stringify({ CommandName: name, Parameters: params || {}, RequestId: id }));
    });
}

function ok(label, condition, detail) {
    if (condition) {
        console.log(`  ✅ PASS  ${label}${detail ? '  (' + detail + ')' : ''}`);
        passed++;
    } else {
        console.log(`  ❌ FAIL  ${label}${detail ? '  (' + detail + ')' : ''}`);
        failed++;
    }
}

async function run() {
    console.log('═══════════════════════════════════════');
    console.log('  RevitMCP Smoke Test');
    console.log(`  Revit: ${revitYear}`);
    console.log(`  Target: ${WS_URL}`);
    console.log('═══════════════════════════════════════\n');

    // Step 1: WebSocket connection
    console.log('[1/5] WebSocket Connection');
    const ws = await new Promise((resolve, reject) => {
        const socket = new WebSocket(WS_URL);
        const timer = setTimeout(() => reject(new Error('Connection timeout')), TIMEOUT_MS);
        socket.on('open', () => { clearTimeout(timer); resolve(socket); });
        socket.on('error', (e) => { clearTimeout(timer); reject(e); });
    }).catch(e => { ok('WebSocket connect', false, e.message); return null; });

    if (!ws) {
        console.log('\n❌ Cannot connect — is Revit open with MCP service started?\n');
        process.exit(1);
    }
    ok('WebSocket connect', true, WS_URL);

    // Step 2: get_project_info
    console.log('\n[2/5] get_project_info');
    let r = await sendCmd(ws, 'get_project_info', {}, 's2').catch(e => ({ Success: false, Error: e.message }));
    ok('get_project_info', r.Success, r.Success ? r.Data.ProjectName : r.Error);

    // Step 3: get_all_levels
    console.log('\n[3/5] get_all_levels');
    r = await sendCmd(ws, 'get_all_levels', {}, 's3').catch(e => ({ Success: false, Error: e.message }));
    ok('get_all_levels returns data', r.Success && r.Data.Count > 0, r.Success ? `Count=${r.Data.Count}` : r.Error);

    // Step 4: get_active_view
    console.log('\n[4/5] get_active_view');
    r = await sendCmd(ws, 'get_active_view', {}, 's4').catch(e => ({ Success: false, Error: e.message }));
    ok('get_active_view', r.Success, r.Success ? r.Data.Name : r.Error);

    // Step 5: get_all_views
    console.log('\n[5/5] get_all_views');
    r = await sendCmd(ws, 'get_all_views', {}, 's5').catch(e => ({ Success: false, Error: e.message }));
    const viewCount = r.Success ? (Array.isArray(r.Data) ? r.Data.length : (r.Data?.Count ?? 0)) : 0;
    ok('get_all_views returns data', r.Success && viewCount > 0, r.Success ? `Count=${viewCount}` : r.Error);

    ws.close();

    // Summary
    const total = passed + failed;
    console.log('\n═══════════════════════════════════════');
    console.log(`  Result: ${passed}/${total} passed`);
    if (failed === 0) {
        console.log('  🟢 ALL PASS — MCP service is healthy');
    } else {
        console.log(`  🔴 ${failed} FAILED`);
    }
    console.log('═══════════════════════════════════════\n');

    process.exit(failed > 0 ? 1 : 0);
}

run().catch(e => {
    console.error('FATAL:', e.message);
    process.exit(1);
});
