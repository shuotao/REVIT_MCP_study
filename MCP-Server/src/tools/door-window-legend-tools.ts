import { Tool } from "@modelcontextprotocol/sdk/types.js";

export const doorWindowLegendTools: Tool[] = [
    {
        name: "door-window-legend-tools",
        description:
            "門窗圖例工具。mode=list 列出專案中已使用的門/窗型；mode=create 以 seed Legend 複製生成新圖例；mode=update 更新既有門窗圖例。create/update 會要求使用者明確選擇 layoutDirection、maxPerLine、dimensionTypeId，create 另需 seedLegendViewId，update 另需 legendViewId。",
        inputSchema: {
            type: "object",
            properties: {
                targetType: {
                    type: "string",
                    enum: ["door", "window"],
                    description: "目標類型：door 產生門圖例，window 產生窗圖例。",
                },
                mode: {
                    type: "string",
                    enum: ["list", "create", "update"],
                    description: "list 列出型別；create 建立新 Legend；update 更新既有 Legend。",
                },
                layoutDirection: {
                    type: "string",
                    enum: ["horizontal", "vertical"],
                    description: "create/update 的排列方向。",
                },
                maxPerLine: {
                    type: "number",
                    minimum: 1,
                    description: "create/update 每列或每欄最多放幾個項目，必須大於等於 1。",
                },
                seedLegendViewId: {
                    type: "number",
                    description:
                        "create 使用的 seed Legend 視圖 ID。若缺少，工具會要求先呼叫 list_seeds 讓使用者選擇。",
                },
                legendViewId: {
                    type: "number",
                    description:
                        "update 要更新的既有 Legend 視圖 ID。若缺少，工具會要求先呼叫 list_legend_views 讓使用者選擇。",
                },
                dimensionTypeId: {
                    type: "number",
                    description:
                        "門窗圖例尺寸標註使用的 DimensionType ID。若缺少，工具會要求先呼叫 list_dimension_types 讓使用者選擇。",
                },
            },
            required: ["mode"],
        },
    },
];
