import { RevitSocketClient } from '../build/socket.js';

async function inspect() {
    const client = new RevitSocketClient('localhost', 8964);
    try {
        await client.connect();
        const res = await client.sendCommand('get_element_info', { elementId: 256085 });
        if (res.success) {
            console.log('Instance Params:', res.data.Parameters.map(p => `${p.Name} (${p.Type}) = ${p.Value}`));

            // Check Type
            const typeIdParam = res.data.Parameters.find(p => p.Name === '類型 ID' || p.Name === 'Type ID');
            if (typeIdParam) {
                const typeRes = await client.sendCommand('get_element_info', { elementId: parseInt(typeIdParam.Value) });
                if (typeRes.success) {
                    console.log('Type Params:', typeRes.data.Parameters.map(p => `${p.Name} (${p.Type}) = ${p.Value}`));
                }
            }
        } else {
            console.log('Failed:', res.error);
        }
    } catch (e) { console.error(e); }
    finally { client.disconnect(); }
}
inspect();
