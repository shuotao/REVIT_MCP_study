/**
 * 外牆開口檢討 - 只檢查地界線距離
 * 不檢查鄰棟距離（單棟建築不適用）
 */
import WebSocket from 'ws';
import fs from 'fs';

const ws = new WebSocket('ws://localhost:8964');

ws.on('open', () => {
    console.log('Connected to Revit');
    ws.send(JSON.stringify({
        CommandName: 'check_exterior_wall_openings',
        Parameters: {
            colorizeViolations: true,
            checkArticle45: true,
            checkArticle110: true,
            checkBuildingDistance: false  // 不檢查鄰棟距離
        },
        RequestId: 'boundary_check_' + Date.now()
    }));
});

ws.on('message', (data) => {
    const res = JSON.parse(data.toString());
    if (res.Success && res.Data) {
        const s = res.Data.summary;
        console.log('\n=== 外牆開口檢討結果 (僅地界線距離) ===');
        console.log(`外牆: ${s.totalWalls} | 開口: ${s.totalOpenings} | 地界線: ${s.propertyLineCount}`);
        console.log(`❌ 違規: ${s.violations} | ⚠️ 警告: ${s.warnings} | ✅ 通過: ${s.passed}`);
        console.log('\n--- 明細 ---');

        res.Data.details.forEach(d => {
            const a45 = d.article45;
            const a110 = d.article110;
            const statusIcon = a45?.OverallStatus === 2 || a110?.OverallStatus === 2 ? '❌'
                : a45?.OverallStatus === 1 || a110?.OverallStatus === 1 ? '⚠️' : '✅';

            console.log(`\n${statusIcon} ID:${d.openingId} (${d.openingType}) 面積:${d.area}m²`);
            if (a45) {
                console.log(`   第45條: 距地界線 ${a45.DistanceToBoundary?.toFixed(2)}m - ${a45.BoundaryMessage}`);
            }
            if (a110) {
                console.log(`   第110條: ${a110.BoundaryFireMessage}`);
                if (a110.RequiredFireRating > 0) {
                    console.log(`   → 需要防火時效: ${a110.RequiredFireRating}hr`);
                }
            }
        });

        // 寫入報告檔
        fs.writeFileSync('exterior_wall_boundary_report.json',
            JSON.stringify(res.Data, null, 2), 'utf8');
        console.log('\n報告已儲存: exterior_wall_boundary_report.json');
    } else {
        console.error('檢查失敗:', res.Error);
    }
    ws.close();
});

ws.on('error', (err) => {
    console.error('連線失敗:', err.message);
    process.exit(1);
});

setTimeout(() => { ws.close(); process.exit(0); }, 60000);
