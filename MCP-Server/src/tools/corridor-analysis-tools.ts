/**
 * 走廊分析工具
 */

import { Tool } from "@modelcontextprotocol/sdk/types.js";

export const corridorAnalysisTools: Tool[] = [
    {
        name: "analyze_corridor_width",
        description: "分析走廊寬度。使用 Revit 端的房間邊界線段找出平行牆對，回傳實測寬度、最小寬度與各區段檢討結果。",
        inputSchema: {
            type: "object",
            properties: {
                roomId: { type: "number", description: "房間 Element ID（選填）" },
                roomName: { type: "string", description: "房間名稱（選填）" },
                minWidth: { type: "number", description: "最小寬度門檻（mm）", default: 1200 },
            },
        },
    },
];
