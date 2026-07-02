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
    }
];
