import { Tool } from "@modelcontextprotocol/sdk/types.js";

export const textNoteTools: Tool[] = [
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
