/**
 * 查詢 1FL 房間清單
 */

import WebSocket from 'ws';

const ws = new WebSocket('ws://localhost:8964');

ws.on('open', function () {
    console.log('=== 查詢 1FL 房間清單 ===');

    // 猜測樓層名稱為 1FL (因為二樓是 2FL)
    const command = {
        CommandName: 'get_rooms_by_level',
        Parameters: {
            level: '1FL'
        },
        RequestId: 'query_1fl_' + Date.now()
    };

    ws.send(JSON.stringify(command));
});

ws.on('message', function (data) {
    const response = JSON.parse(data.toString());

    if (response.Success) {
        console.log('\n找到', response.Data.TotalRooms, '間房間');
        console.log('樓層:', response.Data.Level);

        console.log('\n房間列表:');
        response.Data.Rooms.forEach(room => {
            console.log(`- [${room.Number}] ${room.Name} (${room.Area} m²)`);
        });
    } else {
        console.log('查詢失敗:', response.Error);
    }

    ws.close();
});

ws.on('error', function (error) {
    console.error('連線錯誤:', error.message);
});

ws.on('close', function () {
    process.exit(0);
});

setTimeout(() => {
    console.log('超時');
    ws.close();
    process.exit(1);
}, 30000);
