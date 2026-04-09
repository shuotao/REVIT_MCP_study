import { Tool } from "@modelcontextprotocol/sdk/types.js";

export const STAIR_COMPLIANCE_TOOLS: Tool[] = [
  {
    name: 'create_stair_section_view',
    description: '建立樓梯法規檢核剖面視圖（自動判斷長向走向以顯示踏板剖面）',
    inputSchema: {
      type: 'object',
      properties: {
        stairId: { type: 'number', description: '樓梯的 Element ID' },
        viewName: { type: 'string', description: '剖面視圖名稱（預設：樓梯檢核剖面）' },
        offset: { type: 'number', description: '剖面偏移距離（mm，預設 1500）' },
        scale: { type: 'number', description: '視圖比例（預設 50）' }
      },
      required: ['stairId']
    }
  },
  {
    name: 'get_stair_actual_width',
    description: '取得樓梯梯段的真實實測寬度（透過 StairsRun.ActualRunWidth 屬性）',
    inputSchema: {
      type: 'object',
      properties: {
        stairId: { type: 'number', description: '樓梯的 Element ID' }
      },
      required: ['stairId']
    }
  },
  {
    name: 'check_stair_headroom',
    description: '檢查樓梯淨高是否符合法規要求（190cm + 裝修厚度）',
    inputSchema: {
      type: 'object',
      properties: {
        stairId: { type: 'number', description: '樓梯的 Element ID' },
        finishThicknessCm: { type: 'number', description: '裝修面厚度 (cm，預設 0)' },
        headroomLimitCm: { type: 'number', description: '法規最低淨高需求 (cm，預設 190)' }
      },
      required: ['stairId']
    }
  },
  {
    name: 'create_stair_text_note_with_leader',
    description: '建立帶有引線的文字標註 (專用於樓梯檢核標示違規位置)',
    inputSchema: {
      type: 'object',
      properties: {
        viewId: { type: 'number', description: '目標視圖的 Element ID' },
        x: { type: 'number', description: '文字 X 座標 (mm)' },
        y: { type: 'number', description: '文字 Y 座標 (mm)' },
        leaderX: { type: 'number', description: '引線指向的 X 座標 (mm)' },
        leaderY: { type: 'number', description: '引線指向的 Y 座標 (mm)' },
        text: { type: 'string', description: '標註文字內容' }
      },
      required: ['viewId', 'x', 'y', 'leaderX', 'leaderY', 'text']
    }
  }
];
