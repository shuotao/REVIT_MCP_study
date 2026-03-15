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

        // ========== 排煙窗檢討 ==========

        // 排煙窗檢討（Step 2+5 合併）
        {
            name: "check_smoke_exhaust_windows",
            description: `排煙窗檢討：檢查天花板下 80cm 內可開啟窗面積是否 ≥ 區劃面積 2%。
同時判定「無窗居室」（建技規§1第35款第三目 + §100②）。
法源：建技規§101① + 消防§188③⑦。
功能：
- 計算每個房間天花板高度（支援 Room 參數或 Ceiling 元素兩種來源）
- 找出天花板下 80cm 有效帶內的窗戶
- 從族群名稱推斷開啟方式並計算折減（Casement=1.0, Sliding=0.5, Fixed=0）
- 自動上色：綠=全開、黃=折減、紅=固定、灰=未知
- 產生改善建議（換窗型、加窗、機械排煙）
非居室空間（走廊、樓梯、電梯、廁所等）自動跳過，面積 ≤ 50m² 的房間自動跳過。`,
            inputSchema: {
                type: "object",
                properties: {
                    levelName: {
                        type: "string",
                        description: "樓層名稱",
                    },
                    ceilingHeightSource: {
                        type: "string",
                        enum: ["room_parameter", "ceiling_element"],
                        description: "天花板高度來源：room_parameter（讀 Room 上限偏移，預設）或 ceiling_element（搜尋 Ceiling 元素）",
                        default: "room_parameter",
                    },
                    colorize: {
                        type: "boolean",
                        description: "是否自動上色窗戶（綠=全開型、黃=折減型、紅=固定窗、灰=未知），預設 true",
                        default: true,
                    },
                    smokeZoneHeight: {
                        type: "number",
                        description: "有效帶高度（mm），法規為天花板下 80cm，預設 800",
                        default: 800,
                    },
                    excludeKeywords: {
                        type: "array",
                        description: "非居室排除關鍵字陣列，房間名稱包含這些關鍵字則跳過檢討。預設排除：走廊、樓梯、電梯、管道、機房、廁所、浴室、玄關、陽台等",
                        items: { type: "string" },
                    },
                },
                required: ["levelName"],
            },
        },

        // 無開口樓層判定（Step 1）
        {
            name: "check_floor_effective_openings",
            description: `無開口樓層判定（Step 1）：檢查樓層外牆有效開口面積是否 ≥ 樓地板面積 1/30。
法源：消防設置標準§4 + §28③。
有效開口條件：
- 可內切直徑 ≥ 50cm 圓
- 下緣距地板 ≤ 1.2m
- 面臨道路或 ≥ 1m 通路（加註需人工確認）
- 無柵欄、玻璃厚 ≤ 6mm（從族群名稱推斷 + 加註）
- 十層以下另需至少 2 個大型開口（直徑≥1m 或 75cm×120cm）
自動上色：綠=有效開口、紅=無效開口。
後果：≥ 1000m² 的無開口樓層須設排煙設備。`,
            inputSchema: {
                type: "object",
                properties: {
                    levelName: {
                        type: "string",
                        description: "樓層名稱",
                    },
                    colorize: {
                        type: "boolean",
                        description: "是否自動上色開口（綠=有效、紅=無效），預設 true",
                        default: true,
                    },
                },
                required: ["levelName"],
            },
        },

        // ========== 視覺化工具 ==========

        // 建立剖面視圖
        {
            name: "create_section_view",
            description: `建立剖面視圖（用於排煙窗檢討的立面檢視）。
指定一面牆，建立面向該牆的剖面視圖，可用於檢視窗戶與天花板的高度關係。`,
            inputSchema: {
                type: "object",
                properties: {
                    wallId: {
                        type: "number",
                        description: "目標牆的 Element ID",
                    },
                    viewName: {
                        type: "string",
                        description: "視圖名稱，預設「排煙檢討剖面」",
                        default: "排煙檢討剖面",
                    },
                    offset: {
                        type: "number",
                        description: "剖面距牆面的偏移距離（mm），預設 1000",
                        default: 1000,
                    },
                    scale: {
                        type: "number",
                        description: "視圖比例尺（如 50 代表 1:50），預設 50",
                        default: 50,
                    },
                },
                required: ["wallId"],
            },
        },

        // 繪製詳圖線
        {
            name: "create_detail_lines",
            description: `在視圖上繪製詳圖線（如天花板線、有效帶範圍線等）。
可指定顏色和標籤，用於排煙檢討的視覺化標註。`,
            inputSchema: {
                type: "object",
                properties: {
                    viewId: {
                        type: "number",
                        description: "目標視圖的 Element ID",
                    },
                    lines: {
                        type: "array",
                        description: "線段陣列",
                        items: {
                            type: "object",
                            properties: {
                                startX: { type: "number", description: "起點 X（mm）" },
                                startY: { type: "number", description: "起點 Y（mm）" },
                                endX: { type: "number", description: "終點 X（mm）" },
                                endY: { type: "number", description: "終點 Y（mm）" },
                                color: {
                                    type: "object",
                                    description: "線條顏色 RGB",
                                    properties: {
                                        r: { type: "number" },
                                        g: { type: "number" },
                                        b: { type: "number" },
                                    },
                                },
                                lineStyle: { type: "string", description: "線條樣式名稱（選填）" },
                                label: { type: "string", description: "標籤說明（選填）" },
                            },
                            required: ["startX", "startY", "endX", "endY"],
                        },
                    },
                },
                required: ["viewId", "lines"],
            },
        },

        // 建立填充區域
        {
            name: "create_filled_region",
            description: `建立填充區域（如排煙有效帶的半透明色塊）。
在指定視圖上以多邊形邊界建立填充區域，可設定顏色和透明度。`,
            inputSchema: {
                type: "object",
                properties: {
                    viewId: {
                        type: "number",
                        description: "目標視圖的 Element ID",
                    },
                    points: {
                        type: "array",
                        description: "多邊形頂點陣列（至少 3 個點，自動封閉）",
                        items: {
                            type: "object",
                            properties: {
                                x: { type: "number", description: "X 座標（mm）" },
                                y: { type: "number", description: "Y 座標（mm）" },
                            },
                            required: ["x", "y"],
                        },
                    },
                    color: {
                        type: "object",
                        description: "填充顏色 RGB",
                        properties: {
                            r: { type: "number" },
                            g: { type: "number" },
                            b: { type: "number" },
                        },
                    },
                    transparency: {
                        type: "number",
                        description: "透明度 0-100，預設 50",
                        default: 50,
                    },
                    regionType: {
                        type: "string",
                        description: "填充區域類型名稱（選填，預設使用第一個可用類型）",
                    },
                },
                required: ["viewId", "points"],
            },
        },

        // 建立文字標註
        {
            name: "create_text_note",
            description: `在視圖上建立文字標註（如排煙檢討統計摘要）。`,
            inputSchema: {
                type: "object",
                properties: {
                    viewId: {
                        type: "number",
                        description: "目標視圖的 Element ID",
                    },
                    x: {
                        type: "number",
                        description: "文字位置 X 座標（mm）",
                    },
                    y: {
                        type: "number",
                        description: "文字位置 Y 座標（mm）",
                    },
                    text: {
                        type: "string",
                        description: "文字內容",
                    },
                    textSize: {
                        type: "number",
                        description: "文字大小（mm），預設 2.5",
                        default: 2.5,
                    },
                },
                required: ["viewId", "x", "y", "text"],
            },
        },

        // ========== Excel 匯出 ==========

        // 匯出排煙窗檢討 Excel
        {
            name: "export_smoke_review_excel",
            description: `匯出排煙窗檢討結果為 Excel (.xlsx) 報告。
包含四個區段：
1. 樓層總覽（無開口樓層判定）
2. 房間排煙檢討明細（天花板高度、有效面積、合規判定）
3. 窗戶明細（每扇窗的尺寸、位置、開啟方式、有效面積）
4. 改善建議（不合規房間的具體改善方案）`,
            inputSchema: {
                type: "object",
                properties: {
                    levelName: {
                        type: "string",
                        description: "樓層名稱",
                    },
                    ceilingHeightSource: {
                        type: "string",
                        enum: ["room_parameter", "ceiling_element"],
                        description: "天花板高度來源，預設 room_parameter",
                        default: "room_parameter",
                    },
                    outputPath: {
                        type: "string",
                        description: "輸出檔案路徑（選填，預設存在專案資料夾）",
                    },
                },
                required: ["levelName"],
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
