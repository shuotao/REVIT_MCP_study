import { Tool } from "@modelcontextprotocol/sdk/types.js";

export const fillPatternTools: Tool[] = [
    {
        name: "list_fill_patterns",
        description: "列出專案中所有填充樣式（FillPatternElement）的名稱、ID 與 Model/Drafting 類型。在指定任何 hatch 之前應先呼叫這個工具確認名稱，不要憑印象猜——create_view_filter 的 patternName、以及材質表面樣式都吃這裡的名稱。Model 樣式隨視圖比例縮放（實際尺寸，地形與材質紋理用這種）；Drafting 樣式在圖紙上固定大小。唯讀。觸發條件：使用者提到填充樣式、hatch、剖面線、網格線、fill pattern、有哪些樣式可以選、或要設 hatch 但不知道名稱。",
        inputSchema: {
            type: "object",
            properties: {
                contains: {
                    type: "string",
                    description: "只回傳名稱包含此字串的樣式（不分大小寫），用於縮小清單，例如 earth、sand、cross"
                },
                target: {
                    type: "string",
                    enum: ["any", "model", "drafting"],
                    description: "只列出指定類型：model（隨比例縮放）、drafting（圖紙固定大小）、any（全部，預設）",
                    default: "any"
                }
            },
            required: []
        }
    }
];
