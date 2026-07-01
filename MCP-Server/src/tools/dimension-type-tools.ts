import { Tool } from "@modelcontextprotocol/sdk/types.js";

export const dimensionTypeTools: Tool[] = [
    {
        name: "list_dimension_types",
        description:
            "列出目前 Revit 專案可用的 DimensionType。door-window-legend-tools create/update 需要先由使用者選擇 dimensionTypeName，再把對應 dimensionTypeId 傳回。",
        inputSchema: {
            type: "object",
            properties: {},
            required: [],
        },
    },
];
