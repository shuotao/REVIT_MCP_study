/**
 * Revit MCP 工具定義
 * 定義可供 AI 呼叫的 Revit 操作工具
 */

import { Tool } from "@modelcontextprotocol/sdk/types.js";
import { RevitSocketClient } from "../socket.js";

/**
 * 註冊所有 Revit 工具
 */
export function registerRevitTools(): Tool[] {
    return [
        // 1. 建立牆元素
        {
            name: "create_wall",
            description: "在 Revit 中建立一面牆。需要指定起點、終點座標和高度。",
            inputSchema: {
                type: "object",
                properties: {
                    startX: {
                        type: "number",
                        description: "起點 X 座標（公釐）",
                    },
                    startY: {
                        type: "number",
                        description: "起點 Y 座標（公釐）",
                    },
                    endX: {
                        type: "number",
                        description: "終點 X 座標（公釐）",
                    },
                    endY: {
                        type: "number",
                        description: "終點 Y 座標（公釐）",
                    },
                    height: {
                        type: "number",
                        description: "牆高度（公釐）",
                        default: 3000,
                    },
                    wallType: {
                        type: "string",
                        description: "牆類型名稱（選填）",
                    },
                },
                required: ["startX", "startY", "endX", "endY"],
            },
        },

        // 2. 查詢專案資訊
        {
            name: "get_project_info",
            description: "取得目前開啟的 Revit 專案基本資訊，包括專案名稱、建築物名稱、業主等。",
            inputSchema: {
                type: "object",
                properties: {},
            },
        },

        // 3. 查詢元素
        {
            name: "query_elements",
            description: "查詢 Revit 專案中的元素。可依類別、族群、類型等條件篩選。",
            inputSchema: {
                type: "object",
                properties: {
                    category: {
                        type: "string",
                        description: "元素類別（如：牆、門、窗等）",
                    },
                    family: {
                        type: "string",
                        description: "族群名稱（選填）",
                    },
                    type: {
                        type: "string",
                        description: "類型名稱（選填）",
                    },
                    level: {
                        type: "string",
                        description: "樓層名稱（選填）",
                    },
                },
            },
        },

        // 4. 建立樓板
        {
            name: "create_floor",
            description: "在 Revit 中建立樓板。需要指定矩形範圍的四個角點座標。",
            inputSchema: {
                type: "object",
                properties: {
                    points: {
                        type: "array",
                        description: "樓板邊界點陣列，每個點包含 x, y 座標（公釐）",
                        items: {
                            type: "object",
                            properties: {
                                x: { type: "number" },
                                y: { type: "number" },
                            },
                        },
                    },
                    levelName: {
                        type: "string",
                        description: "樓層名稱",
                        default: "Level 1",
                    },
                    floorType: {
                        type: "string",
                        description: "樓板類型名稱（選填）",
                    },
                },
                required: ["points"],
            },
        },

        // 5. 刪除元素
        {
            name: "delete_element",
            description: "依 Element ID 刪除 Revit 元素。",
            inputSchema: {
                type: "object",
                properties: {
                    elementId: {
                        type: "number",
                        description: "要刪除的元素 ID",
                    },
                },
                required: ["elementId"],
            },
        },

        // 6. 取得元素資訊
        {
            name: "get_element_info",
            description: "取得指定元素的詳細資訊，包括參數、幾何資訊等。",
            inputSchema: {
                type: "object",
                properties: {
                    elementId: {
                        type: "number",
                        description: "元素 ID",
                    },
                },
                required: ["elementId"],
            },
        },

        // 7. 修改元素參數
        {
            name: "modify_element_parameter",
            description: "修改 Revit 元素的參數值。",
            inputSchema: {
                type: "object",
                properties: {
                    elementId: {
                        type: "number",
                        description: "元素 ID",
                    },
                    parameterName: {
                        type: "string",
                        description: "參數名稱",
                    },
                    value: {
                        type: "string",
                        description: "新的參數值",
                    },
                },
                required: ["elementId", "parameterName", "value"],
            },
        },

        // 8. 取得所有樓層
        {
            name: "get_all_levels",
            description: "取得專案中所有樓層的清單，包括樓層名稱和標高。",
            inputSchema: {
                type: "object",
                properties: {},
            },
        },

        // 9. 建立門
        {
            name: "create_door",
            description: "在指定的牆上建立門。",
            inputSchema: {
                type: "object",
                properties: {
                    wallId: {
                        type: "number",
                        description: "要放置門的牆 ID",
                    },
                    locationX: {
                        type: "number",
                        description: "門在牆上的位置 X 座標（公釐）",
                    },
                    locationY: {
                        type: "number",
                        description: "門在牆上的位置 Y 座標（公釐）",
                    },
                    doorType: {
                        type: "string",
                        description: "門類型名稱（選填）",
                    },
                },
                required: ["wallId", "locationX", "locationY"],
            },
        },

        // 10. 建立窗
        {
            name: "create_window",
            description: "在指定的牆上建立窗。",
            inputSchema: {
                type: "object",
                properties: {
                    wallId: {
                        type: "number",
                        description: "要放置窗的牆 ID",
                    },
                    locationX: {
                        type: "number",
                        description: "窗在牆上的位置 X 座標（公釐）",
                    },
                    locationY: {
                        type: "number",
                        description: "窗在牆上的位置 Y 座標（公釐）",
                    },
                    windowType: {
                        type: "string",
                        description: "窗類型名稱（選填）",
                    },
                },
                required: ["wallId", "locationX", "locationY"],
            },
        },

        // 11. 取得所有網格線
        {
            name: "get_all_grids",
            description: "取得專案中所有網格線（Grid）的資訊，包含名稱、方向、起點和終點座標。可用於計算網格交會點。",
            inputSchema: {
                type: "object",
                properties: {},
            },
        },

        // 12. 取得柱類型
        {
            name: "get_column_types",
            description: "取得專案中所有可用的柱類型，包含名稱、尺寸和族群資訊。",
            inputSchema: {
                type: "object",
                properties: {
                    material: {
                        type: "string",
                        description: "篩選材質（如：混凝土、鋼），選填",
                    },
                },
            },
        },

        // 13. 建立柱子
        {
            name: "create_column",
            description: "在指定位置建立柱子。需要指定座標和底部樓層。",
            inputSchema: {
                type: "object",
                properties: {
                    x: {
                        type: "number",
                        description: "柱子位置 X 座標（公釐）",
                    },
                    y: {
                        type: "number",
                        description: "柱子位置 Y 座標（公釐）",
                    },
                    bottomLevel: {
                        type: "string",
                        description: "底部樓層名稱",
                        default: "Level 1",
                    },
                    topLevel: {
                        type: "string",
                        description: "頂部樓層名稱（選填，如不指定則使用非約束高度）",
                    },
                    columnType: {
                        type: "string",
                        description: "柱類型名稱（選填，如不指定則使用預設類型）",
                    },
                },
                required: ["x", "y"],
            },
        },

        // 14. 取得家具類型
        {
            name: "get_furniture_types",
            description: "取得專案中已載入的家具類型清單，包含名稱和族群資訊。",
            inputSchema: {
                type: "object",
                properties: {
                    category: {
                        type: "string",
                        description: "家具類別篩選（如：椅子、桌子、床），選填",
                    },
                },
            },
        },

        // 15. 放置家具
        {
            name: "place_furniture",
            description: "在指定位置放置家具實例。",
            inputSchema: {
                type: "object",
                properties: {
                    x: {
                        type: "number",
                        description: "X 座標（公釐）",
                    },
                    y: {
                        type: "number",
                        description: "Y 座標（公釐）",
                    },
                    furnitureType: {
                        type: "string",
                        description: "家具類型名稱（需與 get_furniture_types 回傳的名稱一致）",
                    },
                    level: {
                        type: "string",
                        description: "樓層名稱",
                        default: "Level 1",
                    },
                    rotation: {
                        type: "number",
                        description: "旋轉角度（度），預設 0",
                        default: 0,
                    },
                },
                required: ["x", "y", "furnitureType"],
            },
        },

        // 16. 取得房間資訊
        {
            name: "get_room_info",
            description: "取得房間詳細資訊，包含中心點座標和邊界範圍。可用於智慧放置家具。",
            inputSchema: {
                type: "object",
                properties: {
                    roomId: {
                        type: "number",
                        description: "房間 Element ID（選填，如果知道的話）",
                    },
                    roomName: {
                        type: "string",
                        description: "房間名稱（選填，用於搜尋）",
                    },
                },
            },
        },

        // 17. 取得樓層房間清單
        {
            name: "get_rooms_by_level",
            description: "取得指定樓層的所有房間清單，包含名稱、編號、面積、用途等資訊。可用於容積檢討。",
            inputSchema: {
                type: "object",
                properties: {
                    level: {
                        type: "string",
                        description: "樓層名稱（如：1F、Level 1）",
                    },
                    includeUnnamed: {
                        type: "boolean",
                        description: "是否包含未命名的房間，預設 true",
                        default: true,
                    },
                },
                required: ["level"],
            },
        },

        // 18. 取得所有視圖
        {
            name: "get_all_views",
            description: "取得專案中所有視圖的清單，包含平面圖、天花圖、3D視圖、剖面圖等。可用於選擇要標註的視圖。",
            inputSchema: {
                type: "object",
                properties: {
                    viewType: {
                        type: "string",
                        description: "視圖類型篩選：FloorPlan（平面圖）、CeilingPlan（天花圖）、ThreeD（3D視圖）、Section（剖面圖）、Elevation（立面圖）",
                    },
                    levelName: {
                        type: "string",
                        description: "樓層名稱篩選（選填）",
                    },
                },
            },
        },

        // 19. 取得目前視圖
        {
            name: "get_active_view",
            description: "取得目前開啟的視圖資訊，包含視圖名稱、類型、樓層等。",
            inputSchema: {
                type: "object",
                properties: {},
            },
        },

        // 20. 切換視圖
        {
            name: "set_active_view",
            description: "切換至指定的視圖。",
            inputSchema: {
                type: "object",
                properties: {
                    viewId: {
                        type: "number",
                        description: "要切換的視圖 Element ID",
                    },
                },
                required: ["viewId"],
            },
        },

        // 21. 選取元素
        {
            name: "select_element",
            description: "在 Revit 中選取指定的元素，讓使用者可以視覺化確認目標元素。",
            inputSchema: {
                type: "object",
                properties: {
                    elementId: {
                        type: "number",
                        description: "要選取的元素 ID (單選)",
                    },
                    elementIds: {
                        type: "array",
                        items: { type: "number" },
                        description: "要選取的元素 ID 列表 (多選)",
                    },
                },
                // required: ["elementId"], // 讓後端驗證
            },
        },

        // 22. 縮放至元素
        {
            name: "zoom_to_element",
            description: "將視圖縮放至指定元素，讓使用者可以快速定位。",
            inputSchema: {
                type: "object",
                properties: {
                    elementId: {
                        type: "number",
                        description: "要縮放至的元素 ID",
                    },
                },
                required: ["elementId"],
            },
        },

        // 23. 測量距離
        {
            name: "measure_distance",
            description: "測量兩個點之間的距離。回傳距離（公釐）。",
            inputSchema: {
                type: "object",
                properties: {
                    point1X: {
                        type: "number",
                        description: "第一點 X 座標（公釐）",
                    },
                    point1Y: {
                        type: "number",
                        description: "第一點 Y 座標（公釐）",
                    },
                    point1Z: {
                        type: "number",
                        description: "第一點 Z 座標（公釐），預設 0",
                        default: 0,
                    },
                    point2X: {
                        type: "number",
                        description: "第二點 X 座標（公釐）",
                    },
                    point2Y: {
                        type: "number",
                        description: "第二點 Y 座標（公釐）",
                    },
                    point2Z: {
                        type: "number",
                        description: "第二點 Z 座標（公釐），預設 0",
                        default: 0,
                    },
                },
                required: ["point1X", "point1Y", "point2X", "point2Y"],
            },
        },

        // 24. 取得牆資訊
        {
            name: "get_wall_info",
            description: "取得牆的詳細資訊，包含厚度、長度、高度、位置線座標等。用於計算走廊淨寬。",
            inputSchema: {
                type: "object",
                properties: {
                    wallId: {
                        type: "number",
                        description: "牆的 Element ID",
                    },
                },
                required: ["wallId"],
            },
        },

        // 25. 建立尺寸標註
        {
            name: "create_dimension",
            description: "在指定視圖中建立尺寸標註。需要指定視圖和兩個參考點。",
            inputSchema: {
                type: "object",
                properties: {
                    viewId: {
                        type: "number",
                        description: "要建立標註的視圖 ID（使用 get_active_view 或 get_all_views 取得）",
                    },
                    startX: {
                        type: "number",
                        description: "起點 X 座標（公釐）",
                    },
                    startY: {
                        type: "number",
                        description: "起點 Y 座標（公釐）",
                    },
                    endX: {
                        type: "number",
                        description: "終點 X 座標（公釐）",
                    },
                    endY: {
                        type: "number",
                        description: "終點 Y 座標（公釐）",
                    },
                    offset: {
                        type: "number",
                        description: "標註線偏移距離（公釐），預設 500",
                        default: 500,
                    },
                },
                required: ["viewId", "startX", "startY", "endX", "endY"],
            },
        },

        // 25. 根據位置查詢牆體
        {
            name: "query_walls_by_location",
            description: "查詢指定座標附近的牆體，回傳牆厚度、位置線與牆面座標。",
            inputSchema: {
                type: "object",
                properties: {
                    x: {
                        type: "number",
                        description: "搜尋中心 X 座標",
                    },
                    y: {
                        type: "number",
                        description: "搜尋中心 Y 座標",
                    },
                    searchRadius: {
                        type: "number",
                        description: "搜尋半徑 (mm)",
                    },
                    level: {
                        type: "string",
                        description: "樓層名稱 (選填，例如 '2FL')",
                    },
                },
                required: ["x", "y", "searchRadius"],
            },
        },

        // 26. Get active schema (Phase 1: Exploration)
        {
            name: "get_active_schema",
            description: "[Phase 1: Exploration] Get all categories and their element counts in the active view. ALWAYS run this first to confirm if the target category exists. (取得目前視圖中的所有品類及數量。在查詢前請先執行此工具確認目標品類是否存在。)",
            inputSchema: {
                type: "object",
                properties: {
                    viewId: {
                        type: "number",
                        description: "The view Element ID (Optional, defaults to active view)",
                    },
                },
            },
        },

        // 27. Get category fields (Phase 2: Alignment)
        {
            name: "get_category_fields",
            description: "[Phase 2: Alignment] Get all parameter names for a specific category. MANDATORY: Run this before 'query_elements_with_filter' to identify exact localized parameter names. (取得指定品類的所有參數欄位名稱。在執行進階查詢前，務必先跑此工具確認精確名稱，嚴禁猜測。)",
            inputSchema: {
                type: "object",
                properties: {
                    category: {
                        type: "string",
                        description: "The category internal name (e.g., 'Walls', 'Windows')",
                    },
                },
                required: ["category"],
            },
        },

        // 28. Get field value distribution (Phase 2.5)
        {
            name: "get_field_values",
            description: "[Optional Phase 2.5] Get the distribution of existing values (unique list or range) for a specific parameter. (取得指定參數的現有值分佈情況，協助確定過濾條件的值範圍。)",
            inputSchema: {
                type: "object",
                properties: {
                    category: {
                        type: "string",
                        description: "The category internal name",
                    },
                    fieldName: {
                        type: "string",
                        description: "The parameter name (e.g., 'Fire Rating')",
                    },
                    maxSamples: {
                        type: "number",
                        description: "Max samples to analyze (Default: 500)",
                        default: 500,
                    },
                },
                required: ["category", "fieldName"],
            },
        },

        // 29. Advanced element query (Phase 3: Retrieval)
        {
            name: "query_elements_with_filter",
            description: "[Phase 3: Retrieval] Query elements with multi-filter support. NOTE: The 'field' name MUST match names from 'get_category_fields'. Units are typically in mm. (進階查詢工具，支援多重過濾。注意：filters 中的 field 必須嚴格匹配從 get_category_fields 取得的名稱。)",
            inputSchema: {
                type: "object",
                properties: {
                    category: {
                        type: "string",
                        description: "The category internal name (e.g., 'Walls', 'Windows')",
                    },
                    viewId: {
                        type: "number",
                        description: "The view Element ID (Optional)",
                    },
                    filters: {
                        type: "array",
                        description: "List of filter conditions",
                        items: {
                            type: "object",
                            properties: {
                                field: { type: "string", description: "Parameter name (MUST be from get_category_fields)" },
                                operator: {
                                    type: "string",
                                    enum: ["equals", "contains", "less_than", "greater_than", "not_equals"],
                                    description: "Comparison operator"
                                },
                                value: { type: "string", description: "Comparison value (strings for text, numeric strings for numbers)" }
                            },
                            required: ["field", "operator", "value"]
                        }
                    },
                    returnFields: {
                        type: "array",
                        description: "指定要回傳的參數欄位清單",
                        items: { type: "string" }
                    },
                    maxCount: {
                        type: "number",
                        description: "最大回傳數量 (預設 100)",
                        default: 100,
                    },
                },
                required: ["category"],
            },
        },

        // 30. 覆寫元素圖形顯示
        {
            name: "override_element_graphics",
            description: "在指定視圖中覆寫元素的圖形顯示（填滿顏色、圖樣、線條顏色等）。",
            inputSchema: {
                type: "object",
                properties: {
                    elementId: {
                        type: "number",
                        description: "要覆寫的元素 ID",
                    },
                    viewId: {
                        type: "number",
                        description: "視圖 ID（若不指定則使用當前視圖）",
                    },
                    surfaceFillColor: {
                        type: "object",
                        description: "表面填滿顏色 RGB (0-255)",
                        properties: {
                            r: { type: "number", minimum: 0, maximum: 255 },
                            g: { type: "number", minimum: 0, maximum: 255 },
                            b: { type: "number", minimum: 0, maximum: 255 },
                        },
                    },
                    surfacePatternId: {
                        type: "number",
                        description: "表面填充圖樣 ID（-1 表示使用實心填滿，0 表示不設定圖樣）",
                        default: -1,
                    },
                    lineColor: {
                        type: "object",
                        description: "線條顏色 RGB（可選）",
                        properties: {
                            r: { type: "number", minimum: 0, maximum: 255 },
                            g: { type: "number", minimum: 0, maximum: 255 },
                            b: { type: "number", minimum: 0, maximum: 255 },
                        },
                    },
                    transparency: {
                        type: "number",
                        description: "透明度 (0-100)，0 為不透明",
                        minimum: 0,
                        maximum: 100,
                        default: 0,
                    },
                },
                required: ["elementId"],
            },
        },

        // 31. 清除元素圖形覆寫
        {
            name: "clear_element_override",
            description: "清除元素在指定視圖中的圖形覆寫，恢復為預設顯示。",
            inputSchema: {
                type: "object",
                properties: {
                    elementId: {
                        type: "number",
                        description: "要清除覆寫的元素 ID",
                    },
                    elementIds: {
                        type: "array",
                        items: { type: "number" },
                        description: "要清除覆寫的元素 ID 列表（批次操作）",
                    },
                    viewId: {
                        type: "number",
                        description: "視圖 ID（若不指定則使用當前視圖）",
                    },
                },
            },
        },

        // 29. 外牆開口檢討（第45條、第110條）
        {
            name: "check_exterior_wall_openings",
            description: "依據台灣建築技術規則第45條（外牆開口距離限制）及第110條（防火間隔）檢討外牆開口。自動讀取 PropertyLine（地界線）計算距離，並以顏色標示違規項目。",
            inputSchema: {
                type: "object",
                properties: {
                    checkArticle45: {
                        type: "boolean",
                        description: "是否檢查第45條（開口距離限制：距境界線≥1.0m，同基地建築間≥2.0m或≥1.0m）",
                        default: true,
                    },
                    checkArticle110: {
                        type: "boolean",
                        description: "是否檢查第110條（防火間隔：依距離要求不同防火時效）",
                        default: true,
                    },
                    colorizeViolations: {
                        type: "boolean",
                        description: "是否在 Revit 中以顏色標示檢查結果（紅色=違規，橘色=警告，綠色=通過）",
                        default: true,
                    },
                    exportReport: {
                        type: "boolean",
                        description: "是否匯出 JSON 報表",
                        default: false,
                    },
                    reportPath: {
                        type: "string",
                        description: "JSON 報表輸出路徑（需啟用 exportReport）",
                        default: "D:\\\\Reports\\\\exterior_wall_check.json",
                    },
                },
                required: [],
            },
        },

        // 34. 取消牆體接合（上色前置作業）
        {
            name: "unjoin_wall_joins",
            description: "取消牆體與柱子等元素的幾何接合關係。常用於元素上色前的前置作業，避免接合導致顏色無法正確顯示。會記錄取消的接合對，供後續 rejoin_wall_joins 恢復。",
            inputSchema: {
                type: "object",
                properties: {
                    wallIds: {
                        type: "array",
                        items: { type: "number" },
                        description: "要取消接合的牆體 Element ID 列表（選填，若不提供則需指定 viewId）",
                    },
                    viewId: {
                        type: "number",
                        description: "視圖 ID，若未提供 wallIds 則會取消此視圖中所有牆體的接合",
                    },
                },
            },
        },

        // 35. 恢復牆體接合
        {
            name: "rejoin_wall_joins",
            description: "恢復先前由 unjoin_wall_joins 取消的牆體接合關係。應在上色作業完成後呼叫，以還原模型的幾何正確性。",
            inputSchema: {
                type: "object",
                properties: {},
            },
        },

        // 36. 取得房間採光資訊
        {
            name: "get_room_daylight_info",
            description: "取得房間的採光資訊，包含居室面積、外牆開口（窗戶）面積、採光比例等。可依樓層篩選。用於建築技術規則居室採光檢討。",
            inputSchema: {
                type: "object",
                properties: {
                    level: {
                        type: "string",
                        description: "樓層名稱（選填，如 '1F'、'Level 1'），不指定則查詢所有樓層",
                    },
                },
            },
        },

        // 37. 取得視圖樣版
        {
            name: "get_view_templates",
            description: "取得專案中所有視圖樣版（View Templates）的完整設定，包含詳細等級、視覺樣式、比例尺、控制參數數量、隱藏品類、篩選器等。可用於視圖樣版比對與整併分析。",
            inputSchema: {
                type: "object",
                properties: {
                    includeDetails: {
                        type: "boolean",
                        description: "是否包含詳細設定（如隱藏品類、篩選器、裁剪設定等），預設 true",
                        default: true,
                    },
                },
            },
        },

        // ========== 帷幕牆工具 ==========

        // 38. 取得帷幕牆資訊
        {
            name: "get_curtain_wall_info",
            description: "取得目前選取的帷幕牆詳細資訊，包含 Grid 排列（列數、行數）、面板尺寸、面板類型分佈等。使用前請先在 Revit 中選取一個帷幕牆。",
            inputSchema: {
                type: "object",
                properties: {
                    elementId: {
                        type: "number",
                        description: "帷幕牆的 Element ID（選填，若不指定則使用目前選取的元素）",
                    },
                },
            },
        },

        // 39. 取得帷幕牆面板類型
        {
            name: "get_curtain_panel_types",
            description: "取得專案中所有可用的帷幕牆面板類型，包含類型名稱、族群、材料等資訊。用於選擇要套用的面板類型。",
            inputSchema: {
                type: "object",
                properties: {},
            },
        },

        // 40. 建立帷幕牆面板類型
        {
            name: "create_curtain_panel_type",
            description: "建立新的帷幕牆面板類型，可指定顏色。用於實現自訂面板排列模式。",
            inputSchema: {
                type: "object",
                properties: {
                    typeName: {
                        type: "string",
                        description: "新類型的名稱",
                    },
                    color: {
                        type: "string",
                        description: "面板顏色（HEX 格式，如 '#5C4033'）",
                    },
                    baseTypeId: {
                        type: "number",
                        description: "基礎類型 ID（選填，用於複製現有類型的設定）",
                    },
                },
                required: ["typeName", "color"],
            },
        },

        // 41. 套用帷幕牆面板排列模式
        {
            name: "apply_panel_pattern",
            description: "將指定的面板排列模式套用到帷幕牆。需要提供類型映射表（字母對應類型 ID）和排列矩陣。",
            inputSchema: {
                type: "object",
                properties: {
                    elementId: {
                        type: "number",
                        description: "帷幕牆的 Element ID",
                    },
                    typeMapping: {
                        type: "object",
                        description: "類型映射表，鍵為矩陣中的字母（如 'A', 'B'），值為對應的面板類型 ID",
                    },
                    matrix: {
                        type: "array",
                        description: "面板排列矩陣，每行是一個字串陣列，由上到下、由左到右",
                        items: {
                            type: "array",
                            items: { type: "string" },
                        },
                    },
                },
                required: ["elementId", "typeMapping", "matrix"],
            },
        },

        // ========== 立面面板工具 ==========

        // 42. 建立單片立面面板
        {
            name: "create_facade_panel",
            description: "在指定牆面前方建立一片立面面板（DirectShape）。支援 5 種幾何類型：curved_panel（弧形面板）、beveled_opening（斜切凹窗框）、angled_panel（傾斜平板）、rounded_opening（圓角開口）、flat_panel（平面面板）。",
            inputSchema: {
                type: "object",
                properties: {
                    wallId: {
                        type: "number",
                        description: "參考牆的 Element ID（選填，若不指定則使用目前選取的牆）",
                    },
                    geometryType: {
                        type: "string",
                        enum: ["curved_panel", "beveled_opening", "angled_panel", "rounded_opening", "flat_panel"],
                        description: "幾何類型：curved_panel=弧形面板, beveled_opening=斜切凹窗框, angled_panel=傾斜平板, rounded_opening=圓角開口, flat_panel=平面面板。預設 curved_panel",
                    },
                    positionAlongWall: {
                        type: "number",
                        description: "面板中心沿牆方向的位置（mm，從牆起點算起）",
                    },
                    positionZ: {
                        type: "number",
                        description: "面板底部的 Z 高度（mm）",
                    },
                    width: {
                        type: "number",
                        description: "面板/外框寬度（mm），預設 800",
                    },
                    height: {
                        type: "number",
                        description: "面板/外框高度（mm），預設 3400",
                    },
                    depth: {
                        type: "number",
                        description: "深度（mm）：弧形面板的弧深 / 開口類型的凹入深度，預設 150",
                    },
                    thickness: {
                        type: "number",
                        description: "板厚（mm），預設 30",
                    },
                    offset: {
                        type: "number",
                        description: "距牆面的偏移量（mm），預設 200",
                    },
                    color: {
                        type: "string",
                        description: "顏色（HEX 格式，如 '#B85C3A'）",
                    },
                    name: {
                        type: "string",
                        description: "面板名稱標識",
                    },
                    // curved_panel 專用
                    curveType: {
                        type: "string",
                        enum: ["concave", "convex"],
                        description: "[curved_panel] 曲線類型：concave 凹面 / convex 凸面",
                    },
                    // angled_panel 專用
                    tiltAngle: {
                        type: "number",
                        description: "[angled_panel] 傾斜角度（度），預設 15",
                    },
                    tiltAxis: {
                        type: "string",
                        enum: ["horizontal", "vertical"],
                        description: "[angled_panel] 傾斜軸：horizontal 前後傾 / vertical 左右傾",
                    },
                    // beveled_opening 專用
                    bevelDirection: {
                        type: "string",
                        enum: ["center", "up", "down", "left", "right"],
                        description: "[beveled_opening] 斜切方向：center 均勻 / up 上深 / down 下深 / left 左深 / right 右深",
                    },
                    bevelDepth: {
                        type: "number",
                        description: "[beveled_opening] 斜切凹陷深度（mm），預設 300。控制從外框到開口底部的斜面深度",
                    },
                    openingWidth: {
                        type: "number",
                        description: "[beveled_opening/rounded_opening] 內開口寬度（mm）",
                    },
                    openingHeight: {
                        type: "number",
                        description: "[beveled_opening/rounded_opening] 內開口高度（mm）",
                    },
                    // rounded_opening 專用
                    cornerRadius: {
                        type: "number",
                        description: "[rounded_opening] 圓角半徑（mm），預設 100",
                    },
                    openingShape: {
                        type: "string",
                        enum: ["rounded_rect", "arch", "stadium", "rect"],
                        description: "[rounded_opening] 開口形狀：rounded_rect 圓角矩形 / arch 圓拱 / stadium 跑道形 / rect 直角",
                    },
                },
            },
        },

        // 43. 批次建立整面立面
        {
            name: "create_facade_from_analysis",
            description: "根據 AI 分析結果或預覽工具設定，一次建立整面弧形立面。在指定牆面前方批次建立多片 DirectShape 弧形面板，支援多種面板類型和排列模式。建議先使用立面預覽工具（http://localhost:10002）確認效果後再套用。",
            inputSchema: {
                type: "object",
                properties: {
                    wallId: {
                        type: "number",
                        description: "目標牆的 Element ID（選填，若不指定則使用目前選取的牆）",
                    },
                    facadeLayers: {
                        type: "object",
                        description: "立面層級定義",
                        properties: {
                            outer: {
                                type: "object",
                                description: "外層弧形面板定義",
                                properties: {
                                    offset: {
                                        type: "number",
                                        description: "距牆面偏移量（mm），預設 200",
                                    },
                                    panelTypes: {
                                        type: "array",
                                        description: "面板類型定義陣列",
                                        items: {
                                            type: "object",
                                            properties: {
                                                id: {
                                                    type: "string",
                                                    description: "類型代號（如 'A', 'B'），用於 pattern 矩陣對應",
                                                },
                                                name: {
                                                    type: "string",
                                                    description: "類型名稱（如 'FP_Concave_800_150_Terracotta'）",
                                                },
                                                width: {
                                                    type: "number",
                                                    description: "面板寬度（mm）",
                                                },
                                                height: {
                                                    type: "number",
                                                    description: "面板高度（mm）",
                                                },
                                                depth: {
                                                    type: "number",
                                                    description: "弧形深度（mm）",
                                                },
                                                thickness: {
                                                    type: "number",
                                                    description: "板厚（mm）",
                                                },
                                                curveType: {
                                                    type: "string",
                                                    enum: ["concave", "convex"],
                                                    description: "[curved_panel] 曲線類型",
                                                },
                                                color: {
                                                    type: "string",
                                                    description: "顏色（HEX 格式）",
                                                },
                                                geometryType: {
                                                    type: "string",
                                                    enum: ["curved_panel", "beveled_opening", "angled_panel", "rounded_opening", "flat_panel"],
                                                    description: "幾何類型，預設 curved_panel",
                                                },
                                                tiltAngle: { type: "number", description: "[angled_panel] 傾斜角度" },
                                                tiltAxis: { type: "string", description: "[angled_panel] 傾斜軸" },
                                                bevelDirection: { type: "string", description: "[beveled_opening] 斜切方向" },
                                                openingWidth: { type: "number", description: "[opening] 開口寬度(mm)" },
                                                openingHeight: { type: "number", description: "[opening] 開口高度(mm)" },
                                                cornerRadius: { type: "number", description: "[rounded_opening] 圓角半徑(mm)" },
                                                openingShape: { type: "string", description: "[rounded_opening] 開口形狀" },
                                            },
                                            required: ["id", "width", "color"],
                                        },
                                    },
                                    pattern: {
                                        type: "array",
                                        description: "排列矩陣，每個元素為一個字串代表一層（如 'ABABAB'），由上到下排列",
                                        items: { type: "string" },
                                    },
                                    gap: {
                                        type: "number",
                                        description: "面板間水平間距（mm），預設 20",
                                    },
                                    horizontalBandHeight: {
                                        type: "number",
                                        description: "層間水平分隔帶高度（mm），0 表示不建立分隔帶",
                                    },
                                    floorHeight: {
                                        type: "number",
                                        description: "每層高度（mm），預設 3600",
                                    },
                                },
                                required: ["panelTypes", "pattern"],
                            },
                        },
                        required: ["outer"],
                    },
                },
                required: ["facadeLayers"],
            },
        },
    ];
}

/**
 * 執行 Revit 工具
 */
export async function executeRevitTool(
    toolName: string,
    args: Record<string, any>,
    client: RevitSocketClient
): Promise<any> {
    // 將工具名稱轉換為 Revit 命令名稱
    // 如果是 query_elements_with_filter，映射到 C# 的 query_elements
    const commandName = toolName === "query_elements_with_filter" ? "query_elements" : toolName;

    // 發送命令到 Revit
    const response = await client.sendCommand(commandName, args);

    if (!response.success) {
        throw new Error(response.error || "命令執行失敗");
    }

    return response.data;
}
