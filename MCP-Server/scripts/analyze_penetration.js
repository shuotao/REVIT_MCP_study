import WebSocket from 'ws';

const ws = new WebSocket('ws://localhost:8964');

async function sendCommand(commandName, parameters = {}) {
    return new Promise((resolve, reject) => {
        const requestId = 'req-' + Date.now();
        const command = {
            CommandName: commandName,
            Parameters: parameters,
            RequestId: requestId
        };
        
        const onMessage = (data) => {
            const response = JSON.parse(data.toString());
            if (response.RequestId === requestId) {
                ws.off('message', onMessage);
                resolve(response);
            }
        };
        
        ws.on('message', onMessage);
        ws.send(JSON.stringify(command));
        
        setTimeout(() => {
            ws.off('message', onMessage);
            reject(new Error(`Timeout waiting for ${commandName}`));
        }, 10000);
    });
}

ws.on('open', async () => {
    try {
        console.log('Connected to Revit');
        
        // 1. Get selected sleeve
        const selResp = await sendCommand('get_selected_elements');
        if (!selResp.Success || selResp.Data.Count === 0) {
            console.log('No elements selected.');
            process.exit(0);
        }
        
        const sleeve = selResp.Data.Elements[0];
        console.log(`Analyzing Sleeve: ${sleeve.Name} (ID: ${sleeve.Id})`);
        
        // 2. Get detailed info (including Location)
        const infoResp = await sendCommand('get_element_info', { elementId: sleeve.Id });
        const sleeveInfo = infoResp.Data;
        console.log('Sleeve Location:', JSON.stringify(sleeveInfo.Location));
        
        // 3. Find intersecting beams (Structural Framing)
        // Note: For simplicity, I'll search for all beams in active view and check distance or use Revit-side intersection if available.
        // There is a 'detect_clashes' command in CommandExecutor.cs
        console.log('Searching for intersecting beams...');
        const clashResp = await sendCommand('detect_clashes', { 
            elementId: sleeve.Id,
            targetCategory: 'Structural Framing'
        });
        
        if (!clashResp.Success || !clashResp.Data || clashResp.Data.length === 0) {
            console.log('No intersecting beams found.');
        } else {
            const clash = clashResp.Data[0];
            const beamId = clash.ClashElementId;
            console.log(`Found Beam: ${clash.ClashElementName} (ID: ${beamId})`);
            
            // 4. Get Beam info
            const beamInfoResp = await sendCommand('get_element_info', { elementId: beamId });
            const beamInfo = beamInfoResp.Data;
            
            console.log('--- ANALYSIS DATA ---');
            console.log(JSON.stringify({
                sleeve: sleeveInfo,
                beam: beamInfo,
                clash: clash
            }, null, 2));
        }
        
        ws.close();
        process.exit(0);
    } catch (err) {
        console.error('Error during analysis:', err.message);
        process.exit(1);
    }
});
