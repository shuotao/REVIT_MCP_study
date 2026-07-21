/**
 * 房間/法規檢討工具 — architect, fire-safety Profile
 */

import { Tool } from "@modelcontextprotocol/sdk/types.js";

export const roomTools: Tool[] = [
    {
        name: "get_room_info",
        description: "取得房間詳細資訊，包含中心點座標和邊界範圍。",
        inputSchema: {
            type: "object",
            properties: {
                roomId: { type: "number", description: "房間 Element ID（選填）" },
                roomName: { type: "string", description: "房間名稱（選填）" },
            },
        },
    },
    {
        name: "get_rooms_by_level",
        description: "取得指定樓層的所有房間清單，包含名稱、編號、面積、用途等。可用於容積檢討。",
        inputSchema: {
            type: "object",
            properties: {
                level: { type: "string", description: "樓層名稱（如：1F、Level 1）" },
                includeUnnamed: { type: "boolean", description: "是否包含未命名的房間", default: true },
            },
            required: ["level"],
        },
    },
    {
        name: "analyze_tall_partition_rooms",
        description: "Find rooms on target levels that share or contain TYPE partition walls taller than a threshold. Uses room boundary segment ElementIds so shared room-to-room walls are assigned to both rooms, and can add upward ray evidence from room bottom to floor underside.",
        inputSchema: {
            type: "object",
            properties: {
                levels: {
                    type: "array",
                    items: { type: "string" },
                    description: "Level names to scan. Defaults to B-1F and B-3F unless autoDetectLevels is true.",
                },
                autoDetectLevels: {
                    type: "boolean",
                    description: "When true and levels is omitted, scan all placed-room levels and let tall TYPE walls identify candidates.",
                    default: false,
                },
                wallTypeContains: {
                    type: "string",
                    description: "Case-insensitive wall type name keyword for partition walls.",
                    default: "TYPE",
                },
                minWallHeightMm: {
                    type: "number",
                    description: "Minimum wall height in millimeters. Default is 6000.",
                    default: 6000,
                },
                excludeRoomNameContains: {
                    type: "array",
                    items: { type: "string" },
                    description: "Room name keywords to exclude. Defaults include shafts, stairs, and elevators.",
                },
                includeSingleRoomBoundaryWalls: {
                    type: "boolean",
                    description: "Include tall TYPE walls that only appear on one target room boundary. Set false to keep only walls shared by two or more target rooms.",
                    default: true,
                },
                includeRoomRayHeight: {
                    type: "boolean",
                    description: "Add ray-cast evidence from room bottom to the nearest floor underside above sampled points.",
                    default: true,
                },
                sampleGrid: {
                    type: "number",
                    description: "Room ray sample grid size, clamped to 1-7. Default 3.",
                    default: 3,
                },
                maxSearchDistanceMm: {
                    type: "number",
                    description: "Maximum upward/downward floor search distance used by the ray helper.",
                    default: 40000,
                },
                includeDetails: {
                    type: "boolean",
                    description: "Return wall-level and room-side details.",
                    default: true,
                },
            },
        },
    },
    {
        name: "get_room_door_counts",
        description: "Count doors by room. Each door is counted once against its primary room; default primary room is ToRoom with FromRoom fallback. Compatible with Revit 2020.",
        inputSchema: {
            type: "object",
            properties: {
                level: {
                    type: "string",
                    description: "Optional level name or unambiguous partial level name. If omitted, all placed rooms are considered.",
                },
                roomIds: {
                    type: "array",
                    items: { type: "number" },
                    description: "Optional explicit Room ElementId list. Overrides level filtering for rooms.",
                },
                includeUnnamed: {
                    type: "boolean",
                    description: "Whether unnamed placed rooms should be included.",
                    default: true,
                },
                includeDoorDetails: {
                    type: "boolean",
                    description: "Whether to include door-level detail rows under each room.",
                    default: true,
                },
                primaryRoomSource: {
                    type: "string",
                    enum: ["toRoom", "fromRoom", "auto"],
                    description: "Which Revit door room relationship counts as the primary room. Default toRoom counts each door once using ToRoom, then falls back to FromRoom.",
                    default: "toRoom",
                },
            },
        },
    },
    {
        name: "get_room_window_counts",
        description: "Count windows by room. Each window is counted once against its primary room; default primary room is ToRoom with FromRoom fallback. Compatible with Revit 2020.",
        inputSchema: {
            type: "object",
            properties: {
                level: {
                    type: "string",
                    description: "Optional level name or unambiguous partial level name. If omitted, all placed rooms are considered.",
                },
                roomIds: {
                    type: "array",
                    items: { type: "number" },
                    description: "Optional explicit Room ElementId list. Overrides level filtering for rooms.",
                },
                includeUnnamed: {
                    type: "boolean",
                    description: "Whether unnamed placed rooms should be included.",
                    default: true,
                },
                includeWindowDetails: {
                    type: "boolean",
                    description: "Whether to include window-level detail rows under each room.",
                    default: true,
                },
                primaryRoomSource: {
                    type: "string",
                    enum: ["toRoom", "fromRoom", "auto"],
                    description: "Which Revit window room relationship counts as the primary room. Default toRoom counts each window once using ToRoom, then falls back to FromRoom.",
                    default: "toRoom",
                },
            },
        },
    },
    {
        name: "renumber_rooms_by_level",
        description: "Batch-renumber placed rooms on one level in a single Revit transaction. Sorts by room center from top to bottom, then left to right, using a configurable Y-row tolerance. Supports dry-run preview and starts from a seed such as B134.",
        inputSchema: {
            type: "object",
            properties: {
                level: {
                    type: "string",
                    description: "Level name or unambiguous partial level name, e.g. B1F or C-B1F.",
                },
                startNumber: {
                    type: "string",
                    description: "First room number to assign, e.g. B134. The trailing digit width is preserved.",
                },
                dryRun: {
                    type: "boolean",
                    description: "true previews the planned order without writing to Revit.",
                    default: false,
                },
                includeUnnamed: {
                    type: "boolean",
                    description: "Whether unnamed placed rooms should be included.",
                    default: true,
                },
                yToleranceMm: {
                    type: "number",
                    description: "Y-axis grouping tolerance in millimeters for row detection.",
                    default: 3000,
                },
                parameterName: {
                    type: "string",
                    description: "Optional explicit room number parameter name. If omitted, the Revit built-in room number parameter is used.",
                },
                allowExistingNumberConflicts: {
                    type: "boolean",
                    description: "Allow proposed numbers even when the same room numbers already exist outside the target level.",
                    default: false,
                },
            },
            required: ["level", "startNumber"],
        },
    },
    {
        name: "sync_room_ceiling_finish_from_ceilings",
        description: "依房間範圍偵測同樓層天花板，讀取天花板類型標記，預覽或寫回房間參數（預設：天花板塗層）以更新粉刷明細表。",
        inputSchema: {
            type: "object",
            properties: {
                level: { type: "string", description: "樓層名稱篩選（選填）。" },
                roomName: { type: "string", description: "房間名稱或房間編號部分匹配（選填）。" },
                roomIds: {
                    type: "array",
                    items: { type: "number" },
                    description: "指定房間 ElementId 清單（選填，優先於 level/roomName）。",
                },
                targetParameter: {
                    type: "string",
                    description: "要寫入的房間參數名稱。粉刷明細表的天花板欄位預設為「天花板塗層」。",
                    default: "天花板塗層",
                },
                apply: {
                    type: "boolean",
                    description: "false 只預覽，true 實際寫回房間參數。",
                    default: false,
                },
                overwrite: {
                    type: "boolean",
                    description: "是否覆寫已有值的房間參數。",
                    default: false,
                },
                sampleGrid: {
                    type: "number",
                    description: "在天花板與房間 BoundingBox 重疊區內取樣確認是否位於房間內，範圍 1-7，預設 3。",
                    default: 3,
                },
                multiMatchStrategy: {
                    type: "string",
                    enum: ["largestOverlap", "join"],
                    description: "多個天花板類型命中同一房間時，取最大重疊類型標記或用 + 合併。",
                    default: "largestOverlap",
                },
            },
        },
    },
    {
        name: "remap_room_finish_codes",
        description: "Batch-remap room finish code parameters in one Revit transaction. Designed for painting/finish schedules: split values by '+', replace exact code tokens such as F11 -> F10 without touching F1 inside F11, and let room schedules update from the changed room parameters. Defaults to dryRun=true.",
        inputSchema: {
            type: "object",
            properties: {
                mapping: {
                    type: "object",
                    additionalProperties: { type: "string" },
                    description: "Required code mapping, e.g. { \"F11\": \"F10\", \"W4\": \"W3\" }.",
                },
                fields: {
                    type: "array",
                    items: { type: "string" },
                    description: "Room parameter names to update. Defaults to 樓板塗層, 踢腳, 牆面塗層, 天花板塗層.",
                },
                apply: {
                    type: "boolean",
                    description: "Set true to write changes to Revit. When omitted, the tool previews only.",
                    default: false,
                },
                dryRun: {
                    type: "boolean",
                    description: "When true, preview planned changes without writing. Defaults to true unless apply=true.",
                    default: true,
                },
                level: {
                    type: "string",
                    description: "Optional level name or unique partial name filter.",
                },
                roomName: {
                    type: "string",
                    description: "Optional room name contains filter.",
                },
                roomNumber: {
                    type: "string",
                    description: "Optional room number contains filter.",
                },
                roomIds: {
                    type: "array",
                    items: { type: "number" },
                    description: "Optional explicit Room ElementId list. Overrides all-room collection before filters are applied.",
                },
                includeUnplaced: {
                    type: "boolean",
                    description: "Whether unplaced or zero-area rooms should be included.",
                    default: false,
                },
                maxChangedRooms: {
                    type: "number",
                    description: "Maximum changed room detail rows returned in the response. Summary counts are always complete.",
                    default: 200,
                },
            },
            required: ["mapping"],
        },
    },
    {
        name: "check_sanitary_fixture_requirements",
        description: "Calculate sanitary fixture requirements by detecting the building type and applying the matching rule. This rule package currently supports C-1 factory/warehouse only; future building types should be added as separate rules. Output maps to the code table columns: building type, water closets, urinals, lavatories, and bathtubs/showers. Net area excludes stairs, elevators, air-raid shelter/refuge rooms, and parking spaces. This tool does not create or write Revit parameters.",
        inputSchema: {
            type: "object",
            properties: {
                level: {
                    type: "string",
                    description: "Optional level name. If omitted, roomIds or all placed rooms matching filters are used.",
                },
                roomNameContains: {
                    type: "string",
                    description: "Optional Room name filter, useful for factory/building scopes such as C-1.",
                },
                roomNumberContains: {
                    type: "string",
                    description: "Optional Room number filter.",
                },
                buildingType: {
                    type: "string",
                    description: "Optional building type / occupancy group hint, such as C-1, C-1 factory, factory, or warehouse. If omitted, the tool detects from level/view/project/room context and defaults to C-1 because this package currently supports C-1 only.",
                },
                roomIds: {
                    type: "array",
                    items: { type: "number" },
                    description: "Optional explicit Room ElementId list. Overrides level/name/number filters.",
                },
                areaPerPersonM2: {
                    type: "number",
                    description: "Occupancy density in square meters per person.",
                    default: 10,
                },
                maleRatio: {
                    type: "number",
                    description: "Male side of male:female ratio. Default 1.",
                    default: 1,
                },
                femaleRatio: {
                    type: "number",
                    description: "Female side of male:female ratio. Default 1.",
                    default: 1,
                },
                excludeKeywords: {
                    type: "array",
                    items: { type: "string" },
                    description: "Optional extra Room name/number keywords to exclude from occupancy area in addition to stairs, elevators, refuge/shelter, and parking defaults.",
                },
            },
        },
    },
    {
        name: "get_room_daylight_info",
        description: "取得房間的採光資訊，包含居室面積、外牆開口面積、採光比例。用於建築技術規則居室採光檢討。",
        inputSchema: {
            type: "object",
            properties: {
                level: { type: "string", description: "樓層名稱（選填）" },
            },
        },
    },
    {
        name: "check_exterior_wall_openings",
        description: "依據台灣建築技術規則第45條及第110條檢討外牆開口。自動讀取地界線計算距離，以顏色標示違規。",
        inputSchema: {
            type: "object",
            properties: {
                checkArticle45: { type: "boolean", description: "檢查第45條", default: true },
                checkArticle110: { type: "boolean", description: "檢查第110條", default: true },
                colorizeViolations: { type: "boolean", description: "以顏色標示", default: true },
                exportReport: { type: "boolean", description: "匯出 JSON 報表", default: false },
                reportPath: { type: "string", description: "報表輸出路徑" },
            },
        },
    },
    {
        name: "get_room_surface_areas",
        description: "計算房間內部表面積（牆面、地板、天花板），支援門窗開口扣除。即使模型中無實體天花板或地板元素，仍會以房間平面面積估算。回傳含 EstimatedSurfaces 欄位標示哪些為估算值。用於材料估算、塗裝面積計算、聲學分析。啟用 includeFinishLayers 可偵測房間內非邊界粉刷層，自動寫入房間飾面參數、建立明細表、匯出 Excel。【兩次呼叫工作流程】當 includeFinishLayers=true 時，建議分兩次呼叫：第一次不帶 defaultXxxFinish 參數，取得分析結果後檢查哪些房間/表面缺少粉刷層（FloorFinishLayers / CeilingFinishLayers / Breakdown.FinishLayers 為 null），詢問使用者要統一填入什麼預設類型標記（地板/牆面/天花各一種，留空=不填），再以 defaultFloorFinish / defaultWallFinish / defaultCeilingFinish 參數第二次呼叫產出最終明細表與 Excel。",
        inputSchema: {
            type: "object",
            properties: {
                roomId: { type: "number", description: "房間 Element ID（選填）" },
                roomName: { type: "string", description: "房間名稱篩選（選填）" },
                level: { type: "string", description: "樓層名稱 — 計算該層所有房間（選填）" },
                includeBreakdown: { type: "boolean", description: "是否包含各牆面詳細資訊（預設 true）", default: true },
                subtractOpenings: { type: "boolean", description: "是否扣除門窗開口面積（預設 true）", default: true },
                includeFinishLayers: { type: "boolean", description: "是否偵測房間內的粉刷層/面飾層並建立明細表、匯出 Excel。必須明確指定 true 或 false。" },
                outputPath: { type: "string", description: "Excel 匯出路徑（選填，預設為專案目錄）" },
                defaultFloorFinish: { type: "string", description: "未偵測到地板粉刷層時的預設類型標記（Type Mark）。留空表示不填。" },
                defaultWallFinish: { type: "string", description: "未偵測到牆面粉刷層時的預設類型標記（Type Mark）。留空表示不填。" },
                defaultCeilingFinish: { type: "string", description: "未偵測到天花粉刷層時的預設類型標記（Type Mark）。留空表示不填。" },
            },
            required: ["includeFinishLayers"],
        },
    },
    {
        name: "create_finish_legend",
        description: "在 Revit 中自動建立粉刷／油漆材料填滿圖例。同時偵測兩種資料來源：(1) 全專案房間的粉刷層（CompoundStructure Function=Finish）、(2) 被「油漆工具」塗在 Wall/Floor/Ceiling 的材料（依面法向量分類牆/地/天）。為每種材料建立 FilledRegionType 並在 Legend 視圖中繪製三張表（地坪/牆面/天花）。每張表三欄：編號 | 圖例 | 說明；粉刷類型使用 TypeMark/TypeName，油漆材料使用 Material.Mark/Description（空值顯示『(未填)』）。粉刷列在上、油漆列在下，中間以分隔列隔開。前提：專案必須已有任一 Legend 視圖（即使空白）作為複製模板，因 Revit API 不允許直接建立 Legend。版面固定（1:100 比例，欄寬 130/120/650 cm、列高 50 cm）。",
        inputSchema: {
            type: "object",
            properties: {
                legendName: { type: "string", description: "新 Legend 視圖名稱（選填，預設『粉刷圖例_yyyyMMdd』）" },
                legendTemplateName: { type: "string", description: "指定要複製的 Legend 名稱（選填，預設取專案第一個 Legend）" },
            },
        },
    },
    {
        name: "batch_set_room_height",
        description: "批次依房間名稱或用途分組，設定 Room 的 Upper Limit（ROOM_UPPER_LEVEL）與 Limit Offset（ROOM_UPPER_OFFSET）。不動樓層，只改 Room 參數。對 Model Group 內的 Room 會自動進入 EditGroup 模式（每個 GroupType 只編輯一次，變更同步到所有 instance），避免「modified outside group edit mode」警告。Transaction 內註冊 WarningSwallower 吞掉其他警告。單一 Transaction，可 Ctrl+Z 整批還原。",
        inputSchema: {
            type: "object",
            properties: {
                groups: {
                    type: "array",
                    description: "分組規則陣列。每個 group 指定一個房間名稱/用途關鍵字與目標高度(mm)。",
                    items: {
                        type: "object",
                        properties: {
                            nameMatch: {
                                type: "string",
                                description: "房間名稱/用途關鍵字（不區分大小寫，部分比對）。例：'居室'、'浴室'、'走廊'",
                            },
                            heightMm: {
                                type: "number",
                                description: "目標高度（mm，絕對值）。即 Upper Limit 對應樓層之上的 Limit Offset。範圍 1~10000。",
                            },
                            upperLevelName: {
                                type: "string",
                                description: "選填：指定 Upper Limit 要使用的樓層名稱。若留空則使用 Room 的 Base Level（房間高度 = Limit Offset = heightMm）。",
                            },
                        },
                        required: ["nameMatch", "heightMm"],
                    },
                },
                levelName: {
                    type: "string",
                    description: "選填：限制只修改某一樓層的 Room。留空則全專案。",
                },
                matchField: {
                    type: "string",
                    enum: ["name", "department"],
                    description: "比對欄位：'name' (ROOM_NAME) 或 'department' (ROOM_DEPARTMENT)。預設 'name'。",
                    default: "name",
                },
                summaryOnly: {
                    type: "boolean",
                    description: "true（預設）=只回計數與錯誤列表，避免 500+ Room 時 payload 爆炸；false=附帶每個 Room 的 OriginalValues 與 Modifications 明細（供 audit）。",
                    default: true,
                },
            },
            required: ["groups"],
        },
    },
];
