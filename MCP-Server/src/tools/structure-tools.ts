/**
 * 結構分析工具 — structural Profile
 * 來源 fork: seven777/main:MCP-Server/src/tools/structure-tools.ts
 */

import { Tool } from "@modelcontextprotocol/sdk/types.js";

export const structureTools: Tool[] = [
    {
        name: "analyze_beam_penetration",
        description: "分析單一結構梁上的穿孔（套管）狀況。回傳梁的地位（大梁/小梁）、梁深，以及各個套管的距離、直徑、上下邊距與形狀資訊。可選 linkInstanceId 查詢連結模型內的梁。",
        inputSchema: {
            type: "object",
            properties: {
                beamId: { type: "number", description: "結構梁的元素 ID" },
                linkInstanceId: { type: "number", description: "連結模型 ID（選填，搭配 scan_penetrated_beams_in_view 回傳的 LinkId 使用）" },
            },
            required: ["beamId"],
        },
    },
    {
        name: "scan_penetrated_beams_in_view",
        description: "掃描目前視圖中所有被套管（Sleeves）穿過的結構梁。支援連結模型——同時掃描主文件與所有 RevitLinkInstance 內的結構梁。回傳包含梁 ID、連結模型 ID 及穿過該梁的套管清單。",
        inputSchema: {
            type: "object",
            properties: {
                targetLevel: { type: "string", description: "限定樓層名稱（選填，未指定則掃所有樓層）" },
            },
        },
    },
    {
        name: "visualize_penetration",
        description: "在 Revit 圖面上視覺化穿梁檢核結果。會自動將套管變更顏色（綠=合格 / 紅=不合格）並在旁邊放置 TextNote 標籤。",
        inputSchema: {
            type: "object",
            properties: {
                results: {
                    type: "array",
                    description: "檢核結果清單（通常來自 analyze_beam_penetration 的批次輸出）",
                    items: {
                        type: "object",
                        properties: {
                            SleeveId: { type: "number" },
                            IsOk: { type: "boolean" },
                            Message: { type: "string" },
                            Position: {
                                type: "object",
                                properties: {
                                    X: { type: "number" },
                                    Y: { type: "number" },
                                    Z: { type: "number" },
                                },
                            },
                        },
                    },
                },
            },
        },
    },
    {
        name: "get_src_beam_mapping",
        description: "偵測專案中所有 RC 梁與鋼梁重疊的狀況，建立 SRC 映射清單。用於識別需要優先套用鋼梁原則的區域。⚠️ Wave 1 為 stub，完整實作於 Wave 2。",
        inputSchema: {
            type: "object",
            properties: {},
        },
    },
];
