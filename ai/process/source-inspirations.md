# 來源啟發（Source Inspirations）

這套 kit 是對現有公開方法論的綜合整理。

## BMAD Method

採用：

- 角色導向的工作流程。
- 產品、UX、架構、開發、QA 等人設（persona）。
- 從分析到實作的完整生命週期思維。

沒有直接採用：

- 偏重框架特定的繁瑣儀式。

## GitHub Spec Kit

採用：

- 規格驅動（spec-driven）的階段劃分。
- Constitution、specify、plan、tasks、implement 的結構。
- 實作前先 clarify（澄清）與 analyze（分析）。

新增：

- UI mockup 關卡。
- 人工看板式的就緒判斷。
- 情境（context）預算管理。

## Kiro Specs

採用：

- `requirements.md`、`design.md`、`tasks.md` 風格的產出物。
- 任務狀態追蹤。
- 需求優先與設計優先的工作流程。

新增：

- 工具無關（tool-agnostic）的 repo 範本，而非綁定特定 IDE。

## Task Master

採用：

- 從 PRD 拆解到任務。
- 模型角色分工。
- 對 MCP 友善的任務管理。

新增：

- 任務執行前的人工核准關卡。

## Serena

採用：

- 把語意化程式碼檢索當成一級能力。
- 符號感知搜尋，而非整份 repo 閱讀。

新增：

- 給沒有語意化工具的 agent 用的書面情境協定。

## SuperClaude

採用：

- 可重複使用的 skill 與指令。
- 人設導向的工作流程。

新增：

- 由 Claude、Codex 與其他工具共用的跨 agent 流程文件。

## Archon

採用：

- 確定性的階段關卡。
- Workflow runner 的思維方式。
- 把驗證與審查拆成獨立階段。

新增：

- 人類可讀的任務卡與 UI 核准產出物。

## Plandex

採用：

- 大型任務規劃。
- 受控的執行方式。
- 套用變更前先做 diff 審查。
- 多模型思維。

新增：

- 規格成熟度與人工驗收關卡。

## CodeRabbit 與 Qodo

採用：

- 以審查為優先的品質層。
- 客製化審查準則。
- 安全性與可維護性政策的落實。

新增：

- 在程式碼寫出來之前，更早的產品與 UI 關卡。
