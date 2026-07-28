#!/bin/bash
# detect-registry-trigger.sh
# PostToolUse hook: 偵測 MCP Registry「主要檔案」異動，注入一致性同步指令
#
# 主要檔案（任一異動即觸發）：
#   - server.json（repo root）
#   - MCP-Server/package.json
#   - scripts/schemas/server.schema.json
#   - .github/workflows/publish-mcp.yml
#   - docs/MCP_REGISTRY_PUBLISH.md
#
# 觸發後提醒：依序 mcp-registry-sync → mcp-registry-ops-inspect（兩支皆 Sonnet），
# 並跑 scripts/validate_publish_consistency.py 作硬閘門確認。

INPUT=$(cat)
TOOL_NAME=$(echo "$INPUT" | jq -r '.tool_name // empty')
TRIGGER=""

# 主要檔案的路徑樣式（Write/Edit 的 file_path，或 Bash 指令字串內出現）
REGISTRY_RE='(^|/)server\.json|MCP-Server/package\.json|scripts/schemas/server\.schema\.json|\.github/workflows/publish-mcp\.yml|docs/MCP_REGISTRY_PUBLISH\.md'

# --- 偵測檔案寫入/編輯 ---
if [ "$TOOL_NAME" = "Write" ] || [ "$TOOL_NAME" = "Edit" ]; then
  FILE_PATH=$(echo "$INPUT" | jq -r '.tool_input.file_path // empty')
  if echo "$FILE_PATH" | grep -qE "$REGISTRY_RE"; then
    TRIGGER="REGISTRY_MAIN_FILE"
  fi
fi

# --- 偵測 Bash 指令改到主要檔案（jq/sed/mv/npm version/git tag v*） ---
if [ "$TOOL_NAME" = "Bash" ]; then
  COMMAND=$(echo "$INPUT" | jq -r '.tool_input.command // empty')
  if echo "$COMMAND" | grep -qE "$REGISTRY_RE"; then
    TRIGGER="REGISTRY_MAIN_FILE"
  fi
  # 打 v* tag（發佈動作）也提醒先確認一致性
  if echo "$COMMAND" | grep -qE 'git\s+tag\s+v[0-9]'; then
    TRIGGER="REGISTRY_RELEASE_TAG"
  fi
fi

# --- 無觸發 → 正常通過 ---
if [ -z "$TRIGGER" ]; then
  exit 0
fi

TRIGGER_LABELS='{"REGISTRY_MAIN_FILE":"Registry 主要檔案異動","REGISTRY_RELEASE_TAG":"打 v* 發佈 tag"}'
LABEL=$(echo "$TRIGGER_LABELS" | jq -r ".${TRIGGER}")

jq -n \
  --arg trigger "$TRIGGER" \
  --arg label "$LABEL" \
  '{
    hookSpecificOutput: {
      hookEventName: "PostToolUse",
      additionalContext: (
        "⚠️ MCP Registry 一致性檢查觸發（事件：" + $label + "）\n\n" +
        "你剛動到 registry 發佈的主要檔案。請依 CLAUDE.md §「MCP Registry Publish Consistency」執行同步回圈：\n\n" +
        "  1. 呼叫 mcp-registry-sync（Sonnet）→ 修正 server.json / package.json / README registry 段 / playbook 的漂移（3 處版本平價、名稱、URL）。\n" +
        "  2. 呼叫 mcp-registry-ops-inspect（Sonnet）→ 唯讀稽核並用繁體中文回報一致/漂移。\n" +
        "  3. 跑 `python3 scripts/validate_publish_consistency.py` 作硬閘門，必須 exit 0。\n\n" +
        "鐵則：永不回退版本、永不手動 npm publish（發佈只走打 v* tag）。\n" +
        "完成後用繁體中文告訴老師這次更新了什麼。"
      )
    }
  }'

exit 0
