import { Tool } from "@modelcontextprotocol/sdk/types.js";
export const framingFloorTools: Tool[] = [
 {name:"shape_floor_ramp",description:"沿平面模型線或相切圓弧路徑修改指定平坦樓板，右側端點為低端。每隔指定水平里程畫橫向分割線，以低高端高程線性插值所有形狀頂點。保留同高既有形狀頂點與分割線；拒絕既有非平坦形狀、分割線交叉與不明確路徑。預覽後需 token、apply=true 與 dryRun=false；單一交易驗證失敗回滾。",inputSchema:{type:"object",additionalProperties:false,required:["floorId","pathElementIds","lowElevationMm","highElevationMm"],properties:{floorId:{type:"integer",minimum:1},pathElementIds:{type:"array",minItems:1,maxItems:20,uniqueItems:true,items:{type:"integer",minimum:1}},lowElevationMm:{type:"number"},highElevationMm:{type:"number"},trimStartToBoundary:{type:"boolean",default:false,description:"經確認後，計算時裁去低端板外路徑，以入板處為零里程並以該直線板邊為低端控制線；不修改模型線。"},spacingMm:{type:"number",minimum:100,maximum:10000,default:1000},dryRun:{type:"boolean",default:true},apply:{type:"boolean",default:false},previewToken:{type:"string"}}}},
 {
  name:"list_floor_types",
  description:"列出目前 Revit 專案樓板類型 ID、名稱、厚度、基礎板與可變厚度旗標，供使用者選擇批次建板類型。",
  inputSchema:{type:"object",properties:{},additionalProperties:false}
 },
 {
  name:"create_floors_from_framing_regions",
  description:"依指定樓層既有梁，或梁加柱的實體平面輪廓辨識封閉淨空區域，批次建立同一種樓板。新樓板建立後自動接合相交牆，強制樓板切割牆；逐組接合失敗記入 WallJoins 與 WallJoinFailed，保留可成功建立的樓板。沿梁柱內側，不伸入梁柱；扣除區內柱洞，不自動補缺口。僅目前文件與目前平面視圖階段，不含連結。無法解析的梁柱／既有樓板自動以保守包圍範圍隔離，其相鄰整區跳過並列出元素 ID 與原因；不支援可變厚板。採樓板厚度範圍內投影；跳過過小區域、短邊與既有樓板重疊。預設 dryRun=true，回傳多邊形、面積、regionKeys 與 previewToken。正式寫入須 apply=true、dryRun=false 及相同設定的 previewToken；每區獨立交易，失敗僅回滾該區並繼續其他區；致命文件錯誤才整批回滾。回傳 ExcludedItems、Created、Skipped、Failed、PartialSuccess。請先讓使用者選擇 list_floor_types 回傳的 floorTypeId，再預覽並確認要建的區域。",
  inputSchema:{
   type:"object",additionalProperties:false,
   required:["levelId","floorTypeId"],
   properties:{
    levelId:{type:"integer",minimum:1,description:"目前平面視圖的樓層 ID"},
    placementLevelId:{type:"integer",minimum:1,description:"樓板實際樓層；省略沿用 levelId。offsetMm 相對此樓層。"},
    includeBeamsAcrossLevels:{type:"boolean",default:false,description:"依實際板厚高程篩選梁，包含其他參考樓層的梁。"},
    beamProjectionBelowSlabMm:{type:"number",minimum:0,maximum:3000,default:0,description:"經確認後，梁邊界分析向板底以下延伸的mm深度，讓低梁的平面投影參與封閉分區。僅影響梁投影；不改樓板高程、柱分析或既有樓板重疊判斷。預覽後須按regionKeys限定要建區域。"},
    newFloorTypeName:{type:"string",minLength:1,description:"缺少指定厚度類型時，由單層 floorTypeId 複製；同名尺寸不符則停止。"},
    newThicknessMm:{type:"number",minimum:1,maximum:2000},
    cadSchedule:{type:"object",description:"以區域內 CAD 板號選厚度，同筆交易填寫文字群組的編號與 Floor。精確編號優先於字首加數字萬用規則；無板號或多種板號跳過。",required:["importInstanceUniqueId","textLayerName","cadUnits","pythonExecutable","floorValue","rules"],properties:{
     importInstanceUniqueId:{type:"string"},textLayerName:{type:"string"},cadUnits:{type:"string",enum:["mm","cm","m"]},pythonExecutable:{type:"string"},floorValue:{type:"string",minLength:1},
     elevationLayerName:{type:"string"},elevationUnits:{type:"string",enum:["mm","cm","m"]},
     defaultLevelId:{type:"integer",minimum:1},numericLevelId:{type:"integer",minimum:1},
     elevationPairDistanceMm:{type:"number",minimum:1,maximum:10000,default:1800},elevationPairGapMm:{type:"number",minimum:1,maximum:10000,default:100},
     foldedAsFlat:{type:"boolean",default:false,description:"經使用者同意後，(折板)依預設樓層零偏移建平板。"},
     skipLabels:{type:"array",items:{type:"string"}},
     labelBoundaryToleranceMm:{type:"number",minimum:0,maximum:300,default:0,description:"板號定位點略在淨邊界外時允許配對距離；只配至明確最近區域，不擴張板邊界。"},
     labelBoundaryGapMm:{type:"number",minimum:1,maximum:1000,default:100,description:"最近與次近板區距離差不足時不補配。"},
     rules:{type:"array",minItems:1,items:{type:"object",required:["pattern","thicknessMm"],properties:{pattern:{type:"string"},thicknessMm:{type:"number",exclusiveMinimum:0}}}}
    }},
    floorTypeId:{type:"integer",minimum:1,description:"使用者選擇的既有一般樓板類型 ID"},
    beamIds:{type:"array",minItems:1,uniqueItems:true,items:{type:"integer",minimum:1},description:"選填；限本樓層梁。省略掃描本樓層所有梁，不受暫時隔離影響。"},
    includeColumns:{type:"boolean",default:true,description:"true 梁加柱；false 僅梁。"},
    offsetMm:{type:"number",minimum:-10000,maximum:10000,default:0,description:"板頂相對樓層偏移 mm"},
    minimumAreaM2:{type:"number",minimum:0.001,maximum:100000,default:0.1},
    structural:{type:"boolean",default:true},
    regionKeys:{type:"array",minItems:1,uniqueItems:true,items:{type:"string",minLength:1},description:"選填，只建立指定預覽區域；更改選取後須重跑預覽。"},
    dryRun:{type:"boolean",default:true},
    apply:{type:"boolean",default:false},
    previewToken:{type:"string",minLength:1,description:"正式寫入必填，必須來自相同設定且模型未改變的預覽。"}
   }
  }
 }
];

