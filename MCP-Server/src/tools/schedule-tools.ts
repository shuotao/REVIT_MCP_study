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
];
