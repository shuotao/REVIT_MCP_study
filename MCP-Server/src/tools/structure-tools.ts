/**
 * 結構分析工具 — structure Profile
 */

import { Tool } from "@modelcontextprotocol/sdk/types.js";

export const structureTools: Tool[] = [
    {name:"fit_beam_to_floor",description:"將指定直梁頂面貼齊指定車道板底或板頂（targetSurface），沿原XY軸調整兩端坡度；板底全寬取樣非單一縱坡則拒絕。預設回滾預覽，正式寫入需previewToken及apply=true/dryRun=false。移除該梁既有接合並停用梁端接合；保留尺寸及平面位置。",inputSchema:{type:"object",required:["beamId","floorId"],properties:{beamId:{type:"integer"},floorId:{type:"integer"},targetSurface:{type:"string",enum:["bottom","top"],default:"bottom",description:"梁頂對齊板底或板頂；top模式允許梁板預期重疊，仍驗證梁頂貼合與XY不變。"},extendAlongEdge:{type:"boolean",default:false,description:"明確允許由近旁平行樓板實際直邊延伸高程至未覆蓋部分。距梁外框600mm內、縱向投影重疊至少500mm且20%；衝突坡度拒絕，實際重疊板面仍須符合。保留XY。"},extendOverhang:{type:"boolean",default:false,description:"明確允許梁寬部分超出板邊；僅依沿全長存在的重疊區單一縱坡延伸，維持XY。跨向坡度、折板、縱向缺口或覆蓋不足拒絕。"},dryRun:{type:"boolean",default:true},apply:{type:"boolean",default:false},previewToken:{type:"string"}}}},
    {name:"create_beam_additions",description:"依明確指定的既有水平直梁，在原平面位置建立同寬指定深度的增築梁。保留 Y 對正與偏移，頂部對正，參考樓層與 Z 偏移明確指定。高程只依指定參考樓層及 Z 偏移，不要求與原梁頂銜接；缺類型時同族群複製建立，以公分類型命名。文字群組編號精確寫入，單一交易驗證實體體積與高程，錯誤回滾。先預覽，再以相同 token 正式寫入。",inputSchema:{type:"object",additionalProperties:false,required:["sourceBeamIds"],anyOf:[{required:["mode","floorId"],properties:{mode:{const:"slopedSolid"},sourceBeamIds:{maxItems:1}}},{required:["referenceLevelId","depthMm","zOffsetMm"],properties:{mode:{const:"rectangular"}}}],properties:{mode:{type:"string",enum:["rectangular","slopedSolid"],default:"rectangular",description:"slopedSolid 建立結構構架 DirectShape：底貼來源水平梁頂、頂貼指定樓板平面；不具一般梁參數。須先預覽。"},floorId:{type:"integer",minimum:1},extendFloorPlane:{type:"boolean",default:false},sourceBeamIds:{type:"array",minItems:1,maxItems:500,uniqueItems:true,items:{type:"integer",minimum:1}},referenceLevelId:{type:"integer",minimum:1},depthMm:{type:"number",exclusiveMinimum:0,maximum:10000},zOffsetMm:{type:"number"},numberValue:{type:"string",minLength:1,default:"增築"},dryRun:{type:"boolean",default:true},apply:{type:"boolean",default:false},previewToken:{type:"string"}}}},
    {
        name: "create_structural_beam_types",
        description: "批次複製 Structural Framing FamilySymbol，不放置梁實體。同族群既有同名類型跳過、不覆寫；同批重複名稱採第一筆。單一 Transaction，任一失敗整批回滾。回傳 Success、Created、Skipped、Failed、Error；回滾項目列入 Failed。",
        inputSchema: {
            type: "object",
            properties: {
                familyName: { type: "string", minLength: 1, default: "混凝土樑-矩形", description: "已載入基礎族群名稱（精確比對）" },
                baseTypeName: { type: "string", minLength: 1, description: "基礎類型名稱；省略時依名稱排序取第一個" },
                types: {
                    type: "array", minItems: 1,
                    items: {
                        type: "object",
                        properties: {
                            typeName: { type: "string", minLength: 1 },
                            widthMm: { type: "number", exclusiveMinimum: 0, description: "梁寬 mm，參數 b／寬度／Width" },
                            heightMm: { type: "number", exclusiveMinimum: 0, description: "梁高 mm，參數 h／高度／Height" },
                        },
                        required: ["typeName", "widthMm", "heightMm"],
                        additionalProperties: false,
                    },
                },
            },
            required: ["types"],
        },
    },
    {
        name: "analyze_beam_penetration",
        description: "分析特定結構梁上的套管穿孔。回傳精確的幾何數據，如距離柱心長度、梁深度、開孔直徑等。",
        inputSchema: {
            type: "object",
            properties: {
                beamId: { type: "number", description: "要分析的目標結構梁 Element ID" },
                diameterParamNames: { type: "array", items: { type: "string" }, description: "可選。搜尋套管『直徑』的動態參數名稱清單（實體與類型自動 Fallback）。預設為 ['開孔直徑', '直徑', '管徑', 'Diameter', 'Size']" },
                lengthParamNames: { type: "array", items: { type: "string" }, description: "可選。搜尋套管『長度』的動態參數名稱清單。預設為 ['長度', 'Length']" },
                widthParamNames: { type: "array", items: { type: "string" }, description: "可選。搜尋梁『寬度』的動態參數名稱清單。預設為 ['b', '梁寬', 'Width']" },
                sleeveIds: { type: "array", items: { type: "number" }, description: "可選。指定檢核的套管 ID 清單，避免全區掃描" },
                linkInstanceId: { type: "number", description: "可選。連結模型的 ID" }
            },
            required: ["beamId"],
        },
    },
    {
        name: "scan_penetrated_beams_in_view",
        description: "掃描目前視圖中所有被套管（Sleeves）穿過的結構梁。回傳包含梁 ID、連結模型 ID 及穿過該梁的套管數量的清單。",
        inputSchema: {
            type: "object",
            properties: {},
        },
    },
    {
        name: "align_structural_framing",
        description: "自動將結構構架（梁）端點的實體延伸切齊到相接的樑面或柱面，含連結檔 (RVT Links) 中的構材。只寫起點/終點延伸參數，構架的軸線位置不會被移動。相接構材貫穿本樑（T 接）時退到近端面貼齊；相接構材在此終止（L 轉角）時延伸到遠端面。節點過於繁忙（相接候選超過 4 個）的端點會跳過不動並回報，需人工確認。支援單一/多支構架或當前視圖中的所有結構構架。",
        inputSchema: {
            type: "object",
            properties: {
                framingIds: { type: "array", items: { type: "number" }, description: "可選。指定要對齊的結構構架 Element ID 清單。若未指定，則自動對齊當前視圖內的所有結構構架。" },
                targetCategoryTypes: { type: "array", items: { type: "string" }, description: "可選。對齊目標類別清單，可用值 'framing' / 'column' / 'floor' / 'wall'。預設為 ['framing', 'column']；'floor' 與 'wall' 不在預設內，因為樓板 bbox 中心可能離樑端十幾公尺，實測會把樑對到離譜位置。" },
                maxDistanceMm: { type: "number", description: "可選。沿樑軸的搜尋窗口（毫米 mm），預設 700.0 mm。此值經真模型校準：容得下相接大梁半寬與 600 柱半寬，同時排除軸線外約 900 mm、實際並未相接的柱。調大可能重新引入該誤判。" },
                disallowJoinAtEnds: { type: "boolean", description: "可選。對齊後是否自動禁用端點自動接合 (Disallow Join)，避免 Revit 自動縮回，預設為 true。" },
                viewId: { type: "number", description: "可選。指定視圖 Element ID，預設為當前啟用視圖。" }
            },
        },
    },
];

// The assistant collects these inputs before the Revit transaction; no silent batch defaults.
const beamAdditionTool = structureTools.find(tool => tool.name === "create_beam_additions");
if (beamAdditionTool) {
    beamAdditionTool.description = "rectangular 模式呼叫前必須依序詢問：先確認增築厚度（作為新梁深），再確認參考樓層與含正負號的 Z 偏移值，單位明確後換算為 mm。使用者本次已提供的設定不重複詢問；缺少資料不得預覽或寫入，不沿用其他批次設定。資料齊全後先預覽，檢查指定高程與類型，再在已授權範圍內正式執行。 " + beamAdditionTool.description + " slopedSolid 模式改以 sourceBeamIds（限一支）、floorId 及明確同意的 extendFloorPlane 決定幾何，不需 depthMm/referenceLevelId/zOffsetMm；不得宣稱為一般梁族群。";
}

