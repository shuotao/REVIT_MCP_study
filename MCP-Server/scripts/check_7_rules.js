import WebSocket from 'ws';

const ws = new WebSocket('ws://localhost:8964');

ws.on('open', () => {
    ws.once('message', data => {
        const resp = JSON.parse(data.toString());
        if (!resp.Success || !resp.Data.Success) {
            console.log('Error:', resp.Error || resp.Data.Error);
            process.exit(1);
        }

        const rawResults = resp.Data.Results;
        
        // Group by BeamId to check adjacent sleeves
        const beamGroups = {};
        rawResults.forEach(r => {
            if (!beamGroups[r.BeamId]) beamGroups[r.BeamId] = [];
            beamGroups[r.BeamId].push(r);
        });

        const finalResults = [];
        let passCount = 0;
        let failCount = 0;

        for (const beamId in beamGroups) {
            const sleevesOnBeam = beamGroups[beamId];
            // Sort by distance to start point
            sleevesOnBeam.sort((a, b) => a.DistanceToStart - b.DistanceToStart);

            for (let i = 0; i < sleevesOnBeam.length; i++) {
                const s = sleevesOnBeam[i];
                let status = 'PASS';
                let reason = '';
                
                const H = s.BeamDepth;
                const D = s.SleeveDiameter;
                const dist = s.MinDistance;

                // 規則 2, 3, 4: 距離與孔徑
                if (dist < 1.0 * H) {
                    status = 'FAIL';
                    reason += `[禁開區] 距柱邊不足(${dist.toFixed(0)} < 1.0H=${(1.0*H).toFixed(0)}); `;
                } else if (dist >= 1.0 * H && dist < 1.5 * H) {
                    if (D > H / 4.0) {
                        status = 'FAIL';
                        reason += `[限制區] 孔徑過大(${D.toFixed(0)} > 1/4H=${(H/4).toFixed(0)}); `;
                    }
                } else {
                    // dist >= 1.5H (包含 >= 2.0H 一般區)
                    if (D > H / 3.0) {
                        status = 'FAIL';
                        reason += `[一般區] 孔徑過大(${D.toFixed(0)} > 1/3H=${(H/3).toFixed(0)}); `;
                    }
                }

                // 規則 6: 垂直位置 (梁頂底間距 >= 1/3H 且 >= 200mm)
                // 這裡大梁統一以 200mm 計算
                const distToTop = s.BeamTopZ - (s.SleeveZ + D / 2);
                const distToBottom = (s.SleeveZ - D / 2) - s.BeamBottomZ;
                const reqVert = Math.max(H / 3.0, 200);
                
                if (distToTop < reqVert - 1) {
                    status = 'FAIL';
                    reason += `[垂直] 距梁頂不足(${distToTop.toFixed(0)} < ${reqVert.toFixed(0)}); `;
                }
                if (distToBottom < reqVert - 1) {
                    status = 'FAIL';
                    reason += `[垂直] 距梁底不足(${distToBottom.toFixed(0)} < ${reqVert.toFixed(0)}); `;
                }

                // 規則 7: 相鄰套管邊距 (至少 D1 + D2)
                if (i > 0) {
                    const prevS = sleevesOnBeam[i-1];
                    const centerDist = s.DistanceToStart - prevS.DistanceToStart;
                    const clearSpacing = centerDist - (D / 2) - (prevS.SleeveDiameter / 2);
                    const requiredSpacing = D + prevS.SleeveDiameter;
                    if (clearSpacing < requiredSpacing - 1) {
                        status = 'FAIL';
                        reason += `[相鄰] 淨距不足(${clearSpacing.toFixed(0)} < ${requiredSpacing.toFixed(0)}); `;
                    }
                }

                if (status === 'FAIL') failCount++; else passCount++;
                s.FinalStatus = status;
                s.FinalReason = reason.trim();
                finalResults.push(s);
            }
        }

        let md = "# 穿梁套管檢核報告 (終極架構 7規則引擎版)\n\n";
        md += `> 本檢核完全透過 JavaScript MCP Server 執行，涵蓋分區開口限制、垂直位置淨距與相鄰孔距。\n\n`;
        md += `### 檢核摘要: 總案件: ${finalResults.length} 處, ✅ 通過: ${passCount}, ❌ 失敗: ${failCount}\n\n`;
        md += "| 套管 ID | 樓層 | 穿過的梁 ID | 梁深(H) | 孔徑(D) | 狀態 | 違規原因 |\n";
        md += "| :--- | :--- | :--- | :--- | :--- | :--- | :--- |\n";
        
        finalResults.forEach(r => {
            const statusEmoji = r.FinalStatus === 'PASS' ? '✅' : '❌';
            md += `| ${r.SleeveId} | ${r.SleeveLevel} | ${r.BeamId} | ${r.BeamDepth.toFixed(0)} | ${r.SleeveDiameter.toFixed(0)} | ${statusEmoji} ${r.FinalStatus} | ${r.FinalReason || '-'} |\n`;
        });

        console.log('--- REPORT START ---');
        console.log(md);
        console.log('--- REPORT END ---');

        ws.close();
        process.exit(0);
    });

    ws.send(JSON.stringify({
        CommandName: 'advanced_analyze', 
        Parameters: { Keyword: '圓形開口' }, // 只傳入過濾關鍵字，複雜邏輯交給 JS
        RequestId: 'adv-' + Date.now()
    }));
});
