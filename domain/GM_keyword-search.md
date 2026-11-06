---
name: GM_keyword-search
description: "建築 Agents 綠建材關鍵字擴充檢索、網頁自動跳出機制與 DATA Engine 規範。定義當有人提及『綠建材』相關提示詞時自動跳出 green-material-showcase.html 網頁、關鍵字包含檢索 (Substring Match)、同義與部位相關材料擴充 (Synonym Expansion)，以及 TABC 官方線上全量資料 Mapping 規則。"
metadata:
  version: "1.3"
  updated: "2026-08-07"
  created: "2026-07-28"
  references:
    - "https://tabcmgr.hopto.org/mgr/SearchCaseAction.aspx"
    - "EEWH 綠建築評估手冊裝修與材料指標"
  related:
    - GM_catalog.md
    - GM_parameter-schema.md
  referenced_by: []
  tags: [綠建材, 關鍵字檢索, 建築Agents, 語意擴充, 自動跳出網頁, Showcase, SubstringMatch, DATAEngine, TABC, RevitBIM]
---

# 綠建材關鍵字擴充檢索、自動跳出網頁與 DATA Engine 規範 (`GM_keyword-search`)

本文件為全域「建築 Agents」處理使用者綠建築 (EEWH/LEED) 與 BIM 裝修需求時之**關鍵字擴充檢索權威規範、自動跳出綠建材展示網頁機制與全量 DATA Engine 準則**。

---

## 0. 關鍵字觸發與 `green-material-showcase.html` 網頁開啟詢問機制

1. **觸發條件 (Trigger Conditions)**：
   * 當使用者於對話中提及包含 **「綠建材」** 或相關提示詞（如：`綠建材`、`綠建材標章`、`牆體綠建材`、`地坪綠建材`、`TABC綠建材`、`健康綠建材`、`高性能綠建材`、`再生綠建材`、`生態綠建材`、`綠建材採購`、`綠建材網頁`、`綠建材展示` 等）。
2. **AI Agent 行為準則 (Agent Action Rules)**：
   * **先詢問，不自動開啟**：回應中先顯示 `assets/green-material-showcase.html`（本機產生物，非版控檔；由 /GM_update 從 assets/green-material-showcase.template.html 產生，見 tools/green-material/README.md） 的連結，並詢問使用者是否要開啟；使用者確認後才執行 `/GM_web open`（見 `.claude/skills/GM_web/SKILL.md`）。若使用者的訊息本身已是明確的開啟請求（例如直接輸入 `/GM_web open` 或「開啟綠建材檢索平台」），視為已確認，直接執行即可，不需要再多問一次。
   * **提供互動與導引**：說明使用者可在該網頁進行全量 TABC 綠建材搜尋、四大標章過濾、關鍵字高亮顯示，以及 Revit 共享參數 (Shared Parameters) 的一鍵導出。

---

## 1. 建築 Agents 重新訓練之 15+ 大類建材關鍵字對照表

全域建築 Agents 整理建築師、BIM 工程師與業主常用的 15+ 大類關鍵字，與 TABC 官方線上檢索系統 (`tabcmgr.hopto.org`) 真實合格案件之完整 mapping：

| 建築 Agents 關鍵字 (Search Query) | 核心同義擴充與涵蓋品類 (Synonyms & Scope) | TABC 官方原網頁真實合格案件 Mapping |
| :--- | :--- | :--- |
| **牆 / 牆面 / 隔間 / 壁面** | 石膏磚、吸音牆板、薄塗飾材、外牆水泥漆、牆體綠混凝土、重組竹壁板、矽酸鈣板、纖維水泥板 | `GBM0104190` (寶富達石膏磚)<br/>`GBM0104200` (晏欣岩棉吸音板)<br/>`GBM0104204` (中國製釉薄塗紋理裝飾塗材)<br/>`GBM0104195` (椿樺水性水泥漆)<br/>`GBM0104201` (幸峰牆體綠混凝土)<br/>`GBM0104197` (重組竹材牆面壁板)<br/>`GBM0102924` (大倡防潮矽酸鈣板)<br/>`GBM0300412` (國砂高強度再生纖維水泥板) |
| **地板 / 地坪 / 鋪面 / 步道** | 複合木地板、綠混凝土地坪、高壓透水磚、塑木棧板、橡膠安全地磚、磨石子地磚 | `GBM0104194` (昇揚複合木質地板)<br/>`GBM0104202` (幸峰綠混凝土地坪鋪面)<br/>`GBM0201840` (信全高強高透水磚)<br/>`GBM0103520` (美天利塑木棧板)<br/>`GBM0102810` (台橡膠防護安全地磚)<br/>`GBM0101982` (諾拉超耐磨卡扣木地板) |
| **塗料 / 油漆 / 漆 / 水泥漆** | 水性水泥漆、薄塗裝飾材、防霉乳膠漆、節能隔熱塗料、珪藻頁岩塗料、木器漆 | `GBM0104195` (椿樺水性水泥漆)<br/>`GBM0104204` (中國製釉薄塗裝飾材)<br/>`GBM0101850` (虹牌防霉乳膠漆)<br/>`GBM0202110` (得利日光反射節能塗料)<br/>`GBM0400520` (關西天然珪藻頁岩塗料)<br/>`GBM0102140` (歐德水性無毒木器漆) |
| **天花板 / 吸音 / 隔音** | 岩棉吸音板、多孔吸音天花板、樓板衝擊音隔音緩衝墊 | `GBM0104200` (晏欣岩棉吸音板)<br/>`GBM0200540` (佳音多孔吸音天花板)<br/>`GBM0201308` (靜音王樓板隔音緩衝墊) |
| **混凝土 / 綠混凝土 / 結構** | 結構綠混凝土、地坪綠混凝土、G類綠混凝土 | `GBM0104201` (幸峰綠混凝土結構用)<br/>`GBM0104202` (幸峰綠混凝土鋪面用)<br/>`GBM0104193` (鍵蒼綠混凝土 G類) |
| **板材 / 矽酸鈣 / 石膏 / 木材** | 矽酸鈣板、纖維水泥板、石膏磚、重組竹材 | `GBM0102924` (大倡防潮矽酸鈣板)<br/>`GBM0300412` (國砂再生纖維水泥板)<br/>`GBM0104190` (寶富達石膏磚)<br/>`GBM0104197` (重組竹材壁板) |
| **樹脂 / 補修 / 填縫** | 環氧樹脂、建築灌注補修材 | `GBM0104192` (貴宏 LR 建築灌注補修用環氧樹脂) |

---

## 2. DATA Engine 檢索呈現邏輯

1. 當使用者在對話或動態網頁中輸入任意包含「綠建材」相關關鍵字時，DATA Engine 執行：
   * **詢問是否開啟 Showcase 網頁**：於回答中提供 `assets/green-material-showcase.html`（本機產生物，非版控檔；由 /GM_update 從 assets/green-material-showcase.template.html 產生，見 tools/green-material/README.md） 連結，並詢問使用者是否要開啟；同意後執行 `/GM_web open`，不自動跳出。
   * **包含比對 (Substring match)**：名稱/細項包含查詢字的所有真實合格案件全數列出。
   * **同義與部位擴充 (Expansion match)**：自動擴充同義詞與相關工法材料。
2. 網頁頂部標示檢索統計 Banner：
   * `✅ 建築 Agent 關鍵字 DATA Engine：共檢索出 [N] 項【包含關鍵字及語意擴充同義材料】之 TABC 官方線上合格案件`

