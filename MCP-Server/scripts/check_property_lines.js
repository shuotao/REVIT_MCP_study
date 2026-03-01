import { RevitSocketClient } from '../build/socket.js';

async function checkPropertyLines() {
    const client = new RevitSocketClient('localhost', 8964);

    try {
        console.log('🔌 Connecting to Revit...');
        await client.connect();

        console.log('🔍 Querying Property Lines...');
        // Try query_elements
        const res = await client.sendCommand('query_elements', {
            category: 'OST_SiteProperty', // Try BuiltInCategory
            maxCount: 10
        });

        if (res.success && res.data.Elements && res.data.Elements.length > 0) {
            console.log(`✅ Found ${res.data.Elements.length} Property Lines.`);
            console.log(JSON.stringify(res.data.Elements[0], null, 2));
        } else {
            console.log('⚠️  No Property Lines found with category "Property Lines". Trying "OST_PropertyLines"...');
            const res2 = await client.sendCommand('query_elements', {
                category: 'OST_PropertyLines',
                maxCount: 10
            });
            if (res2.success && res2.data.Elements && res2.data.Elements.length > 0) {
                console.log(`✅ Found ${res2.data.Elements.length} Property Lines (OST).`);
            } else {
                console.log('❌ No Property Lines found. Please create Property Lines in Revit (Massing & Site > Property Line).');
            }
        }

    } catch (error) {
        console.error('❌ Error:', error);
    } finally {
        client.disconnect();
        process.exit(0);
    }
}

checkPropertyLines();
