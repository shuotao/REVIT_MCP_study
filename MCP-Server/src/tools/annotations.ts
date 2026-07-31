/**
 * 工具標註 — 為每個 Tool 衍生 title 與 MCP ToolAnnotations（readOnlyHint / destructiveHint）
 *
 * 分類邏輯（依優先順序）：
 *   1. OVERRIDE            — 精確工具名稱覆寫（讀模型寫外部檔 / 只動環境-視圖狀態的例外）
 *   2. DESTRUCTIVE_NAMES   — 精確工具名稱，破壞性操作（不可逆刪除）
 *   3. READONLY_PREFIXES   — 名稱前綴比對，唯讀操作
 *   4. 其餘（含 WRITE_PREFIXES 比對到的與完全比對不到的）— 預設視為非破壞性寫入操作
 */

import { Tool, ToolAnnotations } from "@modelcontextprotocol/sdk/types.js";

/** 唯讀工具的名稱前綴（readOnlyHint=true, destructiveHint=false） */
export const READONLY_PREFIXES: string[] = [
    "get_",
    "query_",
    "analyze_",
    "check_",
    "list_",
    "measure_",
    "read_",
    "trace_",
    "detect_",
    "diagnose_",
    "calculate_",
    "preview_",
    "scan_",
    "debug_",
    "zoom_",
];

/** 寫入類工具的名稱前綴（readOnlyHint=false, destructiveHint=false） */
export const WRITE_PREFIXES: string[] = [
    "create_",
    "modify_",
    "set_",
    "batch_",
    "move_",
    "apply_",
    "assign_",
    "change_",
    "align_",
    "arrange_",
    "sync_",
    "copy_",
    "renumber_",
    "remap_",
    "flip_",
    "hide_",
    "unhide_",
    "join_",
    "rejoin_",
    "unjoin_",
    "link_",
    "place_",
    "position_",
    "override_",
    "clear_",
    "convert_",
    "grade_",
    "duplicate_",
    "adjust_",
    "add_",
    "auto_",
    "scale_",
    "shift_",
    "import_",
];

/** 精確工具名稱：破壞性操作（destructiveHint=true） */
export const DESTRUCTIVE_NAMES: Set<string> = new Set([
    "delete_element",
    "dedup_detail_elements_in_view",
]);

type ClassificationHints = Pick<ToolAnnotations, "readOnlyHint" | "destructiveHint"> & {
    readOnlyHint: boolean;
    destructiveHint: boolean;
};

/**
 * 精確工具名稱覆寫表：前綴規則無法正確反映實際行為的例外
 *   - export_*, import_excel_to_drafting_views：會讀取模型，但寫入的是外部檔案，非 Revit 模型本身 → readOnlyHint=true
 *   - select_element, set_active_view, override_element_graphics, clear_element_override,
 *     colorize_clashes, set_category_visibility, hide_elements, unhide_elements：
 *     只異動環境／視圖狀態（選取、作用視圖、圖形覆寫、可見性），不異動 Revit 模型本身
 */
export const OVERRIDE: Record<string, ClassificationHints> = {
    export_families: { readOnlyHint: true, destructiveHint: false },
    export_clash_report: { readOnlyHint: true, destructiveHint: false },
    export_smoke_review_excel: { readOnlyHint: true, destructiveHint: false },
    import_excel_to_drafting_views: { readOnlyHint: true, destructiveHint: false },

    select_element: { readOnlyHint: false, destructiveHint: false },
    set_active_view: { readOnlyHint: false, destructiveHint: false },
    override_element_graphics: { readOnlyHint: false, destructiveHint: false },
    clear_element_override: { readOnlyHint: false, destructiveHint: false },
    colorize_clashes: { readOnlyHint: false, destructiveHint: false },
    set_category_visibility: { readOnlyHint: false, destructiveHint: false },
    hide_elements: { readOnlyHint: false, destructiveHint: false },
    unhide_elements: { readOnlyHint: false, destructiveHint: false },
};

/**
 * 依工具名稱分類出 readOnlyHint / destructiveHint。
 * 比對優先順序：OVERRIDE > DESTRUCTIVE_NAMES > READONLY_PREFIXES > 預設（非破壞性寫入）。
 */
export function classify(name: string): ClassificationHints {
    const override = OVERRIDE[name];
    if (override) {
        return { readOnlyHint: override.readOnlyHint, destructiveHint: override.destructiveHint };
    }

    if (DESTRUCTIVE_NAMES.has(name)) {
        return { readOnlyHint: false, destructiveHint: true };
    }

    if (READONLY_PREFIXES.some((prefix) => name.startsWith(prefix))) {
        return { readOnlyHint: true, destructiveHint: false };
    }

    // 涵蓋 WRITE_PREFIXES 比對到的工具，以及完全比對不到任何前綴／覆寫表的工具（安全預設）。
    return { readOnlyHint: false, destructiveHint: false };
}

/** 常見英文縮寫，句尾標點判定時不視為句子終止（例如 "e.g." "i.e." "etc."） */
const ABBREVIATION_SUFFIX = /(?:^|[^A-Za-z])(?:e\.g|i\.e|etc|vs|approx)\.$/i;

/**
 * 取 description 的「第一子句」：在中文句號「。」或後面接空白／字串結尾的「.」「!」「?」處截斷，
 * 並略過常見縮寫（避免在小數點或 "e.g." 這類縮寫處誤判斷句）。
 */
function firstClause(description: string): string {
    const text = description.trim();
    const terminatorRegex = /[。!?]|\.(?=\s|$)/g;

    let match: RegExpExecArray | null;
    while ((match = terminatorRegex.exec(text)) !== null) {
        if (match[0] === ".") {
            const precedingText = text.slice(0, match.index + 1);
            if (ABBREVIATION_SUFFIX.test(precedingText)) {
                continue;
            }
        }
        return text.slice(0, match.index).trim();
    }

    return text;
}

/** 將 snake_case（或含連字號）的工具名稱轉成 Title Case，作為沒有 description 時的後備標題 */
function toTitleCase(name: string): string {
    return name
        .split(/[_-]+/)
        .filter((word) => word.length > 0)
        .map((word) => word.charAt(0).toUpperCase() + word.slice(1))
        .join(" ");
}

const MAX_TITLE_LENGTH = 60;

/**
 * 衍生人類可讀的工具標題：優先取 description 的第一子句（截斷至約 60 字），
 * 若 description 不存在或為空，則將 snake_case 名稱轉成 Title Case。
 */
export function deriveTitle(tool: Tool): string {
    const description = tool.description;
    if (description && description.trim().length > 0) {
        let clause = firstClause(description);
        if (clause.length > MAX_TITLE_LENGTH) {
            clause = clause.slice(0, MAX_TITLE_LENGTH).trim();
        }
        if (clause.length > 0) {
            return clause;
        }
    }

    return toTitleCase(tool.name);
}

/**
 * 回傳 tool 的淺拷貝，補上 title（僅在缺少時）與 annotations（僅在缺少時，
 * 不覆寫既有 annotations）。不修改 tool 的行為或 inputSchema。
 */
export function withAnnotations(tool: Tool): Tool {
    const title = tool.title ?? deriveTitle(tool);
    const { readOnlyHint, destructiveHint } = classify(tool.name);

    const result: Tool = { ...tool };

    if (!result.title) {
        result.title = title;
    }

    if (!result.annotations) {
        result.annotations = { title, readOnlyHint, destructiveHint };
    }

    return result;
}
