#!/usr/bin/env node

/**
 * Revit MCP Server
 * Bridge between AI and Revit via MCP
 */

import { Server } from "@modelcontextprotocol/sdk/server/index.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import {
  CallToolRequestSchema,
  ListToolsRequestSchema,
  ListResourcesRequestSchema,
  ReadResourceRequestSchema,
  Tool,
} from "@modelcontextprotocol/sdk/types.js";
import { RevitSocketClient } from "./socket.js";
import { registerRevitTools, executeRevitTool } from "./tools/revit-tools.js";
import { listAppResources, readAppResource } from "./apps/register-apps.js";

// MCP Server Instance
const server = new Server(
  {
    name: "revit-mcp-server",
    version: "1.0.0",
  },
  {
    capabilities: {
      tools: {},
      // MCP Apps (io.modelcontextprotocol/ui): serve ui:// HTML resources for interactive tools.
      resources: {},
    },
  }
);

// Revit Socket Client
const revitClient = new RevitSocketClient();

/**
 * Handle List Tools Request
 */
server.setRequestHandler(ListToolsRequestSchema, async () => {
  const tools = registerRevitTools();
  console.error(`[MCP Server] Registered ${tools.length} Revit tools`);
  return { tools };
});

/**
 * Handle List Resources Request (MCP Apps: ui:// interactive UI resources)
 */
server.setRequestHandler(ListResourcesRequestSchema, async () => {
  return { resources: listAppResources() };
});

/**
 * Handle Read Resource Request (serves the ui:// App HTML)
 */
server.setRequestHandler(ReadResourceRequestSchema, async (request) => {
  const resource = readAppResource(request.params.uri);
  if (!resource) {
    throw new Error(`Resource not found: ${request.params.uri}`);
  }
  return resource;
});

/**
 * Handle Call Tool Request
 */
server.setRequestHandler(CallToolRequestSchema, async (request) => {
  console.error(`[MCP Server] Calling tool: ${request.params.name}`);
  console.error(`[MCP Server] Arguments:`, JSON.stringify(request.params.arguments, null, 2));

  try {
    // Check Revit Connection
    if (!revitClient.isConnected()) {
      // 取得目前 AI 客戶端名稱（MCP initialize 的 clientInfo.name），連線時一併回報給 Revit add-in
      const clientInfo = server.getClientVersion();
      revitClient.clientName = clientInfo?.name || "unknown";
      console.error(`[MCP Server] Revit not connected, connecting as client="${revitClient.clientName}"...`);
      await revitClient.connect();
    }

    // Execute Revit Tool
    const result = await executeRevitTool(
      request.params.name,
      request.params.arguments || {},
      revitClient
    );

    console.error(`[MCP Server] Tool executed successfully`);

    return {
      content: [
        {
          type: "text",
          text: JSON.stringify(result, null, 2),
        },
      ],
    };
  } catch (error) {
    const errorMessage = error instanceof Error ? error.message : String(error);
    console.error(`[MCP Server] Tool execution failed: ${errorMessage}`);

    return {
      content: [
        {
          type: "text",
          text: `Error: ${errorMessage}`,
        },
      ],
      isError: true,
    };
  }
});

/**
 * Start Server
 */
async function main() {
  console.error("Revit MCP Server starting...");
  console.error("Waiting for Revit Plugin...");

  const transport = new StdioServerTransport();
  await server.connect(transport);

  const configuredPort = process.env.REVIT_MCP_PORT || "8964";
  console.error("MCP Server Started");
  console.error(`Socket Server listening on ${configuredPort}`);

  // 處理優雅退出
  const shutdown = async () => {
    console.error("\n[MCP Server] 正在關閉伺服器...");
    try {
      await revitClient.disconnect();
      console.error("[MCP Server] 已中斷 Revit 連線");
    } catch (e) {
      // 忽略關閉時的錯誤
    }
    process.exit(0);
  };

  process.on("SIGINT", shutdown);
  process.on("SIGTERM", shutdown);
}

main().catch((error) => {
  console.error("Server startup failed", error);
  process.exit(1);
});