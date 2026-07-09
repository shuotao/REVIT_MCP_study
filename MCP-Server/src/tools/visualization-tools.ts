/**
 * 視覺化工具 — 圖形覆寫、視圖樣版
 * 所有 Profile 都可選用
 */

import { Tool } from "@modelcontextprotocol/sdk/types.js";

export const visualizationTools: Tool[] = [
    {
        name: "override_element_graphics",
        description: "在指定視圖中覆寫元素的圖形顯示（填滿顏色、圖樣、線條顏色等）。",
        inputSchema: {
            type: "object",
            properties: {
                elementId: { type: "number", description: "要覆寫的元素 ID" },
                viewId: { type: "number", description: "視圖 ID（若不指定則使用當前視圖）" },
                surfaceFillColor: {
                    type: "object",
                    description: "表面填滿顏色 RGB (0-255)",
                    properties: {
                        r: { type: "number", minimum: 0, maximum: 255 },
                        g: { type: "number", minimum: 0, maximum: 255 },
                        b: { type: "number", minimum: 0, maximum: 255 },
                    },
                },
                surfacePatternId: { type: "number", description: "表面填充圖樣 ID（-1 = 實心填滿）", default: -1 },
                lineColor: {
                    type: "object",
                    description: "線條顏色 RGB（可選）",
                    properties: {
                        r: { type: "number", minimum: 0, maximum: 255 },
                        g: { type: "number", minimum: 0, maximum: 255 },
                        b: { type: "number", minimum: 0, maximum: 255 },
                    },
                },
                transparency: { type: "number", description: "透明度 (0-100)", minimum: 0, maximum: 100, default: 0 },
                patternMode: {
                    type: "string",
                    enum: ["auto", "surface", "cut"],
                    description: "填滿層：auto（依視圖類型自動，樓板/屋頂於平面圖自動用表面）、surface（強制表面樣式，立面/剖面/3D 或平面圖樓板）、cut（強制切割樣式，平面圖被剖切的牆/柱/門窗）",
                    default: "auto",
                },
            },
            required: ["elementId"],
        },
    },
    {
        name: "clear_element_override",
        description: "清除元素在指定視圖中的圖形覆寫。",
        inputSchema: {
            type: "object",
            properties: {
                elementId: { type: "number", description: "要清除覆寫的元素 ID" },
                elementIds: { type: "array", items: { type: "number" }, description: "批次操作" },
                viewId: { type: "number", description: "視圖 ID" },
            },
        },
    },
    {
        name: "get_view_templates",
        description: "取得專案中所有視圖樣版的完整設定。可用於視圖樣版比對與整併分析。",
        inputSchema: {
            type: "object",
            properties: {
                includeDetails: { type: "boolean", description: "是否包含詳細設定", default: true },
            },
        },
    },
    {
        name: "set_category_visibility",
        description: "在指定視圖中隱藏或顯示整個類別（同時影響主模型與連結模型）。使用 View.SetCategoryHidden() API。",
        inputSchema: {
            type: "object",
            properties: {
                category: { type: "string", description: "類別名稱（如 Planting, Furniture, Doors, 或 OST_Planting）" },
                hidden: { type: "boolean", description: "true = 隱藏, false = 顯示", default: true },
                viewId: { type: "number", description: "視圖 ID（若不指定則使用當前視圖）" },
            },
            required: ["category"],
        },
    },
    {
        name: "hide_elements",
        description: "在指定視圖中隱藏元素。使用 View.HideElements() API，支援單一或批次操作。",
        inputSchema: {
            type: "object",
            properties: {
                elementId: { type: "number", description: "要隱藏的單一元素 ID" },
                elementIds: { type: "array", items: { type: "number" }, description: "批次隱藏的元素 ID 陣列" },
                viewId: { type: "number", description: "視圖 ID（若不指定則使用當前視圖）" },
            },
        },
    },
    {
        name: "unhide_elements",
        description: "在指定視圖中取消隱藏元素。使用 View.UnhideElements() API，支援單一或批次操作。",
        inputSchema: {
            type: "object",
            properties: {
                elementId: { type: "number", description: "要取消隱藏的單一元素 ID" },
                elementIds: { type: "array", items: { type: "number" }, description: "批次取消隱藏的元素 ID 陣列" },
                viewId: { type: "number", description: "視圖 ID（若不指定則使用當前視圖）" },
            },
        },
    },
    {
        name: "get_types_by_category",
        description: "查詢指定類別中所有元素類型及其目前材質資訊。回傳每個 Type 的 ID、名稱、族群、實例數量、目前材質。用於在批次修改材質前，讓使用者確認要修改哪些類型。",
        inputSchema: {
            type: "object",
            properties: {
                category: {
                    type: "string",
                    description: "類別名稱：Walls, Floors, Columns, StructuralFraming",
                },
                excludeCurtainWalls: {
                    type: "boolean",
                    description: "是否排除帷幕牆（預設 true，僅對 Walls 類別有效）",
                    default: true,
                },
            },
            required: ["category"],
        },
    },
    {
        name: "assign_existing_material",
        description: "將既有材質（透過名稱查找）套用到指定的 Type。不建立新材質。用於復原或批次指派既有材質（例如把 9 個柱子從 'White_MCP' 改回 '鋼 AISI 1015'）。",
        inputSchema: {
            type: "object",
            properties: {
                typeIds: {
                    type: "array",
                    items: { type: "number" },
                    description: "要套用材質的 Type Element ID 陣列",
                },
                materialName: {
                    type: "string",
                    description: "既有材質名稱（必須已存在於專案中）",
                },
            },
            required: ["typeIds", "materialName"],
        },
    },
    {
        name: "batch_set_material",
        description: "批次修改指定 Type 的材質（複製原材質模式）。為每個 Type 的原材質建立複本 '{原名}_{suffix}'，只修改複本的 Appearance Asset（diffuse color），保留 Graphics 顏色與原材質其他屬性。影響 Enscape/V-Ray 等渲染引擎，但平面圖切割填充和 Revit Shaded 3D 維持原材質外觀。牆/樓板只修改 CompoundStructure 最外層（Layer 0），其他層保留。已含 suffix 的材質會被冪等跳過。",
        inputSchema: {
            type: "object",
            properties: {
                typeIds: {
                    type: "array",
                    items: { type: "number" },
                    description: "要修改材質的 Type Element ID 陣列（從 get_types_by_category 取得）",
                },
                color: {
                    type: "object",
                    description: "目標 Appearance diffuse 顏色 RGB (0-255)",
                    properties: {
                        r: { type: "number", minimum: 0, maximum: 255 },
                        g: { type: "number", minimum: 0, maximum: 255 },
                        b: { type: "number", minimum: 0, maximum: 255 },
                    },
                },
                materialName: {
                    type: "string",
                    description: "材質名稱 suffix（後綴）。例如 '護眼白_MCP' 會把原材質 '鋼 AISI 1015' 複製成 '鋼 AISI 1015_護眼白_MCP'。預設 'White_MCP'。",
                    default: "White_MCP",
                },
                roughness: {
                    type: "number",
                    description: "Appearance roughness（選填）。0.0=鏡面反射，1.0=完全啞光。若值 > 1 會被當成百分比（除以 100）。不設則維持原值。建議白模用 1.0 避免金屬感反光。",
                },
            },
            required: ["typeIds", "color"],
        },
    },
];
