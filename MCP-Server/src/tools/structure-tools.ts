/**
 * 結構分析工具 — structure Profile
 */

import { Tool } from "@modelcontextprotocol/sdk/types.js";

export const structureTools: Tool[] = [
    {
        name: "analyze_beam_penetration",
        description: "分析單一結構梁上的穿孔（套管）狀況。回傳梁的地位（大梁/小梁）、梁深，以及各個套管的距離、直徑、上下邊距與形狀資訊。",
        inputSchema: {
            type: "object",
            properties: {
                beamId: { type: "number", description: "結構梁的元素 ID" },
            },
            required: ["beamId"],
        },
    },
    {
        name: "scan_penetrated_beams_in_view",
        description: "掃描目前視圖中所有被套管（Sleeves）穿過的結構梁。回傳包含梁 ID、連結模型 ID 及穿過該梁的套管數量的清單。",
        inputSchema: {
            type: "object",
            properties: {},
        },
    },
    {
        name: "visualize_penetration",
        description: "在 Revit 圖面上視覺化穿梁檢核結果。會自動將套管變更顏色（合格/不合格）並在旁邊放置標籤文字。",
        inputSchema: {
            type: "object",
            properties: {
                results: {
                    type: "array",
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
                                    Z: { type: "number" }
                                }
                            }
                        }
                    }
                }
            }
        },
    },
    {
        name: "get_src_beam_mapping",
        description: "偵測專案中所有 RC 梁與鋼梁重疊的狀況，建立 SRC 映射清單。用於識別需要優先套用鋼梁原則的區域。",
        inputSchema: {
            type: "object",
            properties: {},
        },
    },
];
