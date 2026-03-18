/**
 * 工具註冊中心 — 根據 MCP_PROFILE 篩選載入的工具模組
 *
 * Profile 設定方式（AI Client config）：
 *   "env": { "MCP_PROFILE": "architect" }
 *
 * 可用 Profile：full（預設）, architect, mep, structural, fire-safety
 */

import { Tool } from "@modelcontextprotocol/sdk/types.js";
import { baseTools } from "./base-tools.js";
import { wallTools } from "./wall-tools.js";
import { roomTools } from "./room-tools.js";
import { visualizationTools } from "./visualization-tools.js";
import { scheduleTools } from "./schedule-tools.js";
import { mepTools } from "./mep-tools.js";

import { curtainWallTools } from "./curtain-wall-tools.js";
import { smokeExhaustTools } from "./smoke-exhaust-tools.js";

/**
 * Profile 對照表：每個 profile 包含哪些模組
 */
const PROFILE_MODULES: Record<string, Tool[][]> = {
    full: [baseTools, wallTools, roomTools, visualizationTools, scheduleTools, mepTools, curtainWallTools, smokeExhaustTools],
    architect: [baseTools, wallTools, roomTools, visualizationTools, scheduleTools, curtainWallTools],
    mep: [baseTools, mepTools, scheduleTools, visualizationTools, smokeExhaustTools],
    structural: [baseTools, wallTools, visualizationTools],
    "fire-safety": [baseTools, roomTools, visualizationTools, smokeExhaustTools],
};

/**
 * 根據 MCP_PROFILE 環境變數註冊工具
 */
export function registerRevitTools(): Tool[] {
    const profile = process.env.MCP_PROFILE || "full";
    const modules = PROFILE_MODULES[profile];

    if (!modules) {
        console.error(`[Tools] Unknown MCP_PROFILE="${profile}", falling back to "full"`);
        return PROFILE_MODULES.full.flat();
    }

    const tools = modules.flat();
    console.error(`[Tools] Profile="${profile}", loaded ${tools.length} tools`);
    return tools;
}
