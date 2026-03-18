/**
 * 視覺化工具 — 圖形覆寫、視圖樣版
 * 所有 Profile 都可選用
 */

import { Tool } from "@modelcontextprotocol/sdk/types.js";

export const visualizationTools: Tool[] = [
    {
        name: "override_element_graphics",
        description: "在指定視圖中覆寫元素的圖形顯示（填滿顏色、圖樣、線條顏色等）。",
        inputSchema: {
            type: "object",
            properties: {
                elementId: { type: "number", description: "要覆寫的元素 ID" },
                viewId: { type: "number", description: "視圖 ID（若不指定則使用當前視圖）" },
                surfaceFillColor: {
                    type: "object",
                    description: "表面填滿顏色 RGB (0-255)",
                    properties: {
                        r: { type: "number", minimum: 0, maximum: 255 },
                        g: { type: "number", minimum: 0, maximum: 255 },
                        b: { type: "number", minimum: 0, maximum: 255 },
                    },
                },
                surfacePatternId: { type: "number", description: "表面填充圖樣 ID（-1 = 實心填滿）", default: -1 },
                lineColor: {
                    type: "object",
                    description: "線條顏色 RGB（可選）",
                    properties: {
                        r: { type: "number", minimum: 0, maximum: 255 },
                        g: { type: "number", minimum: 0, maximum: 255 },
                        b: { type: "number", minimum: 0, maximum: 255 },
                    },
                },
                transparency: { type: "number", description: "透明度 (0-100)", minimum: 0, maximum: 100, default: 0 },
            },
            required: ["elementId"],
        },
    },
    {
        name: "clear_element_override",
        description: "清除元素在指定視圖中的圖形覆寫。",
        inputSchema: {
            type: "object",
            properties: {
                elementId: { type: "number", description: "要清除覆寫的元素 ID" },
                elementIds: { type: "array", items: { type: "number" }, description: "批次操作" },
                viewId: { type: "number", description: "視圖 ID" },
            },
        },
    },
    {
        name: "get_view_templates",
        description: "取得專案中所有視圖樣版的完整設定。可用於視圖樣版比對與整併分析。",
        inputSchema: {
            type: "object",
            properties: {
                includeDetails: { type: "boolean", description: "是否包含詳細設定", default: true },
            },
        },
    },
];
