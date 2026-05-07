import { Tool } from "@modelcontextprotocol/sdk/types.js";

export const titleblockAlignTools: Tool[] = [
    {
        name: "align_titleblocks_on_sheets",
        description: "批次將多個 sheet 上的 titleblock 移動到同一個 anchor 座標。可用『referenceSheetNumber』指定要拷貝某張 sheet 上 titleblock 的位置，或用『referencePositionMm』直接給絕對座標。anchor 決定要對齊 titleblock bbox 的哪個角（top-left / top-right / bottom-left / bottom-right / center）。建議先 dryRun=true 預覽 delta，確認後再實際執行。觸發條件：使用者提到 titleblock 對齊、圖框對齊、把所有 sheet 圖框對齊到 X、change_element_type 後圖框沒跟著移動、align titleblock、move titleblock to match。",
        inputSchema: {
            type: "object",
            properties: {
                referenceSheetNumber: {
                    type: "string",
                    description: "參考 sheet 編號，會抓取此 sheet 上 titleblock 在 anchor 位置的座標作為 ground truth。不可與 referencePositionMm 同時使用"
                },
                referencePositionMm: {
                    type: "object",
                    description: "直接給絕對座標（公釐，sheet 座標）。不可與 referenceSheetNumber 同時使用",
                    properties: {
                        x: { type: "number", description: "X 座標（mm）" },
                        y: { type: "number", description: "Y 座標（mm）" }
                    },
                    required: ["x", "y"]
                },
                anchor: {
                    type: "string",
                    enum: ["top-left", "top-right", "bottom-left", "bottom-right", "center"],
                    description: "對齊 titleblock bbox 的哪個角（預設 top-left）",
                    default: "top-left"
                },
                targetSheetNumbers: {
                    type: "array",
                    items: { type: "string" },
                    description: "要對齊的 sheet 編號清單。省略 = 全部 sheets（會跳過沒有 titleblock 的）"
                },
                toleranceMm: {
                    type: "number",
                    description: "若 delta 小於此公差（mm）則視為已對齊不移動，預設 0.1",
                    default: 0.1
                },
                dryRun: {
                    type: "boolean",
                    description: "若 true 則只計算不實際移動，回傳預期 delta 供確認",
                    default: false
                }
            },
            required: ["anchor"]
        }
    }
];
