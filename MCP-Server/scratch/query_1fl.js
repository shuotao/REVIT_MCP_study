/**
 * ?¥è©¢ 1FL ?¿é?æ¸…å–®
 */

import WebSocket from 'ws';

const ws = new WebSocket('ws://localhost:11111');

ws.on('open', function () {
    console.log('=== ?¥è©¢ 1FL ?¿é?æ¸…å–® ===');

    // ?œæ¸¬æ¨“å±¤?ç¨±??1FL (? ç‚ºäºŒæ???2FL)
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
        console.log('\n?¾åˆ°', response.Data.TotalRooms, '?“æˆ¿??);
        console.log('æ¨“å±¤:', response.Data.Level);

        console.log('\n?¿é??—è¡¨:');
        response.Data.Rooms.forEach(room => {
            console.log(`- [${room.Number}] ${room.Name} (${room.Area} mÂ²)`);
        });
    } else {
        console.log('?¥è©¢å¤±æ?:', response.Error);
    }

    ws.close();
});

ws.on('error', function (error) {
    console.error('????¯èª¤:', error.message);
});

ws.on('close', function () {
    process.exit(0);
});

setTimeout(() => {
    console.log('è¶…æ?');
    ws.close();
    process.exit(1);
}, 30000);

