/**
 * 模型感知 / 安全批次編輯 / 梁下淨高與管線間距檢討
 *
 * C# 實作：
 *   MCP/Core/Commands/CommandExecutor.ModelAwareness.cs
 *   MCP/Core/Commands/CommandExecutor.SafeBatchEdit.cs
 *   MCP/Core/Commands/CommandExecutor.BeamClearance.cs
 *   MCP/Core/Commands/CommandExecutor.MepSpacing.cs
 * 方法：domain/beam-clear-height-check.md、domain/mep-spacing-clearance-check.md
 */

import { Tool } from "@modelcontextprotocol/sdk/types.js";

const LEVELS = {
    type: "array",
    items: { type: "string" },
    description: "只檢討這些樓層的空間（樓層名稱，例如 [\"3F\",\"4F\"]）。以空間所在樓層為準：3F 代表 3F 樓板到 4F 之間。省略 = 全部樓層",
};

export const modelReviewTools: Tool[] = [
    {
        name: "get_model_snapshot",
        title: "模型狀態快照",
        description:
            "一次取得 Revit 目前狀態，不需要截圖：文件、作用視圖（含平面視圖範圍）、目前選取、樓層與標高、軸網、連結模型、主要類別數量、警告總數、變更序號 changeSeq。" +
            "開始任何建模或檢討前先呼叫，用來確認 AI 對模型的認知是最新的。長度值單位 mm。",
        inputSchema: { type: "object", properties: {} },
    },
    {
        name: "get_model_warnings",
        title: "模型警告清單",
        description:
            "讀取 Revit 警告（依訊息分組，含數量與範例元素 ID）。修改模型後用 elementIds 只看受影響元素的警告，可提早發現重疊牆、重複實例、房間未封閉等建置錯誤。",
        inputSchema: {
            type: "object",
            properties: {
                elementIds: { type: "array", items: { type: "number" }, description: "只回傳涉及這些元素的警告" },
                contains: { type: "string", description: "訊息文字篩選（部分比對）" },
                maxGroups: { type: "number", description: "最多回傳幾組訊息（預設 50）" },
            },
        },
    },
    {
        name: "get_recent_changes",
        title: "模型變更紀錄",
        description:
            "取得模型變更紀錄（使用者手動或 AI 的每筆交易：新增/修改/刪除數量、類別、範例 ID）。" +
            "把 get_model_snapshot 回傳的 changeSeq 當 sinceSeq 傳入，就能知道之後使用者在 Revit 改了什麼，避免拿過期狀態做決策。",
        inputSchema: {
            type: "object",
            properties: {
                sinceSeq: { type: "number", description: "只回傳序號大於此值的紀錄（預設 0 = 全部）" },
                limit: { type: "number", description: "最多筆數（預設 50，上限 200）" },
                currentDocumentOnly: { type: "boolean", description: "只看目前文件（預設 true）" },
            },
        },
    },
    {
        name: "export_view_image",
        title: "視圖輸出影像",
        description:
            "把 Revit 視圖輸出成 PNG 影像回傳給 AI 檢視（不需要使用者截圖）。可指定視圖名稱/ID，或用 focusElementIds 在「AI檢討_結果3D」視圖以剖面框框住指定元素後輸出，" +
            "適合修改後目視確認或查看檢討結果。",
        inputSchema: {
            type: "object",
            properties: {
                viewName: { type: "string", description: "視圖名稱（省略 = 作用視圖）" },
                viewId: { type: "number", description: "視圖 Element ID" },
                focusElementIds: { type: "array", items: { type: "number" }, description: "聚焦這些主模型元素（自動建立剖面框）" },
                marginMm: { type: "number", description: "聚焦時剖面框外擴距離 mm（預設 1500）" },
                pixelSize: { type: "number", description: "影像寬度像素（預設 1600，400-4000）" },
            },
        },
    },
    {
        name: "batch_modify_elements",
        title: "安全批次調整模型",
        description:
            "安全地批次調整現有模型（改參數、換類型、移動、複製、旋轉），全部包在同一個交易群組，任一步失敗就整批回滾。" +
            "預設 dryRun=true：實際跑過 Revit 的接合/約束/警告檢查後回滾，回報每個元素修改前後的值與會產生的警告，模型不變。" +
            "流程：先 dryRun 檢查 → 向使用者說明結果 → 取得同意後 dryRun=false 正式執行（可在 Revit Ctrl+Z 復原）。" +
            "參數值以專案顯示單位解讀（台灣專案通常 mm）。位移與旋轉中心一律 mm。不支援刪除（請用 delete_element）。",
        inputSchema: {
            type: "object",
            properties: {
                operations: {
                    type: "array",
                    description: "依序執行的操作（最多 200）",
                    items: {
                        type: "object",
                        properties: {
                            action: { type: "string", enum: ["set_parameter", "change_type", "move", "copy", "rotate"] },
                            elementIds: { type: "array", items: { type: "number" } },
                            parameterName: { type: "string", description: "set_parameter：參數名稱（顯示名稱或 BuiltInParameter 名稱）" },
                            value: { description: "set_parameter：新值。數值依專案單位；樓層/類型/材料參數可給名稱；是否參數可給 true/false" },
                            target: { type: "string", enum: ["instance", "type"], description: "set_parameter：改實例或類型參數（預設 instance；type 會影響同類型所有實例）" },
                            typeName: { type: "string", description: "change_type：目標類型名稱" },
                            familyName: { type: "string", description: "change_type：類型名稱重複時指定族群名稱" },
                            typeId: { type: "number", description: "change_type：目標類型 ID" },
                            dx: { type: "number", description: "move/copy：X 位移 mm" },
                            dy: { type: "number", description: "move/copy：Y 位移 mm" },
                            dz: { type: "number", description: "move/copy：Z 位移 mm" },
                            angleDeg: { type: "number", description: "rotate：角度（逆時針為正）" },
                            centerX: { type: "number", description: "rotate：旋轉中心 X mm（省略 = 元素外框中心）" },
                            centerY: { type: "number", description: "rotate：旋轉中心 Y mm" },
                        },
                        required: ["action", "elementIds"],
                    },
                },
                dryRun: { type: "boolean", description: "試跑不改模型（預設 true）" },
                rollbackOnWarning: { type: "boolean", description: "出現任何 Revit 警告也整批回滾（預設 false）" },
                transactionName: { type: "string", description: "交易名稱（Revit 復原清單顯示為「AI: 名稱」）" },
            },
            required: ["operations"],
        },
    },
    {
        name: "check_beam_clear_height",
        title: "梁下淨高檢討",
        description:
            "梁下淨高檢討（含連結的結構模型）：沿每支梁取樣，量測梁底到下方第一片樓板頂面的淨高，與要求值比較；" +
            "同時量測梁底到天花板頂的管線可用空間與天花高度。回傳不合格清單、各樓層最小淨高，可選擇在模型放置檢討標記或在「AI檢討_結果3D」上色。" +
            "方法見 domain/beam-clear-height-check.md。",
        inputSchema: {
            type: "object",
            properties: {
                requiredClearHeightMm: { type: "number", description: "要求的梁下淨高 mm。必填、無預設值：依法規或設計條件由使用者指定（例如停車空間依 domain/parking-clearance-check.md）" },
                levels: LEVELS,
                categories: { type: "array", items: { type: "string" }, description: "要量測其下方淨高的類別（預設 [\"beams\"]；也可加 ducts、pipes、cable_trays 量管線下淨高）" },
                includeLinks: { type: "boolean", description: "包含連結模型的梁與樓板（預設 true）" },
                includeCeilings: { type: "boolean", description: "同時量測天花板（預設 true）" },
                minCeilingSpaceMm: { type: "number", description: "梁底到天花頂至少要留的管線空間 mm；不足者標 CEILING_SPACE_LOW（預設不檢查）" },
                samplesPerBeam: { type: "number", description: "每支梁取樣點數（預設 5）" },
                includePassed: { type: "boolean", description: "清單也列出合格的梁（預設 false）" },
                maxResults: { type: "number", description: "清單最多筆數（預設 300）" },
                createMarkers: { type: "boolean", description: "在不合格位置放置檢討標記（一般模型，可用 clear_review_markers 清除；預設 false）" },
                highlightInView: { type: "boolean", description: "在「AI檢討_結果3D」把主模型中的不合格元素上紅色（預設 false）" },
            },
            required: ["requiredClearHeightMm"],
        },
    },
    {
        name: "check_mep_spacing",
        title: "管線間距檢討",
        description:
            "管線間預留淨距檢討（管、風管、電纜架、導線管；含連結模型、含保溫）：找出彼此淨距小於要求或已碰撞的管段，" +
            "可用 rules 依類別組合設定不同淨距，可用 checkAgainst 同時檢查管線與梁/柱等結構的淨距。" +
            "經由管件相連的同一路由不互相比較。方法見 domain/mep-spacing-clearance-check.md。",
        inputSchema: {
            type: "object",
            properties: {
                clearanceMm: { type: "number", description: "要求淨距 mm（外緣到外緣，含保溫）。必填、無預設值：使用者未指定時先詢問，不可自行代入慣例值" },
                rules: {
                    type: "array",
                    description: "依類別組合覆寫淨距，例如 [{a:\"ducts\",b:\"cable_trays\",clearanceMm:150}]",
                    items: {
                        type: "object",
                        properties: {
                            a: { type: "string" },
                            b: { type: "string" },
                            clearanceMm: { type: "number" },
                        },
                        required: ["a", "b", "clearanceMm"],
                    },
                },
                categories: { type: "array", items: { type: "string" }, description: "要檢討的管線類別（預設 pipes, ducts, cable_trays, conduits）" },
                checkAgainst: { type: "array", items: { type: "string" }, description: "另外檢查管線與這些類別的淨距，例如 [\"beams\",\"structural_columns\"]（預設不檢查）" },
                againstClearanceMm: { type: "number", description: "管線與 checkAgainst 類別的要求淨距 mm（預設同 clearanceMm）" },
                levels: LEVELS,
                includeLinks: { type: "boolean", description: "包含連結模型（預設 true）" },
                includeInsulation: { type: "boolean", description: "外徑加計保溫厚度（預設 true）" },
                maxResults: { type: "number", description: "清單最多筆數（預設 300）" },
                createMarkers: { type: "boolean", description: "在問題位置放置檢討標記（預設 false）" },
                highlightInView: { type: "boolean", description: "在「AI檢討_結果3D」把主模型中的相關元素上橘色（預設 false）" },
            },
            required: ["clearanceMm"],
        },
    },
    {
        name: "clear_review_markers",
        title: "清除檢討標記",
        description: "清除 check_beam_clear_height / check_mep_spacing 放置的檢討標記（只刪除本工具建立的標記）。",
        inputSchema: {
            type: "object",
            properties: {
                checkName: { type: "string", enum: ["beam_clear_height", "mep_spacing"], description: "只清除某一種檢討的標記（省略 = 全部）" },
            },
        },
    },
];
