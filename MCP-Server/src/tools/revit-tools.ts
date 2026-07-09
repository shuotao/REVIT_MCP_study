/**
 * Revit MCP 工具定義 — 相容層
 *
 * 此檔案保留為相容層，實際工具定義已拆分到模組檔案：
 *   base-tools.ts, wall-tools.ts, room-tools.ts,
 *   visualization-tools.ts, schedule-tools.ts, mep-tools.ts
 *
 * 匯總與 Profile 篩選邏輯在 index.ts
 */

import { RevitSocketClient } from "../socket.js";
import { Tool } from "@modelcontextprotocol/sdk/types.js";
import { gradingTools } from "./grading-tools.js";
import { registerRevitTools as registerProfileTools } from "./index.js";

/**
 * 依 Profile 註冊工具，並將 Toposolid 整地限制於建築與結構相關 Profile。
 */
export function registerRevitTools(): Tool[] {
    const tools = registerProfileTools();
    const requestedProfile = process.env.MCP_PROFILE || "full";
    const profile = ["full", "architect", "mep", "structural", "fire-safety"].includes(requestedProfile)
        ? requestedProfile
        : "full";

    return ["full", "architect", "structural"].includes(profile)
        ? [...tools, ...gradingTools]
        : tools;
}

/**
 * 執行 Revit 工具
 */
export async function executeRevitTool(
    toolName: string,
    args: Record<string, any>,
    client: RevitSocketClient
): Promise<any> {
    // 將工具名稱轉換為 Revit 命令名稱
    // 如果是 query_elements_with_filter，映射到 C# 的 query_elements
    const commandName = toolName === "query_elements_with_filter" ? "query_elements" : toolName;

    // 發送命令到 Revit
    const response = await client.sendCommand(commandName, args);

    if (!response.success) {
        throw new Error(response.error || "命令執行失敗");
    }

    return response.data;
}
