/**
 * 工具註冊中心 — 根據 MCP_PROFILE 篩選載入的工具模組
 *
 * Profile 設定方式（AI Client config）：
 *   "env": { "MCP_PROFILE": "architect" }
 *
 * 可用 Profile：full（預設）, architect, mep, structural, fire-safety
 */

import { Tool } from "@modelcontextprotocol/sdk/types.js";
import { withAnnotations } from "./annotations.js";
import { withAppUi } from "../apps/register-apps.js";
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
import { openingTypeTools } from "./opening-type-tools.js";
import { dimensionTools } from "./dimension-tools.js";
import { dependentViewTools } from "./dependent-view-tools.js";
import { clashTools } from "./clash-tools.js";
import { doorWindowLegendTools } from "./door-window-legend-tools.js";
import { listSeedsTools } from "./list-seeds-tools.js";
import { dimensionTypeTools } from "./dimension-type-tools.js";
import { legendViewTools } from "./legend-view-tools.js";
import { dwgColumnTools } from "./dwg-column-tools.js";
import { dwgBeamTools } from "./dwg-beam-tools.js";
import { cadBlockPlacementTools } from "./cad-block-placement-tools.js";
import { cadLinkTools } from "./cad-link-tools.js";
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
import { tagsTools } from "./tags-tools.js";
import { gridTools } from "./grid-tools.js";
import { modelReviewTools } from "./model-review-tools.js";
import { areaFormulaTools } from "./area-formula-tools.js";
import { numberingTools } from "./numbering-tools.js";
import { viewCaptureTools } from "./view-capture-tools.js";
import { fillPatternTools } from "./fill-pattern-tools.js";
import { viewRangeTools } from "./view-range-tools.js";
import { linkDisplayTools } from "./link-display-tools.js";
import { documentTools } from "./document-tools.js";
import { cameraTools } from "./camera-tools.js";
import { cameraManagerTools } from "./camera-manager-tools.js";
import { materialGraphicsTools } from "./material-graphics-tools.js";

/**
 * 診斷與環境層工具：擷取畫面、填充樣式查詢、視圖範圍、連結顯示設定、文件操作、相機位置、材質圖形。
 * 這些不綁特定專業領域（排查「為什麼看不到」「為什麼設了沒反應」到處都會用到），
 * 所以每個 profile 都要有，不隨 architect/mep/structural 切換而消失。
 */
const DIAGNOSTIC_TOOLS = [
    viewCaptureTools,
    fillPatternTools,
    viewRangeTools,
    linkDisplayTools,
    documentTools,
    cameraTools,
    materialGraphicsTools,
];

/**
 * Profile 對照表：每個 profile 包含哪些模組
 */
const PROFILE_MODULES: Record<string, Tool[][]> = {
    full: [baseTools, wallTools, roomTools, corridorAnalysisTools, visualizationTools, scheduleTools, mepTools, curtainWallTools, smokeExhaustTools, smokeDetectorTools, parallelSectionTools, STAIR_COMPLIANCE_TOOLS, sheetTools, detailComponentTools, openingTypeTools, dimensionTools, dependentViewTools, dwgColumnTools, dwgBeamTools, cadBlockPlacementTools, cadLinkTools, clashTools, doorWindowLegendTools, listSeedsTools, dimensionTypeTools, legendViewTools, structureTools, gradingTools, detailCopyTools, scopeBoxTools, viewCropBoxTools, textNoteTools, titleblockAlignTools, viewCreationTools, viewportPositionTools, crossDocumentTools, legendTools, scaffoldTools, viewDuplicateTools, fillRegionTools, ifcStructuralSyncTools, tagsTools, gridTools, cameraManagerTools, modelReviewTools, areaFormulaTools, numberingTools],
    architect: [baseTools, wallTools, roomTools, corridorAnalysisTools, visualizationTools, scheduleTools, curtainWallTools, parallelSectionTools, STAIR_COMPLIANCE_TOOLS, sheetTools, detailComponentTools, openingTypeTools, dimensionTools, dependentViewTools, dwgColumnTools, dwgBeamTools, cadLinkTools, doorWindowLegendTools, listSeedsTools, dimensionTypeTools, legendViewTools, gradingTools, detailCopyTools, scopeBoxTools, viewCropBoxTools, textNoteTools, titleblockAlignTools, viewCreationTools, viewportPositionTools, crossDocumentTools, legendTools, scaffoldTools, viewDuplicateTools, fillRegionTools, tagsTools, gridTools, cameraManagerTools, modelReviewTools, areaFormulaTools, numberingTools],
    mep: [baseTools, mepTools, scheduleTools, visualizationTools, smokeExhaustTools, smokeDetectorTools, parallelSectionTools, cadBlockPlacementTools, clashTools, modelReviewTools],
    structural: [baseTools, wallTools, visualizationTools, dwgColumnTools, dwgBeamTools, clashTools, structureTools, gradingTools, ifcStructuralSyncTools, tagsTools, gridTools, modelReviewTools],
    "fire-safety": [baseTools, roomTools, corridorAnalysisTools, visualizationTools, smokeExhaustTools, smokeDetectorTools],
};

// 診斷層工具對每個 profile 都補上。用迴圈而不是往五個陣列各貼一次，
// 是為了避免日後新增 profile 時漏掉其中一個（那種漏法不會報錯，只會安靜地少工具）。
for (const profileName of Object.keys(PROFILE_MODULES)) {
    PROFILE_MODULES[profileName] = [...PROFILE_MODULES[profileName], ...DIAGNOSTIC_TOOLS];
}

/**
 * 根據 MCP_PROFILE 環境變數註冊工具
 */
export function registerRevitTools(): Tool[] {
    const profile = process.env.MCP_PROFILE || "full";
    const modules = PROFILE_MODULES[profile];

    if (!modules) {
        console.error(`[Tools] Unknown MCP_PROFILE="${profile}", falling back to "full"`);
        return PROFILE_MODULES.full.flat()
            .map(withAnnotations)
            .map(withAppUi)
            .sort((a, b) => (a.name < b.name ? -1 : a.name > b.name ? 1 : 0));
    }

    const tools = modules.flat()
        .map(withAnnotations)
        .map(withAppUi)
        .sort((a, b) => (a.name < b.name ? -1 : a.name > b.name ? 1 : 0));
    console.error(`[Tools] Profile="${profile}", loaded ${tools.length} tools`);
    return tools;
}
