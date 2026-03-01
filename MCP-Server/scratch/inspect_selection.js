import { RevitSocketClient } from '../build/socket.js';

async function inspectSelection() {
    const client = new RevitSocketClient('localhost', 8964);

    try {
        console.log('🔌 Connecting to Revit...');
        await client.connect();

        console.log('🔍 Getting current selection...');
        const res = await client.sendCommand('get_selection', {});

        if (res.success) {
            const selection = res.data;
            if (selection && selection.length > 0) {
                console.log(`✅ Selected ${selection.length} elements.`);

                // Get info for the first selected element
                const firstId = selection[0];
                console.log(`\n🧐 Inspecting Element ID: ${firstId}`);

                const infoRes = await client.sendCommand('get_element_info', { elementId: firstId });
                if (infoRes.success) {
                    console.log(JSON.stringify(infoRes.data, null, 2));
                } else {
                    console.error(`❌ Failed to get info: ${infoRes.error}`);
                }
            } else {
                console.log('⚠️  No elements selected. Please select the Property Line in Revit.');
            }
        } else {
            console.error(`❌ Failed to get selection: ${res.error}`);
        }

    } catch (error) {
        console.error('❌ Error:', error);
    } finally {
        client.disconnect();
        process.exit(0);
    }
}

inspectSelection();
