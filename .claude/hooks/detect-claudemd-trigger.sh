#!/bin/bash
# detect-claudemd-trigger.sh
# PostToolUse hook: 偵測三種觸發事件，注入雙向驗證指令
#
# 觸發條件：
#   1. git merge / pull / rebase（合併外部 PR）
#   2. 寫入 .claude/skills/ 路徑（Domain 升級 Skill）
#   3. 寫入 MCP-Server/src/tools/ 或 MCP/Core/CommandExecutor（Tools 檢討）

INPUT=$(cat)
TOOL_NAME=$(echo "$INPUT" | jq -r '.tool_name // empty')
TRIGGER=""

# --- 偵測 Bash 指令 ---
if [ "$TOOL_NAME" = "Bash" ]; then
  COMMAND=$(echo "$INPUT" | jq -r '.tool_input.command // empty')

  # 合併外部 PR
  if echo "$COMMAND" | grep -qE '(git\s+merge|git\s+pull|git\s+rebase)'; then
    TRIGGER="MERGE_EXTERNAL"
  fi
fi

# --- 偵測檔案寫入/編輯 ---
if [ "$TOOL_NAME" = "Write" ] || [ "$TOOL_NAME" = "Edit" ]; then
  FILE_PATH=$(echo "$INPUT" | jq -r '.tool_input.file_path // empty')

  # Domain 升級 Skill
  if echo "$FILE_PATH" | grep -qE '\.claude/skills/.*SKILL\.md'; then
    TRIGGER="SKILL_CHANGE"
  fi

  # Tools 檢討
  if echo "$FILE_PATH" | grep -qE '(MCP-Server/src/tools/|MCP/Core/CommandExecutor)'; then
    TRIGGER="TOOLS_CHANGE"
  fi
fi

# --- 無觸發 → 正常通過 ---
if [ -z "$TRIGGER" ]; then
  exit 0
fi

# --- 有觸發 → 注入雙向驗證指令 ---
TRIGGER_LABELS='{"MERGE_EXTERNAL":"合併外部 PR","SKILL_CHANGE":"Domain 升級 Skill / Skill 異動","TOOLS_CHANGE":"Tools 定義異動"}'
LABEL=$(echo "$TRIGGER_LABELS" | jq -r ".${TRIGGER}")

jq -n \
  --arg trigger "$TRIGGER" \
  --arg label "$LABEL" \
  '{
    hookSpecificOutput: {
      hookEventName: "PostToolUse",
      additionalContext: (
        "⚠️ CLAUDE.md 同步檢查觸發（事件：" + $label + "）\n\n" +
        "你剛完成的操作可能影響 CLAUDE.md 的正確性。\n" +
        "請立即執行 CLAUDE.md 雙向驗證流程（/claude-md-sync）：\n\n" +
        "【正向驗證】作業結果 → CLAUDE.md\n" +
        "  1. Skills 表格數量和名稱是否正確\n" +
        "  2. Domain 觸發關鍵字表是否需要更新或刪除\n" +
        "  3. 工具數量（58→?）是否變化\n" +
        "  4. 檔案路徑引用是否仍然存在\n" +
        "  5. 架構描述是否仍然正確\n\n" +
        "【反向驗證】修正後的 CLAUDE.md → 回頭檢證作業結果\n" +
        "  6. 修正後的 CLAUDE.md 是否與作業結果矛盾\n" +
        "  7. Deployment Rules 是否阻止了剛才的操作\n" +
        "  8. Build/Deploy 流程是否因修正而需要更新\n\n" +
        "兩邊一致後才能結束此驗證。如有衝突，需反覆修正直到一致。"
      )
    }
  }'

exit 0
