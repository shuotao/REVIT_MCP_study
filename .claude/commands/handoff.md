# /handoff — Session 交接指令

在重要 session 結束時呼叫，產出 `session-summary` 條目到 `log/YYYY-MM.md`，供下次跨 AI / 跨機器延續使用。

## 何時觸發

**應該觸發的情境**：
- 多主題研討告一段落（例：一次對話完成 3 個獨立主題）
- MVP 落地後（新功能完成並 commit）
- 重大架構決策完成（例：選擇了某個 pattern、拒絕了某些替代方案）
- 使用者手動要求跨機器延續（「我等一下換電腦繼續」）

**不應該觸發的情境**：
- 日常小對話、單一 bug fix、文件小改——**noop**，避免 log 膨脹
- commit 級別的事件（git hook 已處理）
- 單純 qaqc 執行（`qaqc` event type 已處理）

## 為什麼需要

日常 log 事件記的是「**發生過什麼**」（What happened），但：
- **下一步要做什麼**不會被記錄
- **對齊過的概念、mental model**不會被記錄
- **曾經評估過但拒絕的選項**不會被記錄

`session-summary` 條目**專門**沉澱這三件事，讓新 session 的 AI（包括跨機器、跨模型、跨 AI client）**讀一筆就接得上**，不需要使用者重講脈絡。

## 執行步驟

1. AI 回顧本 session 的對話脈絡
2. 依下方模板產出 `session-summary` 條目
3. Append 到 `log/YYYY-MM.md`（若當月檔不存在則 git hook 會自動創建，本命令只負責 append）
4. **不要** commit log 檔——讓使用者決定何時 sync

## 格式模板

```markdown
## [YYYY-MM-DD HH:MM] session-summary | {一句話標題}
- actor: {model-id} (via {client-name})
- trigger: manual (/handoff)
- summary: {一句話摘要}

### 本 session 完成
- {bullet 1}（若有 commit 附 sha）
- {bullet 2}

### 對齊的概念
- {概念 1：陳述 + 為什麼這樣選、拒絕什麼}
- {概念 2}

### Pending（下 session 可接續）
- {待辦 1：具體動作 + 觸發時機}
- {待辦 2}

### 拒絕的選項（省得重議）
- {選項 A 及拒絕理由}
- {選項 B 及拒絕理由}

### 下次可接續的起手式
- {具體建議新 AI session 該做什麼第一步}
```

## 五段落必填性

| 段落 | 必填 | 說明 |
|------|------|------|
| 本 session 完成 | ✅ | 若無可列「純研討，無程式異動」 |
| 對齊的概念 | ✅ | **最重要**——這是 mental model 沉澱 |
| Pending | ✅ | 若無可列「無（已結束）」 |
| 拒絕的選項 | ✅ | **次重要**——避免下次重新討論 |
| 下次可接續的起手式 | ✅ | 給新 session 明確 entry point |

## 隱私規則

沿用 Logging Protocol：
- ✅ 記：概念、決策、待辦、摘要
- ❌ 不記：使用者原始訊息逐字、API keys、程式碼片段

## 跟其他 log event type 的關係

| Event type | 觸發者 | 粒度 |
|-----------|--------|------|
| `commit`、`merge` | git hook 自動 | 每 commit |
| `lessons`、`domain`、`qaqc`、`review` | Layer 2 AI 主動 | 每命令執行 |
| `skill-change`、`tool-change` | Claude hook（未來） | 每檔案編輯 |
| **`session-summary`** | **/handoff 命令手動** | **每重要 session** |

三種粒度並存——一次 session 可能對應多筆 commit + 多筆 qaqc + **1 筆 session-summary**。後者是前者的精華濃縮。

## 執行後提醒

完成 append 後，建議使用者執行：

```bash
git add log/ && git commit -m "log: sync session-summary"
```

同步這筆交接紀錄到 repo，讓其他機器 pull 後能讀到。
