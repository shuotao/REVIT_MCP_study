/**
 * 牆/門窗/結構工具 — architect, structural Profile
 */

import { Tool } from "@modelcontextprotocol/sdk/types.js";

export const wallTools: Tool[] = [
    {
        name: "create_wall",
        description: "建立非結構直線牆，座標與高度為公釐。可明確指定樓層與基本牆類型；未指定樓層時使用目前平面視圖的樓層，無樓層則報錯。定位線為牆中心線、基準偏移為零。回傳值會揭露實際採用的樓層與牆類型（LevelSource / WallTypeSource）。",
        inputSchema: {
            type: "object",
            properties: {
                startX: { type: "number", description: "起點 X 座標（公釐）" },
                startY: { type: "number", description: "起點 Y 座標（公釐）" },
                endX: { type: "number", description: "終點 X 座標（公釐）" },
                endY: { type: "number", description: "終點 Y 座標（公釐）" },
                height: { type: "number", exclusiveMinimum: 0, description: "牆不連續高度（公釐）", default: 3000 },
                levelId: { type: "integer", minimum: 1, description: "基準樓層 ID（選填；與 levelName 同時提供時必須一致）" },
                levelName: { type: "string", minLength: 1, description: "基準樓層完整名稱（選填；需唯一且完全相符，找不到就報錯）" },
                wallTypeId: { type: "integer", minimum: 1, description: "基本牆類型 ID（選填；與 wallType 同時提供時必須一致）" },
                wallType: { type: "string", minLength: 1, description: "基本牆類型完整名稱（選填；找不到或重名時報錯）" },
            },
            required: ["startX", "startY", "endX", "endY"],
        },
    },
    {
        name: "create_floor",
        description: "在 Revit 中建立樓板。需要指定邊界點座標。回傳值會揭露實際採用的樓層與樓板類型。",
        inputSchema: {
            type: "object",
            properties: {
                points: { type: "array", description: "樓板邊界點陣列", items: { type: "object", properties: { x: { type: "number" }, y: { type: "number" } } } },
                levelName: { type: "string", minLength: 1, description: "樓層名稱（找不到會退回最低樓層，並在回傳值的 Warning / LevelFellBackToLowest 回報）", default: "Level 1" },
                floorType: { type: "string", minLength: 1, description: "樓板類型名稱（選填；可用「族群名: 類型名」。指定了卻找不到或重名會報錯，未指定則用專案預設並以 FloorTypeSource 回報）" },
            },
            required: ["points"],
        },
    },
    {
        name: "create_door",
        description: "在指定的牆上建立門。可指定 sourceElementId 來複製現有門的類型、instance 參數與 facing/hand 朝向。",
        inputSchema: {
            type: "object",
            properties: {
                wallId: { type: "number", description: "要放置門的牆 ID" },
                locationX: { type: "number", description: "門在牆上的位置 X 座標（公釐）" },
                locationY: { type: "number", description: "門在牆上的位置 Y 座標（公釐）" },
                doorType: { type: "string", minLength: 1, description: "門類型名稱（選填；可用「族群名: 類型名」。指定了卻找不到或重名會報錯，不會自動換成別的類型）" },
                sourceElementId: { type: "number", description: "來源門 ID（選填，用於複製其類型、參數與朝向）" },
            },
            required: ["wallId", "locationX", "locationY"],
        },
    },
    {
        name: "create_window",
        description: "在指定的牆上建立窗。可指定 sourceElementId 來複製現有窗的類型、instance 參數與 facing/hand 朝向。",
        inputSchema: {
            type: "object",
            properties: {
                wallId: { type: "number", description: "要放置窗的牆 ID" },
                locationX: { type: "number", description: "窗在牆上的位置 X 座標（公釐）" },
                locationY: { type: "number", description: "窗在牆上的位置 Y 座標（公釐）" },
                windowType: { type: "string", minLength: 1, description: "窗類型名稱（選填；可用「族群名: 類型名」。指定了卻找不到或重名會報錯，不會自動換成別的類型）" },
                sourceElementId: { type: "number", description: "來源窗 ID（選填，用於複製其類型、參數與朝向）" },
            },
            required: ["wallId", "locationX", "locationY"],
        },
    },
    {
        name: "get_wall_info",
        description: "取得牆的詳細資訊，包含厚度、長度、高度、位置線座標等。",
        inputSchema: {
            type: "object",
            properties: { wallId: { type: "number", description: "牆的 Element ID" } },
            required: ["wallId"],
        },
    },
    {
        name: "create_dimension",
        description: "在指定視圖中建立尺寸標註。",
        inputSchema: {
            type: "object",
            properties: {
                viewId: { type: "number", description: "要建立標註的視圖 ID" },
                startX: { type: "number", description: "起點 X 座標（公釐）" },
                startY: { type: "number", description: "起點 Y 座標（公釐）" },
                endX: { type: "number", description: "終點 X 座標（公釐）" },
                endY: { type: "number", description: "終點 Y 座標（公釐）" },
                offset: { type: "number", description: "標註線偏移距離（公釐）", default: 500 },
            },
            required: ["viewId", "startX", "startY", "endX", "endY"],
        },
    },
    {
        name: "create_corridor_dimension",
        description: "走廊寬度標註 — 自動偵測房間邊界的平行牆對，建立精確的牆到牆尺寸標註。回傳每個區段的實測寬度與合規判定。",
        inputSchema: {
            type: "object",
            properties: {
                roomId: { type: "number", description: "走廊房間的 Element ID" },
                viewId: { type: "number", description: "要建立標註的平面視圖 ID" },
            },
            required: ["roomId", "viewId"],
        },
    },
    {
        name: "query_walls_by_location",
        description: "查詢指定座標附近的牆體，回傳牆厚度、位置線與牆面座標。",
        inputSchema: {
            type: "object",
            properties: {
                x: { type: "number", description: "搜尋中心 X 座標" },
                y: { type: "number", description: "搜尋中心 Y 座標" },
                searchRadius: { type: "number", description: "搜尋半徑 (mm)" },
                level: { type: "string", description: "樓層名稱 (選填)" },
            },
            required: ["x", "y", "searchRadius"],
        },
    },
    {
        name: "rejoin_wall_joins",
        description: "恢復先前由 unjoin_element_joins 取消的接合關係（共用 pair 儲存，限同一 Revit session）。",
        inputSchema: { type: "object", properties: {} },
    },
    {
        name: "unjoin_element_joins",
        description: "以指定來源類別為中心，解除其與鄰近 target 類別元素的幾何接合（BoundingBox 外擴 1 ft 搜尋）。預設 target 為 8 類（Walls、Floors、Columns、StructuralColumns、StructuralFraming、StructuralFoundation、Roofs、Ceilings）。常見用法：牆-柱（上色前置）＝ sourceCategory: Walls、targetCategories: [Columns, StructuralColumns]；柱本位 ＝ sourceCategories: [Columns, StructuralColumns]、targetCategories: [Walls, Floors, StructuralFraming]。解除的配對存在同一份 session 儲存，可用 rejoin_wall_joins 還原（限同一 Revit session）。",
        inputSchema: {
            type: "object",
            properties: {
                sourceCategory: { type: "string", description: "來源類別名稱（不含 OST_ 前綴，例如 StructuralFraming、Walls、Floors）。與 sourceCategories 擇一" },
                sourceCategories: {
                    type: "array",
                    items: { type: "string" },
                    description: "多個來源類別（例如 [\"Columns\", \"StructuralColumns\"] 一次涵蓋建築柱與結構柱）。與 sourceCategory 擇一",
                },
                targetCategories: {
                    type: "array",
                    items: { type: "string" },
                    description: "目標類別名稱陣列（選填，預設 8 類：Walls、Floors、Columns、StructuralColumns、StructuralFraming、StructuralFoundation、Roofs、Ceilings）",
                },
                elementIds: { type: "array", items: { type: "number" }, description: "要處理的來源 Element ID 列表（選填，預設為來源類別全部）" },
                viewId: { type: "number", description: "視圖 ID（選填，未給 elementIds 時用來限縮範圍）" },
            },
        },
    },
    {
        name: "join_wall_tops",
        description: "將指定樓層的牆，其頂部與上方的樓板、天花板、結構樑做幾何接合 (JoinGeometry)。回傳成功接合/已接合跳過/失敗的統計與各樓層明細。常用於樓層牆體建模後的批次頂接合。",
        inputSchema: {
            type: "object",
            properties: {
                wallTopToBeams: {type:"object",additionalProperties:false,required:["wallId","beamIds"],properties:{wallId:{type:"integer",minimum:1},beamIds:{type:"array",items:{type:"integer",minimum:1},minItems:1,maxItems:50,uniqueItems:true},targetSurface:{type:"string",enum:["top","bottom"],default:"top"}},description:"以牆中心面與指定梁表面交會的最高、最低兩點連成單一斜牆頂並延伸至牆端。targetSurface=bottom取梁底，top取梁頂。等高區取區間中點。保留牆底貼附、牆底形狀與平面位置，驗證失敗整組回滾。先dryRun預覽，再以previewToken套用；不支援已編輯輪廓、洞口或頂部貼附。"},
                wallTopToFloor: {type:"object",additionalProperties:false,required:["wallId","floorId"],properties:{wallId:{type:"integer",minimum:1},floorId:{type:"integer",minimum:1},extendFloorPlane:{type:"boolean",default:false}},description:"將單一直立直牆的頂部輪廓修剪至指定樓板斜頂面，保留牆底與位置；非動態貼附。不支援既有輪廓/洞口/跨向坡度，預覽後以token套用。extendFloorPlane 明確允許未覆蓋處沿同一坡面延伸，限600mm。"},
                wallBottomToBeam: { type: "object", additionalProperties: false, required: ["wallId","beamId"], properties: {wallId:{type:"integer",minimum:1},beamId:{type:"integer",minimum:1}}, description: "修改指定直立直牆輪廓的下緣，貼合指定斜梁實際頂面；保留牆頂、平面位置、牆厚及原牆ID。須先解除頂底貼附，不支援洞口、已編輯輪廓或跨向坡度。不是動態貼附。先預覽，再以token套用。" },
                previewToken: {type:"string"},
                beamCutsFloor: { type: "boolean", default: false, description: "梁切割樓板；預設唯讀，dryRun=false 且 apply=true 才修正，與其他模式互斥。逐組失敗回滾；回傳提交後 Verification。" },
                auditBeamFloor: { type: "boolean", default: false, description: "唯讀檢核梁＞樓板；回傳 Correct/Reversed/NotJoined/Failed。必須 dryRun=true、apply=false，與其他模式互斥。levels 依樓板樓層精確篩選；省略檢查全專案。不修改接合。" },
                floorCutsWall: { type: "boolean", default: false, description: "全牆高度檢查與樓板的相交及既有接合，強制樓板切割牆；與 beamCutsWall 互斥。牆與樓板接合請使用此模式。" },
                beamCutsWall: { type: "boolean", default: false, description: "只接合牆頂結構梁，強制梁切割牆，並修正既有反向接合。" },
                dryRun: { type: "boolean", default: true, description: "切割順序模式預設僅預覽。" },
                apply: { type: "boolean", default: false, description: "切割順序模式須 dryRun=false 且 apply=true 才寫入。" },
                levels: {
                    type: "array",
                    items: { type: "string" },
                    description: "要處理的樓層名稱列表，例如 [\"2F\", \"3F\"]。省略或空陣列代表處理全部樓層。",
                },
            },
        },
    },
    {
        name: "create_wall_sweeps",
        description: "在指定牆上用牆飾條（Wall Sweep）或分隔縫（Reveal）把牆等分：垂直方向沿牆長、水平方向沿牆高，divisions 等分會建立 divisions-1 條。可指定既有輪廓，或給 profileWidth/profileDepth 自動新建矩形輪廓族群。先用 listOnly 查專案有哪些飾條類型與輪廓，或用 dryRun 預覽等分位置。",
        inputSchema: {
            type: "object",
            properties: {
                wallId: { type: "number", description: "要加飾條的牆 Element ID" },
                divisions: { type: "number", description: "等分數（5 等分會建立 4 條）", default: 5 },
                orientation: { type: "string", enum: ["vertical", "horizontal"], description: "vertical=垂直飾條沿牆長等分；horizontal=水平飾條沿牆高等分", default: "vertical" },
                sweepKind: { type: "string", enum: ["sweep", "reveal"], description: "sweep=凸出的牆飾條；reveal=內凹的分隔縫", default: "sweep" },
                wallSide: { type: "string", enum: ["exterior", "interior"], description: "附著在牆的哪一側", default: "exterior" },
                distanceMeasuredFrom: { type: "string", enum: ["base", "top"], description: "水平飾條的距離基準（垂直飾條不適用）", default: "base" },
                profileTypeId: { type: "number", description: "指定既有輪廓類型的 Element ID（選填，優先於自動建輪廓）" },
                profileWidth: { type: "number", description: "自動建矩形輪廓的寬度 mm（沿牆面方向）；與 profileDepth 都給才會建立" },
                profileDepth: { type: "number", description: "自動建矩形輪廓的凸出深度 mm（垂直牆面方向）" },
                profileFamilyName: { type: "string", description: "自動建立的輪廓族群名稱（選填，預設 MCP-矩形輪廓-寬x深）" },
                baseTypeId: { type: "number", description: "要複製的來源飾條類型 Element ID（選填，預設取第一個）" },
                typeName: { type: "string", description: "新建飾條類型的名稱（選填）" },
                listOnly: { type: "boolean", description: "只回報專案現有的飾條類型與輪廓清單，不動模型", default: false },
                dryRun: { type: "boolean", description: "只預演等分位置，不建立元素", default: false },
            },
        },
    },
    {
        name: "get_all_grids",
        description: "取得專案中所有網格線（Grid）的資訊。",
        inputSchema: { type: "object", properties: {} },
    },
    {
        name: "get_column_types",
        description: "取得專案中所有可用的柱類型。",
        inputSchema: {
            type: "object",
            properties: { material: { type: "string", description: "篩選材質（選填）" } },
        },
    },
    {
        name: "create_column",
        description: "在指定位置建立柱子。placements 可單次批次建立結構柱，使用明確類型ID及上下樓層；預設預覽，apply=true且dryRun=false才以單一交易寫入。相同位置及樓層既有柱跳過，不逐筆核准。",
        inputSchema: {
            type: "object",
            properties: {
                placements: {type:"array",minItems:1,maxItems:500,items:{type:"object",additionalProperties:false,required:["x","y","typeId"],properties:{x:{type:"number"},y:{type:"number"},typeId:{type:"integer",minimum:1}}},description:"批次柱中心座標mm與明確結構柱類型ID。"},
                dryRun:{type:"boolean",default:true,description:"僅placements批次模式適用。"},
                apply:{type:"boolean",default:false,description:"僅placements批次模式適用。"},
                x: { type: "number", description: "X 座標（公釐）" },
                y: { type: "number", description: "Y 座標（公釐）" },
                bottomLevel: { type: "string", description: "底部樓層名稱", default: "Level 1" },
                topLevel: { type: "string", description: "頂部樓層名稱（選填）" },
                columnType: { type: "string", description: "柱類型名稱（選填）" },
            },
            anyOf: [{required:["x","y"]},{required:["placements","bottomLevel","topLevel"]}],
        },
    },
    {
        name: "get_furniture_types",
        description: "取得專案中已載入的家具類型清單。",
        inputSchema: {
            type: "object",
            properties: { category: { type: "string", description: "家具類別篩選（選填）" } },
        },
    },
    {
        name: "place_furniture",
        description: "在指定位置放置家具實例。",
        inputSchema: {
            type: "object",
            properties: {
                x: { type: "number", description: "X 座標（公釐）" },
                y: { type: "number", description: "Y 座標（公釐）" },
                furnitureType: { type: "string", description: "家具類型名稱" },
                level: { type: "string", description: "樓層名稱", default: "Level 1" },
                rotation: { type: "number", description: "旋轉角度（度）", default: 0 },
            },
            required: ["x", "y", "furnitureType"],
        },
    },
    {
        name: "get_wall_types",
        description: "取得專案中所有可用的牆類型，包含名稱和 Element ID。",
        inputSchema: {
            type: "object",
            properties: {
                search: { type: "string", description: "關鍵字篩選（選填）" },
            },
        },
    },
    {
        name: "change_element_type",
        description: "變更 Revit 元素的類型（例如將牆從 Type A 改為 Type B）。",
        inputSchema: {
            type: "object",
            properties: {
                elementId: { type: "number", description: "元素 ID" },
                elementIds: { type: "array", items: { type: "number" }, description: "元素 ID 列表（用於批量變更）" },
                typeId: { type: "number", description: "目標類型的 Element ID" },
                keepOuterFace: { type: "boolean", description: "加厚直線單一結構層牆：先固定實體外側面的定位線再改類型，不平移牆。複製類型並保留材質；驗證其他牆（含獨立飾面）位置與範圍不變，失敗整批回滾。群組牆不支援。預覽預設開啟；新版回傳 Method=LocationLineFaceAnchorV2，須確認外掛已更新。" },
                targetThicknessMm: { type: "number", exclusiveMinimum: 0 },
                newTypeName: { type: "string", minLength: 1 },
                dryRun: { type: "boolean", default: true },
                apply: { type: "boolean", default: false },
                walls: { type: "array", minItems: 1, items: { type: "object", properties: { elementId: { type: "integer" }, outwardNormal: { type: "object", properties: { x: { type: "number" }, y: { type: "number" } }, required: ["x", "y"] } }, required: ["elementId", "outwardNormal"] } },
            },
            anyOf: [{ required: ["typeId"] }, { required: ["keepOuterFace", "targetThicknessMm", "newTypeName", "walls"] }],
        },
    },
    {
        name: "get_line_styles",
        description: "取得目前專案中可用的線型 (GraphicsStyles)，例如：虛線、細線等。",
        inputSchema: { type: "object", properties: {} },
    },
    {
        name: "trace_stair_geometry",
        description: "自動分析視圖中的樓梯幾何，偵測被牆、版等物件遮擋的邊緣線段，回傳座標以供後續繪製虛線。",
        inputSchema: { type: "object", properties: {} },
    },
];





// Abby retains the upstream compatibility handlers for existing workflows.
wallTools.push(
{
        name: "unjoin_wall_joins",
        description: "取消牆體與柱子等元素的幾何接合關係。常用於元素上色前的前置作業。",
        inputSchema: {
            type: "object",
            properties: {
                wallIds: { type: "array", items: { type: "number" }, description: "要取消接合的牆體 Element ID 列表" },
                viewId: { type: "number", description: "視圖 ID" },
            },
        },
    },
{
        name: "unjoin_column_joins",
        description: "以柱子為中心解除其與鄰近元素的幾何接合，涵蓋牆、樓板、結構樑。未指定 columnIds 時預設整個專案所有柱（或 viewId 內的柱）。可用 rejoin_wall_joins 還原（限同一 Revit session）。",
        inputSchema: {
            type: "object",
            properties: {
                columnIds: { type: "array", items: { type: "number" }, description: "要處理的柱子 Element ID 列表（選填，預設為全部柱）" },
                viewId: { type: "number", description: "視圖 ID（選填，未給 columnIds 時用來限縮範圍）" },
            },
        },
    }
);
