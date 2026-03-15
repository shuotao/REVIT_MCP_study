const { RevitSocketClient } = require('../build/socket.js');
const client = new RevitSocketClient();

async function run() {
    await client.connect();
    try {
        console.log('--- 正在查詢 Georg Fischer 法蘭族群類型 ---');
        // 使用一個通用的元素查詢，搭配詳細資訊過濾
        const response = await client.sendCommand('get_column_types', { material: 'PIF' });
        // 註：我之前在 get_column_types 寫了過濾邏輯，可以用來偷看族群
        console.log(JSON.stringify(response.data, null, 2));
    } catch (e) {
        console.error('查詢失敗:', e.message);
    } finally {
        process.exit(0);
    }
}

run();
