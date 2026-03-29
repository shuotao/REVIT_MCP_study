import { Tool } from "@modelcontextprotocol/sdk/types.js";

/**
 * 標註與編號工具模組
 */
export const markingTools: Tool[] = [
    {
        name: "set_element_mark",
        description: "標註元素 — 為指定的 Revit 元素設定標記（Mark）參數內容。",
        inputSchema: {
            type: "object",
            properties: {
                elementId: { type: "number", description: "Revit 元素 ID" },
                markValue: { type: "string", description: "要設定的標記字串（如 'M01', 'A-001'）" },
            },
            required: ["elementId", "markValue"],
        },
    },
    {
        name: "batch_set_marks",
        description: "批量自動標註 — 為一系列元素依照指定的前綴與起始序號進行編號。",
        inputSchema: {
            type: "object",
            properties: {
                elementIds: { type: "array", items: { type: "number" }, description: "Revit 元素 ID 列表" },
                prefix: { type: "string", description: "標註前綴（如 'W-', 'P'）", default: "" },
                startNumber: { type: "number", description: "起始序號", default: 1 },
                digits: { type: "number", description: "流水號位數（如 2 代表 01, 02）", default: 2 },
            },
            required: ["elementIds"],
        },
    },
];
