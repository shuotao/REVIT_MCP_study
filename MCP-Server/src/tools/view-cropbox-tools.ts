import { Tool } from "@modelcontextprotocol/sdk/types.js";

export const viewCropBoxTools: Tool[] = [
    {
        name: "align_view_cropbox_to_element",
        description: "將指定視圖的 CropBox 對齊到目標元素（如 Detail Group 圖框）的 BoundingBox。保留原 Z 軸深度與 CropBox Transform。viewId 不指定時使用 active view。",
        inputSchema: {
            type: "object",
            properties: {
                elementId: { type: "number", description: "要對齊的目標元素 ID（例如 Detail Group 的 ID）" },
                viewId: { type: "number", description: "目標視圖 ID（選填）；不填則使用當前 active view" },
                padding_mm: { type: "number", description: "向外擴大的邊距（公釐），預設 0", default: 0 },
            },
            required: ["elementId"],
        },
    },
    {
        name: "shift_view_cropbox",
        description: "在 CropBox 自身座標系中平移視圖的 CropBox。dx 正值往右、dy 正值往上（單位公釐）。viewId 不指定時使用 active view。",
        inputSchema: {
            type: "object",
            properties: {
                viewId: { type: "number", description: "目標視圖 ID（選填）；不填則使用當前 active view" },
                dx_mm: { type: "number", description: "X 方向位移（公釐，正值往右）", default: 0 },
                dy_mm: { type: "number", description: "Y 方向位移（公釐，正值往上）", default: 0 },
            },
        },
    },
];
