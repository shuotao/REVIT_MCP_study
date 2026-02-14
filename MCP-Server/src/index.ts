#!/usr/bin/env node

/**
 * Revit MCP Server
 * 提供 AI 與 Revit 之間的 MCP 協定橋接
 */

import { Server } from "@modelcontextprotocol/sdk/server/index.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import {
    CallToolRequestSchema,
    ListToolsRequestSchema,
    Tool,
} from "@modelcontextprotocol/sdk/types.js";
import { RevitSocketClient } from "./socket.js";
import { registerRevitTools, executeRevitTool } from "./tools/revit-tools.js";

// MCP 伺服器實例
const server = new Server(
    {
        name: "revit-mcp-server",
        version: "1.0.0",
    },
    {
        capabilities: {
            tools: {},
        },
    }
);

// Revit Socket 客戶端
const revitClient = new RevitSocketClient();

/**
 * 處理工具列表請求
 */
server.setRequestHandler(ListToolsRequestSchema, async () => {
    const tools = registerRevitTools();
    console.error(`[MCP Server] 已註冊 ${tools.length} 個 Revit 工具`);
    return { tools };
});

/**
 * 處理工具呼叫請求
 */
server.setRequestHandler(CallToolRequestSchema, async (request) => {
    console.error(`[MCP Server] 執行工具: ${request.params.name}`);
    console.error(`[MCP Server] 參數:`, JSON.stringify(request.params.arguments, null, 2));

    try {
        // 檢查 Revit 連線狀態
        if (!revitClient.isConnected()) {
            console.error("[MCP Server] Revit 未連線，嘗試連線...");
            await revitClient.connect();
        }

        // 執行 Revit 工具
        const result = await executeRevitTool(
            request.params.name,
            request.params.arguments || {},
            revitClient
        );

        console.error(`[MCP Server] 工具執行成功`);

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
        console.error(`[MCP Server] 工具執行失敗: ${errorMessage}`);

        return {
            content: [
                {
                    type: "text",
                    text: `錯誤: ${errorMessage}`,
                },
            ],
            isError: true,
        };
    }
});

/**
 * 啟動伺服器
 */
async function main() {
    console.error("Revit MCP Server 啟動中...");
    console.error("等待 Revit Plugin 連線...");

    const transport = new StdioServerTransport();
    await server.connect(transport);

    console.error("MCP Server 已準備就緒");
    console.error("Socket 伺服器監聽埠號: 8964");

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
    console.error("伺服器啟動失敗:", error);
    process.exit(1);
});
