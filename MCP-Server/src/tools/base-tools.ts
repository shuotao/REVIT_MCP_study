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
        name: "set_project_units",
        description: "一次把整個專案的顯示單位切換到指定系統/模式（全案性動作，單一 Transaction 可 Ctrl+Z 還原）。mode='taiwan' = 公制底 + Air Flow m³/h（建築設備編 §102 通風量單位）+ Length 補單位符號；mode='taiwan-plumbing' = taiwan 再加給排水管路側六格（管徑 mm、流量 L/min、流速 m/s、壓力 mH2O、摩擦 mmH2O/m、坡度 1:ratio），對齊給水排水設備設計技術規範（單位慣例參考建築設備編 §43/§46——該兩條為消防栓立管，非給排水條文）；'metric' / 'imperial' 為純公制/英制預設。每個 spec 都會連同「精度」與「單位符號」一起設定——只設單位會讓 Length 顯示成裸數字（673 而非 673 mm），且流速精度 0.1 位會讓 2.98 與 3.05 看起來相同而產生假綠燈。可再用 length/area/volume/airFlow/pipeSize/flow/velocity/pressure/friction/slope 個別覆寫。⚠️ 台灣法規的壓力單位 kgf/cm² 在 Revit 2026 不存在（305 個 UnitTypeId 逐一反射確認），故以 mH2O 代用：1 kgf/cm² = 10.0 mH2O。回傳的 Result 是套用後從 Document 回讀的實際單位/符號/精度，不是參數回聲。",
        inputSchema: {
            type: "object",
            properties: {
                mode: { type: "string", description: "預設模式：'taiwan'（公制 + Air Flow m³/h + Length 帶符號，空調案適用）、'taiwan-plumbing'（taiwan 再加管路側六格，給排水案適用）、'metric'、'imperial'。與 system 擇一。" },
                system: { type: "string", description: "基底單位系統：'metric' 或 'imperial'（未給 mode 時使用；預設 metric）。" },
                length: { type: "string", description: "長度單位覆寫：m / mm / cm / ft / ft-in（選填）" },
                area: { type: "string", description: "面積單位覆寫：m2 / sf（選填）" },
                volume: { type: "string", description: "體積單位覆寫：m3 / l / cf（選填）" },
                airFlow: { type: "string", description: "風量單位覆寫：m3/h / l/s / cfm（選填）" },
                pipeSize: { type: "string", description: "管徑單位覆寫：mm / cm / m / in（選填）" },
                flow: { type: "string", description: "管路流量單位覆寫：l/min / l/s / m3/h / gpm（選填）。台灣技術規範 3.4 用 L/min。" },
                velocity: { type: "string", description: "管路流速單位覆寫：m/s / fps（選填）" },
                pressure: { type: "string", description: "管路壓力單位覆寫：mh2o / mmh2o / kpa / pa / bar / kgf/m2（選填）。⚠️ 台灣法規的 kgf/cm² 在 Revit 不存在，預設以 mh2o 代用（1 kgf/cm² = 10.0 mH2O）。" },
                friction: { type: "string", description: "管路摩擦損失單位覆寫：mmh2o/m / mh2o/m / pa/m（選填）" },
                slope: { type: "string", description: "管路坡度單位覆寫：1:ratio / ratio:1 / % / deg（選填）。台灣技術規範 3.2.2 寫 1/50、1/100，對應 '1:ratio'；選 'ratio:1' 會顯示成 0.01:1，與法規寫法相反。" },
            },
        },
    },
    {
        name: "get_all_levels",
        description: "取得專案中所有樓層的清單，包括樓層名稱和標高。",
        inputSchema: { type: "object", properties: {} },
    },
    {
        name: "create_level",
        description: "在 Revit 中建立一個新的樓層 (Level)。指定標高（公釐）與可選名稱；若名稱已存在 Revit 會自動附加尾號。",
        inputSchema: {
            type: "object",
            properties: {
                elevation: {
                    type: "number",
                    description: "樓層標高（公釐，會自動轉成 Revit 內部單位 feet）",
                },
                name: {
                    type: "string",
                    description: "樓層名稱（選填，例如 '3F'、'RF'）。未填則使用 Revit 預設名稱",
                },
            },
            required: ["elevation"],
        },
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
        name: "get_door_window_coordinates",
        description: "一次撈全部門窗的座標清單：插入點 (LocationPoint) 與 boundingbox（含中心點），座標為 mm。可依門/窗/全部與樓層篩選，並可選擇一併掃描連結模型（自動套用 Transform 轉專案座標）。回傳每筆含 ElementId、族群、類型、標記、樓層、宿主牆ID、朝向。",
        inputSchema: {
            type: "object",
            properties: {
                category: { type: "string", description: "門 / 窗 / 全部（預設全部）。也接受 Doors / Windows。" },
                level: { type: "string", description: "只回傳指定樓層名稱的門窗（選填）" },
                includeLinks: { type: "boolean", description: "是否一併掃描連結模型的門窗（預設 false）" },
                maxCount: { type: "number", description: "最大回傳數量（預設 1000）" },
            },
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
        name: "load_shared_parameters",
        description: "載入共享參數檔 (Shared Parameter File) 並將參數綁定至指定品類的 Type 或 Instance 層級。用於在寫入自訂參數前，先確保專案已載入參數定義。例如載入 GreenMaterial_SharedParams.txt 的 64 個綠建材參數（Mat1~Mat6 六槽位）至 Walls/Floors/Ceilings，之後用 set_green_material_type_parameters 寫值。",
        inputSchema: {
            type: "object",
            properties: {
                filePath: {
                    type: "string",
                    description: "共享參數檔的絕對路徑 (如 C:/path/to/GreenMaterial_SharedParams.txt)",
                },
                categories: {
                    type: "array",
                    items: { type: "string" },
                    description: "要綁定的品類名稱清單: Walls, Floors, Ceilings, Windows, Doors, Materials, Roofs, CurtainPanels, Columns（同時綁 OST_Columns 與 OST_StructuralColumns）, StructuralFraming（樑）",
                },
                bindToInstance: {
                    type: "boolean",
                    description: "true = 綁定至 Instance 參數, false = 綁定至 Type 參數 (預設 false)",
                    default: false,
                },
                dryRun: {
                    type: "boolean",
                    description: "true = 只回報哪些參數會被新綁定/擴充綁定/冪等跳過，不實際呼叫任何 BindingMap 綁定變更，Revit 文件不受影響。預設 false = 正常執行綁定。",
                },
            },
            required: ["filePath", "categories"],
        },
    },
    {
        name: "get_selected_elements",
        description: "取得使用者目前在 Revit 中選取的所有元素的基本資訊（ID、名稱、品類）。若是視圖或剖面標記，會一併回傳 Origin (X,Y,Z) 供空間排序使用。",
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
        name: "rename_view",
        description: "重新命名指定的 Revit 視圖（包含剖面圖、平面圖等），此工具不受軟體語系本地化影響。",
        inputSchema: {
            type: "object",
            properties: {
                viewId: { type: "number", description: "視圖的 Element ID" },
                newName: { type: "string", description: "新的視圖名稱" },
            },
            required: ["viewId", "newName"],
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
    {
        name: "move_element",
        description: "移動指定的 Revit 元素（依 dx, dy, dz 指定位移量）。",
        inputSchema: {
            type: "object",
            properties: {
                elementId: { type: "number", description: "要移動的元素 ID" },
                dx: { type: "number", description: "X 軸移動距離 (mm)", default: 0 },
                dy: { type: "number", description: "Y 軸移動距離 (mm)", default: 0 },
                dz: { type: "number", description: "Z 軸移動距離 (mm)", default: 0 },
            },
            required: ["elementId"],
        },
    },
    {
        name: "flip_element",
        description: "翻轉指定的 Revit 建築元素（例如門或窗）。可以選擇翻轉面向(facing)或開向(hand)。",
        inputSchema: {
            type: "object",
            properties: {
                elementId: { type: "number", description: "要翻轉的元素 ID" },
                flipType: { type: "string", description: "翻轉類型: 'facing' (預設，依牆為軸翻轉) 或是 'hand' (左右翻轉)", default: "facing" },
            },
            required: ["elementId"],
        },
    },
    {
        name: "adjust_section_datums",
        description: "自動調整剖面視圖的網格線 (Grids) 與樓層線 (Levels) 2D 範圍與氣泡顯示。",
        inputSchema: {
            type: "object",
            properties: {
                viewIds: {
                    type: "array",
                    items: { type: "number" },
                    description: "要調整的剖面視圖或剖面標記的 Element ID 列表"
                }
            },
            required: ["viewIds"]
        }
    },
    {
        name: "analyze_floor_slopes",
        description: "分析樓板頂面排水坡度：以 Solid→PlanarFace 法向量與 Z 軸夾角計算每片朝上頂面的坡度百分比，回傳每片樓板的 Min/Max 坡度。預設只分析、不寫入模型（不開 Transaction）；只有明確指定 paramName 時才會回寫該參數。未指定 elementIds 時自動收集 Function=Exterior 的樓板。",
        inputSchema: {
            type: "object",
            properties: {
                elementIds: {
                    type: "array",
                    items: { type: "number" },
                    description: "要分析的樓板 Element ID 陣列；省略則自動收集所有 Function=Exterior 樓板"
                },
                paramName: {
                    type: "string",
                    description: "選填。坡度回寫的目標參數名稱；**省略即完全不寫入模型**，分析結果仍會回傳。指定後會覆寫該參數既有內容：填 \"Comments\" 會寫入內建註解參數（BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS，不受 Revit 介面語言影響），其他名稱則以 LookupParameter 解析自訂／共用參數或本地化顯示名（例如「註解」）"
                }
            }
        }
    },
    {
        name: "list_categories",
        description: "列舉專案中所有品類 (Category) 及其 CategoryType (Model / Annotation / Internal / AnalyticalModel / Invalid)。回傳每個品類的 Name、CategoryType、Id、子品類數量，並附各 CategoryType 的統計。唯讀查詢。",
        inputSchema: {
            type: "object",
            properties: {
                categoryType: {
                    type: "string",
                    description: "只列出指定 CategoryType 的品類 (Model / Annotation / Internal / AnalyticalModel / Invalid)；省略則全部列出。"
                },
                includeSubcategories: {
                    type: "boolean",
                    description: "是否連同各品類的子品類名稱一併回傳 (預設 false，僅回傳子品類數量)。"
                }
            }
        }
    }
];
