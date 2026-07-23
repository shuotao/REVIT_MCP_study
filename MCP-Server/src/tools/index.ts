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
import { corridorAnalysisTools } from "./corridor-analysis-tools.js";
import { visualizationTools } from "./visualization-tools.js";
import { scheduleTools } from "./schedule-tools.js";
import { mepTools } from "./mep-tools.js";

import { curtainWallTools } from "./curtain-wall-tools.js";
import { smokeExhaustTools } from "./smoke-exhaust-tools.js";
import { STAIR_COMPLIANCE_TOOLS } from "./stair-compliance-tools.js";
import { sheetTools } from "./sheet-tools.js";
import { detailComponentTools } from "./detail-component-tools.js";
import { dimensionTools } from "./dimension-tools.js";
import { dependentViewTools } from "./dependent-view-tools.js";
import { clashTools } from "./clash-tools.js";
import { doorWindowLegendTools } from "./door-window-legend-tools.js";
import { listSeedsTools } from "./list-seeds-tools.js";
import { dimensionTypeTools } from "./dimension-type-tools.js";
import { legendViewTools } from "./legend-view-tools.js";
import { dwgColumnTools } from "./dwg-column-tools.js";
import { dwgBeamTools } from "./dwg-beam-tools.js";
import { cadLinkTools } from "./cad-link-tools.js";
import { cadAnnotationTools } from "./cad-annotation-tools.js";
import { structureTools } from "./structure-tools.js";
import { parallelSectionTools } from "./parallel-section-tools.js";
import { smokeDetectorTools } from "./smoke-detector-tools.js";
import { gradingTools } from "./grading-tools.js";
import { detailCopyTools } from "./detail-copy-tools.js";
import { scopeBoxTools } from "./scope-box-tools.js";
import { viewCropBoxTools } from "./view-cropbox-tools.js";
import { textNoteTools } from "./text-note-tools.js";
import { titleblockAlignTools } from "./titleblock-align-tools.js";
import { viewCreationTools } from "./view-creation-tools.js";
import { viewportPositionTools } from "./viewport-position-tools.js";
import { crossDocumentTools } from "./cross-document-tools.js";
import { legendTools } from "./legend-tools.js";
import { scaffoldTools } from "./scaffold-tools.js";
import { viewDuplicateTools } from "./view-duplicate-tools.js";
import { fillRegionTools } from "./fill-region-tools.js";
import { ifcStructuralSyncTools } from "./ifc-structural-sync-tools.js";

/**
 * Profile 對照表：每個 profile 包含哪些模組
 */
const PROFILE_MODULES: Record<string, Tool[][]> = {
    full: [baseTools, wallTools, roomTools, corridorAnalysisTools, visualizationTools, scheduleTools, mepTools, curtainWallTools, smokeExhaustTools, smokeDetectorTools, parallelSectionTools, STAIR_COMPLIANCE_TOOLS, sheetTools, detailComponentTools, dimensionTools, dependentViewTools, dwgColumnTools, dwgBeamTools, cadLinkTools, cadAnnotationTools, clashTools, doorWindowLegendTools, listSeedsTools, dimensionTypeTools, legendViewTools, structureTools, gradingTools, detailCopyTools, scopeBoxTools, viewCropBoxTools, textNoteTools, titleblockAlignTools, viewCreationTools, viewportPositionTools, crossDocumentTools, legendTools, scaffoldTools, viewDuplicateTools, fillRegionTools, ifcStructuralSyncTools],
    architect: [baseTools, wallTools, roomTools, corridorAnalysisTools, visualizationTools, scheduleTools, curtainWallTools, parallelSectionTools, STAIR_COMPLIANCE_TOOLS, sheetTools, detailComponentTools, dimensionTools, dependentViewTools, dwgColumnTools, dwgBeamTools, cadLinkTools, cadAnnotationTools, doorWindowLegendTools, listSeedsTools, dimensionTypeTools, legendViewTools, gradingTools, detailCopyTools, scopeBoxTools, viewCropBoxTools, textNoteTools, titleblockAlignTools, viewCreationTools, viewportPositionTools, crossDocumentTools, legendTools, scaffoldTools, viewDuplicateTools, fillRegionTools],
    mep: [baseTools, mepTools, scheduleTools, visualizationTools, smokeExhaustTools, smokeDetectorTools, parallelSectionTools, clashTools],
    structural: [baseTools, wallTools, visualizationTools, dwgColumnTools, dwgBeamTools, cadAnnotationTools, clashTools, structureTools, gradingTools, ifcStructuralSyncTools],
    "fire-safety": [baseTools, roomTools, corridorAnalysisTools, visualizationTools, smokeExhaustTools, smokeDetectorTools],
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
