/**
 * RevitMCP Core Reload Verification Test
 *
 * 目的：驗證「不重啟 Revit」的 Core 熱重載是否真的生效。
 *
 * 使用方式（手動模式）：
 *   Step 1: node scripts/core-reload-verify.js --label before
 *   Step 2: 修改 CoreRuntime 程式碼（例如改 CoreVersion 字串），重新 build & deploy
 *   Step 3: 在 Revit Ribbon 按「Reload Core」
 *   Step 4: node scripts/core-reload-verify.js --label after
 *   => 比較 before/after 的 CoreVersion，有變化就代表熱重載成功
 *
 * 使用方式（全自動模式 --auto）：
 *   node scripts/core-reload-verify.js --auto --revit 2022
 *   腳本會自動：[1] 記錄 before → [2] 觸發 reload_core → [3] 記錄 after → [4] 比對結果
 *   需要 Loader 支援 reload_core WebSocket 命令（Revit 重啟後生效）
 *
 * 使用方式（完整驗證模式 --full）：
 *   node scripts/core-reload-verify.js --full --revit 2022
 *   在 --auto 基礎上額外確認 Log Viewer 日誌訊息：
 *   [1] 記錄 before CoreVersion → [2] 觸發 reload_core → [3] 查詢 get_recent_logs
 *   → [4] 確認 5 條 CoreRuntime 重載訊息 → [5] 確認 after CoreVersion
 *
 * 選用參數：
 *   --port  <num>     WebSocket port（預設 8964）
 *   --label <text>    標記此次是 before 或 after（預設 'check'）
 *   --revit <year>    Revit 版本標籤，僅顯示用（預設 'unknown'）
 *   --auto            全自動模式：before → reload → after
 *   --full            完整驗證模式：--auto + Log Viewer 訊息確認
 *   --view  <id>      要用來建立 dimension 的 view ElementId
 *                     （不指定則用 get_active_view 自動取得）
 */

import { WebSocket } from 'ws';
import { execFileSync } from 'child_process';
import { appendFileSync, existsSync, readFileSync } from 'fs';
import { resolve, dirname } from 'path';
import { fileURLToPath } from 'url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const RESULTS_FILE = resolve(__dirname, 'core-reload-results.jsonl');

function saveResult(record) {
    appendFileSync(RESULTS_FILE, JSON.stringify(record) + '\n', 'utf8');
}

function printResultsSummary() {
    if (!existsSync(RESULTS_FILE)) return;
    const lines = readFileSync(RESULTS_FILE, 'utf8')
        .split('\n')
        .filter(l => l.trim());
    if (lines.length === 0) return;
    const rows = lines.map(l => JSON.parse(l));

    console.log('\n┌────────────────────────────────────────────────────────────────┐');
    console.log('│  測試記錄摘要                                                  │');
    console.log('├────────┬──────┬───────────┬─────────┬──────────────────────────┤');
    console.log('│ Revit  │ Mode │ CoreVer   │ Log 5/5 │ 時間                     │');
    console.log('├────────┼──────┼───────────┼─────────┼──────────────────────────┤');
    for (const r of rows) {
        const revit   = (r.revitYear ?? 'unknown').padEnd(6);
        const mode    = (r.mode ?? 'auto').padEnd(4);
        const ver     = (r.coreVersion ?? '?').padEnd(9);
        const log5    = r.logCheckPassed === true ? '  ✅    ' : r.logCheckPassed === false ? '  ❌    ' : '  -     ';
        const passed  = r.passed ? '✅' : '❌';
        const ts      = (r.timestamp ?? '').substring(0, 19).replace('T', ' ');
        console.log(`│ ${revit} │ ${mode} │ ${ver} │ ${log5} │ ${ts}  ${passed} │`);
    }
    console.log('└────────┴──────┴───────────┴─────────┴──────────────────────────┘');
}

const args = process.argv.slice(2);
const portIdx  = args.indexOf('--port');
const labelIdx = args.indexOf('--label');
const revitIdx = args.indexOf('--revit');
const viewIdx  = args.indexOf('--view');
const autoMode = args.includes('--auto') || args.includes('--full');
const fullMode = args.includes('--full');

const port      = portIdx  !== -1 ? (args[portIdx  + 1] ?? '8964')    : '8964';
const label     = labelIdx !== -1 ? (args[labelIdx + 1] ?? 'check')   : (autoMode ? 'auto' : 'check');
let   revitYear = revitIdx !== -1 ? (args[revitIdx + 1] ?? 'unknown') : 'unknown';
const forcedViewId = viewIdx !== -1 ? parseInt(args[viewIdx + 1], 10) : null;

const WS_URL = `ws://localhost:${port}/`;
const TIMEOUT_MS = 8000;

function detectRevitVersionFromProcess() {
    if (process.platform !== 'win32') return null;

    try {
        const script = "$p = Get-Process Revit -ErrorAction SilentlyContinue | Select-Object -First 1; if ($null -eq $p) { exit 1 }; $path = $p.Path; if ($path -match 'Revit ([0-9]{4})') { Write-Output $matches[1]; exit 0 }; $fv = (Get-Item $path).VersionInfo.FileVersion; if ($fv -match '^([0-9]{2})\\.') { Write-Output ('20' + $matches[1]); exit 0 }; exit 1";
        return execFileSync('powershell', ['-NoProfile', '-Command', script], { encoding: 'utf8' }).trim() || null;
    } catch {
        return null;
    }
}

function sendCmd(ws, name, params, id) {
    return new Promise((resolve, reject) => {
        const timer = setTimeout(() => reject(new Error(`timeout: ${name}`)), TIMEOUT_MS);
        const handler = (data) => {
            const r = JSON.parse(data.toString());
            if (r.RequestId === id) {
                clearTimeout(timer);
                ws.off('message', handler);
                resolve(r);
            }
        };
        ws.on('message', handler);
        ws.send(JSON.stringify({ CommandName: name, Parameters: params, RequestId: id }));
    });
}

async function getCoreVersion(ws, forcedViewId) {
    let viewId = forcedViewId;
    if (!viewId) {
        const viewResp = await sendCmd(ws, 'get_active_view', {}, 'crv_view_' + Date.now());
        if (!viewResp.Success) throw new Error(`get_active_view failed: ${viewResp.Error}`);
        viewId = viewResp.Data.ElementId;
    }
    const dimResp = await sendCmd(ws, 'create_dimension', {
        viewId,
        startX: -5954, startY: 3916,
        endX:    446,  endY: 3916,
        offset: 500
    }, 'crv_dim_' + Date.now());
    return { viewId, dimResp };
}

async function run() {
    console.log('═══════════════════════════════════════════════');
    console.log('  RevitMCP Core Reload Verification');
    console.log(`  Mode  : ${fullMode ? '🔬 FULL (reload + Log Viewer check)' : autoMode ? '🤖 AUTO (before → reload → after)' : `[${label}]`}`);
    console.log(`  Revit : ${revitYear === 'unknown' ? '(auto-detect)' : revitYear}`);
    console.log(`  Target: ${WS_URL}`);
    console.log('═══════════════════════════════════════════════\n');

    const ws = new WebSocket(WS_URL);
    await new Promise((resolve, reject) => {
        ws.once('open', resolve);
        ws.once('error', (e) => reject(new Error(`WebSocket error: ${e.message}`)));
        setTimeout(() => reject(new Error('Connection timeout')), TIMEOUT_MS);
    });
    console.log('✅ Connected\n');

    // 自動偵測 Revit 版本（如果 --revit 未指定或為預設值 'unknown'）
    if (revitYear === 'unknown') {
        try {
            const verResp = await sendCmd(ws, 'get_revit_version', {}, 'crv_ver_' + Date.now());
            if (verResp.Success && verResp.Data?.VersionNumber) {
                revitYear = verResp.Data.VersionNumber;
                console.log(`🔍 自動偵測 Revit 版本: ${revitYear} (${verResp.Data.VersionName})\n`);
            } else {
                console.log(`⚠️  get_revit_version: Success=${verResp.Success}, Error=${verResp.Error ?? '(none)'}`);
                const processYear = detectRevitVersionFromProcess();
                if (processYear) {
                    revitYear = processYear;
                    console.log(`🔍 由本機 Revit.exe 偵測版本: ${revitYear}\n`);
                }
            }
        } catch (e) {
            console.log(`⚠️  get_revit_version 例外: ${e.message}`);
            const processYear = detectRevitVersionFromProcess();
            if (processYear) {
                revitYear = processYear;
                console.log(`🔍 由本機 Revit.exe 偵測版本: ${revitYear}\n`);
            }
        }
    }

    if (autoMode) {
        // === 全自動 / 完整驗證模式 ===
        // Step 1: before
        console.log('[1/4] 記錄 BEFORE 狀態...');
        const { viewId, dimResp: beforeResp } = await getCoreVersion(ws, forcedViewId);
        if (!beforeResp.Success) {
            ws.close();
            throw new Error(`before 狀態取得失敗: ${beforeResp.Error}`);
        }
        const vBefore = beforeResp.Data?.CoreVersion ?? '(none)';
        console.log(`  CoreVersion BEFORE: ${vBefore}`);

        // Step 1.5 (--full): 記錄觸發前的時間戳，稍後用來篩選日誌
        const reloadTriggerTime = new Date();
        const sinceStr = reloadTriggerTime.toISOString().replace('T', ' ').substring(0, 19);
        console.log(`  Log since: ${sinceStr}\n`);

        // Step 2: trigger reload_core
        console.log('[2/4] 觸發 reload_core...');
        const reloadResp = await sendCmd(ws, 'reload_core', {}, 'crv_reload_' + Date.now());
        if (!reloadResp.Success) {
            ws.close();
            console.log(`  ❌ reload_core 失敗: ${reloadResp.Error}`);
            console.log('  → 請確認 Revit 已重啟後部署新版 Loader DLL');
            process.exit(1);
        }
        console.log(`  ✅ ${reloadResp.Data?.Message ?? 'reload triggered'}`);
        console.log('  等待 CoreRuntime 重新載入 (3s)...\n');
        ws.close();
        await new Promise(r => setTimeout(r, 3000));

        // Step 3: after — 重新建立連線（reload 會重啟 WebSocket server，舊連線已斷）
        console.log('[3/4] 記錄 AFTER 狀態...');
        const ws2 = new WebSocket(WS_URL);
        await new Promise((resolve, reject) => {
            ws2.once('open', resolve);
            ws2.once('error', (e) => reject(new Error(`reconnect error: ${e.message}`)));
            setTimeout(() => reject(new Error('reconnect timeout')), TIMEOUT_MS);
        });
        // reload 後用新 CoreRuntime 重新偵測版本（首次偵測可能因舊版不支援而失敗）
        if (revitYear === 'unknown') {
            try {
                const verResp2 = await sendCmd(ws2, 'get_revit_version', {}, 'crv_ver2_' + Date.now());
                if (verResp2.Success && verResp2.Data?.VersionNumber) {
                    revitYear = verResp2.Data.VersionNumber;
                    console.log(`  🔍 Revit 版本偵測: ${revitYear}`);
                } else {
                    console.log(`  ⚠️  get_revit_version (post-reload): Success=${verResp2.Success}, Error=${verResp2.Error ?? '(none)'}`);
                    const processYear = detectRevitVersionFromProcess();
                    if (processYear) {
                        revitYear = processYear;
                        console.log(`  🔍 本機 Revit.exe 版本偵測: ${revitYear}`);
                    }
                }
            } catch (e) {
                console.log(`  ⚠️  get_revit_version (post-reload) 例外: ${e.message}`);
                const processYear = detectRevitVersionFromProcess();
                if (processYear) {
                    revitYear = processYear;
                    console.log(`  🔍 本機 Revit.exe 版本偵測: ${revitYear}`);
                }
            }
        }
        const { dimResp: afterResp } = await getCoreVersion(ws2, viewId);
        if (!afterResp.Success) {
            ws2.close();
            console.log(`  ❌ after 狀態取得失敗: ${afterResp.Error}`);
            process.exit(1);
        }
        const vAfter = afterResp.Data?.CoreVersion ?? '(none)';
        console.log(`  CoreVersion AFTER:  ${vAfter}\n`);

        // Step 4 (--full): 查詢 Log Viewer 日誌，確認 CoreRuntime 重載訊息
        let logCheckPassed = true;
        if (fullMode) {
            console.log('[4/4] 確認 Log Viewer 日誌訊息...');
            const logsResp = await sendCmd(ws2, 'get_recent_logs', { lines: 100, since: sinceStr }, 'crv_logs_' + Date.now());
            if (!logsResp.Success) {
                console.log(`  ⚠️  get_recent_logs 失敗: ${logsResp.Error}`);
                console.log('  → 請確認已部署最新 CoreRuntime（含 get_recent_logs 命令）\n');
                logCheckPassed = false;
            } else {
                const logLines = logsResp.Data?.Lines ?? [];
                console.log(`  收到 ${logLines.length} 行日誌（since ${sinceStr}）`);

                // 確認 CoreRuntimeManager 的 5 條重載訊息
                const EXPECTED = [
                    { key: 'CoreRuntime 開始熱重載',  label: '開始熱重載' },
                    { key: 'CoreRuntime 卸載中',       label: '卸載中' },
                    { key: 'Shadow-copy 路徑',          label: 'Shadow-copy 路徑' },
                    { key: 'CoreRuntime 已載入',        label: '已載入' },
                    { key: 'CoreRuntime 熱重載完成',    label: '熱重載完成' },
                ];
                const joined = logLines.join('\n');
                console.log('');
                for (const { key, label: lbl } of EXPECTED) {
                    const found = joined.includes(key);
                    console.log(`  ${found ? '✅' : '❌'} ${lbl.padEnd(16)} ${found ? '' : '← 未找到'}`);
                    if (!found) logCheckPassed = false;
                }
                console.log('');

                // 印出找到的相關行，方便對照 Log Viewer
                const relatedLines = logLines.filter(l =>
                    EXPECTED.some(e => l.includes(e.key))
                );
                if (relatedLines.length > 0) {
                    console.log('  相關日誌行：');
                    relatedLines.forEach(l => console.log('    ' + l));
                    console.log('');
                }
            }
        } else {
            console.log('[4/4] 跳過 Log Viewer 確認（加 --full 啟用）\n');
        }

        ws2.close();

        // Result
        const versionOk = vBefore === vAfter ? false : true;  // version changed = success for different builds
        // For same-version reload (e.g. v9→v9), version won't change — that's OK in --full mode,
        // what matters is the log messages were emitted
        const reloadOk = fullMode ? logCheckPassed : versionOk;
        const reloadLabel = fullMode
            ? (logCheckPassed ? '日誌確認通過' : '日誌確認失敗')
            : (versionOk ? '版本已變更' : 'CoreVersion 未變化');

        console.log('╔═══════════════════════════════════════════════════╗');
        if (fullMode) {
            console.log(`║  CoreVersion: ${vBefore} (before/after 相同為正常，同版本重載) ║`);
            console.log(`║  Log Viewer: ${logCheckPassed ? '✅ 5/5 訊息全部確認' : '❌ 部分訊息缺失'}                   ║`);
            console.log(`║  ${logCheckPassed ? '✅ 完整驗證通過！' : '❌ 完整驗證失敗'}                                 ║`);
        } else if (versionOk) {
            console.log(`║  ✅ 熱重載成功！                                ║`);
            console.log(`║  BEFORE: ${vBefore.padEnd(22)}  AFTER: ${vAfter.padEnd(10)} ║`);
        } else {
            console.log(`║  ❌ CoreVersion 未變化（${vBefore}）              ║`);
            console.log(`║  → DLL 可能未更新，或 reload 尚未完成          ║`);
        }
        console.log('╚═══════════════════════════════════════════════════╝');

        // 儲存此次測試結果
        saveResult({
            timestamp:      new Date().toISOString(),
            revitYear,
            mode:           fullMode ? 'full' : 'auto',
            coreVersion:    vBefore,
            vBefore,
            vAfter,
            logCheckPassed: fullMode ? logCheckPassed : null,
            passed:         reloadOk,
        });
        printResultsSummary();

        if (!reloadOk) process.exit(1);
        return;
    }

    // === 手動模式 ===
    // 取得作用中視圖
    let viewId = forcedViewId;
    let viewName = '(specified by --view)';
    if (!viewId) {
        const viewResp = await sendCmd(ws, 'get_active_view', {}, 'crv_view');
        if (!viewResp.Success) throw new Error(`get_active_view failed: ${viewResp.Error}`);
        viewId = viewResp.Data.ElementId;
        viewName = viewResp.Data.Name;
    }
    console.log(`Active View: ${viewName} (id=${viewId})`);

    const dimResp = await sendCmd(ws, 'create_dimension', {
        viewId,
        startX: -5954, startY: 3916,
        endX:    446,  endY: 3916,
        offset: 500
    }, 'crv_dim');

    ws.close();

    console.log('');
    if (dimResp.Success) {
        const coreVersion = dimResp.Data?.CoreVersion ?? '(not reported)';
        const message     = dimResp.Data?.Message     ?? '';
        console.log('╔═══════════════════════════════════════════════╗');
        console.log(`║  CoreVersion [${label}] : ${coreVersion.padEnd(20)} ║`);
        console.log(`║  Message              : ${message.substring(0, 46).padEnd(46)} ║`);
        console.log('╚═══════════════════════════════════════════════╝');
        console.log('');
        console.log('下一步提示:');
        if (label === 'before') {
            console.log('  1. 修改 Core 程式碼（例如改 CoreVersion 字串）');
            console.log('  2. 執行 build + deploy CoreRuntime');
            console.log('  3. 在 Revit Ribbon 按「Reload Core」（或用 --auto 全自動）');
            console.log('  4. 再執行: node scripts/core-reload-verify.js --label after --revit ' + revitYear);
        } else if (label === 'after') {
            console.log('  比較 before 和 after 的 CoreVersion：');
            console.log('  若版本字串不同 → 熱重載成功 ✅');
            console.log('  若版本字串相同 → 熱重載未生效，請檢查 deploy 路徑和 Reload 步驟 ❌');
        }
    } else {
        console.log(`❌ create_dimension 失敗: ${dimResp.Error}`);
        console.log('  CoreVersion 無法取得（命令失敗，可能是視圖或座標問題）');
        console.log('  嘗試加上 --view <viewId> 指定有效視圖');
        process.exit(1);
    }
}

run().catch(e => {
    console.error('FATAL:', e.message);
    process.exit(1);
});
