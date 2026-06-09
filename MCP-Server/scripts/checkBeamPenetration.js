import WebSocket from 'ws';
import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

const ws = new WebSocket('ws://localhost:8964');

ws.on('open', () => {
    console.log('連線至 Revit MCP Server...');
    console.log('發送 advanced_analyze 幾何萃取指令...');
    
    ws.once('message', async data => {
        const resp = JSON.parse(data.toString());
        if (!resp.Success || !resp.Data.Success) {
            console.log('幾何萃取失敗:', resp.Error || resp.Data.Error);
            ws.close();
            process.exit(1);
        }

        const rawResults = resp.Data.Results;
        console.log(`幾何萃取完成，共取得 ${rawResults.length} 處相交套管資料。`);
        
        // 1. 第一階段：Sleeve-based 套管幾何與相鄰小梁檢核
        const sleeveGroups = {};
        rawResults.forEach(r => {
            if (!sleeveGroups[r.SleeveId]) sleeveGroups[r.SleeveId] = [];
            sleeveGroups[r.SleeveId].push(r);
        });

        const activeResults = [];
        const excludedResults = [];
        const visualizationResults = [];
        let passCount = 0;
        let failCount = 0;

        for (const sleeveId in sleeveGroups) {
            const relations = sleeveGroups[sleeveId];
            
            // 找出套管穿過的梁 (!IsExcluded)
            const activeRelation = relations.find(r => !r.IsExcluded);
            
            if (!activeRelation) {
                // 若全被排除，則將該套管歸入 excludedResults
                const r = relations[0];
                console.log(`[過濾] 套管 ID ${r.SleeveId} 被排除檢核 (套管長: ${r.SleeveLength.toFixed(0)}mm, 梁寬: ${r.BeamWidth.toFixed(0)}mm，原因: ${r.ExclusionReason})`);
                r.FinalStatus = 'EXCLUDED_WARNING';
                r.FinalReason = r.ExclusionReason;
                excludedResults.push(r);
                continue;
            }

            const s = activeRelation;
            // 找出此套管緊鄰側面（排除原因為偏心過遠）的梁（相交梁）
            const sideRelations = relations.filter(r => r.IsExcluded && r.ExclusionReason && r.ExclusionReason.includes("偏離"));

            let isOk = true;
            let reasons = [];
            
            const H = s.BeamDepth;
            const D = s.SleeveDiameter;
            const dist = (s.MinDistanceFace || s.MinDistance) - D / 2.0;
            let reqVert = 200; 
            let limitD = H / 3.0; 
            let reqDistLimit = 1.0 * H;
            
            // 標註層級控制屬性已移除，改交由 C# 的虛擬節點排序處理

            // 規則 2, 3, 4: 開孔位置分區判定 (大梁與小梁分流)
            if (s.BeamUsage === 'Minor') {
                // 小梁規則 (d >= 1/2H 且 D <= 1/3H)
                reqVert = 150; 
                reqDistLimit = 0.5 * H;
                limitD = H / 3.0;
                if (dist < reqDistLimit) {
                    isOk = false;
                    reasons.push(`小梁禁開區:距端面不足(${dist.toFixed(0)}<0.5H=${(0.5*H).toFixed(0)})`);
                }
                if (D > limitD) {
                    isOk = false;
                    reasons.push(`小梁孔徑過大(${D.toFixed(0)}>1/3H=${(H/3).toFixed(0)})`);
                }
            } else {
                // 大梁規則 (Zone A, B, C)
                reqVert = 200;
                
                // 第一優先檢核：與大梁端點(柱邊)的距離
                reqDistLimit = 1.0 * H;
                if (dist < reqDistLimit) {
                    isOk = false;
                    limitD = 0; 
                    reasons.push(`大梁柱端禁開區:距柱邊不足(${dist.toFixed(0)}<1.0H=${(1.0*H).toFixed(0)})`);
                } else if (dist >= 1.0 * H && dist < 1.5 * H) {
                    limitD = H / 4.0;
                    if (D > limitD) {
                        isOk = false;
                        reasons.push(`大梁限制區:孔徑過大(${D.toFixed(0)}>1/4H=${(H/4).toFixed(0)})`);
                    }
                } else {
                    limitD = H / 3.0;
                    if (D > limitD) {
                        isOk = false;
                        reasons.push(`大梁一般區:孔徑過大(${D.toFixed(0)}>1/3H=${(H/3).toFixed(0)})`);
                    }
                }

                // 第二優先檢核：與相交小梁的邊緣距離 (從 C# 傳來的 NearestSideBeam 資訊)
                if (s.NearestSideBeamId && s.NearestSideBeamId !== 0) {
                    // 距離已計算完畢，標註邏輯移交 C#
                    // DistToNearestSideBeamCenter 是從套管中心到小梁中心線的距離
                    const distToSideBeamEdge = s.DistToNearestSideBeamCenter - (s.NearestSideBeamWidth / 2.0) - (D / 2.0);
                    const reqDistLimitB = 0.5 * s.NearestSideBeamDepth; // 相交梁深的一半
                    
                    if (distToSideBeamEdge < reqDistLimitB) {
                        isOk = false;
                        reasons.push(`大梁套管距正交小梁(${s.NearestSideBeamName})邊緣不足(${distToSideBeamEdge.toFixed(0)} < 0.5*H_minor=${reqDistLimitB.toFixed(0)})`);
                    }
                    
                    // 第三層檢核邏輯：原本會隱藏柱邊距標註的邏輯已移除，不論是否大於 1.0H，皆會保留與柱邊的標註。
                }
            }

            // 規則 6: 垂直位置 (梁頂底間距 >= 1/3H 且符合絕對底限)
            const distToTop = s.BeamTopZ - (s.SleeveZ + D / 2);
            const distToBottom = (s.SleeveZ - D / 2) - s.BeamBottomZ;
            const reqVertLimit = Math.max(H / 3.0, reqVert);
            
            if (distToTop < reqVertLimit - 1) {
                isOk = false;
                reasons.push(`距梁頂不足(${distToTop.toFixed(0)}<${reqVertLimit.toFixed(0)})`);
            }
            if (distToBottom < reqVertLimit - 1) {
                isOk = false;
                reasons.push(`距梁底不足(${distToBottom.toFixed(0)}<${reqVertLimit.toFixed(0)})`);
            }

            s.FinalStatus = isOk ? 'PASS' : 'FAIL';
            s.FinalReason = reasons.join(', ');
            
            s.CheckDetails = {
                BeamUsage: s.BeamUsage === 'Minor' ? '小梁' : '大梁',
                DistToTopStr: `${distToTop.toFixed(0)} / >=${reqVertLimit.toFixed(0)}`,
                DistToBottomStr: `${distToBottom.toFixed(0)} / >=${reqVertLimit.toFixed(0)}`,
                DistToEndStr: `${dist.toFixed(0)} / >=${reqDistLimit.toFixed(0)}`,
                DiameterStr: `${D.toFixed(0)} / ${limitD === 0 ? '禁開' : '<=' + limitD.toFixed(0)}`,
                SpacingStr: '-' 
            };
            
            activeResults.push(s);
        }

        // 3. 第二階段：相鄰套管間距檢核 (Beam-based)
        const activeBeamGroups = {};
        activeResults.forEach(r => {
            if (!activeBeamGroups[r.BeamId]) activeBeamGroups[r.BeamId] = [];
            activeBeamGroups[r.BeamId].push(r);
        });

        for (const beamId in activeBeamGroups) {
            const sleevesOnBeam = activeBeamGroups[beamId];
            // 分兩群：靠近起點的、靠近終點的
            const startGroup = sleevesOnBeam.filter(s => s.DistanceToStart <= s.DistanceToEnd);
            const endGroup = sleevesOnBeam.filter(s => s.DistanceToStart > s.DistanceToEnd);

            // 第一層標註(柱邊距)的隱藏邏輯已移除，因為 C# 會根據節點排序自動處理相鄰標註
            // 靠近起點與終點的群組排序檢核邏輯（僅做檢核，不再控制繪圖）
            startGroup.sort((a, b) => a.DistanceToStart - b.DistanceToStart);
            endGroup.sort((a, b) => a.DistanceToEnd - b.DistanceToEnd);

            // 原本的淨距檢核：依然依照起點距離統一排序來檢核兩兩淨距
            sleevesOnBeam.sort((a, b) => a.DistanceToStart - b.DistanceToStart);
            
            for (let i = 0; i < sleevesOnBeam.length; i++) {
                const s = sleevesOnBeam[i];
                if (i > 0) {
                    const prevS = sleevesOnBeam[i-1];
                    const centerDist = s.DistanceToStart - prevS.DistanceToStart;
                    const D = s.SleeveDiameter;
                    const actualSpacing = centerDist - (D / 2) - (prevS.SleeveDiameter / 2);
                    const reqSpacing = D + prevS.SleeveDiameter;
                    
                    s.CheckDetails.SpacingStr = `${actualSpacing.toFixed(0)} / >=${reqSpacing.toFixed(0)}`;
                    
                    if (actualSpacing < reqSpacing - 1) {
                        s.FinalStatus = 'FAIL';
                        const existingReason = s.FinalReason ? s.FinalReason + ', ' : '';
                        s.FinalReason = existingReason + `相鄰套管淨距不足(${actualSpacing.toFixed(0)}<${reqSpacing.toFixed(0)})`;
                    }
                }
            }
        }
        // 4. 計算通過/失敗總數並建立視覺化物件
        activeResults.forEach(s => {
            if (s.FinalStatus === 'PASS') passCount++; else failCount++;

            visualizationResults.push({
                SleeveId: s.SleeveId,
                BeamId: s.BeamId, // 永遠傳遞 BeamId 以便 C# 進行間距分組與排序
                IsOk: s.FinalStatus === 'PASS',
                Message: s.FinalReason || "PASS"
            });
        });

        // 輸出兩部分 Markdown 報告
        let md = "# 穿梁套管檢核報告\n\n";
        md += `> 本檢核完全透過 JavaScript MCP Server 執行，涵蓋開口位置、垂直位置、孔徑大小與相鄰孔距。\n\n`;
        md += `## Part 1: 穿梁套管檢核明細表 (總數: ${activeResults.length} 處, ✅ 通過: ${passCount}, ❌ 失敗: ${failCount})\n\n`;
        md += "| 套管 ID | 樓層 | 穿過的梁 ID | 梁深(H) | 梁類型 | 距梁頂(實際/規範) | 距梁底(實際/規範) | 距梁端(實際/規範) | 孔徑(D)(實際/規範) | 相鄰孔距(實際/規範) | 狀態 | 違規原因 |\n";
        md += "| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- |\n";
        
        activeResults.forEach(r => {
            const statusEmoji = r.FinalStatus === 'PASS' ? '✅' : '❌';
            const det = r.CheckDetails;
            md += `| ${r.SleeveId} | ${r.SleeveLevel} | ${r.BeamId} | ${r.BeamDepth.toFixed(0)} | ${det.BeamUsage} | ${det.DistToTopStr} | ${det.DistToBottomStr} | ${det.DistToEndStr} | ${det.DiameterStr} | ${det.SpacingStr} | ${statusEmoji} ${r.FinalStatus} | ${r.FinalReason || '-'} |\n`;
        });

        md += "\n---\n\n";
        md += `## Part 2: 已排除套管明細表 (非穿梁或疑似穿牆/穿板，共計: ${excludedResults.length} 處)\n\n`;
        md += "| 套管 ID | 樓層 | 相交的梁 ID | 梁寬 | 套管長度 | 狀態 | 排除原因 |\n";
        md += "| :--- | :--- | :--- | :--- | :--- | :--- | :--- |\n";
        
        excludedResults.forEach(r => {
            md += `| ${r.SleeveId} | ${r.SleeveLevel} | ${r.BeamId} | ${r.BeamWidth.toFixed(0)} | ${r.SleeveLength.toFixed(0)} | ⚠️ EXCLUDED | ${r.FinalReason} |\n`;
        });

        console.log('--- REPORT START ---');
        console.log(md);
        console.log('--- REPORT END ---');

        // 將報告寫入實體 Markdown 檔案
        const reportPath = path.join(__dirname, 'sleeve_report.md');
        fs.writeFileSync(reportPath, md, 'utf8');
        console.log(`明細表報告已儲存至：${reportPath}`);

        // 步驟二：呼叫 visualize_penetration 將結果同步回 Revit 上色與標註
        console.log(`發送 visualize_penetration 視覺化指令 (包含 ${visualizationResults.length} 筆資料)...`);
        ws.send(JSON.stringify({
            CommandName: 'visualize_penetration',
            Parameters: { results: visualizationResults },
            RequestId: 'vis-' + Date.now()
        }));

        ws.once('message', visData => {
            const visResp = JSON.parse(visData.toString());
            console.log('視覺化完成:', visResp.Success ? '成功' : visResp.Error);
            ws.close();
            process.exit(0);
        });
    });

    ws.send(JSON.stringify({
        CommandName: 'advanced_analyze', 
        Parameters: { Keyword: '圓形開口' }, // 只傳入過濾關鍵字
        RequestId: 'adv-' + Date.now()
    }));
});
