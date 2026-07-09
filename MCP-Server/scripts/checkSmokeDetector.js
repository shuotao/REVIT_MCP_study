/**
 * 消防偵煙探測器設置法規檢核腳本
 * 法源：消防法施行細則附表五（偵煙式探測器設置標準）
 * Domain: domain/smoke-detector-check.md
 *
 * 五項必要條件：
 *  1. 距出風口（FCU/冷風機）≥ 1,500mm
 *  2. 距牆壁或樑 ≥ 600mm
 *  3. 探測器下端距天花板 ≤ 600mm（貼近天花板）
 *  4. 每個被 ≥600mm 樑分隔的區格內 ≥ 1 個探測器
 *  5. 每個探測器負責面積：天花板 <4m → ≤150㎡；≥4m → ≤75㎡
 */

import WebSocket from 'ws';
import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const __filename = fileURLToPath(import.meta.url);
const __dirname  = path.dirname(__filename);

const ws = new WebSocket('ws://localhost:8964');

ws.on('open', () => {
    console.log('連線至 Revit MCP Server...');
    console.log('發送 analyze_smoke_detectors 資料萃取指令...');

    ws.once('message', async data => {
        const resp = JSON.parse(data.toString());
        if (!resp.Success || !resp.Data?.Success) {
            console.log('資料萃取失敗:', resp.Error || resp.Data?.Error);
            ws.close();
            process.exit(1);
        }

        const { Detectors, Rooms } = resp.Data;
        console.log(`萃取完成：${Detectors.length} 個探測器，${Rooms.length} 個房間。`);

        // ── 建立 Room 查找表 ────────────────────────────────────────────
        const roomMap = {};
        Rooms.forEach(r => { roomMap[r.RoomId] = r; });

        // ── 逐一套用五條法規 ────────────────────────────────────────────
        const detectorChecks = [];
        let passCount = 0, failCount = 0, warnCount = 0;

        for (const d of Detectors) {
            const reasons  = [];
            let isOk  = true;
            let isWarn = false;

            // ── 條件 1：距出風口 ≥ 1,500mm ─────────────────────────────
            if (d.DistToNearestAirOutlet < 0) {
                // 無出風口資料 → 警告（不視為 FAIL）
                isWarn = true;
                reasons.push('無出風口資料（無法驗證條件1）');
            } else if (d.DistToNearestAirOutlet < 1500) {
                isOk = false;
                reasons.push(`距出風口不足1.5m（${d.DistToNearestAirOutlet.toFixed(0)}mm < 1500mm）`);
            }

            // ── 條件 2：距牆/樑 ≥ 600mm ────────────────────────────────
            if (d.DistToNearestWall >= 0 && d.DistToNearestWall < 600) {
                isOk = false;
                reasons.push(`距牆不足600mm（${d.DistToNearestWall.toFixed(0)}mm）`);
            }
            if (d.DistToNearestBeam >= 0 && d.DistToNearestBeam < 600) {
                isOk = false;
                reasons.push(`距樑不足600mm（${d.DistToNearestBeam.toFixed(0)}mm）`);
            }

            // ── 條件 3：探測器下端距天花板 ≤ 600mm ─────────────────────
            if (d.CeilingZ > 0) {
                const gapToCeiling = d.CeilingZ - d.DetectorBottomZ;
                if (gapToCeiling > 600) {
                    isOk = false;
                    reasons.push(`距天花板過遠（${gapToCeiling.toFixed(0)}mm > 600mm）`);
                }
            } else {
                isWarn = true;
                reasons.push('天花板高度未知（無法驗證條件3）');
            }

            // ── 條件 5：探測面積（以房間為單位）────────────────────────
            const room = roomMap[d.RoomId];
            if (room && room.DetectorCount > 0 && room.Area > 0) {
                const maxArea = room.CeilingHeight < 4.0 ? 150 : 75;
                const avgArea = room.Area / room.DetectorCount;
                if (avgArea > maxArea) {
                    isOk = false;
                    reasons.push(`探測面積超標（${avgArea.toFixed(1)}㎡ > ${maxArea}㎡/個）`);
                }
            }

            const status = !isOk ? 'FAIL' : (isWarn ? 'WARN' : 'PASS');
            if (status === 'PASS') passCount++;
            else if (status === 'WARN') { warnCount++; }
            else failCount++;

            detectorChecks.push({
                ...d,
                FinalStatus: status,
                FinalReason: reasons.join('; ') || 'PASS',
                IsOk:   isOk,
                IsWarn: isWarn,
            });
        }

        // ── 條件 4：區格涵蓋（以房間為單位）───────────────────────────
        const roomIssues = [];
        for (const room of Rooms) {
            if (room.DetectorCount < room.BeamZones) {
                roomIssues.push(
                    `房間「${room.RoomName}」（ID:${room.RoomId}）` +
                    `有 ${room.BeamZones} 個區格但只有 ${room.DetectorCount} 個探測器`
                );
                failCount++;
            }
        }

        // ── 產出 Markdown 報告 ──────────────────────────────────────────
        let md = '# 消防偵煙探測器設置法規檢核報告\n\n';
        md += `> 法源：消防法施行細則附表五（偵煙式探測器設置標準）\n\n`;
        md += `## 總覽\n\n`;
        md += `- 探測器總數：${Detectors.length} 個\n`;
        md += `- ✅ 通過：${passCount}  ⚠️ 警告：${warnCount}  ❌ 失敗：${failCount}\n\n`;

        if (roomIssues.length > 0) {
            md += `## ⚠️ 區格涵蓋缺漏（條件4）\n\n`;
            roomIssues.forEach(issue => { md += `- ${issue}\n`; });
            md += '\n';
        }

        md += `## 探測器明細\n\n`;
        md += '| 探測器ID | 房間 | 距出風口(mm) | 距牆(mm) | 距樑(mm) | 距天花板(mm) | 負責面積(㎡) | 狀態 | 違規原因 |\n';
        md += '| :--- | :--- | ---: | ---: | ---: | ---: | ---: | :---: | :--- |\n';

        detectorChecks.forEach(d => {
            const room   = roomMap[d.RoomId];
            const avgArea = (room && room.DetectorCount > 0)
                ? (room.Area / room.DetectorCount).toFixed(1)
                : '-';
            const ceilGap = d.CeilingZ > 0 ? (d.CeilingZ - d.DetectorBottomZ).toFixed(0) : '-';
            const emoji   = d.FinalStatus === 'PASS' ? '✅' : d.FinalStatus === 'WARN' ? '⚠️' : '❌';
            const outlet  = d.DistToNearestAirOutlet < 0 ? '無資料' : d.DistToNearestAirOutlet.toFixed(0);
            const wallD   = d.DistToNearestWall < 0 ? '-' : d.DistToNearestWall.toFixed(0);
            const beamD   = d.DistToNearestBeam < 0 ? '-' : d.DistToNearestBeam.toFixed(0);
            md += `| ${d.DetectorId} | ${d.RoomName} | ${outlet} | ${wallD} | ${beamD} | ${ceilGap} | ${avgArea} | ${emoji} ${d.FinalStatus} | ${d.FinalReason} |\n`;
        });

        md += '\n---\n\n';
        md += `## 房間統計\n\n`;
        md += '| 房間 | 面積(㎡) | 天花板高(m) | 區格數 | 探測器數 | 最大允許面積(㎡) | 狀態 |\n';
        md += '| :--- | ---: | ---: | ---: | ---: | ---: | :---: |\n';
        Rooms.forEach(r => {
            const maxArea = r.CeilingHeight < 4.0 ? 150 : 75;
            const avgArea = r.DetectorCount > 0 ? (r.Area / r.DetectorCount).toFixed(1) : '-';
            const zoneOk  = r.DetectorCount >= r.BeamZones;
            const areaOk  = r.DetectorCount > 0 && (r.Area / r.DetectorCount) <= maxArea;
            const status  = (zoneOk && areaOk) ? '✅' : '❌';
            md += `| ${r.RoomName} | ${r.Area.toFixed(1)} | ${r.CeilingHeight.toFixed(1)} | ${r.BeamZones} | ${r.DetectorCount} | ${maxArea} | ${status} |\n`;
        });

        console.log('--- REPORT START ---');
        console.log(md);
        console.log('--- REPORT END ---');

        const reportPath = path.join(__dirname, 'smoke_detector_report.md');
        fs.writeFileSync(reportPath, md, 'utf8');
        console.log(`報告已儲存：${reportPath}`);

        // ── 發送視覺化指令 ──────────────────────────────────────────────
        const vizResults = detectorChecks.map(d => ({
            DetectorId: d.DetectorId,
            IsOk:   d.IsOk,
            IsWarn: d.IsWarn,
            Message: d.FinalReason,
        }));

        console.log(`發送 visualize_detector_results 上色指令（${vizResults.length} 筆）...`);
        ws.send(JSON.stringify({
            CommandName: 'visualize_detector_results',
            Parameters:  { results: vizResults },
            RequestId:   'vis-det-' + Date.now(),
        }));

        ws.once('message', visData => {
            const visResp = JSON.parse(visData.toString());
            console.log('上色完成:', visResp.Success ? '成功' : visResp.Error);
            ws.close();
            process.exit(0);
        });
    });

    ws.send(JSON.stringify({
        CommandName: 'analyze_smoke_detectors',
        Parameters:  {},
        RequestId:   'det-' + Date.now(),
    }));
});

ws.on('error', err => {
    console.error('WebSocket 錯誤:', err.message);
    process.exit(1);
});
