#!/bin/bash
# UserPromptSubmit hook: 偵測 Revit/Excel 關鍵字，提醒 AI 先載入 deferred tool schema

INPUT=$(cat)
USER_MSG=$(echo "$INPUT" | jq -r '.user_message // empty')

# 比對關鍵字（不分大小寫）
if echo "$USER_MSG" | grep -qiE '(excel|匯入|import.*revit|drafting|legend|面積計算|revit.*tool|mcp.*revit)'; then
  jq -n '{
    hookSpecificOutput: {
      hookEventName: "UserPromptSubmit",
      additionalContext: "⚡ Deferred Tool 預載提醒：使用者訊息涉及 Revit/Excel 操作。在呼叫任何 mcp__revit-mcp__* 工具之前，你必須先執行 ToolSearch 載入 schema。例如：\n\nToolSearch select:mcp__revit-mcp__import_excel_to_drafting_views\n\n用 select: 語法精確指定工具名稱。不要用模糊關鍵字搜尋。若 ToolSearch 找不到才判定 MCP Server 沒連上。"
    }
  }'
fi

exit 0
