import { Tool } from "@modelcontextprotocol/sdk/types.js";

export const legendTools: Tool[] = [
    {
        name: "create_legends",
        description:
            "從 seed legend 批次複製出多個 legend 視圖。Revit API 不支援憑空建立 legend，必須先在樣板/專案中手動放置一個 seed legend（建議命名為 _SEED_BLANK）。",
        inputSchema: {
            type: "object",
            properties: {
                names: {
                    type: "array",
                    items: { type: "string" },
                    description: "要建立的 legend 視圖名稱清單，例如 ['A01','A02']",
                },
                seedName: {
                    type: "string",
                    description:
                        "指定 seed legend 名稱（選填）。未指定時優先找名稱以 _SEED_ 開頭的 legend，找不到再退到任一 legend。",
                },
                duplicateMode: {
                    type: "string",
                    enum: ["empty", "withDetailing"],
                    description:
                        "複製模式：empty=只複製空 view（純框線/文字場景）；withDetailing=連同詳圖元件一起複製（預設）。",
                },
            },
            required: ["names"],
        },
    },
    {
        name: "read_excel_tables",
        description:
            "讀取 Excel 檔案中的 named table（或整張 worksheet），僅回傳落在 worksheet 列印範圍 (PrintArea) 內的儲存格內容。沒有設定列印範圍的 worksheet 會被略過。預設不傳 borders 陣列以節省 token；若需粗細資訊請傳 includeBorders=true。summary=true 模式只回每張表的維度與是否含非 Thin 邊框，適合大量 sheet 的初探階段。",
        inputSchema: {
            type: "object",
            properties: {
                filePath: {
                    type: "string",
                    description: "Excel 檔案的絕對路徑（.xlsx）",
                },
                mode: {
                    type: "string",
                    enum: ["named_table", "worksheet"],
                    description:
                        "named_table=讀取每個 worksheet 內的命名 table（預設）；worksheet=每個 worksheet 視為一張表，名稱為 worksheet 名稱。兩種模式皆只讀列印範圍。",
                },
                tableNames: {
                    type: "array",
                    items: { type: "string" },
                    description: "選填：只讀取指定名稱的 table（不分大小寫）",
                },
                includeBorders: {
                    type: "boolean",
                    description: "是否回傳每格的 borders 陣列（[top,right,bottom,left]）。預設 false（不回，節省 token）。",
                    default: false,
                },
                summary: {
                    type: "boolean",
                    description: "summary 模式：每張表只回 {name, rowCount, colCount, mergeCount, hasNonThinBorders, clippedByPrintArea}，不含 rows/borders。預設 false。",
                    default: false,
                },
            },
            required: ["filePath"],
        },
    },
    {
        name: "import_excel_to_drafting_views",
        description:
            "原子化命令：讀 Excel → 對每張有列印範圍的 worksheet 建立 Drafting View → 畫框線（merge-aware）→ 寫文字。固定 viewScale=1:1、textSize=3mm、rowH=4.5mm、colW=30mm。Claude 端只送檔案路徑與 sheet 清單，所有座標計算、Revit Transaction 都在 C# 內完成，token 用量極低（25 sheet ≈ 1.5K）。已自動正規化 \\r\\n → \\n。**Overwrite 時會在 doc.Delete 之前先擷取舊 view 在所有 sheet 上的 viewport 中心點，重建後自動 Viewport.Create 放回原位**，使用者不需再到 UI 手動補放。回應新增 ViewportsRestored 計數與 ViewportFailures 列表（個別失敗不會影響整批 Commit）。",
        inputSchema: {
            type: "object",
            properties: {
                filePath: {
                    type: "string",
                    description: "Excel 檔案絕對路徑（.xlsx）",
                },
                sheets: {
                    type: "array",
                    items: { type: "string" },
                    description: "選填：只匯入指定名稱的 worksheet（不分大小寫）。不給=匯入所有有列印範圍的 sheet。",
                },
                namingPattern: {
                    type: "string",
                    description: "Drafting View 命名樣式，可用 {name} 取代為 worksheet 名。預設 '{name}'。例：'面積計算表_{name}'。",
                },
                overwrite: {
                    type: "boolean",
                    description: "同名 view 已存在時是否刪除重建。預設 true。",
                    default: true,
                },
            },
            required: ["filePath"],
        },
    },
];
