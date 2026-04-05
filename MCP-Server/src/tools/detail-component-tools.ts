import { Tool } from "@modelcontextprotocol/sdk/types.js";

export const detailComponentTools: Tool[] = [
    {
        name: "get_detail_components",
        description: "查詢專案中的詳圖元件（Detail Components）實例。可依族群名稱篩選，回傳每個實例的 ID、族群名稱、類型名稱、所屬視圖及參數。",
        inputSchema: {
            type: "object",
            properties: {
                familyName: { type: "string", description: "族群名稱篩選（選填，模糊比對）" },
            },
        },
    },
    {
        name: "create_detail_component_type",
        description: "指定詳圖項目族群，複製並設定新名稱（圖紙編號-圖紙名稱-詳圖名稱），同時自動填寫 詳圖圖號、圖說名稱、詳圖名稱、詳圖編號 等類型參數。",
        inputSchema: {
            type: "object",
            properties: {
                sheetNumber: { type: "string", description: "目標圖紙編號（如 A101）" },
                detailName: { type: "string", description: "詳圖名稱（使用者輸入的新詳圖名稱）" },
                familyName: { type: "string", description: "要複製的基礎詳圖項目族群名稱（選填，若未提供則預設尋找 'AE-圖號'）" },
                detailNumber: { type: "string", description: "詳圖編號（選填，預設為 '1'）" },
            },
            required: ["sheetNumber", "detailName"],
        },
    },
    {
        name: "sync_detail_component_numbers",
        description: "自動同步所有詳圖元件的類型參數（詳圖圖號、圖說名稱）與其所在圖紙的編號和名稱。僅更新類型名稱已匹配圖紙編號的元件，不會影響共用標準詳圖。",
        inputSchema: { type: "object", properties: {} },
    },
    {
        name: "list_family_symbols",
        description: "列出專案中的 FamilySymbol（族群類型）。可依名稱篩選，回傳 ID、類型名稱、族群名稱、類別。用於查詢可用的詳圖項目族群。",
        inputSchema: {
            type: "object",
            properties: {
                filter: { type: "string", description: "名稱篩選關鍵字（選填，模糊比對族群名稱或類型名稱）" },
            },
        },
    },
];
