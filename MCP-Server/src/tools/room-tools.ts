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
];
