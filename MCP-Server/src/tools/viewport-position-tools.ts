import { Tool } from "@modelcontextprotocol/sdk/types.js";

export const viewportPositionTools: Tool[] = [
    {
        name: "debug_viewport_geometry",
        description: "診斷工具：dump 一個 viewport 的所有座標資料（boxOutline、cropbox、view origin、scale 等）。用於反推 Revit 內部 cropbox vs viewport 的座標映射規則。指定 viewportId 或 viewId 其中之一。",
        inputSchema: {
            type: "object",
            properties: {
                viewportId: {
                    type: "number",
                    description: "Viewport ElementId（直接指定 viewport）"
                },
                viewId: {
                    type: "number",
                    description: "View ElementId（會自動找對應 viewport）"
                }
            }
        }
    },
    {
        name: "position_viewports_on_sheet",
        description: "批次將 viewports 移動到 sheet 上的指定位置。位置是用「view 的某個錨點 + sheet 上某個參考點 + offset」三段組合定義。例：『所有平面圖的左上角』移到『title block 左上角 X+10mm Y+10mm』。識別目標 viewports 有多種方式（viewportIds / viewIds / viewNames / viewNameContains），可結合 sheetNumbers 和 viewTypeFilter 進一步過濾。建議搭配 dryRun=true 先預覽位移，再實際執行。觸發條件：使用者提到 viewport 位置、視埠定位、移動視埠到圖框、view 對齊圖框、批次定位、align viewport to titleblock、move viewport。",
        inputSchema: {
            type: "object",
            properties: {
                viewAnchor: {
                    type: "string",
                    enum: ["top-left", "top-right", "bottom-left", "bottom-right", "center"],
                    description: "View 上的錨點（哪一角/中心會被定位到目標位置）"
                },
                sheetReference: {
                    type: "string",
                    enum: [
                        "titleblock-top-left", "titleblock-top-right",
                        "titleblock-bottom-left", "titleblock-bottom-right",
                        "sheet-top-left", "sheet-top-right",
                        "sheet-bottom-left", "sheet-bottom-right"
                    ],
                    description: "Sheet 上的參考點。titleblock-* 用 title block 邊界；sheet-* 用 sheet 印刷區域邊界"
                },
                offsetRightMm: {
                    type: "number",
                    description: "X 方向 offset（公釐）。正值往右、負值往左",
                    default: 0
                },
                offsetDownMm: {
                    type: "number",
                    description: "Y 方向 offset（公釐）。正值往下、負值往上（直覺 sheet layout 慣例）",
                    default: 0
                },
                viewportIds: {
                    type: "array",
                    items: { type: "number" },
                    description: "目標 Viewport ElementId 清單（最精確識別方式）"
                },
                viewIds: {
                    type: "array",
                    items: { type: "number" },
                    description: "目標 View ElementId 清單（會找對應的 viewport）"
                },
                viewNames: {
                    type: "array",
                    items: { type: "string" },
                    description: "目標 view 名稱清單（精確匹配）"
                },
                viewNameContains: {
                    type: "string",
                    description: "view 名稱包含此字串（substring match, case-insensitive）"
                },
                sheetNumbers: {
                    type: "array",
                    items: { type: "string" },
                    description: "限定 sheet 編號清單（與其他識別方式組合使用）"
                },
                viewTypeFilter: {
                    type: "array",
                    items: { type: "string" },
                    description: "限定 view type, 例: ['FloorPlan', 'CeilingPlan']"
                },
                dryRun: {
                    type: "boolean",
                    description: "若 true 則只計算不實際移動，回傳預期 delta 供確認",
                    default: false
                }
            },
            required: ["viewAnchor", "sheetReference"]
        }
    },
    {
        name: "move_viewport_titles",
        description: "批次移動 viewport 標題（label line + text）的位置。支援兩種模式：(1) below-view-center: 將標題置中放在 view 正下方（自動計算 cropbox 底邊中心 + gapMm 距離）；(2) reset: LabelOffset 還原為 XYZ.Zero（Revit 預設位置，通常在 viewport 左下角）。識別目標 viewports 與 position_viewports_on_sheet 相同：viewportIds / viewIds / viewNames / viewNameContains，可加 sheetNumbers / viewTypeFilter 過濾。觸發條件：使用者提到 viewport 標題位置、視埠標題、view title 對齊、把標題挪到視圖下方、reset title position。建議搭配 dryRun=true 先預覽。",
        inputSchema: {
            type: "object",
            properties: {
                mode: {
                    type: "string",
                    enum: ["below-view-center", "reset"],
                    description: "below-view-center: 標題置中於 view 正下方（透過 cropbox 計算）；reset: 還原預設位置",
                    default: "below-view-center"
                },
                gapMm: {
                    type: "number",
                    description: "標題距離 view cropbox 底邊的距離（mm，僅 below-view-center 模式有效）",
                    default: 5
                },
                viewportIds: {
                    type: "array",
                    items: { type: "number" },
                    description: "目標 Viewport ElementId 清單（最精確識別方式）"
                },
                viewIds: {
                    type: "array",
                    items: { type: "number" },
                    description: "目標 View ElementId 清單（會找對應的 viewport）"
                },
                viewNames: {
                    type: "array",
                    items: { type: "string" },
                    description: "目標 view 名稱清單（精確匹配）"
                },
                viewNameContains: {
                    type: "string",
                    description: "view 名稱包含此字串（substring match, case-insensitive）"
                },
                sheetNumbers: {
                    type: "array",
                    items: { type: "string" },
                    description: "限定 sheet 編號清單（與其他識別方式組合使用）"
                },
                viewTypeFilter: {
                    type: "array",
                    items: { type: "string" },
                    description: "限定 view type, 例: ['FloorPlan', 'CeilingPlan']"
                },
                dryRun: {
                    type: "boolean",
                    description: "若 true 則只計算不實際移動，回傳預期 delta 供確認",
                    default: false
                }
            }
        }
    }
];
