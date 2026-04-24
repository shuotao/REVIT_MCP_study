import WebSocket from 'ws';

const ws = new WebSocket('ws://localhost:8964');

ws.on('open', () => {
    const command = {
        CommandName: 'get_all_levels',
        Parameters: {},
        RequestId: 'get_levels_001'
    };
    ws.send(JSON.stringify(command));
});

ws.on('message', (data) => {
    const response = JSON.parse(data.toString());
    console.log('=== Revit 專案樓層清單 ===\n');

    if (response.Success && response.Data) {
        const levels = response.Data;
        if (Array.isArray(levels)) {
            levels.forEach((level, i) => {
                console.log(`${i + 1}. ${level.Name || level.name}`);
                console.log(`   標高: ${level.Elevation || level.elevation} mm`);
                console.log(`   Element ID: ${level.Id || level.id}`);
                console.log('');
            });
            console.log(`共 ${levels.length} 個樓層`);
        } else {
            console.log(JSON.stringify(response.Data, null, 2));
        }
    } else {
        console.log('錯誤:', response.Error || '未知錯誤');
    }

    ws.close();
    process.exit(0);
});

ws.on('error', (err) => {
    console.error('連線錯誤:', err.message);
    console.error('請確認 Revit 已開啟並啟動 MCP 服務');
    process.exit(1);
});

setTimeout(() => {
    console.error('連線逾時');
    process.exit(1);
}, 5000);
