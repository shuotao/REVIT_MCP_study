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
];
