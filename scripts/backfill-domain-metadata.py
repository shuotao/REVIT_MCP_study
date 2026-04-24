#!/usr/bin/env python3
"""
backfill-domain-metadata.py

為 domain/*.md 自動補齊 frontmatter metadata：
- 無 frontmatter 的檔：全新創建（符合 domain/frontmatter-standard.md 規範）
- 已有 frontmatter 的檔：跳過，提示月小聚手動檢視

Auto 欄位（從 git log 取）：
  - metadata.created（第一次 commit 時間）
  - metadata.updated（最新 commit 時間）
  - metadata.contributors（author 清單）

TODO 欄位（留空待月小聚）：
  - metadata.references, related, referenced_by, tags

用法：
  python3 scripts/backfill-domain-metadata.py
"""

import subprocess
import sys
from pathlib import Path


def get_repo_root() -> Path:
    result = subprocess.run(
        ["git", "rev-parse", "--show-toplevel"],
        capture_output=True, text=True, check=True,
    )
    return Path(result.stdout.strip())


def git_cmd(args, cwd):
    try:
        result = subprocess.run(args, capture_output=True, text=True, check=True, cwd=cwd)
        return result.stdout
    except subprocess.CalledProcessError:
        return ""


def git_first_commit_date(file_path: Path, repo_root: Path) -> str:
    rel = str(file_path.relative_to(repo_root))
    out = git_cmd(
        ["git", "log", "--follow", "--diff-filter=A", "--pretty=%ai", "--", rel],
        cwd=repo_root,
    )
    lines = [l for l in out.strip().split("\n") if l]
    if lines:
        return lines[-1].split(" ")[0]
    return ""


def git_last_commit_date(file_path: Path, repo_root: Path) -> str:
    rel = str(file_path.relative_to(repo_root))
    out = git_cmd(
        ["git", "log", "-1", "--follow", "--pretty=%ai", "--", rel],
        cwd=repo_root,
    )
    line = out.strip()
    if line:
        return line.split(" ")[0]
    return ""


def git_contributors(file_path: Path, repo_root: Path) -> list:
    rel = str(file_path.relative_to(repo_root))
    out = git_cmd(
        ["git", "log", "--follow", "--pretty=%an", "--", rel],
        cwd=repo_root,
    )
    return sorted(set(l for l in out.strip().split("\n") if l))


def has_frontmatter(text: str) -> bool:
    return text.startswith("---\n")


def yaml_escape(s: str) -> str:
    """Escape string for YAML double-quoted value."""
    return s.replace("\\", "\\\\").replace('"', '\\"')


def infer_description(body: str, name: str) -> str:
    """Extract first non-trivial prose line from body, limit 120 chars."""
    for line in body.split("\n"):
        stripped = line.strip()
        if not stripped:
            continue
        # Skip headers, blockquotes, tables, code fences, list markers, hr
        if stripped.startswith("#"):
            continue
        if stripped.startswith(">"):
            continue
        if stripped.startswith("|"):
            continue
        if stripped.startswith("```"):
            continue
        if stripped.startswith("---"):
            continue
        if stripped.startswith(("-", "*", "+")) and len(stripped) > 1 and stripped[1] == " ":
            continue
        if stripped[:2].isdigit() or (stripped[0].isdigit() and "." in stripped[:4]):
            continue
        # Found prose
        return stripped[:120] + ("..." if len(stripped) > 120 else "")
    return f"(TODO: 月小聚補描述) {name}"


def build_new_frontmatter(file_path: Path, repo_root: Path) -> str:
    name = file_path.stem
    body = file_path.read_text(encoding="utf-8")
    description = infer_description(body, name)

    created = git_first_commit_date(file_path, repo_root)
    updated = git_last_commit_date(file_path, repo_root)
    contribs = git_contributors(file_path, repo_root)

    lines = ["---"]
    lines.append(f"name: {name}")
    lines.append(f'description: "{yaml_escape(description)}"')
    lines.append("metadata:")
    lines.append('  version: "1.0"')
    if updated:
        lines.append(f'  updated: "{updated}"')
    if created:
        lines.append(f'  created: "{created}"')
    if contribs:
        lines.append("  contributors:")
        for c in contribs:
            lines.append(f'    - "{yaml_escape(c)}"')
    lines.append("  references: []  # TODO: 月小聚補法規條號或外部依據")
    lines.append("  related: []  # TODO: 月小聚補相關 domain（檔名）")
    lines.append("  referenced_by: []  # TODO: 月小聚補（被哪些 skill 引用）")
    lines.append("  tags: []  # TODO: 月小聚補分類關鍵字")
    lines.append("---")
    lines.append("")
    return "\n".join(lines) + "\n"


def process_file(file_path: Path, repo_root: Path) -> str:
    body = file_path.read_text(encoding="utf-8")

    if has_frontmatter(body):
        return "skipped"

    new_frontmatter = build_new_frontmatter(file_path, repo_root)
    new_content = new_frontmatter + body
    file_path.write_text(new_content, encoding="utf-8")
    return "processed"


def main():
    repo_root = get_repo_root()
    domain_dir = repo_root / "domain"

    if not domain_dir.is_dir():
        print(f"錯誤：{domain_dir} 不存在", file=sys.stderr)
        return 1

    files = sorted(domain_dir.glob("*.md"))
    # Skip meta files
    excluded = {"README.md", "frontmatter-standard.md"}
    files = [f for f in files if f.name not in excluded]

    print(f"掃描 {len(files)} 個 domain 檔（排除 {', '.join(sorted(excluded))}）\n")

    processed_files = []
    skipped_files = []

    for f in files:
        result = process_file(f, repo_root)
        if result == "processed":
            processed_files.append(f.name)
            print(f"  [DONE] {f.name}")
        elif result == "skipped":
            skipped_files.append(f.name)
            print(f"  [SKIP] {f.name} (已有 frontmatter)")

    print(f"\n完成：")
    print(f"  新增 frontmatter：{len(processed_files)} 個")
    print(f"  跳過（已有 frontmatter）：{len(skipped_files)} 個")

    if skipped_files:
        print(f"\n下列檔案已有部分 frontmatter，請月小聚時人工檢視是否需遷移到 metadata nested 結構：")
        for name in skipped_files:
            print(f"  - {name}")

    return 0


if __name__ == "__main__":
    sys.exit(main())
