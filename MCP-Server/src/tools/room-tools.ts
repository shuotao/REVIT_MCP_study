/**
 * 房間/法規檢討工具 — architect, fire-safety Profile
 */

import { Tool } from "@modelcontextprotocol/sdk/types.js";

export const roomTools: Tool[] = [
    {
        name: "get_room_info",
        description: "取得房間詳細資訊，包含中心點座標和邊界範圍。",
        inputSchema: {
            type: "object",
            properties: {
                roomId: { type: "number", description: "房間 Element ID（選填）" },
                roomName: { type: "string", description: "房間名稱（選填）" },
            },
        },
    },
    {
        name: "get_rooms_by_level",
        description: "取得指定樓層的所有房間清單，包含名稱、編號、面積、用途等。可用於容積檢討。",
        inputSchema: {
            type: "object",
            properties: {
                level: { type: "string", description: "樓層名稱（如：1F、Level 1）" },
                includeUnnamed: { type: "boolean", description: "是否包含未命名的房間", default: true },
            },
            required: ["level"],
        },
    },
    {
        name: "get_room_daylight_info",
        description: "取得房間的採光資訊，包含居室面積、外牆開口面積、採光比例。用於建築技術規則居室採光檢討。",
        inputSchema: {
            type: "object",
            properties: {
                level: { type: "string", description: "樓層名稱（選填）" },
            },
        },
    },
    {
        name: "check_exterior_wall_openings",
        description: "依據台灣建築技術規則第45條及第110條檢討外牆開口。自動讀取地界線計算距離，以顏色標示違規。",
        inputSchema: {
            type: "object",
            properties: {
                checkArticle45: { type: "boolean", description: "檢查第45條", default: true },
                checkArticle110: { type: "boolean", description: "檢查第110條", default: true },
                colorizeViolations: { type: "boolean", description: "以顏色標示", default: true },
                exportReport: { type: "boolean", description: "匯出 JSON 報表", default: false },
                reportPath: { type: "string", description: "報表輸出路徑" },
            },
        },
    },
];
