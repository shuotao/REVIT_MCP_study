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
            "讀取 Excel 檔案中的 named table（或整張 worksheet），僅回傳落在 worksheet 列印範圍 (PrintArea) 內的儲存格內容。沒有設定列印範圍的 worksheet 會被略過。",
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
            },
            required: ["filePath"],
        },
    },
];
