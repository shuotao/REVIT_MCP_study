---
name: monstrare-task-verify
description: Monstrare 看板任務完成階段規範 (Lessons Learned Rule)。當 Agent 完成任何任務卡時，強制先移至 verify (驗證中) 階段，禁止直接設為 done。
---

# Monstrare 看板任務完成階段規範 (Lessons Learned)

## 📌 核心規則 (Mandatory Rule)

當 Agent 完成任何 Monstrare 看板任務 (Task) 的開發與驗證時：

1. **強制階段落點 (Mandatory Stage)**：
   - 任務卡 (Task Card) 的階段 (`stage`) **必須先設為 `verify` (驗證中)**。

2. **禁止直接進入 `done` (Prohibition)**：
   - Agent **不得**自動將任務卡階段設為 `done`。
   - 所有標註為 `done` 的任務必須由**使用者親自測試成功後，手動或指示 Agent 移至 `done`**。

3. **Monstrare 看板同步說明**：
   - 更新 `sync_monstrare_kanban.py` 或呼叫 Monstrare 工具時，完成的 Task 階段屬性統一定義為 `"stage": "verify"`。
