const { Client } = require('@modelcontextprotocol/sdk/client/index.js');
const { StdioClientTransport } = require('@modelcontextprotocol/sdk/client/stdio.js');

async function main() {
    const transport = new StdioClientTransport({
        command: 'node',
        args: ['h:/0_REVIT MCP/REVIT_MCP_study-main/MCP-Server/build/index.js']
    });

    const client = new Client({ name: 'connector-check-client', version: '1.0.0' }, { capabilities: {} });
    await client.connect(transport);

    console.log('Fetching current selection in Revit...');
    const selectionRes = await client.callTool({ name: 'get_selected_elements', arguments: {} });

    if (selectionRes.isError) {
        console.error('Error getting selection:', selectionRes.content[0].text);
        return;
    }

    const selectionData = JSON.parse(selectionRes.content[0].text);
    if (selectionData.Count === 0) {
        console.log('No element selected. Please select the flange in Revit first.');
        return;
    }

    const element = selectionData.Elements[0];
    console.log(`Selected Element: ${element.Name} (ID: ${element.Id}, Category: ${element.Category})`);
    console.log('Querying connector information...');

    const connRes = await client.callTool({
        name: 'get_connector_info',
        arguments: {
            elementId: element.Id
        }
    });

    if (connRes.isError) {
        console.error('Failed to get connector info:', connRes.content[0].text);
    } else {
        const connData = JSON.parse(connRes.content[0].text);
        console.log('\n--- Connector Information ---');
        console.log(`Element: ${connData.ElementName} (ID: ${connData.ElementId})`);
        console.log(`Total Connectors: ${connData.ConnectorCount}\n`);

        connData.Connectors.forEach((conn, index) => {
            console.log(`[Connector ${index + 1}]`);
            console.log(`  ID: ${conn.ConnectorId}`);
            console.log(`  Type: ${conn.Type}`);
            console.log(`  Description: ${conn.Description || '(None)'}`);
            console.log(`  Connected: ${conn.IsConnected ? 'YES' : 'NO'}`);
            console.log(`  Shape: ${conn.Shape}`);
            console.log(`  Origin: (${conn.Origin.X}, ${conn.Origin.Y}, ${conn.Origin.Z}) mm`);
            console.log('----------------------------');
        });
    }
}

main().catch(err => {
    console.error('Error:', err);
    process.exit(1);
});
