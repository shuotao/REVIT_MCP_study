import { Tool } from "@modelcontextprotocol/sdk/types.js";

export const listSeedsTools: Tool[] = [
    {
        name: "list_seeds",
        description:
            "列出 seed 候選資料。這版支援 seedType=legend，會回傳所有非樣板 Legend 視圖與其中 Legend Component 數量。此 tool 的結果必須顯示給使用者選擇；assistant 不得根據清單自動決定 seed，也不得在使用者未選 ViewName 前自動重試 create。",
        inputSchema: {
            type: "object",
            properties: {
                seedType: {
                    type: "string",
                    enum: ["legend"],
                    description: "seed 類型，目前僅支援 legend。",
                },
            },
            required: ["seedType"],
        },
    },
];
