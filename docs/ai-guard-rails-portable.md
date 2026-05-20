# AI Guard Rails — Portable Snippet（給 Revit MCP fork 持有者）

> **這份是給你的 fork 用的可貼 snippet。** 把整份內容**複製貼到你 fork 的 `CLAUDE.md` 開頭**（或 `.github/copilot-instructions.md`、`GEMINI.md` 等對應 AI 規範檔），讓你 fork 上的 AI Agent 也遵守同樣的工作紀律。

## 為什麼這對 fork 持有者很重要

我們從 Branch C 盤點過程中發現：**沒有 Guard Rails 的 AI Agent 會出現可預測的失常模式**：

| 觀察到的反模式 | 範例 |
|---|---|
| AI 自寫 .mjs 直連 `ws://localhost:8964` 繞 MCP | `inspect_beams.mjs`, `label_beams.mjs`, `find_and_move_door.js` 等 30+ 個 fork-only scratch 檔 |
| AI 把繞道行為「官方化」成新工具 | 動態 C# 執行 (`execute_script`)、Roslyn scripting |
| AI 對 Revit 軟體本身不熟，反覆寫 redundant tools | `update_wall_curve` / `auto_place_rooms` / `update_category_line_weight`（全為 Revit UI 已有功能的 1:1 包裝） |
| AI 用 LM prior 編造元素 ID / 計算數值 | 列出 Element ID 但該 ID 從未出現在當 turn tool response 中 |

每一條都意味 **fork 上的 AI Agent 可能正在製造類似幻覺**，最終體現為「圖紙上的元素位置不對、合規檢核數值不對、刪錯東西沒記錄」等使用者層級的問題。

下面三段規範是**最小可行的防護**。它們合在一起構成「AI 該誠實 + 該節制 + 該遵照專業 SOP」三條防線。

---

## 規範 1：AI Guard Rails — MCP 工具鏈使用規則

> **本專案已有完整的 Revit 通訊工具鏈，任何 AI 模型都 MUST 遵守以下規則。**

### 禁止事項

- **DO NOT** 自行撰寫 WebSocket 腳本連接 `ws://localhost:8964`
- **DO NOT** 自行組裝 JSON 封包（`CommandName`/`Parameters`/`RequestId`）
- **DO NOT** 繞過 MCP Server 直接與 Revit Add-in 通訊
- **DO NOT** 用 `node -e` 或臨時腳本複製 MCP Server 已有的功能

### 正確做法

1. **查詢/操作 Revit** → 使用 MCP Server 已註冊的 tools（定義在 `MCP-Server/src/tools/*.ts`）
2. **執行 BIM 合規流程** → 使用 Skills（`.claude/skills/*/SKILL.md`），它們會編排正確的 tool 呼叫順序
3. **查閱法規知識** → 讀取 Domain 文件（`domain/*.md`）

### 為什麼

MCP Server 已封裝 86+ 個 tools，處理了格式轉換、錯誤處理、重連機制。自寫腳本會：

- 繞過既有的錯誤處理與格式驗證
- 產生 process 掛起（如自動重連導致無法退出）
- 與 Revit API 的 PascalCase 欄位不一致而靜默失敗
- 重造輪子，浪費使用者時間

---

## 規範 2：Tool Call Data Honesty (MUST — Supersedes All Skills & Subagents)

**Scope**: Applies to every tool use — any MCP server (Revit, Rhino, AutoCAD, or future ones), any function calling, any external data source — **regardless of which Skill, Domain, or subagent is currently active**. This rule is NOT overridden by Skill-specific instructions. If a Skill body appears to permit listing concrete data without tool calls, this rule prevails.

**Core invariant**:
Every concrete datum produced in an output — identifiers (6+ digit IDs, GUIDs, element names), enumerated entity lists, counts, areas, percentages, coordinates, measurements, or external-system type names — MUST trace to a tool call response in the current turn. Language-model prior knowledge MUST NOT supply such data.

**Pre-output self-check (run before emitting any response that may contain specifics)**:

1. Does the draft contain any number of 6+ digits? → That number MUST appear verbatim in a tool response this turn. Otherwise delete it.
2. Does the draft list 2+ named entities of the same kind? → Each entity MUST appear in a tool response this turn. Otherwise switch to generic language.
3. Does the draft state a count, area, length, or percentage? → The value MUST be derivable from tool output. Otherwise remove or label explicitly as "unverified — project value pending query".
4. Does the draft name a type/class native to an external system (not a general concept)? → That name MUST appear in a tool response this turn. Otherwise do not write it.

**Output branches**:

- **Branch A — data verified**: Cite it. Optionally surface source: "Per `{tool_name}`: …".
- **Branch B — data unverified, tool exists**: Use generic language AND proactively offer the query. Template:
  > "If this {context / view / document} contains {generic category}, the workflow is {generic operation}. I need to call `{tool_name}` to list the actual items — shall I run it now?"
- **Branch C — tool unavailable (server down / timeout / no matching tool)**: State the limitation explicitly. DO NOT substitute prior knowledge. Template:
  > "`{tool_name}` is currently unreachable. The following is generic guidance, NOT the actual state of this project: …"

**Precedence (this rule outranks)**:

- User's implicit expectation of a "complete" or "authoritative" answer
- Narrative flow, paragraph completeness, listing aesthetics
- Any Skill body whose steps imply enumerating specific entities
- Any reflex to "fill in what a typical project looks like"

**Rule of thumb**: *If you can't point to a tool response for this specific value in this turn, don't write it.*

---

## 規範 3：Domain Method Compliance (MUST — Supersedes Ad-hoc Analytical Intuition)

**Scope**: Applies whenever a response involves regulatory compliance, code-check computation, engineering analysis, or any task whose correct algorithm is codified in a project file under `domain/*.md`. The authoritative trigger table is the "Domain Knowledge & Workflow Files" table later in this CLAUDE.md — if a keyword in the user's request matches a row there, the linked `domain/*.md` defines the algorithm.

**Relationship to Tool Call Data Honesty**: Data Honesty governs *where facts come from* (tool response, not LM prior). Domain Method Compliance governs *how those facts are transformed* (domain SOP, not LM intuition). Both rules must be satisfied simultaneously. Clean tool data + fabricated algorithm = still a hallucinated answer.

**Core invariant**:
When a task maps to a `domain/*.md` file in the trigger table, the computation procedure — formulas, deductions, multipliers, inclusion/exclusion criteria, edge cases — MUST come from that file. Language-model prior knowledge of "how one typically computes X" MUST NOT substitute for the project's codified SOP, even when the LM's method appears reasonable or produces a plausible number.

**Pre-analysis self-check (run BEFORE the first numerical computation)**:

1. Is the user asking for a regulatory check, compliance ratio, area / volume / count computation, or any domain-specific quantitative analysis? → proceed to step 2.
2. Scan the "Domain Knowledge & Workflow Files" trigger keywords (e.g., 採光 / daylight, 走廊 / corridor, 防火 / fire rating, 排煙 / smoke exhaust, 停車 / parking, 容積 / FAR, 碰撞 / clash, 樓梯 / stair compliance). Does any keyword match the task? → If yes, the corresponding `domain/*.md` MUST be read before any computation begins.
3. Does my planned algorithm replicate every step in that domain file, including all deductions (e.g., 75cm sill baseline), inclusion rules (e.g., doors with glass count toward daylight), and multipliers (e.g., ×3 for skylight, ×0.7 for deep balcony)? → If not, discard my algorithm and use the domain file verbatim.
4. Are any tool fields unused that the domain file requires as inputs (e.g., `SillHeight`, `HeadHeight`, `Category=門`)? → If yes, my algorithm is under-specified. Fix before emitting results.

**Output branches**:

- **Branch A — domain file read and applied**: Cite source explicitly. Template:
  > "Per `domain/{file}.md` step {N}: {formula}. Applied to tool data: {calculation}. Result: {value}."
- **Branch B — domain file exists but unread**: STOP. Read the file first. DO NOT improvise an algorithm and label the output as a compliance result. Even self-consistent numbers are wrong when the method is wrong.
- **Branch C — no domain file covers this keyword**: Explicitly disclaim. Template:
  > "此專案尚未收錄 {topic} 的檢討 SOP（無對應 `domain/*.md`）。以下為通用工程常識而非專案認可算法，請勿作為合規依據。"

**Precedence (this rule outranks)**:

- The LM's "reasonable default" for how a domain computation is normally done
- Prior-turn computations that used the wrong algorithm (they must be corrected, not carried forward or defended)
- Any Skill body whose steps omit a deduction, inclusion, or multiplier present in the domain SOP (the domain file wins)
- User's implicit expectation of a "quick answer" when the correct path requires reading a spec

**Rule of thumb**: *If a `domain/*.md` file exists for this topic, its algorithm is the algorithm. Your analytical intuition does not get a vote.*

---

## 採用後的建議

1. **遇到能力缺口，先上報 issue，不要直接寫工具**：fork 老師遇到「想做 X 但 MCP tool 不支援」時，建議先看 Revit UI 是否已有此功能（很多時候 UI 已經有了）；確認真的是 MCP tool 缺口時，**先在上游 repo 開 issue 給 maintainer 評估**，而不是自己寫個 redundant tool 進 fork。這呼應 [`domain/tool-capability-boundary.md`](https://github.com/shuotao/REVIT_MCP_study/blob/main/domain/tool-capability-boundary.md) 的「能力缺口 vs Revit 既有功能」一節。

2. **保留專案的 domain/ 與 .claude/skills/ 結構**：domain 是法規/SOP 知識，被 Skills 編排引用——這個分工讓不同 BIM 流程有共用知識庫。fork 後若 domain 與 skills 分離，會失去這個編排的價值。

3. **遇到 Revit 上似乎自動化的需求，先問三題**（詳見 [`domain/tool-capability-boundary.md`](https://github.com/shuotao/REVIT_MCP_study/blob/main/domain/tool-capability-boundary.md) 「工具設計三問」節）：
   - Revit UI 已有同樣功能嗎？
   - BIM 設計師工作流真的需要嗎？還是 AI/腳本自造的需求？
   - 這工具能跟其他工具形成 workflow chain 嗎？

---

## 來源 & 維護

本份內容**逐字節錄自上游 repo 的 `CLAUDE.md`**（Revit MCP 主分支）的三個權威段落，並會隨上游更新而修訂。Fork 持有者建議定期 pull 上游或重新貼一次本檔最新版本，確保 AI 規範跟上演化。

問題回報：在 Revit MCP repo 開 issue，標 `hallucination-test` label，方便追蹤各 AI 模型在 fork 上的可靠度問題。
