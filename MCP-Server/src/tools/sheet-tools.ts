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
];
