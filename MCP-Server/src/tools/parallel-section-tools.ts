/**
 * 牆面平行剖面視圖工具
 * 依牆面上的套管 / 開孔位置，自動建立裁剪好的剖面視圖
 */

import { Tool } from "@modelcontextprotocol/sdk/types.js";

export const parallelSectionTools: Tool[] = [
    {
        name: "create_parallel_section_view",
        description: "依牆面開孔位置自動建立平行剖面視圖。自動偵測牆上的套管族群，以開孔群組為單位裁剪剖面範圍；支援長牆分割（相鄰開孔 > 3m 則各自建立獨立剖面）、房間方向自動判定。無開孔時建立整面牆的全寬剖面。",
        inputSchema: {
            type: "object",
            properties: {
                wallId: {
                    type: "number",
                    description: "目標牆的 Element ID",
                },
                wallLinkId: {
                    type: "number",
                    description: "牆所在的連結模型 LinkInstanceId（來自 get_linked_models）。主模型的牆不需傳此參數。",
                },
                offset: {
                    type: "number",
                    description: "剖面線距牆面的偏移量（mm），預設 1mm（緊貼牆面）",
                    default: 1,
                },
                splitLongWalls: {
                    type: "boolean",
                    description: "長牆分割：相鄰開孔間距 > 3m 時各自建立獨立剖面視圖",
                    default: true,
                },
                viewNamePrefix: {
                    type: "string",
                    description: "視圖名稱前綴，預設「牆面套管剖面」",
                    default: "牆面套管剖面",
                },
                autoCrop: {
                    type: "boolean",
                    description: "是否依開孔實際範圍自動裁剪剖面框（上下加 marginVertical，左右加 marginHorizontal）",
                    default: true,
                },
                directionLogic: {
                    type: "string",
                    enum: ["auto", "inside_out", "outside_in"],
                    description: "剖面觀察方向。auto = 自動找房間側朝外看；inside_out = 從內向外；outside_in = 從外向內",
                    default: "auto",
                },
                scale: {
                    type: "number",
                    description: "視圖比例尺（如 50 = 1:50），預設 50",
                    default: 50,
                },
                marginHorizontal: {
                    type: "number",
                    description: "左右留白（mm），預設 500",
                    default: 500,
                },
                marginVertical: {
                    type: "number",
                    description: "上下留白（mm），預設 200",
                    default: 200,
                },
            },
            required: ["wallId"],
        },
    },
    {
        name: "batch_create_wall_sections",
        description: "批次建立牆面套管剖面：以當前視圖的 BoundingBox 範圍過濾指定連結模型中的牆，再比對所有 MEP 連結模型的管附件（圓形套管）與風管附件（矩形開口），只對有開孔的牆建立剖面視圖。",
        inputSchema: {
            type: "object",
            properties: {
                wallLinkId: {
                    type: "number",
                    description: "牆所在的連結模型 LinkInstanceId（來自 get_linked_models）",
                },
                splitLongWalls: {
                    type: "boolean",
                    description: "相鄰開孔間距 > 3m 時自動分割為多個剖面，預設 true",
                    default: true,
                },
                viewNamePrefix: {
                    type: "string",
                    description: "視圖名稱前綴，預設「牆面套管剖面」",
                    default: "牆面套管剖面",
                },
                autoCrop: {
                    type: "boolean",
                    description: "依開孔範圍自動裁剪剖面框，預設 true",
                    default: true,
                },
                directionLogic: {
                    type: "string",
                    enum: ["auto", "inside_out", "outside_in"],
                    description: "剖面觀察方向，預設 auto（自動找房間側）",
                    default: "auto",
                },
                scale: {
                    type: "number",
                    description: "視圖比例尺，預設 50（1:50）",
                    default: 50,
                },
                marginHorizontal: {
                    type: "number",
                    description: "剖面左右留白（mm），預設 500",
                    default: 500,
                },
                marginVertical: {
                    type: "number",
                    description: "剖面上下留白（mm），預設 200",
                    default: 200,
                },
                minWallLength: {
                    type: "number",
                    description: "最短牆長過濾（mm），過短的牆略過，預設 500",
                    default: 500,
                },
                sequentialNaming: {
                    type: "boolean",
                    description: "啟用流水號命名：依 sortOrder 排序後命名為 {prefix}-001、{prefix}-002…，預設 false（使用牆 ID 作為後綴）",
                    default: false,
                },
                sortOrder: {
                    type: "string",
                    enum: ["x_then_y", "y_then_x", "creation"],
                    description: "流水號排序方式。x_then_y = 先左後右再由下往上；y_then_x = 先下後上再由左往右；creation = 依建立順序不重排。預設 x_then_y",
                    default: "x_then_y",
                },
            },
            required: ["wallLinkId"],
        },
    },
];
