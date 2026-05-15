import WebSocket from 'ws';

const ws = new WebSocket('ws://localhost:8964');

ws.on('open', async () => {
    console.log('--- REFINED BEAM PENETRATION CHECK START ---');
    
    const send = (cmd, params = {}) => new Promise(res => {
        const id = 'req-' + Date.now() + Math.random();
        ws.once('message', data => {
            const resp = JSON.parse(data.toString());
            if (resp.RequestId === id) res(resp);
        });
        ws.send(JSON.stringify({CommandName: cmd, Parameters: params, RequestId: id}));
    });

    try {
        // Step 1: Scan Beams
        const scanResp = await send('scan_penetrated_beams_in_view');
        if (!scanResp.Success) throw new Error(scanResp.Error);
        
        const beams = scanResp.Data.Beams || [];
        console.log(`Phase 1: Found ${beams.length} beams with potential penetrations.`);

        const finalResults = [];
        const excludedCount = { wall: 0 };

        for (const b of beams) {
            const beamInfoResp = await send('get_element_info', { elementId: b.BeamId, linkInstanceId: b.LinkId });
            const beamData = beamInfoResp.Data;
            const sIds = b.SleeveIds || [];

            for (const sId of sIds) {
                // Step 2: WALL & FLOOR FILTERING (NEW)
                const wallClashResp = await send('detect_clashes', { 
                    mepSource: { categories: ["PipeAccessory", "GenericModel"], filters: [{ field: "Id", operator: "equals", value: sId.toString() }] },
                    csaSource: { categories: ['Walls'] } 
                });
                
                if (wallClashResp.Success && wallClashResp.Data && wallClashResp.Data.length > 0) {
                    console.log(`  Sleeve ${sId}: EXCLUDED (Intersects with Wall)`);
                    excludedCount.wall++;
                    continue;
                }

                const floorClashResp = await send('detect_clashes', { 
                    mepSource: { categories: ["PipeAccessory", "GenericModel"], filters: [{ field: "Id", operator: "equals", value: sId.toString() }] },
                    csaSource: { categories: ['Floors'] } 
                });
                
                if (floorClashResp.Success && floorClashResp.Data && floorClashResp.Data.length > 0) {
                    console.log(`  Sleeve ${sId}: EXCLUDED (Intersects with Floor)`);
                    excludedCount.floor = (excludedCount.floor || 0) + 1;
                    continue;
                }

                // Step 3: Deep Analysis (Level Check + Rules)
                const sleeveInfoResp = await send('get_element_info', { elementId: sId });
                const sleeveData = sleeveInfoResp.Data;

                let isOk = true;
                let messages = [];

                if (sleeveData.Level !== beamData.Level) {
                    isOk = false;
                    messages.push(`樓層不符 (${sleeveData.Level} vs ${beamData.Level})`);
                }

                const finalMsg = isOk ? "合格" : messages.join(",");
                finalResults.push({ SleeveId: sId, IsOk: isOk, Message: finalMsg });
                console.log(`  Sleeve ${sId}: ${isOk ? 'PASS' : 'FAIL'} (${finalMsg})`);
            }
        }

        console.log(`\nSummary:`);
        console.log(`- Total Instances: ${beams.reduce((acc, b) => acc + b.SleeveIds.length, 0)}`);
        console.log(`- Excluded (Wall): ${excludedCount.wall}`);
        console.log(`- Excluded (Floor): ${excludedCount.floor || 0}`);
        console.log(`- Valid Beam Penetrations: ${finalResults.length}`);

        // Step 4: Visualize
        if (finalResults.length > 0) {
            console.log(`Phase 3: Visualizing ${finalResults.length} results.`);
            await send('visualize_penetration', { results: finalResults });
        }
        
        ws.close();
        process.exit(0);
    } catch (err) {
        console.error('Error:', err);
        process.exit(1);
    }
});
