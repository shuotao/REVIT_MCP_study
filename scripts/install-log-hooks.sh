#!/bin/bash
# install-log-hooks.sh — 一次性設定 git 使用 repo 內的 hooks 目錄
# 適用於 Mac / Linux / Windows Git-Bash

set -e

REPO_ROOT="$(git rev-parse --show-toplevel)"
cd "$REPO_ROOT"

# Make all hooks executable
chmod +x scripts/git-hooks/* 2>/dev/null || true

# Point git to repo-shared hooks directory
git config core.hooksPath scripts/git-hooks

CURRENT="$(git config core.hooksPath)"
echo "✅ Git hooks 已設定：$CURRENT"
echo "   下一次 commit 會自動 append 到 log/$(date +%Y-%m).md"
echo ""
echo "驗證方法：touch /tmp/test && git add /tmp/test（不會 commit，只測試路徑）"
echo "卸載方法：git config --unset core.hooksPath"
