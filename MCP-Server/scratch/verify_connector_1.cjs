const { Client } = require('@modelcontextprotocol/sdk/client/index.js');
const { StdioClientTransport } = require('@modelcontextprotocol/sdk/client/stdio.js');

async function main() {
    const transport = new StdioClientTransport({
        command: 'node',
        args: ['h:/0_REVIT MCP/REVIT_MCP_study-main/MCP-Server/build/index.js']
    });

    const client = new Client({ name: 'verify-c1-client', version: '1.0.0' }, { capabilities: {} });
    await client.connect(transport);

    console.log('--- Connector 1 Verification ---');
    console.log('1. Fetching current selection...');
    const selectionRes = await client.callTool({ name: 'get_selected_elements', arguments: {} });

    if (selectionRes.isError) {
        console.error('Error:', selectionRes.content[0].text);
        return;
    }

    const selectionData = JSON.parse(selectionRes.content[0].text);
    if (selectionData.Count === 0) {
        console.warn('⚠️ No element selected in Revit. Please click the flange and run again.');
        return;
    }

    const element = selectionData.Elements[0];
    console.log(`Target: ${element.Name} (ID: ${element.Id})`);

    console.log('2. Analyzing Connectors...');
    const connRes = await client.callTool({
        name: 'get_connector_info',
        arguments: { elementId: element.Id }
    });

    if (connRes.isError) {
        console.error('❌ Failed to get connector info. (Make sure you recompiled and restarted Revit)');
        console.error('Error Details:', connRes.content[0].text);
        return;
    }

    const connData = JSON.parse(connRes.content[0].text);

    // Logic to identify c1
    const c1Connectors = connData.Connectors.filter(c =>
        (c.Description && c.Description.toLowerCase().includes('connect1')) ||
        (c.Description && c.Description.includes('1'))
    );

    if (c1Connectors.length > 0) {
        console.log('\n✅ SUCCESS: Found Connector 1 (c1)!');
        c1Connectors.forEach(c => {
            console.log(`   - ID: ${c.ConnectorId}`);
            console.log(`   - Description: "${c.Description}"`);
            console.log(`   - Origin: (${c.Origin.X}, ${c.Origin.Y}, ${c.Origin.Z}) mm`);
            console.log(`   - Connected: ${c.IsConnected ? 'YES' : 'NO'}`);
        });
    } else {
        console.warn('\n⚠️ Identification Warning:');
        console.warn('   No connector description contains "connect1" or "1".');
        console.warn('   Listing all available connectors for inspection:');
        connData.Connectors.forEach((c, idx) => {
            console.log(`   [Conn ${idx + 1}] ID: ${c.ConnectorId}, Desc: "${c.Description || '(Empty)'}", Type: ${c.Type}`);
        });
    }
}

main().catch(err => {
    console.error('Execution Error:', err);
    process.exit(1);
});
