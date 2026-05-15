import WebSocket from 'ws';
import fs from 'fs';

const ws = new WebSocket('ws://localhost:8964');

ws.on('open', async () => {
    console.log('--- DEBUG BATCH START ---');
    
    const send = (cmd, params = {}) => new Promise(res => {
        const id = 'req-' + Date.now();
        ws.once('message', data => res(JSON.parse(data.toString())));
        ws.send(JSON.stringify({CommandName: cmd, Parameters: params, RequestId: id}));
    });

    try {
        const scanResp = await send('scan_penetrated_beams_in_view');
        const beams = scanResp.Data.Beams || [];
        console.log(`Step 1: Found ${beams.length} potential beams.`);

        const visualizationResults = [];

        for (const b of beams) {
            console.log(`Checking Beam ID: ${b.BeamId} (Link: ${b.LinkId})`);
            
            const beamInfoResp = await send('get_element_info', { elementId: b.BeamId, linkInstanceId: b.LinkId });
            if (!beamInfoResp.Success) {
                console.log(`  BeamInfo Failed: ${beamInfoResp.Error}`);
                continue;
            }
            const beamData = beamInfoResp.Data;
            console.log(`  Beam Name: ${beamData.Name}, Level: ${beamData.Level}`);

            const sIds = b.SleeveIds || [];
            console.log(`  Sleeve Count: ${sIds.length}`);

            for (const sId of sIds) {
                const sleeveInfoResp = await send('get_element_info', { elementId: sId });
                if (!sleeveInfoResp.Success) {
                    console.log(`    Sleeve ${sId} Info Failed`);
                    continue;
                }
                const sleeveData = sleeveInfoResp.Data;
                console.log(`    Sleeve: ${sId}, Level: ${sleeveData.Level}`);

                let isOk = true;
                let messages = [];

                // Logic check
                if (sleeveData.Level !== beamData.Level) {
                    console.log(`    FAIL: Level Mismatch (${sleeveData.Level} vs ${beamData.Level})`);
                    isOk = false;
                    messages.push(`樓層不符`);
                }

                // ... other checks simplified for debugging ...
                const finalMsg = isOk ? "合格" : messages.join(",");
                visualizationResults.push({ SleeveId: sId, IsOk: isOk, Message: finalMsg });
            }
        }

        console.log(`Step 2: Sending ${visualizationResults.length} results to Revit.`);
        await send('visualize_penetration', { results: visualizationResults });
        
        ws.close();
        process.exit(0);
    } catch (err) {
        console.error('Error:', err);
        process.exit(1);
    }
});
