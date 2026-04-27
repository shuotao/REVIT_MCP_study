#!/bin/bash
# backfill-domain-metadata.sh — wrapper for the Python backfill script
# 需要 Python 3 已安裝

set -e

REPO_ROOT="$(git rev-parse --show-toplevel)"
cd "$REPO_ROOT"

if ! command -v python3 &> /dev/null; then
  echo "錯誤：需要 python3，請先安裝" >&2
  exit 1
fi

python3 scripts/backfill-domain-metadata.py "$@"
