import WebSocket from 'ws';

const wsUrl = 'ws://localhost:8964';
const ws = new WebSocket(wsUrl);

function sendCommand(ws, commandName, parameters = {}) {
    const requestId = `req_${Date.now()}_${Math.random().toString(36).substr(2, 9)}`;
    const command = {
        CommandName: commandName,
        Parameters: parameters,
        RequestId: requestId,
    };
    
    return new Promise((resolve, reject) => {
        const messageHandler = (data) => {
            try {
                const response = JSON.parse(data.toString());
                if (response.RequestId === requestId) {
                    ws.off('message', messageHandler);
                    if (response.Success) {
                        resolve(response.Data);
                    } else {
                        reject(new Error(response.Error || 'Command failed'));
                    }
                }
            } catch (err) {
                // Ignore parsing errors for other messages
            }
        };
        
        ws.on('message', messageHandler);
        ws.send(JSON.stringify(command));
        
        setTimeout(() => {
            ws.off('message', messageHandler);
            reject(new Error(`Command ${commandName} timed out`));
        }, 10000);
    });
}

ws.on('open', async () => {
    console.log('Connected to Revit Plugin.');
    try {
        // 1. 取得選取元素
        console.log('Fetching selected elements in Revit...');
        const selected = await sendCommand(ws, 'get_selected_elements');
        
        // 相容 PascalCase 或 camelCase 屬性
        const elements = selected.Elements || selected.elements || [];
        if (elements.length === 0) {
            console.log('Error: No elements selected in Revit. Please select section views or section markers first.');
            ws.close();
            return;
        }
        
        // 2. 過濾出視圖或剖面相關的元素
        const viewElements = elements.filter(e => {
            const cat = e.Category || e.category || '';
            return cat === 'Views' || cat === '視圖' || cat === 'Sections' || cat === '剖面';
        });
        
        if (viewElements.length === 0) {
            console.log('Error: Selected elements do not contain any section views or section markers.');
            ws.close();
            return;
        }
        
        const viewIds = viewElements.map(e => e.Id || e.id);
        console.log(`Found ${viewIds.length} views to adjust. IDs:`, viewIds);
        
        // 3. 呼叫 adjust_section_datums 執行調整
        console.log('Calling adjust_section_datums...');
        const result = await sendCommand(ws, 'adjust_section_datums', { viewIds });
        
        console.log('Execution result:', JSON.stringify(result, null, 2));
    } catch (err) {
        console.error('Error during execution:', err.message);
    } finally {
        ws.close();
        console.log('Disconnected.');
    }
});

ws.on('error', (err) => {
    console.error('WebSocket connection error:', err.message);
});
