#!/bin/bash
# detect-loopup-calibration.sh
# PostToolUse hook: 偵測 /loop-up 的執行紀錄被追加，每滿 5 筆提醒執行校準輪
#
# 紀錄檔：.claude/skills/loop-up/runs.jsonl（每次 loop-up 執行 append 一筆）
#
# 為什麼需要這支 hook：
#   loop-up 的模型配比表（observer=Haiku / inspector-ops=Fable|Opus）目前建立在
#   風險論證上，缺乏實證。要驗證「inspector 夠不夠好」，需要獨立於 inspector 的
#   事實來源；但 human-out-of-the-loop 正好拿掉了那個來源 —— 跑再多次「inspector
#   說 pass」，對於「inspector 會不會誤判」的資訊量都是零。
#   解法是每 5 次做一輪雙稽核，記錄兩支不同模型的「歧異」而非「通過」。

INPUT=$(cat)
TOOL_NAME=$(echo "$INPUT" | jq -r '.tool_name // empty')
RUNS_RE='\.claude/skills/loop-up/runs\.jsonl'
TRIGGER=""

if [ "$TOOL_NAME" = "Write" ] || [ "$TOOL_NAME" = "Edit" ]; then
  FILE_PATH=$(echo "$INPUT" | jq -r '.tool_input.file_path // empty')
  if echo "$FILE_PATH" | grep -qE "$RUNS_RE"; then
    TRIGGER="RUNS_APPENDED"
  fi
fi

if [ "$TOOL_NAME" = "Bash" ]; then
  COMMAND=$(echo "$INPUT" | jq -r '.tool_input.command // empty')
  if echo "$COMMAND" | grep -qE "$RUNS_RE"; then
    TRIGGER="RUNS_APPENDED"
  fi
fi

if [ -z "$TRIGGER" ]; then
  exit 0
fi

RUNS_FILE="${CLAUDE_PROJECT_DIR:-.}/.claude/skills/loop-up/runs.jsonl"
if [ ! -f "$RUNS_FILE" ]; then
  exit 0
fi

# 只數非空行
COUNT=$(grep -cve '^[[:space:]]*$' "$RUNS_FILE" 2>/dev/null || echo 0)

if [ "$COUNT" -eq 0 ] || [ $((COUNT % 5)) -ne 0 ]; then
  # 未達校準點，安靜結束（不打擾）
  exit 0
fi

jq -n --arg count "$COUNT" \
  '{
    hookSpecificOutput: {
      hookEventName: "PostToolUse",
      additionalContext: (
        "📊 /loop-up 校準輪觸發（累計第 " + $count + " 次執行，每 5 次一輪）\n\n" +
        "下一次 /loop-up 必須是**校準輪**。校準輪不是多跑一次同樣的檢查 —— 那不會產生新資訊。\n\n" +
        "做法：挑本次至少一個 Stage，派**兩支不同模型的 inspector-ops 各自獨立稽核**（例如 Sonnet 與 Fable），\n" +
        "兩支都不得看到對方的結論。然後記錄它們是否給出相同 verdict：\n\n" +
        "  • 兩支結論一致 → 弱證據支持當前配比夠用；較弱那支或許可降級省成本\n" +
        "  • **兩支結論不同 → 這才是有價值的樣本**，記下歧異點與哪一支對\n\n" +
        "歧異率是在無人介入下唯一能逼近「inspector 可靠度」的訊號。長期為 0 代表可降級，\n" +
        "上升代表該升級。這樣配比調整才有實證基礎，而不是風險論證。\n\n" +
        "同時檢視 runs.jsonl 累計的**失敗分類**分布（規格不清 / 技術陷阱 / 模型能力不足 / 驗收條件矛盾）。\n" +
        "鐵則：**只有「模型能力不足」該用升級模型解決**；其餘三類升級模型無效，甚至會掩蓋真正的問題。\n\n" +
        "統計效力提醒：n=5、且各次任務性質不同，本來就不是同分布樣本。它給的是趨勢與異常訊號，\n" +
        "不是「誤判率 X%」這種數字 —— 不要用 5 筆資料生一張看起來精確的表。\n\n" +
        "校準結論請寫回 `.claude/skills/loop-up/SKILL.md` 的配比表，並註明依據的 run_id 與樣本數。"
      )
    }
  }'

exit 0
