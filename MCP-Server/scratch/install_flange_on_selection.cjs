const { Client } = require('@modelcontextprotocol/sdk/client/index.js');
const { StdioClientTransport } = require('@modelcontextprotocol/sdk/client/stdio.js');
const path = require('path');

async function main() {
    const transport = new StdioClientTransport({
        command: 'node',
        args: ['h:/0_REVIT MCP/REVIT_MCP_study-main/MCP-Server/build/index.js']
    });

    const client = new Client({ name: 'pipe-task-client', version: '1.0.0' }, { capabilities: {} });
    await client.connect(transport);

    console.log('Checking selection...');
    const selectionRes = await client.callTool({ name: 'get_selected_elements', arguments: {} });

    if (selectionRes.isError) {
        console.error('Error getting selection:', selectionRes.content[0].text);
        return;
    }

    const selectionData = JSON.parse(selectionRes.content[0].text);
    console.log(`Found ${selectionData.Count} selected elements.`);

    const pipe = selectionData.Elements.find(e =>
        e.Category === 'Pipes' || e.Category === '管' || e.Category === 'Ducts' || e.Category === '風管'
    );

    if (!pipe) {
        console.log('No pipe or duct selected. Please select a pipe in Revit first.');
        return;
    }

    console.log(`Target Pipe found: ${pipe.Name} (ID: ${pipe.Id})`);
    console.log('Installing flange: PIF_PROGEF Plus bf - outlet flange adaptor_GF');

    const capRes = await client.callTool({
        name: 'add_pipe_cap',
        arguments: {
            pipeId: pipe.Id,
            familyName: 'PIF_PROGEF Plus bf - outlet flange adaptor_GF'
        }
    });

    if (capRes.isError) {
        console.error('Installation failed:', capRes.content[0].text);
    } else {
        console.log('Installation successful!');
        console.log(capRes.content[0].text);
    }
}

main().catch(err => {
    if (err.message.includes('ECONNREFUSED')) {
        console.error('Connection failed: Please make sure Revit MCP Service is running on Port 11111.');
    } else {
        console.error('Error:', err);
    }
    process.exit(1);
});
