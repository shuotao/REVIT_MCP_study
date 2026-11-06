# Speckle 研究報告（Revit 使用者視角）

> **查證日期**：2026-09-02
> **資料來源性質**：以官方來源為主（speckle.systems 官網／product update／docs.speckle.systems／GitHub `specklesystems` 組織），輔以官方營運的社群論壇 speckle.community（社群回覆內容標示為社群性質，非官方保證）。
> **結論（Revit 版本支援，一句話）**：截至查證當下，Speckle 官方文件（`docs.speckle.systems/connectors/revit/revit`）明載目前的（V3／Next-Gen）Revit 連接器支援 **Revit 2023、2024、2025、2026、2027**（Windows only），2022 已終止支援（需停在 3.21.0 版或升級 Revit）；Revit 2026 為官方已支援版本，且文件另外提醒 2026 年 9/1–11/1 之間需更新連接器一次（舊連接器逾期仍可運作，但不再保證相容）。

---

## 1. Speckle 是什麼

Speckle 官網自我定位為「the intelligent design data layer for AEC」（AEC 產業的智慧設計資料層），角色是連接各種 AEC 軟體（Revit、Rhino、SketchUp 等）的資料互通平台／資料中樞，讓使用者可以：跨工具整合專案資料、自動化驗證與報表、建立跨工具協作流程，並把結構化的模型資料餵給企業系統（如 Power BI）或 AI 分析。來源：[speckle.systems 首頁](https://speckle.systems/)。

另一份官方 GitHub README（`specklesystems/speckle-server`）的說法更直白：「Speckle is the first AEC data hub that connects with your favorite AEC tools」——解決的是 AEC 產業資料破碎（fragmented data）、跨軟體資料無法互通的問題。來源：[github.com/specklesystems/speckle-server](https://github.com/specklesystems/speckle-server)。

對一個已經在用 Revit 的 BIM 人來說，Speckle 解決的實際痛點是：
- Revit 模型資料如何在不透過 IFC／不透過人工重繪的情況下，即時分享給用 Rhino、SketchUp、Power BI、網頁瀏覽器的協作對象看。
- 團隊間版本追蹤、資料串流（stream/model 概念），而非單純檔案傳遞。

**授權（Licence）**：`specklesystems/speckle-server`、`specklesystems/speckle-sharp-connectors`（目前的 Revit V3 連接器所在 repo）皆為 **Apache-2.0** 授權，屬開放原始碼專案。來源：[github.com/specklesystems/speckle-server](https://github.com/specklesystems/speckle-server)、[github.com/specklesystems/speckle-sharp-connectors](https://github.com/specklesystems/speckle-sharp-connectors)。

<!-- ▍補述插入點：Speckle 是什麼 -->

---

## 2. Next-Gen（現行世代）vs Legacy（舊世代）

這是本報告特別提醒的地雷區：**2024 年以前的 Speckle Revit 教學，很高機率是講舊世代（俗稱 v2 / Legacy）連接器**，UI、資料模型、專案概念（stream vs. 現在的 project/model）都已經改變。

- **現行世代的名稱**：官方文件與部落格稱之為「**Next-Gen connectors**」或「**V3 connectors**」（兩詞混用，指同一世代）。來源：[Migrating from v2 Connectors to Next-Gen Connectors (v3)](https://www.speckle.systems/blog/guide-how-to-switch-to-next-gen-connectors)、[Speckle's Next-Gen Connectors: What's In It For You?](https://speckle.systems/blog/speckles-next-gen-connectors-whats-in-it-for-you/)。
- **舊世代的名稱**：官方文件稱為「**legacy connectors**」（v2）。
- **時間線**：
  - Revit 的 next-gen 連接器約於 2024 年 9 月隨首批 next-gen 連接器（含 SketchUp、AutoCAD、Rhino、ArcGIS）一起推出。來源：[Everything We Announced at SpeckleCon 2024](https://speckle.systems/blog/everything-we-announced-at-specklecon-2024/)（頁面顯示發佈日 2025-08-05，內文描述的是 2024 年 SpeckleCon 場次的公告，日期標示方式未能完全查證，故此處存疑標註）。
  - 2025 年 SpeckleCon 場次追加 Archicad、Civil3D、Tekla 三個 next-gen 連接器。同來源。
  - 社群公告「Next-Gen Speckle Connectors are Official - June 1st」指出 next-gen 連接器已於 **2025 年 6 月 1 日**正式成為預設連接器；該公告本身發佈於 2025-05-14。來源：[speckle.community/t/next-gen-speckle-connectors-are-official-june-1st/18009](https://speckle.community/t/next-gen-speckle-connectors-are-official-june-1st/18009)（此為官方帳號在官方社群論壇發布，視為半官方消息，內容未能逐字複查全文，僅取自搜尋摘要，建議讀者自行點開複核）。
  - **Legacy（v2）連接器的終止支援日期**：官方遷移文件明載 legacy 連接器將於 **2026 年 1 月 1 日**停止維護：屆時安裝檔會從 release repo 移除、已安裝的 legacy 連接器仍可在本機繼續運作、但論壇不再提供主動支援、且 V3 不再保證回溯相容 legacy 的資料模型。來源：[Migrate from legacy connectors to current (V3) connectors - Speckle Docs](https://docs.speckle.systems/migration/connectors-v2-to-v3)。
  - V3 與 legacy 使用不同的資料架構（不相容），舊 stream 的資料無法直接沿用，需要重新以 V3 連接器發佈（resend）模型。同來源。
  - 部分工具目前**仍只有 legacy 版本**、尚未有 V3 對應：Excel、QGIS、Unity、Unreal、Dynamo、SAP2000、Bentley 系列產品；「原生 BIM 元素」相關某些工作流程也仍需 legacy 連接器。同來源。**Revit 不在此清單內**——Revit 已有成熟的 V3／next-gen 連接器。

小結：對 2026-09 現在的讀者而言，**只信任 2024 年 9 月之後、且明確標示「V3」或「next-gen」的 Revit 教學**；凡是畫面顯示舊版 Speckle Manager 介面、或提到 "stream"（而非 project/model）的教學，都屬於即將（或已於 2026-01-01）停止支援的 legacy 世代。

<!-- ▍補述插入點：Next-gen vs legacy 世代交接細節 -->

---

## 3. Revit 版本支援矩陣（最重要章節）

**查證頁面**：[docs.speckle.systems/connectors/revit/revit — How to use Speckle for Revit](https://docs.speckle.systems/connectors/revit/revit)（查證時間：2026-09-02，建議讀者自行重新開啟此頁確認最新狀態，因為官方文件本身會隨連接器改版持續更新）。

該頁面原文（逐字引用）：

> "Speckle currently supports Revit versions: 2023, 2024, 2025, 2026, & 2027."
>
> "2022 support has ended. If you use Revit 2022, use connector version 3.21.0 or upgrade Revit to a supported version."
>
> "Update the Revit connector from 1 September 2026, and no later than 1 November 2026."（既有連接器逾期後仍可運作，但官方要求在此窗口內更新一次）

GitHub 上 V3 連接器所在 repo（`specklesystems/speckle-sharp-connectors`）的說明同樣寫著 Revit 連接器目標版本為「Autodesk Revit 2023 - 2027」，與文件站一致。來源：[github.com/specklesystems/speckle-sharp-connectors](https://github.com/specklesystems/speckle-sharp-connectors)。

| Revit 版本 | 支援狀態 | 備註／來源 |
|---|---|---|
| Revit 2022 及更早 | **已終止支援** | 若仍用 2022，須固定在連接器 3.21.0 版，或升級 Revit；docs.speckle.systems/connectors/revit/revit |
| Revit 2023 | 支援 | docs.speckle.systems/connectors/revit/revit |
| Revit 2024 | 支援 | 同上 |
| Revit 2025 | 支援（含 .NET Framework 4.8 → .NET 8 的底層遷移，連接器已處理） | 同上；DeepWiki 摘要提及此遷移發生在 Revit 2025 世代（`deepwiki.com/specklesystems/speckle-sharp-connectors`，屬第三方整理站，未逐字複查，僅供交叉參考） |
| **Revit 2026** | **支援** | docs.speckle.systems/connectors/revit/revit；另需在 2026-09-01～2026-11-01 期間更新一次連接器 |
| Revit 2027 | 文件列為支援版本（截至查證日，Autodesk 尚未正式發布 Revit 2027，此為 Speckle 官方文件對「即將到來版本」的前瞻性列名，實際可用性未能以第二來源交叉驗證，建議讀者以官方文件當下顯示內容為準） | docs.speckle.systems/connectors/revit/revit |

平台限制：Speckle 所有連接器（含 Revit）**目前僅支援 Windows**，無 macOS 版。來源：[docs.speckle.systems/connectors/installation](https://docs.speckle.systems/connectors/installation)。

<!-- ▍補述插入點：版本支援矩陣的後續複查與變動記錄 -->

---

## 4. 安裝方式

官方安裝文件（[docs.speckle.systems/connectors/installation](https://docs.speckle.systems/connectors/installation)）與 Revit 專頁（[docs.speckle.systems/connectors/revit/revit](https://docs.speckle.systems/connectors/revit/revit)）整理出的現行安裝機制：

1. 在 speckle.systems 註冊帳號。
2. 前往 **app.speckle.systems/connectors**，下載對應軟體（Revit、Rhino、Archicad…）的安裝檔（`.exe`）。也可以從 [speckle.systems/download](https://www.speckle.systems/download) 取得「Manager」一次管理多個連接器；或從 [Speckle Releases](https://releases.speckle.systems/) 頁面取得各連接器的手動安裝檔／zip。
3. **安裝前務必關閉目標軟體**（例如 Revit）。
4. **不要用「以系統管理員身分執行」安裝** —— 官方文件明確警告：以系統管理員模式安裝「不會替你的使用者帳號安裝連接器」。正確做法是**直接雙擊 `.exe`，或右鍵選「開啟」**來執行安裝程式。
5. 依安裝精靈完成安裝。
6. 開啟 Revit，功能區（Ribbon）會出現 **Speckle** 分頁；點選即可啟動連接器介面。
7. 若尚未登入，於連接器介面內選擇 **Sign in** 綁定 Speckle 帳號。

來源：[docs.speckle.systems/connectors/installation](https://docs.speckle.systems/connectors/installation)、[docs.speckle.systems/connectors/revit/revit](https://docs.speckle.systems/connectors/revit/revit)。

<!-- ▍補述插入點：安裝疑難排解（企業網路、防火牆、多版本 Revit 共存等） -->

---

## 5. 一個簡單的應用：把 Revit 模型發佈到 Speckle 專案並在瀏覽器檢視

以下步驟依官方 Revit 連接器文件（[docs.speckle.systems/connectors/revit/revit](https://docs.speckle.systems/connectors/revit/revit)）所描述的流程整理，選定情境是**最小可行的 Revit → Speckle → 瀏覽器**單向分享，這是文件明確涵蓋、且對已有 Revit 使用經驗者最實用的入門動作。若某一步驟的確切點擊路徑（例如按鈕確切文字、對話框排版）文件未逐字描述，本節只寫到文件支援的抽象層級，並在該步驟註明「文件未列出精確 UI 細節」。

1. **安裝並開啟連接器**：依第 4 節安裝完成後，在 Revit 開啟一個既有專案，點功能區的 **Speckle** 分頁，啟動連接器面板，並登入帳號（若尚未登入）。
2. **建立或選擇一個 Project**：在連接器介面中，選擇既有的 Speckle Project，或建立 **New Project**。（V3 世代以「Project」取代舊版的 "Stream" 概念——這是判斷教學是否為新世代的關鍵字之一，見第 2 節。）
3. **建立或選擇一個 Model**：在該 Project 底下，選擇既有 Model，或建立 **New Model**（例如可用來代表某個樓層、某個學科的子模型）。
4. **選取要發佈的物件**：在 Revit 視圖中選取要送出的元素（可以是整個模型，也可以是選取的子集合）。
5. **點擊 Publish**：連接器會把選取的幾何與資料上傳到 Speckle 伺服器。文件特別指出，發佈時會**保留共用座標（Shared Coordinates）與參考點（預設為 Internal Origin）**，這對多模型座標協調的團隊工作流程很重要。
6. **在瀏覽器中檢視**：發佈完成後，文件說明「你的模型現已發佈到 Speckle，團隊其他成員可以檢視」——也就是說，其他協作者（或你自己）可以到 Speckle 網頁介面（app.speckle.systems 底下對應的 Project／Model 頁面）用瀏覽器直接檢視這個剛發佈的模型版本，不需要安裝 Revit 或連接器。文件未進一步列出瀏覽器檢視頁面內每個按鈕的確切操作細節，此處只寫到「可在瀏覽器檢視已發佈版本」這個文件明確支持的程度。

這個流程可以延伸的方向（文件有提及但本報告未逐一展開，留給補述插入點）：反向由其他工具（如 Rhino）發佈幾何、指定 Revit 類別後在 Revit 端接收為原生族群或 DirectShape、以及把 Revit 模型接入 Power BI／Speckle Intelligence 做資料分析。相關來源另見第 6 節的教學文章。

<!-- ▍補述插入點：更完整的 Revit↔其他軟體雙向流程實測截圖 -->

---

## 6. 2024 年後的分享與教學補充

以下全部標註官方（speckle.systems / docs.speckle.systems / GitHub）或社群（speckle.community，官方營運但內容多為使用者/官方帳號混合），並各自附上查得的日期（查不到者標「未能查證」）。

| 標題 | 內容概要 | 日期 | 性質 | URL |
|---|---|---|---|---|
| How to use Speckle for Revit（Revit 連接器官方文件） | 現行 V3 連接器支援版本、安裝、發佈／接收流程說明 | 未能查證確切最後更新日（頁面本身持續更新，查證於 2026-09-02） | 官方 | https://docs.speckle.systems/connectors/revit/revit |
| Migrate from legacy connectors to current (V3) connectors | v2→V3 遷移指南、legacy 終止支援時間點（2026-01-01）、資料不相容說明 | 未能查證確切發佈日 | 官方 | https://docs.speckle.systems/migration/connectors-v2-to-v3 |
| Migrating from v2 Connectors to Next-Gen Connectors (v3)（部落格版） | 與上一篇文件版內容呼應的部落格說明文 | 未能查證確切發佈日 | 官方 | https://www.speckle.systems/blog/guide-how-to-switch-to-next-gen-connectors |
| Next-Gen Speckle Connectors are Official – June 1st | 公告 next-gen 連接器自 2025-06-01 起成為預設 | 公告日期 2025-05-14（內容所述生效日 2025-06-01） | 官方（發佈於官方社群論壇） | https://speckle.community/t/next-gen-speckle-connectors-are-official-june-1st/18009 |
| Speckle's Next-Gen Connectors: What's In It For You? | Next-gen 連接器整體特色說明（一致 UI、跨連接器體驗、資料驅動） | 頁面顯示發佈日 2025-08-04 | 官方部落格 | https://speckle.systems/blog/speckles-next-gen-connectors-whats-in-it-for-you/ |
| Everything We Announced at SpeckleCon 2024 | Revit next-gen 連接器約 2024-09 隨首批連接器推出；republish 效能提升「1000% 以上」；資料檔案縮小 50% | 頁面顯示發佈日 2025-08-05（內容描述 2024 年會議公告，日期標示矛盾未能完全釐清） | 官方部落格 | https://speckle.systems/blog/everything-we-announced-at-specklecon-2024/ |
| Updates to the ETABS, Revit, Rhino, Grasshopper, and Navisworks integrations | 連接器 3.8.2：Revit 內新增 snapping（吸附）功能等 | 2025-08-31 | 官方 Product Update | https://speckle.systems/updates/connectors-3-8/ |
| Assign Revit categories to your Rhino model | Rhino 端可指定 Revit 類別，接收端得到分類正確的 DirectShape | 2025-08-18 | 官方 Product Update | https://speckle.systems/updates/ （條目日期取自 updates 首頁列表） |
| Optimized Revit models for faster loading | 重複幾何改用代理（proxy）參照，模型體積平均縮小 35%+，FF&E 密集案場最高縮小逾 90%；需要連接器 3.10.0 以上 | 2025-10-21 | 官方 Product Update | https://speckle.systems/updates/optimized-revit-models-for-faster-loading/ |
| Revit file importer | 免安裝 Revit／連接器即可直接上傳 .RVT（最大 1GB，回溯支援到 Revit 2022 的檔案格式）並抽取屬性、參數、幾何 | 2025-12-22 | 官方 Product Update | https://speckle.systems/updates/revit-file-importer/ |
| Native Revit families, model validation (beta) and more | Blocks 轉為原生 Revit 族群；模型驗證進 dashboard；連接器過濾與穩定性改善 | 2026-02-25 | 官方 Product Update | https://speckle.systems/updates/native-revit-families-model-validation-beta/ |
| From Rhino concept to Revit documentation in minutes with Speckle | Rhino→Speckle→Revit 圖說產出的完整教學（分層對應類別、發佈、接收為原生形狀、雙向回傳） | 未能查證確切發佈日 | 官方教學（tutorials） | https://speckle.systems/tutorials/from-rhino-concept-to-revit-documentation-in-minutes-with-speckle/ |
| Revit 2026 Support（論壇討論串） | 使用者詢問／官方回覆 Revit 2026 支援狀況 | 討論串存在但因站台回應 403，內容未能逐字複查，僅由搜尋摘要得知存在 | 社群（官方論壇） | https://speckle.community/t/revit-2026-support/17963 |

<!-- ▍補述插入點：更多 2024 年後教學（研討會錄影、第三方部落格、中文社群分享） -->

---

## 7. 與 Revit MCP 的可能交會點（推測／待驗證，非事實陳述）

以下為**推測性**討論，用來啟發後續研究方向，不代表已驗證的技術事實，也不代表本專案（Revit MCP）已與 Speckle 有任何整合：

- **推測**：Revit MCP 目前的定位是「AI 透過 MCP 工具直接呼叫 Revit API 做模型查詢／編輯」，而 Speckle 的定位是「模型資料的跨軟體發佈與檢視層」。兩者理論上可以互補——MCP 負責「改模型」，Speckle 負責「把改完的結果快速分享給不裝 Revit 的人看」，但這只是架構層面的合理猜測，未在任何官方文件中看到雙方合作或整合的說明。
- **推測**：若一個工作流程需要「AI 在 Revit 內完成批次修改 → 立即讓遠端協作者或非 Revit 使用者在瀏覽器確認結果」，理論上可以想像成「Revit MCP 完成編輯 → 呼叫 Speckle Revit 連接器發佈 → 協作者於 app.speckle.systems 檢視」的接力，但這需要額外橋接（例如透過 Revit UI Automation 觸發 Speckle 的 Publish，或者未來若 Speckle 提供可程式化的 headless publish API）。**待驗證**：本次研究未查到 Speckle 是否提供可被外部程式（非透過 Revit UI）直接觸發發佈的 API 或 SDK，此為需要進一步查證的關鍵前提。
- **推測**：Speckle 的「Revit file importer」（免安裝 Revit 即可解析 .RVT，見第 6 節）如果日後開放程式化存取，可能可以與 MCP 生態系統中「不需要即時連線 Revit 也能讀模型資料」的離線分析情境互補，但這同樣是根據目前 product update 描述所做的推論，並非官方對外宣稱的用途。

**這一整節的所有陳述都僅供腦力激盪，實作前務必重新查證，不可當作既有事實引用。**

<!-- ▍補述插入點：實際測試 Revit MCP + Speckle 併用的可行性 -->

---

## 本文未能查證的項目

- 「Everything We Announced at SpeckleCon 2024」一文頁面顯示的發佈日期（2025-08-05）與其內容描述的會議時間（2024 年）有出入，確切原始公告日期未能查證，可能是頁面改版或轉載日期，非會議實際舉行日。
- 「Next-Gen Speckle Connectors are Official – June 1st」討論串內文未能逐字複查（工具回傳搜尋摘要而非全文），生效年份（2025）取自摘要推斷，建議讀者自行開啟原文確認。
- 「Revit 2026 Support」與「Revit 2026 support for legacy connectors」兩則社群討論串，因站台對本次查證工具回應 HTTP 403，內容細節（例如社群使用者回報的實際錯誤訊息、Speckle 官方帳號的逐字回覆）未能取得，僅確認討論串存在。
- Revit 2027 是否已有可下載、可實測的 Speckle 連接器版本（而非文件預先列名），未能以第二來源交叉確認——查證當下 Autodesk 尚未正式發布 Revit 2027，此處存在時間邏輯上的不一致，讀者應自行重新開啟官方文件核實。
- Speckle 是否提供可供外部程式（非 Revit UI、非既有連接器 UI）直接呼叫的「headless publish」API 或 SDK，未能查證（第 7 節的推測前提）。
- speckle.systems 首頁文字中是否明確使用「open source」字樣未能在該頁面內容中直接找到；開源結論改以 GitHub repo 授權條款（Apache-2.0）作為證據來源。
- 官方 YouTube 頻道 2024 年後是否有針對 V3／next-gen Revit 連接器的完整「Getting Started」影片，本次搜尋僅找到 2022 年的舊教學影片，未找到明確標示 2024 年後、且針對 next-gen 連接器的官方影片，故未列入第 6 節表格。

<!-- ▍補述插入點：後續查證清單追蹤 -->
