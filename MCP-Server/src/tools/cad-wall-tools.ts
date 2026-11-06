import { Tool } from '@modelcontextprotocol/sdk/types.js';
export const cadWallTools: Tool[] = [{
 name:'create_walls_from_dwg',
 description:'可使用 lining 指定複壁類型，固定室內面並朝參考牆側增加厚度。未使用 lining 時，依目前平面 CAD WALL 雙線量測牆厚，自動對應 RC- 基本牆類型的實際寬度。名稱尺寸與實際寬度不一致的類型排除並回傳 TypeWarnings、影響牆數及修改確認提醒，絕不自動修改類型。相同中心線與厚度的牆段，僅在門窗圖層幾何覆蓋缺口時接續為完整牆，不建立門窗或洞口。支援直線及斜牆；歧義、無類型與既有牆重疊跳過。明確指定底部與頂部樓層，偏移預設 0。先 dryRun 預覽，正式寫入需 apply=true、dryRun=false、最新 previewToken。逐牆交易、整批單一復原群組；致命錯誤整批回滾。',
 inputSchema:{type:'object',additionalProperties:false,required:['baseLevelId','topLevelId'],properties:{
  layerName:{type:'string',minLength:1,default:'WALL'},importInstanceUniqueId:{type:'string',minLength:1},
  baseLevelId:{type:'integer',minimum:1},topLevelId:{type:'integer',minimum:1},
  baseOffsetMm:{type:'number',minimum:-10000,maximum:10000,default:0},topOffsetMm:{type:'number',minimum:-10000,maximum:10000,default:0},
  openingLayerNames:{type:'array',uniqueItems:true,items:{type:'string',minLength:1},default:['DOOR','DOOR-GLAZ','DOOR-HDLN','DOOR-OPNG','WINDOW']},
  toleranceMm:{type:'number',minimum:0.1,maximum:5,default:2},maxOpeningGapMm:{type:'number',minimum:0,maximum:6000,default:3000},
  wallTypeOverrides:{type:'array',minItems:1,description:'明確指定某厚度使用的 RC 牆類型，須符合實際寬度；解決同厚度多類型歧義。',items:{type:'object',additionalProperties:false,required:['widthMm','wallTypeId'],properties:{widthMm:{type:'number',minimum:50,maximum:1000},wallTypeId:{type:'integer',minimum:1}}}},
  lining:{type:'object',additionalProperties:false,required:['wallTypeId','cadWidthMm','expectedWidthMm','referenceWallTypeNames'],description:'複壁模式：固定遠離參考牆的CAD室內面，實際厚度差向參考牆側增加。無相鄰參考或兩側皆有則跳過；非結構，禁止端點自動接合，與wallTypeOverrides互斥。',properties:{wallTypeId:{type:'integer',minimum:1},cadWidthMm:{type:'number',minimum:50,maximum:1000},expectedWidthMm:{type:'number',minimum:50,maximum:1000},referenceWallTypeNames:{type:'array',minItems:1,uniqueItems:true,items:{type:'string',minLength:1}},maxReferenceGapMm:{type:'number',minimum:1,maximum:3000,default:1000}}},
  dryRun:{type:'boolean',default:true},apply:{type:'boolean',default:false},previewToken:{type:'string',minLength:1}
 }}
}];
