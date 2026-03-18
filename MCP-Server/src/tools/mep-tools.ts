/**
 * MEP 管線工具 — mep Profile
 */

import { Tool } from "@modelcontextprotocol/sdk/types.js";

export const mepTools: Tool[] = [
    {
        name: "get_connector_info",
        description: "取得 MEP 元素（管、風管、線管等）的接頭（Connector）資訊，包含座標、連接狀態、形狀等。",
        inputSchema: {
            type: "object",
            properties: {
                elementId: { type: "number", description: "要查詢的 MEP 元素 ID" },
            },
            required: ["elementId"],
        },
    },
    {
        name: "add_pipe_cap",
        description: "在管件的未連線端安裝管帽或法蘭。自動尋找開放的接頭並連接。",
        inputSchema: {
            type: "object",
            properties: {
                pipeId: { type: "number", description: "管件的元素 ID" },
                familyName: { type: "string", description: "要安裝的管帽/法蘭族群名稱" },
            },
            required: ["pipeId", "familyName"],
        },
    },
];
