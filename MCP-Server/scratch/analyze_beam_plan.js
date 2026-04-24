import WebSocket from 'ws';

const ws = new WebSocket('ws://localhost:8964');

ws.on('open', () => {
    console.log('Connected to Revit...');
    const command = {
        CommandName: 'get_view_templates',
        Parameters: { includeDetails: true },
        RequestId: 'beam_plan_analysis'
    };
    ws.send(JSON.stringify(command));
});

ws.on('message', (data) => {
    const response = JSON.parse(data.toString());

    if (response.Success && response.Data) {
        const templates = response.Data.ViewTemplates || [];

        // Find GEP-Beam Plan
        const beamPlan = templates.find(t => t.Name === 'GEP-Beam Plan');

        if (beamPlan) {
            console.log('\n========================================');
            console.log('GEP-Beam Plan 完整資訊');
            console.log('========================================\n');
            console.log('隱藏類別總數:', beamPlan.HiddenCategoryCount);
            console.log('\n隱藏類別完整清單 (前10個):');
            if (beamPlan.HiddenCategories) {
                beamPlan.HiddenCategories.forEach((cat, i) => {
                    console.log(`  ${i + 1}. ${cat}`);
                });
            }
            console.log('\n篩選器:');
            if (beamPlan.Filters) {
                beamPlan.Filters.forEach((f, i) => {
                    console.log(`  ${i + 1}. ${f}`);
                });
            }
        }

        // Also find GEP_Review- Plan_Lower Beam for comparison
        const lowerBeam = templates.find(t => t.Name === 'GEP_Review- Plan_Lower Beam');
        if (lowerBeam) {
            console.log('\n========================================');
            console.log('GEP_Review- Plan_Lower Beam 比較');
            console.log('========================================\n');
            console.log('隱藏類別總數:', lowerBeam.HiddenCategoryCount);
            console.log('\n隱藏類別 (前10個):');
            if (lowerBeam.HiddenCategories) {
                lowerBeam.HiddenCategories.forEach((cat, i) => {
                    console.log(`  ${i + 1}. ${cat}`);
                });
            }
        }

        // Output JSON for full data
        console.log('\n\n========================================');
        console.log('完整 JSON 資料');
        console.log('========================================\n');
        console.log(JSON.stringify({ beamPlan, lowerBeam }, null, 2));
    } else {
        console.error('錯誤:', response.Error);
    }

    ws.close();
    process.exit(0);
});

ws.on('error', (err) => {
    console.error('連線錯誤:', err.message);
    process.exit(1);
});

setTimeout(() => {
    console.error('連線逾時');
    process.exit(1);
}, 10000);
