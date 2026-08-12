/**
 * 視覺化工具 — 圖形覆寫、視圖樣版
 * 所有 Profile 都可選用
 */

import { Tool } from "@modelcontextprotocol/sdk/types.js";

export const visualizationTools: Tool[] = [
    {
        name: "create_green_material",
        description: "在 Revit 專案材質庫 (OST_Materials) 中實體發動 Duplicate/Create 獨立建立純淨綠建材或測試 Material (如 'test材質' 或 'GBM0104106_水性漆(居室外用)')。",
        inputSchema: {
            type: "object",
            properties: {
                materialName: {
                    type: "string",
                    description: "材質名稱，例如 'test材質' 或 'GBM0104106_水性漆(居室外用)'",
                },
                r: { type: "number", default: 235 },
                g: { type: "number", default: 245 },
                b: { type: "number", default: 240 },
            },
            required: ["materialName"],
        },
    },
    {
        name: "create_material",
        description: "在 Revit 專案資料庫 (OST_Materials) 中建立獨立 Material 並關聯獨立 AppearanceAssetElement。",
        inputSchema: {
            type: "object",
            properties: {
                materialName: {
                    type: "string",
                    description: "材質名稱，例如 'test材質'",
                },
            },
            required: ["materialName"],
        },
    },
    {
        name: "create_material_by_domain",
        description: "遵循 Domain 規範建立純淨獨立的 Revit Material（不加前綴、絕不上預設牆字串）。",
        inputSchema: {
            type: "object",
            properties: {
                materialName: {
                    type: "string",
                    description: "材質名稱，例如 'GBM0103810_NICHIAS矽酸鈣板材'",
                },
            },
            required: ["materialName"],
        },
    },
    {
        name: "get_all_materials",
        description: "查詢 Project Materials（材質瀏覽器）清單中所有現存材質，用於在建立材質後主動驗收確認是否已實體存在。",
        inputSchema: {
            type: "object",
            properties: {
                searchKeyword: {
                    type: "string",
                    description: "依名稱關鍵字篩選（如 'GBM'）。留空或 '*' 表示回傳全部材質。",
                },
            },
        },
    },
    {
        name: "duplicate_element_type",
        description: "複製指定 ElementType 建立新類型（如綠建材 Set 專屬牆類型），並依指定的飾面/結構材質名稱實體建立 2 個獨立 Material，套入 Finish1/Structure/Finish2 三層 CompoundStructure 構造層。用於將牆板＋塗料組合 Set 導入為單一牆體 Element Type。",
        inputSchema: {
            type: "object",
            properties: {
                sourceTypeId: { type: "number", description: "來源類型 Element ID（優先選用含粉刷層的型號）" },
                newTypeName: { type: "string", description: "新類型名稱，例如 'TABC_test牆'（禁用中括號）" },
                finishMaterialName: {
                    type: "string",
                    description: "飾面塗料材質名稱，格式須為 'GBM編號_材料名稱'，例如 'GBM0104106_水性漆(居室外用)'",
                },
                structureMaterialName: {
                    type: "string",
                    description: "結構板材材質名稱，格式須為 'GBM編號_材料名稱'，例如 'GBM0103810_無機質NICHIAS NA LUX矽酸鈣板(0.8FK)'",
                },
                finishThicknessMm: { type: "number", description: "飾面層厚度 (mm)", default: 20 },
                structureThicknessMm: { type: "number", description: "結構層厚度 (mm)", default: 150 },
            },
            required: ["sourceTypeId", "newTypeName", "finishMaterialName", "structureMaterialName"],
        },
    },
    {
        name: "duplicate_type_only",
        description: "單純複製指定 ElementType（Wall/Floor/Ceiling 皆可），不修改 CompoundStructure、不建立或指派任何 Material，新類型與來源類型構造層完全一致。用於 TASK-005.5 情境 5「單選非模型綠建材」路徑 A：先複製出一個不影響既有元件的新 Type，再另外呼叫 set_green_material_type_parameters 寫入 adhesive/sealant/waterproofing 等 Construction 欄位。與 duplicate_element_type（寫死板材+塗料兩種材料）、create_single_material_type/create_multi_layer_type（會重新指派構造層材質）不同。",
        inputSchema: {
            type: "object",
            properties: {
                sourceTypeId: { type: "number", description: "來源類型 Element ID（任意 Wall/Floor/Ceiling Type）" },
                newTypeName: { type: "string", description: "新類型名稱" },
            },
            required: ["sourceTypeId", "newTypeName"],
        },
    },
    {
        name: "set_green_material_type_parameters",
        description: "將綠建材共享參數 Schema（GreenMaterial_SharedParams.txt，Mat1~Mat6 六槽位 + Construction 群組共 67 個欄位）實體寫入指定 ElementType 的 Identity Data。參數須已透過 load_shared_parameters 綁定至該 Type 所屬品類（如 Walls），否則對應欄位會列在回傳的 MissingParameters 中。Mat1=主體/牆板，Mat2=面材/塗料，Mat3=附屬/膠材（僅有基本欄位，無 TVOC/Formaldehyde/CNS），Mat4/Mat5/Mat6=追加構造層（欄位與 Mat1/Mat2 同樣完整）。一個 Set 有幾種材料就只傳幾個 matN 物件，其餘留空，不必寫滿 6 組；哪個材料進哪個槽位請依 generate_revit_injection_plan.py 產出的 plan['materialSlotAssignment'] 決定，不要自行猜測順序——非幾何輔助材料（接著劑/填縫劑/防水材料）一樣要填一個 matN 物件（Mat 槽位記錄的是「這個元件用了哪些綠建材」的完整清單，不是只有物理構造層才算數），另外再用 adhesive/sealant/waterproofing 額外補記它們的施工用途，兩者並存不衝突、不是二選一。",
        inputSchema: {
            type: "object",
            properties: {
                typeId: { type: "number", description: "目標 ElementType Element ID（如 duplicate_element_type 建立的新型別）" },
                certified: { type: "boolean", description: "GreenMaterial_Certified：全牆綠建材評定合格狀態" },
                recycledRatio: { type: "number", description: "GreenMaterial_RecycledRatio：再生材料回收摻配率 (%)" },
                acousticNRC: { type: "number", description: "GreenMaterial_AcousticNRC：吸音係數 (NRC / SAA)" },
                mat1: {
                    type: "object",
                    description: "材料1（主體/牆板）",
                    properties: {
                        name: { type: "string" },
                        certNo: { type: "string", description: "綠建材標章證書字號，如 'GBM0103810'" },
                        category: { type: "string" },
                        subCategory: { type: "string" },
                        applicant: { type: "string" },
                        validUntil: { type: "string" },
                        tvoc: { type: "number", description: "TVOC 逸散率 (mg/m2.h)" },
                        formaldehyde: { type: "number", description: "甲醛逸散率 (mg/m2.h)" },
                        cnsSpec: { type: "string" },
                        testItems: { type: "string" },
                        qualifiedItems: { type: "string" },
                    },
                },
                mat2: {
                    type: "object",
                    description: "材料2（面材/塗料）",
                    properties: {
                        name: { type: "string" },
                        certNo: { type: "string" },
                        category: { type: "string" },
                        subCategory: { type: "string" },
                        applicant: { type: "string" },
                        validUntil: { type: "string" },
                        tvoc: { type: "number" },
                        formaldehyde: { type: "number" },
                        cnsSpec: { type: "string" },
                        testItems: { type: "string" },
                        qualifiedItems: { type: "string" },
                    },
                },
                mat3: {
                    type: "object",
                    description: "材料3（附屬/膠材，選填，僅基本欄位）",
                    properties: {
                        name: { type: "string" },
                        certNo: { type: "string" },
                        category: { type: "string" },
                        subCategory: { type: "string" },
                        applicant: { type: "string" },
                        validUntil: { type: "string" },
                    },
                },
                mat4: {
                    type: "object",
                    description: "材料4（追加構造層，欄位與 Mat1/Mat2 同樣完整）",
                    properties: {
                        name: { type: "string" },
                        certNo: { type: "string" },
                        category: { type: "string" },
                        subCategory: { type: "string" },
                        applicant: { type: "string" },
                        validUntil: { type: "string" },
                        tvoc: { type: "number" },
                        formaldehyde: { type: "number" },
                        cnsSpec: { type: "string" },
                        testItems: { type: "string" },
                        qualifiedItems: { type: "string" },
                    },
                },
                mat5: {
                    type: "object",
                    description: "材料5（追加構造層，欄位與 Mat1/Mat2 同樣完整）",
                    properties: {
                        name: { type: "string" },
                        certNo: { type: "string" },
                        category: { type: "string" },
                        subCategory: { type: "string" },
                        applicant: { type: "string" },
                        validUntil: { type: "string" },
                        tvoc: { type: "number" },
                        formaldehyde: { type: "number" },
                        cnsSpec: { type: "string" },
                        testItems: { type: "string" },
                        qualifiedItems: { type: "string" },
                    },
                },
                mat6: {
                    type: "object",
                    description: "材料6（追加構造層，欄位與 Mat1/Mat2 同樣完整）",
                    properties: {
                        name: { type: "string" },
                        certNo: { type: "string" },
                        category: { type: "string" },
                        subCategory: { type: "string" },
                        applicant: { type: "string" },
                        validUntil: { type: "string" },
                        tvoc: { type: "number" },
                        formaldehyde: { type: "number" },
                        cnsSpec: { type: "string" },
                        testItems: { type: "string" },
                        qualifiedItems: { type: "string" },
                    },
                },
                adhesive: { type: "string", description: "GreenMaterial_Adhesive：附著黏貼之接著劑標章資訊，格式 '產品名稱 (標章編號)'（Construction 群組，非幾何輔助材料專用，該材料仍須另外填入對應的 matN）" },
                sealant: { type: "string", description: "GreenMaterial_Sealant：附著填縫之矽利康/密封膠資訊，格式同上" },
                waterproofing: { type: "string", description: "GreenMaterial_Waterproofing：附著塗佈之防水膜資訊，格式同上" },
            },
            required: ["typeId"],
        },
    },
    {
        name: "create_single_material_type",
        description: "情境 2「各別建立」：複製指定 ElementType（Wall/Floor/Ceiling Type）建立新類型，並實體建立一個純淨綠建材 Material，指派到新 Type 的全部 CompoundStructure 層。Type 名稱與 Material 名稱使用同一組字串（GBM編號_材料名稱），不套 TABC_ 前綴。用於一個 Set 裡每個材料各自獨立建 Type 的情境（例如地板材料各別建立），跟 duplicate_element_type（牆板+塗料兩材料合併一個 Type 的單一組合情境）是不同情境，不要混用。",
        inputSchema: {
            type: "object",
            properties: {
                sourceTypeId: { type: "number", description: "來源類型 Element ID（需與目標品類相同，如同為 FloorType）" },
                materialName: {
                    type: "string",
                    description: "同時作為新 Type 名稱與 Material 名稱，格式須為 'GBM編號_材料名稱'，例如 'GBM0104038_托斯卡尼 TOSCANA複合木質地板'",
                },
            },
            required: ["sourceTypeId", "materialName"],
        },
    },
    {
        name: "create_multi_layer_type",
        description: "通用多材料構造層工具：複製指定 ElementType（Wall/Floor/Ceiling 皆可），依任意數量的材料清單建立獨立綠建材 Material，依序套入 CompoundStructure 各層。跟 duplicate_element_type（寫死 2 個材料的牆體 Finish1/Structure/Finish2 三明治）不同，這裡層數、材料、層位機能完全由呼叫端指定，適用於 2 個以上材料、或非 Wall 品類的單一組合情境（例如地板：飾面地磚 Finish1 + 隔音緩衝墊 Substrate + 混凝土 Structure 三層）。layers 陣列請依實際構造由上到下（或由外到內）的順序排列。",
        inputSchema: {
            type: "object",
            properties: {
                sourceTypeId: { type: "number", description: "來源類型 Element ID（需與目標品類相同，如同為 FloorType）" },
                newTypeName: { type: "string", description: "新類型名稱，例如 'TABC_塑膠地板set'" },
                layers: {
                    type: "array",
                    description: "依構造順序排列的層清單",
                    items: {
                        type: "object",
                        properties: {
                            materialName: { type: "string", description: "格式須為 'GBM編號_材料名稱'" },
                            layerFunction: {
                                type: "string",
                                enum: ["Structure", "Substrate", "Insulation", "Finish1", "Finish2", "Membrane"],
                                description: "對應 Revit MaterialFunctionAssignment：Structure=結構核心層，Substrate=底材/緩衝層，Finish1/Finish2=飾面層，Insulation=隔熱層，Membrane=防水膜",
                            },
                            thicknessMm: { type: "number", description: "該層厚度 (mm)，預設 20", default: 20 },
                        },
                        required: ["materialName", "layerFunction"],
                    },
                },
            },
            required: ["sourceTypeId", "newTypeName", "layers"],
        },
    },
    {
        name: "inject_green_material_into_family",
        description: "門窗／獨立元件 RFA 綠建材導入（TASK-005.7 / domain/GM_rfa-family-injection.md）：以使用者指定的既有相似 FamilySymbol 為基底，開啟該家族文件 → 立即另存可復原備份（規則2，先於任何修改）→ 在家族文件內新增一個 Type，絕不改動來源 Type（規則1）→ 寫入 Identity Data 與 GreenMaterial_Mat1_* 共享參數 + 遮陽係數/隔音等級門窗專屬欄位（規則3）→ 另存為新家族檔名 → LoadFamily 載回專案，且在同一個 Transaction 內做載入前後同名家族 Type 參數簽章快照比對，一偵測到非目標 Type 被異動就整批回滾並報錯，不會靜默覆蓋（規則4）。單一原子呼叫涵蓋整個家族文件生命週期（開啟→備份→編輯→另存→關閉→載回），因為家族文件物件無法跨多次 MCP 呼叫保持開啟。呼叫前必須已由使用者明確指定 sourceTypeId——規則1禁止 AI 自行臆測或無型錄依據挑選基底 Family。",
        inputSchema: {
            type: "object",
            properties: {
                sourceTypeId: { type: "number", description: "使用者指定的基底 FamilySymbol（門或窗，或幕牆嵌板等載入式族群）Element ID，必須是既有的、經使用者確認過的相似型號，不可由 AI 自行挑選" },
                newTypeName: { type: "string", description: "家族文件內新建 Type 的名稱" },
                backupFolder: { type: "string", description: "備份根目錄絕對路徑（也是新家族檔案的存放目錄）。預設專案檔所在目錄下的 _rfa_backup/（若專案尚未儲存過則退回系統暫存目錄）" },
                newFamilySuffix: { type: "string", description: "新家族檔名後綴，預設 '_TABC'，會再接上 mat1.certNo 組成完整後綴以避免撞名", default: "_TABC" },
                sharedParamFilePath: { type: "string", description: "GreenMaterial_SharedParams.txt 的絕對路徑（repo 根目錄）" },
                identityData: {
                    type: "object",
                    description: "Family Type 內建 Identity Data。依 Revit 版本與族群樣板不保證每個欄位都存在，缺的欄位會列在回傳的 MissingParameters",
                    properties: {
                        manufacturer: { type: "string" },
                        model: { type: "string" },
                        description: { type: "string" },
                        url: { type: "string" },
                    },
                },
                mat1: {
                    type: "object",
                    description: "門/窗主材料（玻璃或門扇）綠建材資料，寫入 GreenMaterial_Mat1_*，格式與 set_green_material_type_parameters 的 mat1 相同",
                    properties: {
                        name: { type: "string" },
                        certNo: { type: "string", description: "綠建材標章證書字號，如 'GBM0103810'" },
                        category: { type: "string" },
                        subCategory: { type: "string" },
                        applicant: { type: "string" },
                        validUntil: { type: "string" },
                        tvoc: { type: "number", description: "TVOC 逸散率 (mg/m2.h)，只在有實際數據時才填，不得估算" },
                        formaldehyde: { type: "number", description: "甲醛逸散率 (mg/m2.h)，只在有實際數據時才填，不得估算" },
                        cnsSpec: { type: "string" },
                        testItems: { type: "string" },
                        qualifiedItems: { type: "string" },
                    },
                    required: ["name", "certNo"],
                },
                shadingCoefficient: { type: "number", description: "GreenMaterial_Window_ShadingCoefficient：遮陽係數 Sc。僅 Window/Curtain Wall 案例填，Door 案例應留空（不適用）" },
                acousticRw: { type: "number", description: "GreenMaterial_AcousticRw：隔音等級 Rw (dB)。Window 與 Door 皆適用，只在型錄/測試報告有明確數據時才填" },
            },
            required: ["sourceTypeId", "newTypeName", "sharedParamFilePath", "mat1"],
        },
    },
    {
        name: "set_material_surface_pattern",
        description: "為綠建材 Material 建立（或重用既有，依名稱去重不重複建立）Model 目標的 Surface Pattern 並套入該材質的表面／剖切樣式。用於地磚 600×600 網格縫線、木地板木紋等依產品規格需要在平面/剖面顯示紋理的飾面材料（TASK-005.2 情境 2）。",
        inputSchema: {
            type: "object",
            properties: {
                materialId: { type: "number", description: "目標材質 Element ID（與 materialName 至少提供一個，優先使用 materialId）" },
                materialName: { type: "string", description: "目標材質名稱，格式須為 'GBM編號_材料名稱'" },
                patternType: {
                    type: "string",
                    enum: ["Grid", "Wood", "None"],
                    description: "Grid=網格縫線（如地磚），Wood=木紋單向紋理線（如木地板），None=清除既有樣式",
                },
                spacingMm: {
                    type: "number",
                    description: "Grid 為縫線間距 (mm)，預設 600（600×600 網格）；Wood 為紋理線間距 (mm)，預設 100",
                },
                target: {
                    type: "string",
                    enum: ["Surface", "Cut", "Both"],
                    description: "套用範圍：Surface=表面樣式（預設，平面圖可見）、Cut=剖切樣式、Both=兩者皆套用",
                    default: "Surface",
                },
            },
            required: ["patternType"],
        },
    },

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
