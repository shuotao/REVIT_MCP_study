const WebSocket = require('ws');

const ws = new WebSocket('ws://localhost:11111');

ws.on('open', () => {
    console.log('Connected to Revit');
    const requestId = `req_${Date.now()}`;
    // Using query_elements with a generic search if possible, 
    // but the C# Code for query_elements is limited.
    // Let's try to search by OST_Schedules again but with a fallback.
    const command = {
        CommandName: 'get_all_views',
        Parameters: {},
        RequestId: requestId
    };
    ws.send(JSON.stringify(command));

    setTimeout(() => {
        console.log('Timeout waiting for response');
        ws.close();
    }, 15000);
});

ws.on('message', (data) => {
    try {
        const response = JSON.parse(data.toString());
        if (response.Success) {
            const result = response.Data;
            const views = result.Views || [];
            // Many schedules have ViewType 'Schedule' or 'Internal' or 'ProjectBrowser'
            // Let's see all ViewTypes returned
            const types = [...new Set(views.map(v => v.ViewType))];
            console.log('ViewTypes in project:', types);

            const schedules = views.filter(v =>
                v.ViewType.toLowerCase().includes('schedule') ||
                v.ViewType === 'ColumnSchedule' ||
                v.ViewType === 'PanelSchedule'
            );

            console.log(`RESULT: Found ${schedules.length} schedules.`);
            schedules.forEach(s => console.log(`- [${s.ElementId}] ${s.Name} (${s.ViewType})`));
        } else {
            console.error('Error:', response.Error);
        }
    } catch (e) {
        console.error('Parse Error:', e.message);
    }
    ws.close();
});

ws.on('error', (err) => {
    console.error('Socket Error:', err.message);
});

