import { Tool } from "@modelcontextprotocol/sdk/types.js";

/** Toposolid 依樓板底面投影範圍進行整地。 */
export const gradingTools: Tool[] = [
    {
        name: "create_toposolid_from_cad",
        description: "從 CAD 指定三維等高線圖層建立地形實體（Revit 2024+）。保留實例定位，每點 Z 減 surveyZeroMeters，再加基準樓層高程；不修改 CAD。預覽回傳 token，apply=true 且 dryRun=false 才建立。依 CAD 防重複，錯誤與驗證失敗回復。邊界為點雲包絡，不代表地界。",
        inputSchema: {
            type: "object", additionalProperties: false,
            properties: {
                spotElevationUpdate: {
                    type: "object", additionalProperties: false,
                    description: "以 ASCII DXF 的 INSERT/ATTRIB 測點補充既有地形；排除半形或全形括號值，保留既有等高線頂點與開挖關係，預覽列出邊界外及衝突點。",
                    properties: {
                        toposolidId: { type: "integer", minimum: 1 },
                        sourceDxfPath: { type: "string" },
                        sourceUnitMm: { type: "number", exclusiveMinimum: 0, default: 1000 },
                        layer: { type: "string", default: "L01" },
                        attributeTag: { type: "string", default: "HE" },
                        alignX4Y7: { type: "boolean", default: false, description: "使用已確認的軸線／基準圖塊：局部Y為X4、局部X為Y7，對齊主模型水平X4與垂直Y7。" }
                    }, required: ["toposolidId", "sourceDxfPath"]
                },
                excavation: {
                    type: "object", additionalProperties: false,
                    description: "開挖既有地形；自目前視圖唯一含 boundaryLayer 的 CAD 讀取一個封閉無孔邊界，垂直挖至指定樓層偏移。必要時複製地形類型向下加深，保持地表不動；原 cadId 不用於此模式。",
                    properties: {
                        toposolidId: { type: "integer", minimum: 1 },
                        boundaryLayer: { type: "string", default: "area" },
                        boundaryElementIds: { type: "array", minItems: 1, uniqueItems: true, items: { type: "integer", minimum: 1 }, description: "擋土壁／柱實體投影外輪廓，填滿內部，外部分離區域則停止。" },
                        updateExisting: { type: "boolean", default: false, description: "交易內替換既有 Abby 開挖切割體，失敗回復。" },
                        boundaryMode: { type: "string", enum: ["single", "largest_rectangle"], default: "single", description: "largest_rectangle 僅於使用者指定大矩形時使用；辨識真實四角直角輪廓，不取包圍盒。" },
                        bottomOffsetMm: { type: "number" },
                        templatePath: { type: "string", description: "相容版本的未託管公制通用模型.rft" }
                    }, required: ["toposolidId", "bottomOffsetMm", "templatePath"]
                },
                cadId: { type: "integer", minimum: 1 },
                levelId: { type: "integer", minimum: 1 },
                typeId: { type: "integer", minimum: 1, description: "省略時選名稱排序第一個地形類型；預覽回報實際類型。" },
                layers: { type: "array", minItems: 1, uniqueItems: true, items: { type: "string", minLength: 1 }, default: ["AC", "IDX"] },
                surveyZeroMeters: { type: "number", default: 246.69 },
                simplifyToleranceMm: { type: "number", minimum: 0, maximum: 100, default: 20, description: "等高線弦偏差上限 mm；保留端點，最多10000點。" },
                dryRun: { type: "boolean", default: true },
                apply: { type: "boolean", default: false },
                previewToken: { type: "string" }
            },
            required: ["cadId", "levelId"]
        }
    },
    {
        name: "grade_toposolid_to_floors",
        description: "依指定樓板底面的水平投影範圍整平 Toposolid；本次僅支援 footprint_only 模式。",
        inputSchema: {
            type: "object",
            properties: {
                toposolidId: {
                    type: "integer",
                    description: "要整地的 Toposolid 元素 ID",
                },
                floorIds: {
                    type: "array",
                    minItems: 1,
                    items: { type: "integer" },
                    description: "作為整地範圍來源的樓板元素 ID；至少提供一筆",
                },
                mode: {
                    type: "string",
                    enum: ["footprint_only"],
                    default: "footprint_only",
                    description: "整地模式；本次只接受 footprint_only",
                },
                targetFace: {
                    type: "string",
                    enum: ["bottom"],
                    default: "bottom",
                    description: "樓板目標面；本次只接受 bottom",
                },
                allowPhaseSetup: {
                    type: "boolean",
                    default: true,
                    description:
                        "是否自動設定整地所需階段（預設 true）：原地形會改為較早階段建立、於目前階段拆除，設計副本建立於目前階段——與 Revit 原生整地行為一致。設為 false 時不修改階段，僅回報所需變更。",
                },
                updateExisting: {
                    type: "boolean",
                    enum: [false],
                    default: false,
                    description: "是否更新既有整地結果；本次只接受 false",
                },
            },
            required: ["toposolidId", "floorIds"],
        },
    },
];
