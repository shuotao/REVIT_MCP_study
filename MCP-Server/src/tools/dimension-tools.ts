import { Tool } from "@modelcontextprotocol/sdk/types.js";

export const dimensionTools: Tool[] = [
    {
        name: "create_dimension_by_ray",
        description: "指定 elementIds 可混合牆與建築柱／結構柱，以實際平面面參照建立寬度與淨距連續串。samplePoints 可指定個別構件取樣點，尺寸線仍依 origin/offsetMm 統一定位；非平行、缺面或重疊跳過。elementIds 與 wallIds 擇一。指定 wallIds（至少兩道牆）時，鎖定多道直線平行基本牆的實際側面，一次建立包含牆厚與牆間淨距的單一連續尺寸串。origin.x/y/z 為 mm，direction 指向串列延伸方向且垂直牆身。不使用 3D 最近射線、不抓其他牆柱；回傳每道牆穩定面參照及各段實測值。任一選定牆在取樣位置有開口或參照不明確，整串跳過並列出原因。預設 dryRun=true，apply=true 且 dryRun=false 才建立，已存在相同面參照串則不重複建立。未提供 wallIds 時保留原射線流程。",
        inputSchema: {
            type: "object",
            properties: {
                viewId: { type: "number", description: "目標視圖 ID" },
                wallIds: { type: "array", items: { type: "integer", minimum: 1 }, minItems: 2, uniqueItems: true, description: "鎖定多道平行牆，一次建立牆厚及牆間淨距的連續尺寸串" },
                elementIds: { type: "array", items: { type: "integer", minimum: 1 }, minItems: 2, uniqueItems: true, description: "牆與建築柱／結構柱混合連續標註，與 wallIds 擇一。使用實際平面側面參照，依 direction 排序構件寬度與淨距；非平行、缺面、開口或重疊則跳過。需 origin.z，預設預覽，dryRun=false 且 apply=true 才寫入。" },
                samplePoints: { type: "array", description: "指定構件個別實際面取樣點（mm），適用錯位牆柱或避開開口；僅可指定已選 elementIds/wallIds，不改變 origin 與 offsetMm 決定的尺寸線。", items: { type: "object", properties: { elementId: {type:"integer",minimum:1}, x:{type:"number"},y:{type:"number"},z:{type:"number"}}, required:["elementId","x","y","z"],additionalProperties:false } },
                dimensionTypeId: { type: "integer", minimum: 1, description: "指定牆模式的線性尺寸類型 ID" },
                offsetMm: { type: "number", description: "指定牆模式：尺寸線沿牆方向平移量 mm，預設 500" },
                dryRun: { type: "boolean", default: true, description: "指定牆模式預覽，不寫入" },
                apply: { type: "boolean", default: false, description: "指定牆模式正式建立需同時 dryRun=false" },
                origin: {
                    type: "object",
                    description: "射線原點 (通常為房間中心)",
                    properties: { x: { type: "number" }, y: { type: "number" }, z: { type: "number" } },
                    required: ["x", "y"],
                },
                direction: {
                    type: "object",
                    description: "正向射線方向向量",
                    properties: { x: { type: "number" }, y: { type: "number" }, z: { type: "number" } },
                    required: ["x", "y"],
                },
                counterDirection: {
                    type: "object",
                    description: "反向射線方向向量 (若未提供則自動取反)",
                    properties: { x: { type: "number" }, y: { type: "number" }, z: { type: "number" } },
                },
            },
            required: ["viewId", "origin", "direction"],
        },
    },
    {
        name: "create_dimension_by_bounding_box",
        description: "使用房間邊界框自動標註房間淨尺寸（保證100%覆蓋率）",
        inputSchema: {
            type: "object",
            properties: {
                viewId: { type: "number", description: "視圖 ID" },
                roomId: { type: "number", description: "房間 ID" },
                axis: { type: "string", description: "標註軸向：'X' 或 'Y'", enum: ["X", "Y"] },
                offset: { type: "number", description: "標註線偏移距離 (mm)，默認 500" },
            },
            required: ["viewId", "roomId", "axis"],
        },
    },
    {
        name: "auto_dimension_walls",
        description: "批次自動標註牆段尺寸（不需要 Room）。三種模式：overall_bbox（外圍兩條總長串：top 邊沿 X、right 邊沿 Y，預設）/ chained（同列同排共線牆串成 string dimension）/ per_wall（每牆一條長度標註）。標註用 DetailCurve 當 reference，不抓牆面，但建模一次性場景剛好。常用於 sketch-to-revit 蓋完牆後自動補尺寸。",
        inputSchema: {
            type: "object",
            properties: {
                viewId: { type: "number", description: "目標平面視圖 ID（必須是 ViewPlan）" },
                wallIds: {
                    type: "array",
                    items: { type: "number" },
                    description: "要標註的牆 ElementId 列表（選填，預設為 view 範圍內所有牆）",
                },
                mode: {
                    type: "string",
                    enum: ["overall_bbox", "chained", "per_wall"],
                    description: "標註模式（預設 overall_bbox）",
                },
                offsetMm: {
                    type: "number",
                    description: "標註線偏移距離 (mm)，預設 1500",
                },
            },
            required: ["viewId"],
        },
    },
];
