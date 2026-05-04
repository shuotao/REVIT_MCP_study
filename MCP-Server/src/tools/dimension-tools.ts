import { Tool } from "@modelcontextprotocol/sdk/types.js";

export const dimensionTools: Tool[] = [
    {
        name: "create_dimension_by_ray",
        description: "使用射線偵測 (Ray-Casting) 建立尺寸標註。從指定原點向正反方向發射射線，偵測牆面並建立標註。",
        inputSchema: {
            type: "object",
            properties: {
                viewId: { type: "number", description: "目標視圖 ID" },
                origin: {
                    type: "object",
                    description: "射線原點 (通常為房間中心)",
                    properties: { x: { type: "number" }, y: { type: "number" }, z: { type: "number" } },
                    required: ["x", "y"],
                },
                direction: {
                    type: "object",
                    description: "正向射線方向向量",
                    properties: { x: { type: "number" }, y: { type: "number" }, z: { type: "number" } },
                    required: ["x", "y"],
                },
                counterDirection: {
                    type: "object",
                    description: "反向射線方向向量 (若未提供則自動取反)",
                    properties: { x: { type: "number" }, y: { type: "number" }, z: { type: "number" } },
                },
            },
            required: ["viewId", "origin", "direction"],
        },
    },
    {
        name: "create_dimension_by_bounding_box",
        description: "使用房間邊界框自動標註房間淨尺寸（保證100%覆蓋率）",
        inputSchema: {
            type: "object",
            properties: {
                viewId: { type: "number", description: "視圖 ID" },
                roomId: { type: "number", description: "房間 ID" },
                axis: { type: "string", description: "標註軸向：'X' 或 'Y'", enum: ["X", "Y"] },
                offset: { type: "number", description: "標註線偏移距離 (mm)，默認 500" },
            },
            required: ["viewId", "roomId", "axis"],
        },
    },
    {
        name: "auto_dimension_walls",
        description: "批次自動標註牆段尺寸（不需要 Room）。三種模式：overall_bbox（外圍兩條總長串：top 邊沿 X、right 邊沿 Y，預設）/ chained（同列同排共線牆串成 string dimension）/ per_wall（每牆一條長度標註）。標註用 DetailCurve 當 reference，不抓牆面，但建模一次性場景剛好。常用於 sketch-to-revit 蓋完牆後自動補尺寸。",
        inputSchema: {
            type: "object",
            properties: {
                viewId: { type: "number", description: "目標平面視圖 ID（必須是 ViewPlan）" },
                wallIds: {
                    type: "array",
                    items: { type: "number" },
                    description: "要標註的牆 ElementId 列表（選填，預設為 view 範圍內所有牆）",
                },
                mode: {
                    type: "string",
                    enum: ["overall_bbox", "chained", "per_wall"],
                    description: "標註模式（預設 overall_bbox）",
                },
                offsetMm: {
                    type: "number",
                    description: "標註線偏移距離 (mm)，預設 1500",
                },
            },
            required: ["viewId"],
        },
    },
];
