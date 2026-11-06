import { Tool } from "@modelcontextprotocol/sdk/types.js";

/** 四個 View Range 平面共用的參數形狀 */
const planeSchema = (planeLabel: string, allowUnlimited: boolean) => ({
    type: "object" as const,
    description: planeLabel,
    properties: {
        level: {
            type: "string",
            description: allowUnlimited
                ? "樓層名稱，或特殊值 Unlimited / Level Above / Level Below / Current"
                : "樓層名稱，或特殊值 Level Above / Level Below / Current（剖切面不可設 Unlimited）"
        },
        offsetMm: {
            type: "number",
            description: "相對該樓層的偏移 (mm)，負值往下"
        }
    }
});

export const viewRangeTools: Tool[] = [
    {
        name: "get_view_range",
        description: "讀取平面類視圖的視圖範圍（View Range）四個平面設定：topClip（頂部）、cutPlane（剖切面）、bottomClip（底部）、viewDepth（視圖深度），各含所指樓層與偏移量(mm)。排查「元素在模型裡、類別也開著、卻在平面圖看不到」時的第一順位檢查——最常見成因就是該元素落在 View Depth 之外（例如地形、基礎位於樓板下方）。同時回報該視圖的 View Range 是否被視圖樣板鎖住。唯讀。觸發條件：使用者提到視圖範圍、view range、視圖深度、view depth、剖切面高度、cut plane、平面圖看不到某個東西、平面圖顯示不出來。",
        inputSchema: {
            type: "object",
            properties: {
                viewId: {
                    type: "number",
                    description: "目標平面視圖的 ElementId（省略 = 目前作用中的視圖）"
                }
            },
            required: []
        }
    },
    {
        name: "set_view_range",
        description: "設定平面類視圖的視圖範圍。四個平面各自獨立指定，只給要改的即可。level 可填樓層名稱，或四個特殊值：Unlimited（無限延伸，View Depth 最常用）、Level Above、Level Below、Current（視圖關聯樓層）。offsetMm 是相對該樓層的偏移，負值往下。典型用途：把 viewDepth 設成 Unlimited，讓樓板下方的地形或基礎在平面圖顯示出來。視圖範圍被視圖樣板鎖住時會直接報錯而不是靜默失敗；完成後會回讀實際落地的值作為驗收證據。觸發條件：使用者提到改視圖範圍、調 view range、改視圖深度、view depth 設成無限、讓平面圖看到下面的東西、剖切面調高調低。",
        inputSchema: {
            type: "object",
            properties: {
                viewId: {
                    type: "number",
                    description: "目標平面視圖的 ElementId（省略 = 目前作用中的視圖）"
                },
                topClip: planeSchema("頂部平面", true),
                cutPlane: planeSchema("剖切面", false),
                bottomClip: planeSchema("底部平面", true),
                viewDepth: planeSchema("視圖深度。要讓樓板下方的地形或基礎顯示出來，通常設 level 為 Unlimited", true)
            },
            required: []
        }
    }
];
