import { Tool } from '@modelcontextprotocol/sdk/types.js';

export const coordinateTools: Tool[] = [{
    name: 'align_coordinate_points_to_grids',
    description: '將專案基準點與未夾住的測量點移到兩條直線軸網的平面交點，保留基準點內部 Z，將該處共用 XYZ 設為 0，真北角度不變。只操作目前主文件，模型與 CAD/Revit 連結不移動。預設為實際試作後整組回滾；apply=true、dryRun=false 及最新 previewToken 才正式提交。任何非座標元素異動或位置驗證失敗全部回滾。不儲存文件、不發佈至連結。',
    inputSchema: {
        type: 'object', additionalProperties: false,
        properties: {
            gridName1: { type: 'string', minLength: 1, description: '第一條主文件直線軸網的精確名稱，例如 1' },
            gridName2: { type: 'string', minLength: 1, description: '第二條主文件直線軸網的精確名稱，例如 A' },
            dryRun: { type: 'boolean', default: true },
            apply: { type: 'boolean', default: false },
            previewToken: { type: 'string', description: '成功預演回傳的狀態指紋，正式寫入必填' },
        },
        required: ['gridName1', 'gridName2'],
    },
}];
