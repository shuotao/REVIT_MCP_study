/**
 * MEP 管線工具 — mep Profile
 */

import { Tool } from "@modelcontextprotocol/sdk/types.js";

export const mepTools: Tool[] = [
    {
        name: "get_connector_info",
        description: "取得 MEP 元素（管、風管、線管等）的接頭（Connector）資訊，包含座標、連接狀態、形狀等。",
        inputSchema: {
            type: "object",
            properties: {
                elementId: { type: "number", description: "要查詢的 MEP 元素 ID" },
            },
            required: ["elementId"],
        },
    },
    {
        name: "get_mep_segments_and_sizes",
        description: "一次盤點整個專案的 MEP Segment 與 Size 目錄（唯讀）。對應 Manage → MEP Settings 裡 Segments and Sizes 對話框的內容：每個管段（PipeSegment＝材質 × Schedule，如 Copper - K、PVC - Sch 40）各一份尺寸表，含 nominal / inner / outer 直徑與 Used in Size Lists、Used in Sizing 兩個勾選狀態；可一併回傳風管（Round / Rectangular / Oval）尺寸表（風管只有 nominal，Revit 的 duct inner/outer 是佔位值故不輸出）。這些資訊 Schedule 與 System Browser 都撈不到。所有尺寸以 mm 回傳，供台灣 CNS 尺寸對帳使用。全量 dump 可達數百筆，先用 summaryOnly=true 看全貌，再用 segmentName 鑽單一管段。",
        inputSchema: {
            type: "object",
            properties: {
                summaryOnly: { type: "boolean", description: "只回傳每個管段/風管形狀的統計（尺寸筆數、勾選筆數），不逐筆列出尺寸，預設 false。全案盤點建議先用這個。" },
                segmentName: { type: "string", description: "只回傳名稱含此字串的管段（不分大小寫），例如 'Copper'、'PVC'、'Copper - K'。給了這個參數時 includeDuct 預設變 false。" },
                includeDuct: { type: "boolean", description: "是否一併回傳風管尺寸表（Round / Rectangular / Oval）。預設 true；但有給 segmentName 時預設 false。" },
                usedOnly: { type: "boolean", description: "只列出有勾選 Used in Size Lists 或 Used in Sizing 的尺寸，預設 false（全列）。" },
            },
        },
    },
    {
        name: "get_mep_settings",
        description: "讀取 Manage → MEP Settings 裡「尺寸目錄以外」的所有設定頁（唯讀）：Duct/Pipe 的 Angles（fitting 角度用法與各角度勾選狀態）、Pipe 的 Slopes（坡度清單）與 Fluids（流體類型，可選溫度/黏度/密度表）、兩邊的 Calculation（空氣密度與黏度、network-based 計算、接頭容差）、尺寸命名與註記字串、標高文字（Centerline / Set Up / Flat on Top …）、以及 Hidden Line。角度以度回傳、長度以 mm 回傳，物理量另附以專案顯示單位格式化的字串。管段與尺寸目錄不在這裡，請用 get_mep_segments_and_sizes。",
        inputSchema: {
            type: "object",
            properties: {
                includeFluids: { type: "boolean", description: "是否回傳流體類型清單（含是否被使用、溫度筆數），預設 true。" },
                includeFluidTemperatures: { type: "boolean", description: "是否逐筆列出每個流體的溫度/黏度/密度表，預設 false（表可能很長，預設只給筆數）。" },
            },
        },
    },
    {
        name: "get_mep_size_usage",
        description: "盤點模型裡「真的有元件在用」哪些 MEP 尺寸（唯讀）。這與 get_mep_segments_and_sizes 讀的「目錄裡列了哪些尺寸」是兩件事——目錄不知道自己有沒有被用，只看目錄就刪除等於盲刪。掃描來源包含直管/直風管的寬高與直徑，以及配件與附件的 Connector 尺寸（漏掃配件會把「只有變徑頭在用」的尺寸誤判成可刪）。管的用量以 Pipe.PipeSegment 精確歸戶到各 segment。回傳每個目錄尺寸的 usageCount 與 removable 旗標，另列 orphans（模型有用但目錄沒有的尺寸，屬「該增」的候選）。刪除任何尺寸前一定要先跑這支。方法見 domain/mep-mechanical-settings.md。",
        inputSchema: {
            type: "object",
            properties: {
                scope: { type: "string", description: "'both'（預設）、'duct' 或 'pipe'。" },
                shape: { type: "string", description: "只看某個風管形狀：Round / Rectangular / Oval（選填）。" },
                segmentName: { type: "string", description: "只看名稱含此字串的管段，例如 'Copper'（選填）。" },
                includeUnused: { type: "boolean", description: "是否一併列出用量為 0 的目錄尺寸（可刪候選），預設 true。" },
                includeElementIds: { type: "boolean", description: "是否附上使用該尺寸的元件 ID 樣本，方便追查是誰擋住刪除，預設 false。" },
                maxElementIdsPerSize: { type: "number", description: "每個尺寸最多附幾個元件 ID 樣本，預設 5。" },
            },
        },
    },
    {
        name: "curate_mep_sizes",
        description: "增減 MEP 尺寸目錄（會修改模型設定）。規則:增無限制;減只能刪「模型中沒有任何元件在用」的尺寸,工具會自行跑用量盤點把在用的擋下並說明是誰在用。執行採四步:①列表(dryRun,預設 true)→②單一 Transaction 執行(可 Ctrl+Z)→③QC 重讀目錄逐筆比對→④偵測到誤刪自動以快照原樣加回。回傳一律附 RestorePayload(被移除尺寸的完整定義),供事後人工復原。新增管尺寸必須同時給 inner_mm 與 outer_mm(內外徑是水力計算依據,不由工具臆造);風管只需 nominal_mm。務必先用 get_mep_size_usage 確認用量。協定見 domain/mep-mechanical-settings.md。",
        inputSchema: {
            type: "object",
            properties: {
                target: { type: "string", description: "'pipe' 或 'duct'（必填）。" },
                segmentName: { type: "string", description: "target='pipe' 時必填,要精確到單一管段,例如 'Copper - K'。比對到多個會直接報錯要求更精確。" },
                shape: { type: "string", description: "target='duct' 時必填:Round / Rectangular / Oval。" },
                add: {
                    type: "array",
                    description: "要新增的尺寸。pipe 需 nominal_mm + inner_mm + outer_mm;duct 只需 nominal_mm。usedInSizeLists / usedInSizing 省略時預設 true。",
                    items: {
                        type: "object",
                        properties: {
                            nominal_mm: { type: "number", description: "公稱尺寸(mm)" },
                            inner_mm: { type: "number", description: "內徑(mm)。target='pipe' 時必填。" },
                            outer_mm: { type: "number", description: "外徑(mm)。target='pipe' 時必填。" },
                            usedInSizeLists: { type: "boolean", description: "是否出現在尺寸下拉選單,預設 true。" },
                            usedInSizing: { type: "boolean", description: "是否參與自動定尺寸,預設 true。" },
                        },
                        required: ["nominal_mm"],
                    },
                },
                remove: {
                    type: "array",
                    description: "要移除的公稱尺寸(mm)清單。模型中仍在使用的會被擋下並回報使用者。",
                    items: { type: "number" },
                },
                dryRun: { type: "boolean", description: "預設 true,只回傳計畫不改任何東西。確認清單無誤後才帶 false 執行。" },
                ignoreUnattributedFittings: { type: "boolean", description: "管配件沒有 PipeSegment 屬性,只能比對直徑,預設會保守擋下相符的尺寸。設 true 可略過這層保護（僅在你確認過那些配件不屬於此 segment 時使用）。預設 false。" },
            },
            required: ["target"],
        },
    },
    {
        name: "add_pipe_cap",
        description: "在管件的未連線端安裝管帽或法蘭。自動尋找開放的接頭並連接。",
        inputSchema: {
            type: "object",
            properties: {
                pipeId: { type: "number", description: "管件的元素 ID" },
                familyName: { type: "string", description: "要安裝的管帽/法蘭族群名稱" },
            },
            required: ["pipeId", "familyName"],
        },
    },
    {
        name: "export_families",
        description: "把專案中已載入的可編輯族群另存為 .rfa 檔到指定資料夾,建立可重用元件庫。預設匯出管配件(OST_PipeFitting)與管附件(OST_PipeAccessory)。自動依類別建立子資料夾;subFolderBySeries=true 時再依族群名稱系列(CIP/DWV/碳鋼.../)細分。略過系統族群、現地(in-place)與不可編輯族群。",
        inputSchema: {
            type: "object",
            properties: {
                outputFolder: { type: "string", description: "輸出根資料夾絕對路徑,例如 C:\\Users\\xxx\\Desktop\\MEP管元件庫。不存在會自動建立。" },
                categories: {
                    type: "array",
                    items: { type: "string" },
                    description: "要匯出的 BuiltInCategory 名稱清單(如 OST_PipeFitting、OST_PipeAccessory)。省略則預設這兩類。",
                },
                subFolderBySeries: { type: "boolean", description: "是否在類別資料夾下再依族群名稱系列建立子資料夾(預設 false,只依類別分層)。" },
                overwrite: { type: "boolean", description: "目標 .rfa 已存在時是否覆寫(預設 true)。" },
            },
            required: ["outputFolder"],
        },
    },
];
