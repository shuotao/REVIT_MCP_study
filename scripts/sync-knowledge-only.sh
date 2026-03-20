#!/bin/bash
# sync-knowledge-only.sh
# 選擇性同步：只拉「知識層」更新，不碰 C# 程式碼
#
# 適用對象：使用非標準 Revit 版本（如 2020）的貢獻者
# 這些人的 C# 程式碼可能有版本特定的修改，不能直接 git pull
#
# 使用方式：
#   cd your-fork-directory
#   bash scripts/sync-knowledge-only.sh

set -e

UPSTREAM="upstream"
BRANCH="main"

# --- 確認 upstream remote 存在 ---
if ! git remote | grep -q "^${UPSTREAM}$"; then
  echo "設定 upstream remote..."
  git remote add upstream https://github.com/shuotao/REVIT_MCP_study.git
fi

echo "從 upstream 取得最新資料..."
git fetch upstream

# --- 安全目錄清單（版本無關）---
SAFE_PATHS=(
  "domain/"
  ".claude/skills/"
  ".claude/hooks/"
  ".claude/settings.json"
  "CLAUDE.md"
  "GEMINI.md"
  "docs/"
  "MCP-Server/src/tools/"
)

echo ""
echo "=============================="
echo "  選擇性同步：知識層更新"
echo "=============================="
echo ""
echo "以下目錄/檔案將從 upstream 同步："
for path in "${SAFE_PATHS[@]}"; do
  echo "  ✓ $path"
done
echo ""
echo "以下目錄將保持不動（你的版本特定修改）："
echo "  ✗ MCP/Core/*.cs（C# 程式碼）"
echo "  ✗ MCP/RevitMCP.csproj（Build 設定）"
echo "  ✗ MCP-Server/src/index.ts（如有修改）"
echo "  ✗ MCP-Server/src/socket.ts（如有修改）"
echo ""

read -p "確認同步？(y/N) " confirm
if [ "$confirm" != "y" ] && [ "$confirm" != "Y" ]; then
  echo "取消。"
  exit 0
fi

# --- 逐一同步 ---
for path in "${SAFE_PATHS[@]}"; do
  echo ""
  echo "同步 ${path}..."
  # 用 git checkout 從 upstream/main 拉取特定路徑
  git checkout "${UPSTREAM}/${BRANCH}" -- "$path" 2>/dev/null || {
    echo "  ⚠ ${path} 在 upstream 不存在或路徑有誤，跳過"
    continue
  }
  echo "  ✓ 完成"
done

echo ""
echo "=============================="
echo "  同步完成"
echo "=============================="
echo ""
echo "已更新的內容："
git diff --cached --stat
echo ""
echo "請檢查以上變更，然後："
echo "  git commit -m 'chore: sync knowledge layer from upstream'"
echo ""
echo "⚠ 注意：CLAUDE.md 裡的工具數量、Skill 數量等數字"
echo "  可能需要根據你的版本手動調整。"
