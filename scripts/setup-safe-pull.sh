#!/bin/bash
# setup-safe-pull.sh
# 一次性設定：讓 git pull 自動保護你的 Revit 2020 C# 程式碼
#
# 原理：
#   1. 建立一個 "keep-mine" merge driver（永遠保留你的版本）
#   2. 在 .git/info/attributes（本機設定，不會推到 GitHub）裡
#      標記哪些檔案要用這個 driver
#
# 設定後，你可以放心 git pull —
#   知識層（domain/, skills/, docs/）正常更新
#   C# 程式碼保持你的版本不動
#
# 使用方式：
#   cd your-fork-directory
#   bash scripts/setup-safe-pull.sh

set -e

echo "=============================="
echo "  Safe Pull 設定工具"
echo "  適用於 Revit 2020 使用者"
echo "=============================="
echo ""

# --- 1. 設定 keep-mine merge driver ---
echo "[步驟 1/3] 設定 keep-mine merge driver..."

git config merge.keep-mine.name "Always keep local version"
git config merge.keep-mine.driver "true"
# "true" 是 Unix 指令，永遠回傳成功（exit 0）
# 效果：合併時不做任何事，保留你的版本

echo "  ✓ merge driver 已設定"

# --- 2. 設定 .git/info/attributes ---
echo "[步驟 2/3] 標記受保護的檔案..."

mkdir -p .git/info

cat > .git/info/attributes << 'ATTRS'
# === Revit 2020 Safe Pull Protection ===
# 這些檔案在 git pull 時會自動保留你的版本
# 此設定只存在你的本機，不會推到 GitHub

# C# 程式碼（你有 Revit 2020 特定修改）
MCP/Core/CommandExecutor.cs merge=keep-mine
MCP/Core/Commands/CommandExecutor.CurtainWall.cs merge=keep-mine
MCP/Core/Commands/CommandExecutor.SmokeExhaust.cs merge=keep-mine
MCP/Core/RevitCompatibility.cs merge=keep-mine
MCP/Application.cs merge=keep-mine

# Build 設定（沒有 Release.R20）
MCP/RevitMCP.csproj merge=keep-mine
MCP/RevitMCP.addin merge=keep-mine
ATTRS

echo "  ✓ 以下檔案已標記為受保護："
echo "    - MCP/Core/CommandExecutor.cs"
echo "    - MCP/Core/Commands/CommandExecutor.CurtainWall.cs"
echo "    - MCP/Core/Commands/CommandExecutor.SmokeExhaust.cs"
echo "    - MCP/Core/RevitCompatibility.cs"
echo "    - MCP/Application.cs"
echo "    - MCP/RevitMCP.csproj"
echo "    - MCP/RevitMCP.addin"

# --- 3. 設定 upstream remote ---
echo "[步驟 3/3] 確認 upstream remote..."

if git remote | grep -q "^upstream$"; then
  echo "  ✓ upstream 已存在"
else
  git remote add upstream https://github.com/shuotao/REVIT_MCP_study.git
  echo "  ✓ upstream 已新增"
fi

echo ""
echo "=============================="
echo "  設定完成！"
echo "=============================="
echo ""
echo "現在你可以放心執行："
echo ""
echo "  git pull upstream main"
echo ""
echo "結果："
echo "  ✅ domain/, skills/, docs/, CLAUDE.md → 正常更新"
echo "  ✅ MCP-Server/src/tools/*.ts          → 正常更新"
echo "  🔒 CommandExecutor*.cs                → 保留你的版本"
echo "  🔒 RevitCompatibility.cs              → 保留你的版本"
echo "  🔒 RevitMCP.csproj                    → 保留你的版本"
echo ""
echo "如果想解除保護（例如你決定放棄 2020 支援）："
echo "  rm .git/info/attributes"
echo "  git config --unset merge.keep-mine.driver"
