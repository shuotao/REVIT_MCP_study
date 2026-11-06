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
import { registerRevitTools as registerProfileTools } from "./index.js";

/**
 * 依 Profile 註冊工具（實際 Profile 篩選邏輯在 index.ts）
 */
export function registerRevitTools(): Tool[] {
    return registerProfileTools();
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

    // 跨文件命令需要較長 timeout（開啟 .rvt/.rfa 檔案較費時）
    const CROSS_DOC_COMMANDS = new Set(["read_source_file_sheets", "copy_sheets_from_file", "sync_sheet_parameters_from_source", "inject_green_material_into_family"]);

    // 開檔、存檔、重載連結、匯出影像都可能遠超過 30 秒（大模型存檔、地形連結重載尤其明顯）。
    // 逾時斷在中途會讓使用者以為失敗，實際上 Revit 還在跑 —— 所以一律給長 timeout。
    const SLOW_COMMANDS = new Set(["create_beam_additions", "shape_floor_ramp", "create_walls_from_dwg", "copy_parameter_by_group", "fill_column_marks_from_dwg", "fill_beam_marks_from_dwg", "create_beams_from_dwg", "create_floors_from_framing_regions", "open_document", "save_document", "reload_links", "capture_view_image", "run_camera_keyplan", "list_cameras", "run_camera_overview", "create_camera_sheet"]);

    const LONG_RUNNING_COMMANDS = new Set(["check_beam_clear_height", "check_mep_spacing", "export_view_image", "batch_modify_elements"]);
    const timeoutMs = LONG_RUNNING_COMMANDS.has(commandName) ? 300000 : commandName === "create_toposolid_from_cad" || commandName === "export_families" ? 180000
        : CROSS_DOC_COMMANDS.has(commandName) || SLOW_COMMANDS.has(commandName) ? 120000 : 30000;

    // 發送命令到 Revit
    const response = await client.sendCommand(commandName, args, timeoutMs);

    if (!response.success) {
        throw new Error(response.error || "命令執行失敗");
    }

    return response.data;
}

