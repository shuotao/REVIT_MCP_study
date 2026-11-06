import { Tool } from "@modelcontextprotocol/sdk/types.js";
export const beamMarkTools: Tool[] = [{
    name: "fill_beam_marks_from_dwg",
    description: "讀取指定連結 CAD 文字圖層，依 梁中心點 XY 最近距離配對指定既有結構梁，填入內建編號 ALL_MODEL_MARK。不建立梁。必須明確指定梁 ID、CAD UniqueId 與原圖單位。預設 dryRun=true；apply=true 且 dryRun=false 才寫入，單一交易失敗回復。距離超限、相近候選或共用標注跳過。只接受英文字母起首加數字的梁號，可去除尾端括號尺寸；大小寫保留；不支援文字旋轉或引線關係推理。大梁與小梁應分批指定各自文字圖層及明確 ID。回傳原值、新值、距離及分類統計。",
    inputSchema: {
        type: "object",
        required: ["beamIds","importInstanceUniqueId","textLayerName","cadUnits"],
        properties: {
            beamIds: { type:"array", minItems:1, uniqueItems:true, items:{type:"integer",minimum:1} },
            importInstanceUniqueId: {type:"string",minLength:1},
            textLayerName: {type:"string",minLength:1,default:"0S-小梁編號"},
            cadUnits: {type:"string",enum:["mm","cm","m","inch","ft"],description:"CAD 原始座標單位；需與 Revit 連結單位核對，不自動猜測。"},
            maxDistanceMm: {type:"number",exclusiveMinimum:0,default:2000},
            ambiguityGapMm: {type:"number",exclusiveMinimum:0,default:100},
            pythonExecutable: {type:"string",minLength:1,description:"安裝 ezdxf 的 Python 執行檔；省略使用 python。DWG 另需 ODA。"},
            dryRun: {type:"boolean",default:true},
            apply: {type:"boolean",default:false},
            overwrite: {type:"boolean",default:true}
        }
    }
}];
