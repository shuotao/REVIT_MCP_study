/**
 * 順序編號工具 — architect Profile
 *
 * 兩個工具，兩種不同的產出物，不要混用：
 *   renumber_parking_spaces  — 寫入停車格「備註」參數（方法：domain/parking-auto-numbering.md）
 *   create_sequence_numbers  — 在圖面上放置編號 TextNote（方法：domain/sequence-numbering.md）
 */

import { Tool } from "@modelcontextprotocol/sdk/types.js";

export const numberingTools: Tool[] = [
    {
        name: "renumber_parking_spaces",
        description:
            "依幾何位置批次重編停車格編號並寫入參數（預設「備註」）。汽車／機車／大客車各自獨立起號，先依 Y 由上到下分排、同排依 X 由左到右排序。可指定起點元件把某一格釘為該類別的第一號。預設 dryRun=true，必須先看過排序結果再正式寫入。",
        inputSchema: {
            type: "object",
            properties: {
                levelName: {
                    type: "string",
                    description: "目標樓層名稱。省略時使用作用中視圖所屬樓層。",
                },
                only: {
                    type: "string",
                    enum: ["all", "car", "motorcycle", "bus"],
                    description: "只處理某一類別。預設 all。",
                    default: "all",
                },
                parameterName: {
                    type: "string",
                    description: "要寫入的例證參數名稱。",
                    default: "備註",
                },
                carStart: {
                    type: "number",
                    description: "汽車位起始編號。",
                    default: 1,
                },
                motorcycleStart: {
                    type: "number",
                    description: "機車位起始編號。",
                    default: 1,
                },
                busStart: {
                    type: "number",
                    description: "大客車位起始編號。",
                    default: 1,
                },
                prefix: {
                    type: "string",
                    description: "編號前綴，例如 A 會產生 A1、A2。留空則只有數字。",
                    default: "",
                },
                startElementId: {
                    type: "number",
                    description:
                        "指定某個停車格為其所屬類別的第一號。排序後的序列會旋轉到該元件開頭（不是截斷），其餘車位順序不變。",
                },
                yToleranceMm: {
                    type: "number",
                    description: "Y 方向分排容差（mm）。標準車位寬度下 1500 可把同一排正確分到同組。",
                    default: 1500,
                },
                skipFour: {
                    type: "boolean",
                    description: "true = 跳過所有含數字 4 的編號。",
                    default: false,
                },
                motorcycleKeywords: {
                    type: "array",
                    items: { type: "string" },
                    description: "族群／類型名稱含這些字串即歸類為機車位（不分大小寫）。",
                    default: ["機車", "motor", "scooter"],
                },
                busKeywords: {
                    type: "array",
                    items: { type: "string" },
                    description: "族群／類型名稱含這些字串即歸類為大客車位（不分大小寫）。",
                    default: ["大客車", "巴士", "bus", "coach"],
                },
                excludeValues: {
                    type: "array",
                    items: { type: "string" },
                    description:
                        "目標參數的現值命中清單中任一項就排除該車位，例如 [\"變更\"] 可保住既有的設計變更註記不被編號覆蓋。依「值」排除，下次再跑一樣有效，不必維護會過期的 ElementId 清單。",
                    default: [],
                },
                excludeElementIds: {
                    type: "array",
                    items: { type: "number" },
                    description: "一次性手動排除的停車格 ElementId。",
                    default: [],
                },
                excludeMode: {
                    type: "string",
                    enum: ["skip", "reserve"],
                    description:
                        "被排除的車位怎麼處理。skip（預設）＝完全不參與排序，後面的號碼補上來、總數變少；reserve＝仍佔一個號碼但不寫入，圖面上那個位置留空號、其餘車位號碼不位移。",
                    default: "skip",
                },
                dryRun: {
                    type: "boolean",
                    description: "true（預設）＝只回傳排序與提案編號，不寫入 Revit。",
                    default: true,
                },
            },
            required: [],
        },
    },
    {
        name: "create_sequence_numbers",
        description:
            "在視圖上依順序放置編號 TextNote，對象可以是任意元件（車位、設備、開口皆可）。支援數字（1,2,3）與字母（a,b,c）兩種序列，搭配 prefix 可組出 A1、A2；可選擇跳過含 4 的號碼。順序取自 elementIds 的給定次序，或依 Y 上到下、X 左到右自動排序。",
        inputSchema: {
            type: "object",
            properties: {
                viewId: {
                    type: "number",
                    description: "目標視圖 ElementId。省略時使用作用中的視圖。",
                },
                elementIds: {
                    type: "array",
                    items: { type: "number" },
                    description: "要編號的元件 ElementId。order='given' 時，此陣列的順序就是編號順序。",
                },
                category: {
                    type: "string",
                    description:
                        "省略 elementIds 時，改以此 BuiltInCategory 名稱收集該視圖內的元件，例如 OST_Parking。",
                },
                order: {
                    type: "string",
                    enum: ["given", "yx"],
                    description:
                        "given＝照 elementIds 的順序；yx＝依 Y 由上到下分排、同排 X 由左到右。給了 elementIds 預設 given，否則預設 yx。",
                },
                format: {
                    type: "string",
                    enum: ["number", "letter"],
                    description: "number＝1,2,3…；letter＝a,b,c…,z,aa,ab…。",
                    default: "number",
                },
                start: {
                    type: "number",
                    description: "起始序數（format=number 時即起始數字；format=letter 時 1 代表 a）。",
                    default: 1,
                },
                prefix: {
                    type: "string",
                    description: "編號前綴。prefix='A' 搭配 format='number' 會產生 A1、A2。",
                    default: "",
                },
                upperCase: {
                    type: "boolean",
                    description: "format='letter' 時是否輸出大寫。",
                    default: false,
                },
                skipFour: {
                    type: "boolean",
                    description: "true = 跳過所有含數字 4 的編號（僅對 format='number' 有意義）。",
                    default: false,
                },
                offsetXMm: {
                    type: "number",
                    description: "編號文字相對元件中心的 X 偏移（mm）。",
                    default: 0,
                },
                offsetYMm: {
                    type: "number",
                    description: "編號文字相對元件中心的 Y 偏移（mm）。",
                    default: 0,
                },
                yToleranceMm: {
                    type: "number",
                    description: "order='yx' 時的分排容差（mm）。",
                    default: 1500,
                },
                textTypeName: {
                    type: "string",
                    description: "指定 TextNoteType 名稱。省略時使用專案中第一個文字類型。",
                },
                dryRun: {
                    type: "boolean",
                    description: "true（預設）＝只回傳每個元件將取得的編號與放置點，不建立 TextNote。確認順序後再以 false 執行。",
                    default: true,
                },
            },
            required: [],
        },
    },
];
