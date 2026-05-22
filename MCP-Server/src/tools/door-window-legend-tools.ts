import { Tool } from "@modelcontextprotocol/sdk/types.js";

export const doorWindowLegendTools: Tool[] = [
    {
        name: "door-window-legend-tools",
        description:
            "門窗圖例工具。mode=list 會列出專案中已放置實例使用到的 door/window types。mode=create 會建立門表或窗表。create 若缺少 seedLegendViewId，會回傳 awaiting_seed_selection；若缺少或提供了無效的 layoutDirection / maxPerLine，會回傳 awaiting_layout_preferences 或 awaiting_valid_layout_preferences。assistant 必須停下來詢問使用者，不得自動補 seed 或排版參數。",
        inputSchema: {
            type: "object",
            properties: {
                targetType: {
                    type: "string",
                    enum: ["door", "window"],
                    description: "目標類型：door 建立門表，window 建立窗表。",
                },
                mode: {
                    type: "string",
                    enum: ["list", "create"],
                    description: "list 列出已使用類型；create 建立圖例表。",
                },
                layoutDirection: {
                    type: "string",
                    enum: ["horizontal", "vertical"],
                    description: "create 使用的排版方向。",
                },
                maxPerLine: {
                    type: "number",
                    minimum: 1,
                    description: "create 使用的每列或每欄最大數量，必須大於等於 1。",
                },
                seedLegendViewId: {
                    type: "number",
                    description:
                        "create 使用的 seed Legend 視圖 ID。若未提供，tool 會回傳 awaiting_seed_selection，要求先呼叫 list_seeds 並等待使用者選擇。",
                },
            },
            required: ["targetType", "mode"],
        },
    },
];
