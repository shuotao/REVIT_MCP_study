---
name: domain-diagram
description: "引導剛寫完 domain 的老師把 SOP 流程圖像化：先討論主流程、做流程健檢（迴圈／死路／缺口），再把流程整理成結構化 spec，交由 scripts/mermaid_from_spec.py 確定性地產出 GitHub 原生 ```mermaid 圖與健檢結論並回嵌 domain。模型不手寫圖，畫圖一律過腳本以保證一致性。觸發條件：使用者提到 domain 流程圖、流程圖像化、把 domain 畫成圖、mermaid、流程健檢、迴圈檢查、圖表型態、diagram、flowchart、visualize domain、畫流程。"
---

# Domain 流程圖像化助手（腳本驅動）

把一份剛寫好的 `domain/*.md` 變成一張 GitHub 直接渲染、能幫作者抓邏輯漏洞的 Mermaid 圖。
**知識（方法）在 `domain/domain-flow-visualization.md`；本 skill 只負責互動編排。** 啟動時務必先讀該 domain。

設計模式：**Sequential Workflow + Iterative Refinement**（討論 → 健檢 → 產 spec → 跑腳本 → 回嵌 → 修正迴圈）。

## 最高原則：模型不手寫圖

> **flowchart 一律由 `scripts/mermaid_from_spec.py` 產生。模型只負責產出結構化的流程 spec（JSON），絕不手刻 ` ```mermaid ` fence。**

理由：手寫 fence 會在節點形狀、配色、跳脱、安全子集上漂移，每張圖長相不一。把「畫」交給確定性腳本後，一致性由程式保證，模型專注在「流程對不對」。

- ✅ flowchart：**已程式化**，禁止手寫，走腳本。
- ☐ state / sequence / class / mindmap：**尚未程式化**，暫用 `domain` 速查表手寫模板，並貼 <https://mermaid.live> 目視驗證後回嵌。進度見 `references/programmatization-roadmap.md`。

## 啟動前提

- render 目標固定為 **GitHub 原生 ` ```mermaid ` fence**，只能用安全型態子集（見 domain 對照表）。
- 對象是「剛寫完 domain 的老師」，目的是**理解 + 修正 domain**，不是產出漂亮圖。

## Sub-Workflows

### 1. 討論（Discuss）
讀目標 domain 全文，然後和作者確認：
- 主流程一句話：輸入 → 動作 → 輸出。
- 有沒有迴圈 / 分支 / 模式切換。
- 這張圖的讀者是「作者自檢」（詳盡）還是「README 讀者」（精簡）。

**不要急著畫。** 先把流程攤平成節點與邊的草稿，唸給作者聽確認沒誤解。

### 2. 產出流程 spec（Author the spec）
把討論定案的流程寫成 `references/spec-schema.md` 定義的 JSON：
- 每個步驟一個 node，挑對 `kind`（start / action / decision / end / abort / data / io）。
- 每個決策的所有分支都要畫成 labeled 的 edge（是/否都要）。
- `id` 用英數，中文放 `label`；換行寫 `\n`（腳本轉 `<br/>`）。

這一步取代「想圖長怎樣」——模型只整理**邏輯結構**，不碰語法。

### 3. 跑腳本：健檢 + 產圖（Audit & render）
```bash
python3 scripts/mermaid_from_spec.py spec.json
```
腳本同時做兩件事：
- **audit**：自動跑健檢項 1–4（迴圈偵測、死路/不可達、終點覆蓋、決策完備），項 5–6 標記為人工。
- **render**：把 spec 確定性轉成安全子集 fence（形狀、classDef 語意色、`<br/>` 全由程式統一）。

把腳本回報的每條 finding **當場唸給作者**：標 `缺口` 的就是 domain 邏輯要補的洞，標 `人工確認/人工` 的（迴圈退出、前置缺口、原子性）要作者判斷。

### 4. 回嵌 domain（Embed）
- 把腳本吐的 ` ```mermaid ` fence 原封不動貼進目標 domain 的流程章節（原始碼可 diff，不貼圖片）。
- 圖後接腳本的「流程健檢結論」finding 清單。
- 若健檢揭露 domain 文字有漏洞 → **先修 domain 文字，再重跑腳本讓圖與文字一致**。

### 5. 修正迴圈（Refine）
和作者一起看渲染結果 → 若圖讓邏輯漏洞浮現 → 回到步驟 2 改 spec / 改 domain → 重跑腳本，直到圖與 domain 文字一致。**改的是 spec 與 domain，不是手改 fence。**

## Quick Reference

```
flowchart（多數 SOP）： 討論 → 寫 spec.json → mermaid_from_spec.py → 回嵌
state / sequence 等：   討論 → 健檢 → 速查表手寫模板 → mermaid.live 目視 → 回嵌
揭露邏輯漏洞：          健檢 finding → 先修 domain 文字 → 重跑腳本 → 一致為止
```

## 檔案

| 路徑 | 角色 |
|---|---|
| `scripts/mermaid_from_spec.py` | **唯一**的 flowchart 產圖器 + 結構健檢器 |
| `references/spec-schema.md` | flowchart spec 的 JSON schema 與 kind 對照 |
| `references/troubleshooting.md` | GitHub fence 中文/特殊字元/型態踩雷 |
| `references/programmatization-roadmap.md` | 各圖種程式化進度（log / lint 接續依據） |
| `domain/domain-flow-visualization.md` | 方法論：選型表、6 項健檢、安全子集、回嵌慣例 |

## 工具

本 skill 是 domain 編輯與分析工作，**不呼叫 Revit MCP tools**。產圖呼叫本地 `python3 scripts/mermaid_from_spec.py`；其餘用 Read / Edit 操作 `domain/*.md`，必要時用 WebFetch 對照 mermaid.js.org 最新語法。

## Reference

- 方法論與選型/健檢清單：`domain/domain-flow-visualization.md`
- 語法權威來源：<https://github.com/mermaid-js/mermaid>、<https://mermaid.js.org/syntax/flowchart.html>
