/**
 * 結構分析工具 — structure Profile
 */

import { Tool } from "@modelcontextprotocol/sdk/types.js";

export const structureTools: Tool[] = [
    {
        name: "analyze_beam_penetration",
        description: "分析特定結構梁上的套管穿孔。回傳精確的幾何數據，如距離柱心長度、梁深度、開孔直徑等。",
        inputSchema: {
            type: "object",
            properties: {
                beamId: { type: "number", description: "要分析的目標結構梁 Element ID" },
                diameterParamNames: { type: "array", items: { type: "string" }, description: "可選。搜尋套管『直徑』的動態參數名稱清單（實體與類型自動 Fallback）。預設為 ['開孔直徑', '直徑', '管徑', 'Diameter', 'Size']" },
                lengthParamNames: { type: "array", items: { type: "string" }, description: "可選。搜尋套管『長度』的動態參數名稱清單。預設為 ['長度', 'Length']" },
                widthParamNames: { type: "array", items: { type: "string" }, description: "可選。搜尋梁『寬度』的動態參數名稱清單。預設為 ['b', '梁寬', 'Width']" },
                sleeveIds: { type: "array", items: { type: "number" }, description: "可選。指定檢核的套管 ID 清單，避免全區掃描" },
                linkInstanceId: { type: "number", description: "可選。連結模型的 ID" }
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
