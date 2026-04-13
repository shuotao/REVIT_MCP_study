import { Tool } from "@modelcontextprotocol/sdk/types.js";

export const sheetTools: Tool[] = [
    {
        name: "get_all_sheets",
        description: "取得專案中所有的圖紙清單，包含 ID、編號與名稱。",
        inputSchema: { type: "object", properties: {} },
    },
    {
        name: "get_titleblocks",
        description: "取得專案中所有可用的圖框（Title Blocks）類型。",
        inputSchema: { type: "object", properties: {} },
    },
    {
        name: "create_sheets",
        description: "依據指定的清單批次建立空的圖紙。",
        inputSchema: {
            type: "object",
            properties: {
                titleBlockId: { type: "number", description: "圖框類型的 Element ID" },
                sheets: {
                    type: "array",
                    items: {
                        type: "object",
                        properties: {
                            number: { type: "string", description: "圖紙編號（如 A101）" },
                            name: { type: "string", description: "圖紙名稱（如 一樓平面圖）" },
                        },
                        required: ["number", "name"],
                    },
                    description: "要建立的圖紙清單",
                },
            },
            required: ["titleBlockId", "sheets"],
        },
    },
    {
        name: "auto_renumber_sheets",
        description: "自動掃描專案中所有帶有 -1 後綴的圖紙（例如 ARB-D0417-1），並將其合併至主序列中（變成 ARB-D0418），後續編號會自動順延。",
        inputSchema: { type: "object", properties: {} },
    },
    {
        name: "get_viewport_map",
        description: "取得專案中所有視埠（Viewport）與圖紙（Sheet）的對應關係。可用於查詢特定視圖被放置在哪張圖紙上。",
        inputSchema: { type: "object", properties: {} },
    },
    {
        name: "get_sheet_viewport_details",
        description: "取得指定圖紙上所有視埠的詳細資訊，包含中心點座標、邊界框（MinX/MinY/MaxX/MaxY）、寬度與高度（mm）。若不指定 sheetId，則使用當前作用視圖（必須是圖紙）。",
        inputSchema: {
            type: "object",
            properties: {
                sheetId: { type: "number", description: "圖紙的 Element ID（選填，不指定則使用當前作用圖紙）" },
            },
        },
    },
    {
        name: "arrange_viewports_on_sheet",
        description: "依指定順序排列圖紙上的視埠（僅限 DraftingView）。支援水平或垂直排列，邊緣對齊（edge-to-edge）。第一個視埠的位置作為錨點。可用 viewNames（view 名稱陣列）或 viewportIds（視埠 ID 陣列）指定順序，二擇一。",
        inputSchema: {
            type: "object",
            properties: {
                viewNames: {
                    type: "array",
                    items: { type: "string" },
                    description: "依排列順序的 view 名稱陣列（與 viewportIds 二擇一）。工具會在圖紙上查找對應的 DraftingView viewport。",
                },
                viewportIds: {
                    type: "array",
                    items: { type: "number" },
                    description: "依排列順序的視埠 ID 陣列（與 viewNames 二擇一）",
                },
                direction: {
                    type: "string",
                    enum: ["horizontal", "vertical"],
                    description: "排列方向：horizontal（水平，預設）或 vertical（垂直）",
                },
                gapMm: {
                    type: "number",
                    description: "視埠之間的間距（mm），預設 0（邊緣對齊）",
                },
                alignY: {
                    type: "string",
                    enum: ["top", "center", "bottom"],
                    description: "垂直對齊方式（水平排列時有效）：top / center（預設）/ bottom",
                },
                sheetId: {
                    type: "number",
                    description: "圖紙的 Element ID（選填，不指定則使用當前作用圖紙）",
                },
            },
        },
    },
    {
        name: "scale_drafting_view_width",
        description: "縮放圖紙上所有 DraftingView 中表格的寬度（僅 X 軸），高度不變。以每個 view 的左邊緣為錨點，將所有 DetailCurve 和 TextNote 的 X 座標按比例縮放。",
        inputSchema: {
            type: "object",
            properties: {
                scaleFactor: {
                    type: "number",
                    description: "寬度縮放比例（例如 0.9 表示縮小到 90%，1.1 表示放大到 110%）。預設 0.9",
                },
                viewNames: {
                    type: "array",
                    items: { type: "string" },
                    description: "只處理指定名稱的 DraftingView（選填，不指定則處理圖紙上所有 DraftingView）",
                },
                sheetId: {
                    type: "number",
                    description: "圖紙的 Element ID（選填，不指定則使用當前作用圖紙）",
                },
            },
        },
    },
    {
        name: "scale_drafting_view_height",
        description: "縮放圖紙上所有 DraftingView 中表格的行高（僅 Y 軸），寬度不變。以每個 view 的上邊緣為錨點，將所有 DetailCurve 和 TextNote 的 Y 座標按比例縮放。",
        inputSchema: {
            type: "object",
            properties: {
                scaleFactor: {
                    type: "number",
                    description: "行高縮放比例（例如 1.1 表示放大到 110%，0.9 表示縮小到 90%）。預設 1.1",
                },
                viewNames: {
                    type: "array",
                    items: { type: "string" },
                    description: "只處理指定名稱的 DraftingView（選填，不指定則處理圖紙上所有 DraftingView）",
                },
                sheetId: {
                    type: "number",
                    description: "圖紙的 Element ID（選填，不指定則使用當前作用圖紙）",
                },
            },
        },
    },
    {
        name: "move_text_notes_in_views",
        description:
            "在多個 DraftingView 中，依文字內容子字串搜尋 TextNote 並批次平移。適用於跨圖面統一調整法規文字位置。",
        inputSchema: {
            type: "object",
            properties: {
                textMatch: {
                    type: "string",
                    description:
                        "要搜尋的 TextNote 文字內容（子字串比對，不區分大小寫）",
                },
                deltaXMm: {
                    type: "number",
                    description:
                        "水平位移量（mm），正值向右、負值向左。預設 0",
                },
                deltaYMm: {
                    type: "number",
                    description:
                        "垂直位移量（mm），正值向上、負值向下。預設 0",
                },
                viewNames: {
                    type: "array",
                    items: { type: "string" },
                    description:
                        "只在指定名稱的 DraftingView 中搜尋（選填，不指定則搜尋全部 DraftingView）",
                },
                dryRun: {
                    type: "boolean",
                    description:
                        "設為 true 時僅搜尋並回報結果，不實際移動。預設 false",
                },
            },
            required: ["textMatch"],
        },
    },
];
