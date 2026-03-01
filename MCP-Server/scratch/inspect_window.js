
import { RevitSocketClient } from '../build/socket.js';

async function inspectWindow() {
    const client = new RevitSocketClient('localhost', 8964);

    try {
        console.log('🔌 Connecting to Revit...');
        await client.connect();

        // DIRECTLY TARGET WINDOW 256005
        let targetWindowId = 256005;
        console.log(`🔍 Inspecting Specific Window ID: ${targetWindowId}`);
        const res = await client.sendCommand('get_element_info', { elementId: targetWindowId });

        if (!res.success) {
            throw new Error(`Failed to get element info: ${res.error}`);
        }

        const info = res.data;
        console.log(`\n📋 Window Info:`);
        console.log(`Name: ${info.Name}`);
        console.log(`Type: ${info.Type}`);
        console.log(`Category: ${info.Category}`);

        // Dump ALL parameters to console to find the right ones
        console.log(`\n📏 Instance Parameters:`);
        info.Parameters.forEach(p => {
            console.log(`- ${p.Name}: ${p.Value} (Type: ${p.Type})`);
        });

        // Also print all params to file just in case
        console.log('\n(Full parameter list dumped to debug_params.json)');

        let dump = info;

        // Check Type Parameters
        if (info.Type) {
            const typeIdParam = info.Parameters.find(p => p.Name === "類型 ID" || p.Name === "Type ID");
            let typeId = typeIdParam ? typeIdParam.Value : null;

            if (typeId) {
                console.log(`\n🔍 Inspecting Type ID: ${typeId}`);
                const typeRes = await client.sendCommand('get_element_info', { elementId: parseInt(typeId) });
                if (typeRes.success) {
                    console.log(`\n📏 Type Parameters:`);
                    typeRes.data.Parameters.forEach(p => {
                        console.log(`- ${p.Name}: ${p.Value} (Type: ${p.Type})`);
                    });

                    dump.TypeParameters = typeRes.data.Parameters;
                }
            }
        }

        const fs = await import('fs');
        fs.writeFileSync('debug_params.json', JSON.stringify(dump, null, 2));

    } catch (error) {
        console.error('❌ Error:', error.message);
    } finally {
        client.disconnect();
    }
}

inspectWindow();
