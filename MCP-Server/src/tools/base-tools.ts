/**
 * 基礎工具 — 所有 Profile 都會載入
 * 包含：專案資訊、元素操作、樓層、視圖控制、查詢框架、選取
 */

import { Tool } from "@modelcontextprotocol/sdk/types.js";

export const baseTools: Tool[] = [
    // === 專案與元素基礎 ===
    {
        name: "get_project_info",
        description: "取得目前開啟的 Revit 專案基本資訊，包括專案名稱、建築物名稱、業主等。",
        inputSchema: { type: "object", properties: {} },
    },
    {
        name: "get_all_levels",
        description: "取得專案中所有樓層的清單，包括樓層名稱和標高。",
        inputSchema: { type: "object", properties: {} },
    },
    {
        name: "get_element_info",
        description: "取得指定元素的詳細資訊，包括參數、幾何資訊等。",
        inputSchema: {
            type: "object",
            properties: {
                elementId: { type: "number", description: "元素 ID" },
            },
            required: ["elementId"],
        },
    },
    {
        name: "delete_element",
        description: "依 Element ID 刪除 Revit 元素。",
        inputSchema: {
            type: "object",
            properties: {
                elementId: { type: "number", description: "要刪除的元素 ID" },
            },
            required: ["elementId"],
        },
    },
    {
        name: "modify_element_parameter",
        description: "修改 Revit 元素的參數值。",
        inputSchema: {
            type: "object",
            properties: {
                elementId: { type: "number", description: "元素 ID" },
                parameterName: { type: "string", description: "參數名稱" },
                value: { type: "string", description: "新的參數值" },
            },
            required: ["elementId", "parameterName", "value"],
        },
    },
    {
        name: "get_selected_elements",
        description: "取得使用者目前在 Revit 中選取的所有元素的基本資訊（ID、名稱、品類）。",
        inputSchema: { type: "object", properties: {} },
    },

    // === 視圖控制 ===
    {
        name: "get_all_views",
        description: "取得專案中所有視圖的清單，包含平面圖、天花圖、3D視圖、剖面圖等。",
        inputSchema: {
            type: "object",
            properties: {
                viewType: { type: "string", description: "視圖類型篩選：FloorPlan、CeilingPlan、ThreeD、Section、Elevation" },
                levelName: { type: "string", description: "樓層名稱篩選（選填）" },
            },
        },
    },
    {
        name: "get_active_view",
        description: "取得目前開啟的視圖資訊，包含視圖名稱、類型、樓層等。",
        inputSchema: { type: "object", properties: {} },
    },
    {
        name: "set_active_view",
        description: "切換至指定的視圖。",
        inputSchema: {
            type: "object",
            properties: {
                viewId: { type: "number", description: "要切換的視圖 Element ID" },
            },
            required: ["viewId"],
        },
    },
    {
        name: "select_element",
        description: "在 Revit 中選取指定的元素，讓使用者可以視覺化確認目標元素。",
        inputSchema: {
            type: "object",
            properties: {
                elementId: { type: "number", description: "要選取的元素 ID (單選)" },
                elementIds: { type: "array", items: { type: "number" }, description: "要選取的元素 ID 列表 (多選)" },
            },
        },
    },
    {
        name: "zoom_to_element",
        description: "將視圖縮放至指定元素，讓使用者可以快速定位。",
        inputSchema: {
            type: "object",
            properties: {
                elementId: { type: "number", description: "要縮放至的元素 ID" },
            },
            required: ["elementId"],
        },
    },
    {
        name: "measure_distance",
        description: "測量兩個點之間的距離。回傳距離（公釐）。",
        inputSchema: {
            type: "object",
            properties: {
                point1X: { type: "number", description: "第一點 X 座標（公釐）" },
                point1Y: { type: "number", description: "第一點 Y 座標（公釐）" },
                point1Z: { type: "number", description: "第一點 Z 座標（公釐），預設 0", default: 0 },
                point2X: { type: "number", description: "第二點 X 座標（公釐）" },
                point2Y: { type: "number", description: "第二點 Y 座標（公釐）" },
                point2Z: { type: "number", description: "第二點 Z 座標（公釐），預設 0", default: 0 },
            },
            required: ["point1X", "point1Y", "point2X", "point2Y"],
        },
    },

    // === 查詢框架（三階段） ===
    {
        name: "query_elements",
        description: "查詢 Revit 專案中的元素。可依類別、族群、類型、樓層等條件篩選。",
        inputSchema: {
            type: "object",
            properties: {
                category: { type: "string", description: "元素類別（如：Walls, Rooms, Doors, Windows, Floors, Columns）" },
                family: { type: "string", description: "族群名稱（選填）" },
                type: { type: "string", description: "類型名稱（選填）" },
                level: { type: "string", description: "樓層名稱（選填）" },
                maxCount: { type: "number", description: "最大回傳數量（預設 100）", default: 100 },
            },
            required: ["category"],
        },
    },
    {
        name: "get_active_schema",
        description: "[Phase 1: Exploration] Get all categories and their element counts in the active view. ALWAYS run this first to confirm if the target category exists.",
        inputSchema: {
            type: "object",
            properties: {
                viewId: { type: "number", description: "The view Element ID (Optional, defaults to active view)" },
            },
        },
    },
    {
        name: "get_category_fields",
        description: "[Phase 2: Alignment] Get all parameter names for a specific category. MANDATORY: Run this before 'query_elements_with_filter' to identify exact localized parameter names.",
        inputSchema: {
            type: "object",
            properties: {
                category: { type: "string", description: "The category internal name (e.g., 'Walls', 'Windows')" },
            },
            required: ["category"],
        },
    },
    {
        name: "get_field_values",
        description: "[Optional Phase 2.5] Get the distribution of existing values for a specific parameter.",
        inputSchema: {
            type: "object",
            properties: {
                category: { type: "string", description: "The category internal name" },
                fieldName: { type: "string", description: "The parameter name" },
                maxSamples: { type: "number", description: "Max samples to analyze (Default: 500)", default: 500 },
            },
            required: ["category", "fieldName"],
        },
    },
    {
        name: "query_elements_with_filter",
        description: "[Phase 3: Retrieval] Query elements with multi-filter support. NOTE: The 'field' name MUST match names from 'get_category_fields'.",
        inputSchema: {
            type: "object",
            properties: {
                category: { type: "string", description: "The category internal name" },
                viewId: { type: "number", description: "The view Element ID (Optional)" },
                filters: {
                    type: "array",
                    description: "List of filter conditions",
                    items: {
                        type: "object",
                        properties: {
                            field: { type: "string", description: "Parameter name (MUST be from get_category_fields)" },
                            operator: { type: "string", enum: ["equals", "contains", "less_than", "greater_than", "not_equals"] },
                            value: { type: "string", description: "Comparison value" },
                        },
                        required: ["field", "operator", "value"],
                    },
                },
                returnFields: { type: "array", items: { type: "string" }, description: "指定要回傳的參數欄位清單" },
                maxCount: { type: "number", description: "最大回傳數量 (預設 100)", default: 100 },
            },
            required: ["category"],
        },
    },
];
