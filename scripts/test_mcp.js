const { Client } = require("@modelcontextprotocol/sdk/client/index.js");
const { StdioClientTransport } = require("@modelcontextprotocol/sdk/client/stdio.js");
const path = require("path");

async function main() {
  const transport = new StdioClientTransport({
    command: "node",
    args: [path.join(__dirname, "../MCP-Server/build/index.js")],
  });

  const client = new Client({
    name: "test-client",
    version: "1.0.0",
  }, {
    capabilities: {}
  });

  await client.connect(transport);

  // Get active view
  console.log("Calling get_active_view...");
  const viewResult = await client.callTool({
    name: "get_active_view",
    arguments: {}
  });
  console.log("Active View:", JSON.stringify(viewResult, null, 2));

  // Get selected elements
  console.log("Calling get_selected_elements...");
  const selectResult = await client.callTool({
    name: "get_selected_elements",
    arguments: {}
  });
  console.log("Selected Elements:", JSON.stringify(selectResult, null, 2));

  process.exit(0);
}

main().catch(err => {
  console.error(err);
  process.exit(1);
});
