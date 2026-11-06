import { Tool } from "@modelcontextprotocol/sdk/types.js";

export const linkDisplayTools: Tool[] = [
    {
        name: "get_link_display_settings",
        description: "列出指定視圖中每個連結模型（RevitLinkInstance）的 Display Settings：ByHostView / ByLinkView / Custom，以及它在該視圖是否被隱藏。關鍵欄位是 HostFiltersApply——為 false 時，主檔的視圖篩選器與類別覆寫完全不會作用在該連結的元素上，這是「主檔怎麼設、連結都沒反應」最常見也最難從畫面看出來的成因。唯讀。觸發條件：使用者提到連結模型的顯示設定、Revit Links 頁籤、By Host View、連結沒反應、篩選器對連結無效、link display settings。",
        inputSchema: {
            type: "object",
            properties: {
                viewId: {
                    type: "number",
                    description: "目標視圖的 ElementId（省略 = 目前作用中的視圖）"
                }
            },
            required: []
        }
    },
    {
        name: "set_link_display_settings",
        description: "設定指定視圖中連結模型的 Display Settings。設成 byHostView 後，主檔的視圖篩選器與類別覆寫才會套用到連結元素——這通常是讓主檔覆寫對連結生效的前置步驟。可用 linkInstanceIds 或 linkNames 指定目標，兩者都省略 = 該視圖的所有連結。逐個連結獨立處理，一個失敗不影響其他。觸發條件：使用者提到把連結設成 By Host View、改連結顯示方式、讓篩選器對連結生效、連結改用主檔設定。",
        inputSchema: {
            type: "object",
            properties: {
                viewId: {
                    type: "number",
                    description: "目標視圖的 ElementId（省略 = 目前作用中的視圖）"
                },
                displaySetting: {
                    type: "string",
                    enum: ["byHostView", "byLinkView", "custom"],
                    description: "byHostView = 連結套用主檔這張視圖的設定（主檔篩選器才會生效）；byLinkView = 沿用連結檔自己的視圖設定；custom = 自訂",
                    default: "byHostView"
                },
                linkInstanceIds: {
                    type: "array",
                    items: { type: "number" },
                    description: "目標連結實體的 ElementId 清單（最精確；可用 get_linked_models 或 get_link_display_settings 取得）"
                },
                linkNames: {
                    type: "array",
                    items: { type: "string" },
                    description: "以連結類型名稱的子字串比對（不分大小寫），例如 TOPO"
                }
            },
            required: []
        }
    }
];
