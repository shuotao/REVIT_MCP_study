import { Tool } from "@modelcontextprotocol/sdk/types.js";

export const doorWindowLegendTools: Tool[] = [
    {
        name: "door-window-legend-tools",
        description:
            "門窗圖例工具。mode=list 列出專案中已使用的門/窗型；mode=create 以 seed Legend 複製生成 A+ managed 圖例；mode=update 更新既有門窗圖例；mode=migrate 預覽或套用 legacy item 的 A+ ownership migration；mode=scaffold_template 一鍵自動搭建空白門窗圖例樣板視圖（自動畫格線、放佔位文字與圖例）。",
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
                    enum: ["list", "create", "update", "migrate", "scaffold_template"],
                    description: "list 列出型別；create 建立新 Legend；update 更新既有 Legend；migrate 預覽或套用 legacy migration；scaffold_template 自動搭建空白圖例樣板視圖。",
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
                        "create/scaffold_template 使用的 seed Legend 視圖 ID。若缺少，工具會要求先呼叫 list_seeds 讓使用者選擇。",
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
                apply: {
                    type: "boolean",
                    default: false,
                    description:
                        "migrate 專用。false 僅預覽且不修改模型；true 只套用 preview 判定為 ready 的項目。",
                },
                itemKeys: {
                    type: "array",
                    items: {
                        type: "string",
                    },
                    description:
                        "migrate apply=true 時可選，限定本次套用的 item key；ambiguous、overlap、unresolved 仍一律跳過。",
                },
                columns: {
                    type: "number",
                    minimum: 1,
                    maximum: 20,
                    default: 7,
                    description:
                        "scaffold_template 專用。每列格位數（欄數），範圍 1..20，預設 7。",
                },
                rows: {
                    type: "number",
                    minimum: 1,
                    maximum: 20,
                    default: 3,
                    description:
                        "scaffold_template 專用。列數，範圍 1..20，預設 3。",
                },
                templateName: {
                    type: "string",
                    description:
                        "scaffold_template 專用。產生的空白樣板視圖名稱，預設 '01_門窗圖例表樣板'。",
                },
                flLineStyleName: {
                    type: "string",
                    description:
                        "scaffold_template 專用。FFL 基準線使用的細部線型名稱，預設 '中級線0.3'。",
                },
            },
            required: ["mode"],
        },
    },
];
