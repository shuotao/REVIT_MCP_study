/**
 * 建立 MCP 製作的明細表
 */
const { RevitSocketClient } = require('../build/socket.js');

async function main() {
    console.log('🔌 Connecting to Revit MCP...');
    const client = new RevitSocketClient('localhost', 11111);
    
    try {
        await client.connect();
        console.log('✅ Connected');

        const params = {
            name: 'MCP製作的明細表',
            fields: ['Family', '族群'] // 同時提供英文與中文名稱以增加相容性
        };

        console.log('📊 Creating schedule...');
        const result = await client.sendCommand('create_view_schedule', params);
        
        if (result.success) {
            console.log('✨ Success!');
            console.log(JSON.stringify(result.data, null, 2));
        } else {
            console.error('❌ Failed:', result.error);
        }
    } catch (err) {
        console.error('💥 Error:', err.message);
    } finally {
        client.disconnect();
    }
}

main();
