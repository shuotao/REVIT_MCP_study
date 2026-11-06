import { Tool } from "@modelcontextprotocol/sdk/types.js";

/**
 * DWG 結構樑匯入工具：掃描 CAD 匯入/連結圖層、解析正交雙線段、建立 Revit 結構樑。
 *
 * 對應 C# 端 handler: MCP/Core/DwgBeamExecutor.cs
 * 對應 CommandExecutor.cs cases: get_dwg_beam_layers / preview_dwg_beams / create_beams_from_dwg
 */
export const dwgBeamTools: Tool[] = [
    {
        name: "get_dwg_beam_layers",
        description:
            "掃描目前 Revit 平面或立剖面視圖中所有 CAD 匯入/連結的圖層名稱，" +
            "回傳圖層清單並自動推薦可能包含樑、標註文字的圖層。" +
            "使用前請確認 Revit 已開啟平面或立剖面視圖且已匯入 CAD 檔案。",
        inputSchema: {
            type: "object",
            properties: {},
        },
    },
    {
        name: "preview_dwg_beams",
        description:
            "解析 CAD 指定圖層中的雙線幾何，預覽識別到的樑中心線資訊（區分 X 向與 Y 向）。" +
            "此工具不會建立任何 Revit 元素，僅回傳解析結果供確認數量。" +
            "建議在執行 create_beams_from_dwg 前先呼叫此工具確認數量。",
        inputSchema: {
            type: "object",
            properties: {
                cadWidthMm: {type:"number",minimum:100,maximum:2500,description:"CAD雙線實際寬度mm；在配對前限制線距，與模型梁寬分開。支援preview及exactTypeId模式。"},
                cadWidthToleranceMm: {type:"number",minimum:0,maximum:20,default:5,description:"CAD寬度配對容許差mm。指定cadWidthMm時使用嚴格平行判斷。"},
                layerName: {
                    type: "string",
                    description: "CAD 圖層名稱，請從 get_dwg_beam_layers 回傳的清單中選擇樑輪廓圖層",
                },
            },
            required: ["layerName"],
        },
    },
    {
        name: "create_beams_from_dwg",
        description:
            "從 CAD 指定圖層建立 Revit 結構樑（必須搭配文字標注圖層）。\n" +
            "可用 textLayerNames 合併大小梁文字圖層，並以 scheduleRules 指定個別梁號尺寸與角色；\n" +
            "每批處理一個線條圖層；自訂尺寸表模式需要預覽 token 且跳過既有梁重疊。\n" +
            "使用 beamRole 參數標示目前處理的批次。numericSchedule 模式依大小寫敏感的數字梁號規則選既有尺寸類型；預設唯讀，需 apply=true 且 dryRun=false 才建立。" +
            "elevationSteel 模式讀立面CAD水平梁頂線，梁心投影至centerGridName軸網；讀textLayerNameX鋼梁號，依steelRules完整尺寸配型/新建型，並寫約束及文字群組編號。先預覽token才寫入；斜線及歧義跳過。BOX板厚用牆標稱厚度。",
        inputSchema: {
            type: "object",
            properties: {
                replaceOriginalSegments: {type:"boolean",default:false,description:"exactTypeId：以明確replaceElementIds原子替換舊分段配對梁；驗證舊梁仍符合原始CAD分段、類型與高程。新梁只還原同圖層相接/重疊共線邊，不跨空段。"},
                cadWidthMm: {type:"number",minimum:100,maximum:2500,description:"CAD雙線實際寬度mm；在配對前限制線距，與模型梁寬分開。支援preview及exactTypeId模式。"},
                cadWidthToleranceMm: {type:"number",minimum:0,maximum:20,default:5,description:"CAD寬度配對容許差mm。指定cadWidthMm時使用嚴格平行判斷。"},
                layerName: {
                    type: "string",
                    description: "CAD 樑輪廓線條的圖層名稱（必填）",
                },
                familyName: {
                    type: "string",
                    description: "指定使用的族群名稱（必填），例如「2_RC樑-矩形」。",
                },
                mergeContinuous: {type:"boolean",default:false,description:"精確模式：近共線CAD段合成最外起終點的一根，包含中間空段。須使用者明確要求連續梁。"},
                mergeToleranceMm: {type:"number",minimum:0,maximum:100,default:50,description:"連續梁分組與合併軸線最大側向差，mm。"},
                replaceMergedPaths: {type:"boolean",default:false,description:"將先前跨空段合併的梁改回CAD原始線段。需mergeContinuous=false及明確replaceElementIds；舊梁端點須吻合本圖層合併路徑，類型與高程不變。單一交易替換。"},
                replaceElementIds: {type:"array",uniqueItems:true,maxItems:500,items:{type:"integer",minimum:1},description:"明確指定先前此CAD候選建立的梁，與連續梁同交易替換。類型、高程、端點須吻合；涉及其他相依元素則整批復原。"},
                exactTypeId: {type:"integer",minimum:1,description:"既有梁類型ID，精確模式不依文字對應；所有CAD候選線包含斜向均套用此類型。需referenceLevelId、zOffsetMm及expectedCount。"},
                referenceLevelId: {type:"integer",minimum:1,description:"精確類型模式的參考樓層。"},
                zOffsetMm: {type:"number",description:"精確類型模式梁頂相對參考樓層的偏移，mm；頂部對正。"},
                expectedCount: {type:"integer",minimum:1,maximum:500,description:"精確類型模式預期CAD候選數；不符即停止。"},
                importInstanceUniqueId: {type:"string",minLength:1,description:"elevationSteel：指定目前視圖內唯一CAD連結。"},
                centerGridName: {type:"string",minLength:1,description:"elevationSteel：梁心所在的平面直線軸網精確名稱，如C。"},
                labelDistanceMm: {type:"number",exclusiveMinimum:0,maximum:5000,default:1500,description:"elevationSteel：梁號到梁頂的最大垂直距離；投影需在線段起終點之內。"},
                steelRules: {type:"array",description:"elevationSteel：精確梁號到完整斷面長度參數(mm)；省略僅列缺尺寸表。BOX必須提供牆標稱厚度。",items:{type:"object",additionalProperties:false,required:["numbers","baseTypeId","typeName","sectionKind","dimensionsMm"],properties:{
                    numbers:{type:"array",minItems:1,uniqueItems:true,items:{type:"string",pattern:"^(CSB|SB|SD)[0-9]+$"}},
                    baseTypeId:{type:"integer",minimum:1},typeName:{type:"string",minLength:1},sectionKind:{type:"string",enum:["BH","RH","BOX"]},
                    dimensionsMm:{type:"object",minProperties:3,additionalProperties:{type:"number",exclusiveMinimum:0},description:"族群參數精確名稱。BH例 h,b,s,t；BOX例 高度,寬度,牆標稱厚度。"}
                }}},
                mappingMode: { type: "string", enum: ["legacy", "numericSchedule", "elevationSteel"], default: "legacy",
                    description: "numericSchedule: FWB1..5→60x270; FB/FG+數字→70x270; Fb/Fg+數字→60x270; Fb0優先→50x270。類型名稱為cm，核對實際mm尺寸。不猜測未編號梁。" },
                scheduleRules: {
                    type: "array", minItems: 1, description: "numericSchedule 自訂尺寸表，省略沿用原 FWB/FB/Fb 規則。大小寫敏感，明列編號優先於數字範圍，再優先於萬用前綴。",
                    items: { type: "object", additionalProperties: false, required: ["patterns","typeName","widthMm","heightMm","beamRole"], properties: {
                        patterns: {type:"array",minItems:1,items:{type:"string",minLength:1},description:"如 B1、b201~b206、SSg1~SSg3、B*；* 僅代表數字。"},
                        typeName:{type:"string",minLength:1},widthMm:{type:"number",exclusiveMinimum:0},heightMm:{type:"number",exclusiveMinimum:0},
                        beamRole:{type:"string",enum:["大樑","次樑"]}
                    }}
                },
                textLayerNames:{type:"array",minItems:1,uniqueItems:true,items:{type:"string",minLength:1},description:"所有方向共用的文字圖層集合，優先於 textLayerNameX/Y；可同時讀取大小梁編號。"},
                defaultLevelId:{type:"integer",minimum:1,description:"numericSchedule：無括號梁號的參考樓層，省略採目前視圖樓層，Z 偏移為 0。"},
                uncertainElevationLevelId:{type:"integer",minimum:1,description:"高程配對歧義、超距、多筆或格式錯誤時改以此樓層、Z偏移0建立，回傳 ElevationFallbacks 梁號、原因、狀態及元素ID；省略仍跳過。不放寬梁號或尺寸配對。"},
                elevationLayerPairs:{type:"array",minItems:1,description:"獨立高程文字圖層配對。高程先配對同組最近梁號（2000mm、次近差100mm）；歧義或距離過遠會排除受影響梁，無高程標註者採預設樓層。",items:{type:"object",additionalProperties:false,required:["numberLayer","elevationLayer"],properties:{numberLayer:{type:"string",minLength:1},elevationLayer:{type:"string",minLength:1}}}},
                parenthesizedLevelId:{type:"integer",minimum:1,description:"numericSchedule：有數值括號的梁號採此參考樓層；必須同時指定 parenthesizedOffsetUnits。"},
                parenthesizedOffsetUnits:{type:"string",enum:["mm","cm"],description:"括號數值的單位（非 CAD 座標單位），換算後寫入梁 Z 向偏移。"},
                writeTextNumber:{type:"boolean",default:false,description:"numericSchedule：同交易寫入文字群組的編號，去掉括號與內容；缺少、唯讀或歧義參數導致整批回滾。"},
                previewToken:{type:"string",minLength:1,description:"自訂 scheduleRules 正式寫入必須提供相同設定與模型的最新預覽 token。"},
                cadUnits: { type: "string", enum: ["mm","cm","m"], description: "numericSchedule 必填，CAD座標原始單位。" },
                pythonExecutable: { type: "string", minLength: 1, description: "numericSchedule 可指定含ezdxf的Python執行檔。" },
                dryRun: { type: "boolean", default: true, description: "numericSchedule 或 exactTypeId 模式生效；legacy 保留直接建立行為。" },
                apply: { type: "boolean", default: false, description: "numericSchedule 或 exactTypeId 須 true 且 dryRun=false 才建立。" },
                typeName: {
                    type: "string",
                    description:
                        "（選填）指定族群類型名稱（例如「60x80」）。" +
                        "有填此參數時為「快速模式」，該批次所有樑都用這個型別建立，忽略文字圖層。" +
                        "沒填時為「名稱對應模式」，依文字標注比對類型。",
                },
                textLayerNameX: {
                    type: "string",
                    description:
                        "（名稱對應模式必填其一）CAD 圖面上 X 軸向標注樑名稱（例如水平向的 B1）的文字圖層。" +
                        "文字僅會與 X 軸向的中心線進行配對，避免干擾。",
                },
                textLayerNameY: {
                    type: "string",
                    description:
                        "（名稱對應模式必填其一）CAD 圖面上 Y 軸向標注樑名稱（例如垂直向的 B1）的文字圖層。" +
                        "文字僅會與 Y 軸向的中心線進行配對，避免干擾。",
                },
                beamRole: {
                    type: "string",
                    description:
                        "（選填）標示此批次處理的樑角色，例如「大樑」、「次樑」、「地樑」。" +
                        "除回報顯示外，也用於設定樑的結構用途：大樑／地樑→大樑(Girder)、次樑→格柵樑(Joist)。",
                },
            },
            required: ["layerName", "familyName"],
        },
    },
];
