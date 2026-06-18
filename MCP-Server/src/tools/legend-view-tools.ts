import { Tool } from "@modelcontextprotocol/sdk/types.js";

export const legendViewTools: Tool[] = [
    {
        name: "list_legend_views",
        description:
            "列出目前 Revit 專案中的 Legend 視圖與其中的門窗 legend component 數量。door-window-legend-tools update 需要先由使用者選擇 viewName。",
        inputSchema: {
            type: "object",
            properties: {},
            required: [],
        },
    },
];
