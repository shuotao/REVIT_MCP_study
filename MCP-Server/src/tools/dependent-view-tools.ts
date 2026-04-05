import { Tool } from "@modelcontextprotocol/sdk/types.js";

export const dependentViewTools: Tool[] = [
    {
        name: "calculate_grid_bounds",
        description: "計算給定 X 與 Y 網格範圍的邊界 BoundingBox 座標。可指定外擴偏移量 (offset_mm)。",
        inputSchema: {
            type: "object",
            properties: {
                xGrids: { type: "array", items: { type: "string" }, description: "X 軸網格名稱清單 (例如 ['B23', 'B27'])" },
                yGrids: { type: "array", items: { type: "string" }, description: "Y 軸網格名稱清單 (例如 ['BE'])" },
                offset_mm: { type: "number", description: "邊界向外偏移距離 (公釐)，預設 0", default: 0 },
            },
        },
    },
    {
        name: "create_dependent_views",
        description: "依據指定的母視圖 ID 與 BoundingBox 邊界，批次建立並裁切從屬視圖 (Dependent View)。",
        inputSchema: {
            type: "object",
            properties: {
                parentViewIds: { type: "array", items: { type: "number" }, description: "母視圖的 Element ID 清單" },
                min: {
                    type: "object",
                    description: "裁切框最小座標點",
                    properties: { x: { type: "number" }, y: { type: "number" }, z: { type: "number" } },
                    required: ["x", "y", "z"],
                },
                max: {
                    type: "object",
                    description: "裁切框最大座標點",
                    properties: { x: { type: "number" }, y: { type: "number" }, z: { type: "number" } },
                    required: ["x", "y", "z"],
                },
                suffixName: { type: "string", description: "視圖命名後綴 (例如 '-1')。若不指定則自動編號流水號。" },
            },
            required: ["parentViewIds", "min", "max"],
        },
    },
];
