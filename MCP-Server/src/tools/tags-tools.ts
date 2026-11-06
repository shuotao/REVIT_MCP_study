/**
 * 標籤查詢工具 — 一次讀取全專案所有標籤資料
 */

import { Tool } from "@modelcontextprotocol/sdk/types.js";

export const tagsTools: Tool[] = [
    {
        name: "get_all_tags",
        description:
            "一次讀取全專案所有標籤（Tag）資料。涵蓋 IndependentTag（門/窗/梁/柱/牆/材料/多類別標籤）與空間標籤（房間 RoomTag、面積 AreaTag、空間 SpaceTag）。" +
            "每筆重點回傳：標籤頭座標 TagHeadPosition{x,y,z}（mm）、標註對象 TaggedElements（哪根梁/柱/房間，含 ElementId、類別、族群、類型、Mark；被標註者若在連結模型會標示來源與引線末端座標）、TagText 顯示文字、HasLeader、Orientation、所在視圖 ViewId/ViewName。" +
            "可用 viewName 只掃單一視圖、category 篩選標籤類別（子字串比對，如「門」「柱」「房間」）、includeSpatialTags 關閉空間標籤、maxCount 限制數量。",
        inputSchema: {
            type: "object",
            properties: {
                viewName: {
                    type: "string",
                    description: "只回傳指定視圖名稱內的標籤（選填；預設掃全專案所有視圖）",
                },
                category: {
                    type: "string",
                    description: "依標籤所屬類別名稱做子字串篩選（選填，如「門標籤」「結構柱標籤」「房間標籤」）",
                },
                includeSpatialTags: {
                    type: "boolean",
                    description: "是否包含房間/面積/空間標籤（預設 true）",
                },
                maxCount: {
                    type: "number",
                    description: "最大回傳標籤數量（預設 5000）",
                },
            },
        },
    },
];
