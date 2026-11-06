/**
 * 帷幕牆 + 立面面板工具 — architect Profile
 * 來源：PR#11 (@7alexhuang-ux)，經跨版本修正後整合
 */

import { Tool } from "@modelcontextprotocol/sdk/types.js";

export const curtainWallTools: Tool[] = [
    {
        name: "get_curtain_wall_info",
        description: "取得帷幕牆詳細資訊，包含 Grid 排列、面板尺寸、面板類型分佈等。",
        inputSchema: {
            type: "object",
            properties: {
                elementId: { type: "number", description: "帷幕牆的 Element ID（選填，若不指定則使用目前選取的元素）" },
            },
        },
    },
    {
        name: "get_curtain_panel_types",
        description: "取得專案中所有可用的帷幕牆面板類型。",
        inputSchema: { type: "object", properties: {} },
    },
    {
        name: "create_curtain_panel_type",
        description: "建立新的帷幕牆面板類型，可指定顏色（HEX）和透明度。",
        inputSchema: {
            type: "object",
            properties: {
                typeName: { type: "string", description: "新類型的名稱" },
                color: { type: "string", description: "面板顏色（HEX 格式，如 '#5C4033'）" },
                baseTypeId: { type: "number", description: "基礎類型 ID（選填）" },
            },
            required: ["typeName", "color"],
        },
    },
    {
        name: "apply_panel_pattern",
        description: "將面板排列模式套用到帷幕牆。需要類型映射表和排列矩陣。",
        inputSchema: {
            type: "object",
            properties: {
                elementId: { type: "number", description: "帷幕牆的 Element ID" },
                typeMapping: { type: "object", description: "類型映射表（字母→面板類型 ID）" },
                matrix: {
                    type: "array",
                    description: "面板排列矩陣，由上到下、由左到右",
                    items: { type: "array", items: { type: "string" } },
                },
            },
            required: ["elementId", "typeMapping", "matrix"],
        },
    },
    {
        name: "create_curtain_wall_elevations",
        description: "批次建立每一道帷幕牆的永久外立面視圖，建立/套用名為「帷幕立面」的視圖樣板，保留 crop box 與 far clip depth 可由各視圖自行控制，並回傳方向與 crop 診斷欄位。這個工具建立的是持久成果，不需要 cleanup；除非使用者明確要求清理，AI client 不得呼叫 delete_element 刪除這些 generated views。未指定 wallIds/maxWalls 時處理專案內全部帷幕牆；專案帷幕牆數量多時（例如數百道）建議用 maxWalls 分批處理，避免超過回應時限，可搭配回傳的 RemainingWallIds 續跑下一批。",
        inputSchema: {
            type: "object",
            properties: {
                placementViewId: { type: "number", description: "放置 ElevationMarker 的 ViewPlan ElementId；優先於 placementViewName。" },
                placementViewName: { type: "string", description: "放置 ElevationMarker 的 ViewPlan 名稱。" },
                scale: { type: "number", description: "立面視圖比例，預設 50。", default: 50 },
                offsetMm: { type: "number", description: "marker 放在牆外側的距離，單位 mm，預設 1500。", default: 1500 },
                horizontalMarginMm: { type: "number", description: "crop 左右餘裕，單位 mm，預設 0；直線帷幕以 Wall.LocationCurve 端點為左右界，餘裕只擴張 crop，不影響尺寸端點。", default: 0 },
                verticalMarginMm: { type: "number", description: "crop 上下餘裕，單位 mm，預設 0；剪裁範圍依 elevation view 內目標帷幕元素的 2D 可視範圍計算。", default: 0 },
                depthMm: { type: "number", description: "自動深度計算失敗時的 fallback 遠剪裁深度，單位 mm，預設 1200；正常情況會剛好包住帷幕牆所有相關元素，遠端剪裁模式固定為剪裁含線。", default: 1200 },
                viewTemplateName: { type: "string", description: "視圖樣板名稱，預設「帷幕立面」。", default: "帷幕立面" },
                applyViewTemplate: { type: "boolean", description: "是否建立/套用視圖樣板，預設 true。", default: true },
                nameSeparator: { type: "string", description: "樓層與標記之間的分隔字串，預設空字串。", default: "" },
                wallTagTypeId: { type: "number", description: "必須由使用者選擇的牆標籤 FamilySymbol ElementId。省略時工具不修改模型，並回傳 AvailableWallTagTypes 選單；應選擇具有[標記]參數的標籤類型。" },
                addDimensions: { type: "boolean", description: "是否加入總寬、總高、帷幕底至所屬樓層線距離，以及水平／垂直帷幕網格尺寸，預設 true。", default: true },
                dimensionTypeSelectionMode: { type: "string", enum: ["auto", "prompt"], description: "標註類型選擇模式；auto 自動解析可用類型，prompt 則要求先指定類型。", default: "auto" },
                dimensionTypeId: { type: "number", description: "標註類型 DimensionType 的 ElementId。" },
                dimensionTypeName: { type: "string", description: "標註類型 DimensionType 名稱；ID 無效或未提供時使用。" },
                dimensionOffsetMm: { type: "number", description: "DimensionType 無法提供有效輔助線長度時使用的帷幕至內層網格尺寸線距離 fallback，單位 mm，預設 300。正常情況會採用（輔助線長度 + 圖紙 3 mm）乘以視圖比例。", default: 300 },
                dimensionStackOffsetMm: { type: "number", description: "DimensionType 無法提供有效輔助線長度時使用的內外層尺寸間距 fallback，單位 mm，預設 250。正常情況會採用輔助線長度乘以視圖比例。", default: 250 },
                dryRun: { type: "boolean", description: "只回報將建立的視圖，不修改模型，預設 false。", default: false },
                wallIds: {
                    type: "array",
                    description: "選填，只處理指定的帷幕牆 Element ID。未指定時處理專案內全部帷幕牆。給定但非帷幕牆或不存在的 ID 會列在回傳的 InvalidWallIds，不會被靜默略過。可與 maxWalls 併用（先套用 wallIds 過濾，再套用 maxWalls 上限）。",
                    items: { type: "number" },
                },
                maxWalls: {
                    type: "number",
                    description: "選填，單次呼叫處理的帷幕牆數量上限。專案帷幕牆數量多時（例如數百道）建議設定此參數分批處理，避免單次回應時間過長超過 MCP 回應時限。超過上限時只處理前 N 道，其餘 ID 會列在回傳的 RemainingWallIds 供下一輪呼叫續跑（可作為下一輪的 wallIds），並在 Truncated=true 與 Message 中說明本次處理與剩餘數量。",
                },
                verbose: {
                    type: "boolean",
                    description: "是否回傳完整逐牆診斷欄位，預設 false。false 時只回統計與摘要：Created 逐牆明細只留 ViewId/ViewName/WallId，DimensionWarnings/WallTagWarnings/TemplateWarnings/Skipped 收斂為筆數；牆數多時可避免回應超過單次回傳上限。true 時維持既有完整輸出（含每道牆的 crop、標註、方向診斷欄位）。",
                    default: false,
                },
            },
        },
    },
    {
        name: "diagnose_curtain_wall_elevation_direction",
        description: "非破壞診斷指定帷幕牆的立面方向判定流程；以 rollback transaction 建立暫時 ElevationMarker/ViewSection，回傳 wall.Orientation、預期 marker 位置、暫時 view 方向與 dot 檢查，不留下視圖或 marker。",
        inputSchema: {
            type: "object",
            properties: {
                wallId: { type: "number", description: "要診斷的帷幕牆 Wall ElementId。" },
                placementViewId: { type: "number", description: "放置暫時 ElevationMarker 的 ViewPlan ElementId；優先於 placementViewName。" },
                placementViewName: { type: "string", description: "放置暫時 ElevationMarker 的 ViewPlan 名稱。" },
                scale: { type: "number", description: "暫時立面視圖比例，預設 50。", default: 50 },
                offsetMm: { type: "number", description: "暫時 marker 放在 wall.Orientation 側的距離，單位 mm，預設 1500。", default: 1500 },
                includeCropDiagnostics: { type: "boolean", description: "Include non-mutating crop diagnostics for the temporary elevation view. Default: false", default: false },
            },
            required: ["wallId"],
        },
    },
    {
        name: "diagnose_curtain_wall_elevation_directions",
        description: "Batch, non-destructive curtain wall elevation direction diagnostic. Compares wall.Orientation and -wall.Orientation candidate sides for selected or all curtain walls; temporary markers/views are created inside rollback transactions.",
        inputSchema: {
            type: "object",
            properties: {
                wallIds: {
                    type: "array",
                    description: "Optional curtain wall ElementIds. When omitted, all walls with CurtainGrid != null are diagnosed.",
                    items: { type: "number" },
                },
                placementViewId: { type: "number", description: "Optional ViewPlan ElementId used for temporary ElevationMarker placement." },
                placementViewName: { type: "string", description: "Optional ViewPlan name used for temporary ElevationMarker placement." },
                scale: { type: "number", description: "Temporary elevation scale. Default: 50", default: 50 },
                offsetMm: { type: "number", description: "Candidate marker offset from wall centerline in millimeters. Default: 1500", default: 1500 },
                includeTemporaryMarker: { type: "boolean", description: "Create temporary ElevationMarker/ViewSection inside rollback transactions to read ViewDirection. Default: true", default: true },
                includeCropDiagnostics: { type: "boolean", description: "Include non-mutating crop diagnostics for the auto-resolved temporary elevation view. Requires includeTemporaryMarker. Default: false", default: false },
                knownExteriorSideByWallId: {
                    type: "object",
                    description: "Optional truth labels by wall id. Value must be 'api_orientation' or 'opposite_orientation'.",
                    additionalProperties: {
                        type: "string",
                        enum: ["api_orientation", "opposite_orientation"],
                    },
                },
            },
        },
    },
    {
        name: "diagnose_curtain_wall_elevation_dimensions",
        description: "非破壞診斷帷幕立面的尺寸標示；可比較 CurtainGridLine profiles，或以 level_offset 在實際立面測試 Level plane、Invisible DetailCurve 與完整 production path，所有 level_offset 元素強制 rollback。",
        inputSchema: {
            type: "object",
            properties: {
                viewId: { type: "number", description: "既有帷幕立面 ViewSection ElementId；未提供時使用目前作用中立面。" },
                wallId: { type: "number", description: "帷幕牆 ElementId；level_offset 未提供時必須在作用中立面唯一選取一道帷幕牆，禁止使用第一道牆 fallback。其他模式維持既有 fallback。" },
                dimensionTypeId: { type: "number", description: "供診斷使用的 DimensionType ElementId。" },
                dimensionTypeName: { type: "string", description: "供診斷使用的 DimensionType 名稱。" },
                testMode: { type: "string", enum: ["geometry_reference", "reference_plane_fallback", "both", "level_offset"], description: "level_offset 會測試 inactive/active 下的 Level plane、Invisible DetailCurve 兩／三 Reference 尺寸及完整 production path；預設 both。", default: "both" },
                rollback: { type: "boolean", description: "是否 rollback 診斷元素，預設 true；level_offset 固定強制 rollback，忽略 false。", default: true },
                dimensionOffsetMm: { type: "number", description: "DimensionType 無法提供有效輔助線長度時使用的帷幕至內層網格尺寸線距離 fallback，單位 mm，預設 300。正常情況會採用（輔助線長度 + 圖紙 3 mm）乘以視圖比例。", default: 300 },
                dimensionStackOffsetMm: { type: "number", description: "DimensionType 無法提供有效輔助線長度時使用的內外層尺寸間距 fallback，單位 mm，預設 250。正常情況會採用輔助線長度乘以視圖比例。", default: 250 },
                verifyAssociationByMove: { type: "boolean", description: "是否在 rollback transaction 內移動一條 CurtainGridLine 10 mm，驗證尺寸段值與 reference 關聯，預設 true。", default: true },
                verbose: {
                    type: "boolean",
                    description: "是否回傳完整逐筆診斷欄位，預設 false。false 時只回頂層統計與 AssociationMoveTest/Failures，CurtainGridLineReferenceSamples/CurtainGridLineReferenceFailures/BackgroundAttemptedDimensions/ActiveViewProfileAttempts/AttemptedDimensions 收斂為筆數（不逐筆展開）；true 時維持既有完整輸出。testMode=level_offset 的 runtime 矩陣不受此參數影響，一律回傳完整內容。",
                    default: false,
                },
            },
        },
    },
    {
        name: "create_facade_panel",
        description: "建立單片立面面板（DirectShape）。支援 5 種幾何：curved_panel、beveled_opening、angled_panel、rounded_opening、flat_panel。",
        inputSchema: {
            type: "object",
            properties: {
                wallId: { type: "number", description: "參考牆的 Element ID" },
                geometryType: {
                    type: "string",
                    enum: ["curved_panel", "beveled_opening", "angled_panel", "rounded_opening", "flat_panel"],
                    description: "幾何類型",
                },
                positionAlongWall: { type: "number", description: "沿牆位置（mm）" },
                positionZ: { type: "number", description: "底部 Z 高度（mm）" },
                width: { type: "number", description: "寬度（mm），預設 800" },
                height: { type: "number", description: "高度（mm），預設 3400" },
                depth: { type: "number", description: "弧深/凹入深度（mm），預設 150" },
                thickness: { type: "number", description: "板厚（mm），預設 30" },
                offset: { type: "number", description: "距牆偏移（mm），預設 200" },
                color: { type: "string", description: "顏色（HEX）" },
                name: { type: "string", description: "面板名稱" },
                curveType: { type: "string", enum: ["concave", "convex"], description: "[curved_panel] 曲線類型" },
                tiltAngle: { type: "number", description: "[angled_panel] 傾斜角度（度）" },
                tiltAxis: { type: "string", enum: ["horizontal", "vertical"], description: "[angled_panel] 傾斜軸" },
                bevelDirection: { type: "string", enum: ["center", "up", "down", "left", "right"], description: "[beveled_opening] 斜切方向" },
                bevelDepth: { type: "number", description: "[beveled_opening] 斜切深度（mm）" },
                openingWidth: { type: "number", description: "[opening] 開口寬度（mm）" },
                openingHeight: { type: "number", description: "[opening] 開口高度（mm）" },
                cornerRadius: { type: "number", description: "[rounded_opening] 圓角半徑（mm）" },
                openingShape: { type: "string", enum: ["rounded_rect", "arch", "stadium", "rect"], description: "[rounded_opening] 開口形狀" },
            },
        },
    },
    {
        name: "create_facade_from_analysis",
        description: "根據分析結果批次建立整面立面。在牆面前方批次建立多片 DirectShape 面板，支援多種面板類型和排列模式。",
        inputSchema: {
            type: "object",
            properties: {
                wallId: { type: "number", description: "目標牆的 Element ID" },
                facadeLayers: {
                    type: "object",
                    description: "立面層級定義",
                    properties: {
                        outer: {
                            type: "object",
                            description: "外層面板定義",
                            properties: {
                                offset: { type: "number", description: "距牆偏移（mm），預設 200" },
                                panelTypes: {
                                    type: "array",
                                    description: "面板類型定義陣列",
                                    items: {
                                        type: "object",
                                        properties: {
                                            id: { type: "string", description: "類型代號（如 'A'）" },
                                            name: { type: "string", description: "類型名稱" },
                                            width: { type: "number", description: "寬度（mm）" },
                                            height: { type: "number", description: "高度（mm）" },
                                            depth: { type: "number", description: "弧深（mm）" },
                                            thickness: { type: "number", description: "板厚（mm）" },
                                            curveType: { type: "string", enum: ["concave", "convex"] },
                                            color: { type: "string", description: "顏色（HEX）" },
                                            geometryType: { type: "string", enum: ["curved_panel", "beveled_opening", "angled_panel", "rounded_opening", "flat_panel"] },
                                        },
                                        required: ["id", "width", "color"],
                                    },
                                },
                                pattern: { type: "array", description: "排列矩陣（如 ['ABABAB', 'BABABA']）", items: { type: "string" } },
                                gap: { type: "number", description: "間距（mm），預設 20" },
                                horizontalBandHeight: { type: "number", description: "分隔帶高度（mm）" },
                                floorHeight: { type: "number", description: "層高（mm），預設 3600" },
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
