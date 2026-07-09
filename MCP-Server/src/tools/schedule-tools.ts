/**
 * 明細表工具 — architect, mep Profile
 */

import { Tool } from "@modelcontextprotocol/sdk/types.js";

export const scheduleTools: Tool[] = [
    {
        name: "create_view_schedule",
        description: "在 Revit 中建立一個新的視圖明細表（Schedule/Quantities）。可以指定名稱、品類以及要包含的欄位。",
        inputSchema: {
            type: "object",
            properties: {
                name: { type: "string", description: "明細表名稱" },
                category: { type: "string", description: "品類名稱（如：'Walls', 'Rooms', 'Pipes'）" },
                fields: { type: "array", items: { type: "string" }, description: "欄位名稱列表" },
            },
            required: ["name"],
        },
    },
    {
        name: "list_schedules",
        description: "列出目前專案中所有可讀取的明細表（排除樣板、圖框修訂表）。回傳每張表的 ElementId、名稱、品類、列數與欄數，供儀錶板分類與展開使用。",
        inputSchema: {
            type: "object",
            properties: {},
        },
    },
    {
        name: "read_schedule",
        description: "讀取單一明細表的完整表格內容（欄位標頭 + 逐格 body 資料，忠實呈現 Revit 畫面顯示的文字）。用 scheduleId 或 scheduleName 指定。",
        inputSchema: {
            type: "object",
            properties: {
                scheduleId: { type: "number", description: "明細表 ElementId（建議；由 list_schedules 取得）" },
                scheduleName: { type: "string", description: "明細表名稱（scheduleId 未提供時使用，支援部分比對）" },
                maxRows: { type: "number", description: "最多回傳的資料列數（預設 2000，避免回應過大）" },
            },
        },
    },
];
