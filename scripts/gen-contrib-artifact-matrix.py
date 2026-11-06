#!/usr/bin/env python3
"""Generate the lesleyliuke artifact-level contribution matrix from git facts.

Every mechanical field (tool list, commit sha/date, presence in main, dispatcher
case) is DERIVED, never typed. Curated fields (talk mapping) are marked
"curated": true so a reader knows which half is machine-checked.
"""
import json, subprocess, re, os, datetime, pathlib

REPO = str(pathlib.Path(__file__).resolve().parent.parent)
FORK = "lesley/main"
os.chdir(REPO)

def sh(*a):
    return subprocess.run(a, capture_output=True, text=True).stdout

MB = sh("git", "merge-base", FORK, "main").strip()

def tools_in(ref):
    out = {}
    files = [f for f in sh("git","ls-tree","-r","--name-only",ref,"--","MCP-Server/src/tools/").split() if f.endswith(".ts")]
    for f in files:
        src = sh("git","show",f"{ref}:{f}")
        for m in re.finditer(r'name:\s*"([a-z0-9_]+)"', src):
            out.setdefault(m.group(1), os.path.basename(f))
    return out

t_fork, t_base, t_main = tools_in(FORK), tools_in(MB), tools_in("main")
new_tools = sorted(set(t_fork) - set(t_base))

# dispatcher case labels present in main's C# sources
cs = ""
for p in ["MCP/Core/CommandExecutor.cs"] + sorted(pathlib.Path("MCP/Core/Commands").glob("*.cs")):
    cs += pathlib.Path(p).read_text(encoding="utf-8", errors="replace")
cs = re.sub(r'//.*?$|/\*.*?\*/', '', cs, flags=re.S | re.M)
cases = set(re.findall(r'case\s+"([a-z0-9_]+)"', cs))

def first_commit(tool):
    line = sh("git","log","--reverse","--format=%h|%ad|%s","--date=short",
              "-S", f'"{tool}"', f"{MB}..{FORK}", "--", "MCP-Server/src/tools/").splitlines()
    return line[0].split("|") if line else ["", "", ""]

# ---- curated: which part of the 2026-08-29 talk each tool answers ----
# keys are tools; value = the pain point 劉可 described on stage.
TALK = {
 "import_excel_to_drafting_views": "外部 HTML 算完的表格要回到 Revit 建照圖說（她的第 2 步「形成對應的表格，貼入 Revit 建照圖說」）",
 "read_excel_tables": "同上，純讀取 Excel 結構不建 view 時使用",
 "create_legends": "同上，需要 Legend View 而非 Drafting View 時使用",
 "scale_drafting_view_width": "彎路四：DXF 文字大小與 Revit 不一致，文字跑出表格外 → 欄寬縮放",
 "scale_drafting_view_height": "彎路四同上 → 行高縮放",
 "move_text_notes_in_views": "彎路四同上 → 匯入後文字位置微調，避免在 Revit 重新排版",
 "set_category_visibility": "「把模型設置成 halftone 顯示模式」，只留面積線與牆心線做視覺比對",
 "hide_elements": "同上，檢核面積線時把干擾元件關掉",
 "unhide_elements": "同上，檢核完還原",
 "get_types_by_category": "halftone／材質批次改色前的型別盤點",
 "batch_set_material": "把底圖壓成淡色、面積線留亮色的批次材質手段",
 "assign_existing_material": "上一步的復原路徑",
 "auto_dimension_walls": "面積分割圖的牆心線批次標註（她的第 1 步「把面積的牆心線畫成分割圖」）",
 "unjoin_element_joins": "牆心線要乾淨，接合幾何會讓分割線判讀失準",
 "unjoin_column_joins": "同上，柱牆接合的解除",
 "copy_sheets_from_file": "一案 60 幾筆建照、每棟面寬以 0.5 米級距變化 → 跨專案複製整套圖紙",
 "read_source_file_sheets": "上一步的前置讀取與衝突確認",
 "sync_sheet_parameters_from_source": "上一步的補丁：補齊圖集／繪圖人等 sheet metadata",
 "create_floor_plans_from_template": "連棟透天每戶都要同一套平面圖系列 → 依範本批次建立",
 "batch_apply_view_template": "60 幾筆建照要長得一模一樣 → 批次統一視圖樣板",
 "align_titleblocks_on_sheets": "批次出圖時圖框位置必須一致，否則印出來對不齊",
 "position_viewports_on_sheet": "同上，viewport 在圖框中的定位",
 "arrange_viewports_on_sheet": "同上，多個 viewport 的排列",
 "move_viewport_titles": "同上，視埠標題位置",
 "debug_viewport_geometry": "上述對齊出錯時的幾何診斷",
 "get_sheet_viewport_details": "對齊前的現況盤點",
 "set_scope_box_for_views": "面積分割圖的裁剪範圍要跨戶一致",
 "align_view_cropbox_to_element": "把 view 的裁剪框對齊到圖框",
 "shift_view_cropbox": "上一步的微調",
 "copy_detail_items_to_views": "標準節點／表格附註一次散佈到整套建照圖說",
 "dedup_detail_elements_in_view": "防呆：重複貼入的詳圖元件會讓人誤判，先清乾淨再檢核",
 "batch_set_room_height": "房間資料整備（面積系統的上游）",
}

people = {
 "real_name": "劉可 (Liu Ke)",
 "real_name_confidence": "SELF-DECLARED",
 "github_handle": "lesleyliuke",
 "fork_url": "https://github.com/lesleyliuke/REVIT_MCP_study",
 "affiliation": "THINKLAB 之物建築事務所（高雄／紐約）",
 "affiliation_source": "2026-08-29 MCP8 分享自述（sessions/2026-08-29_mcp8-02-liu-ke/context.txt）",
}

tools = []
for t in new_tools:
    sha, date, subj = first_commit(t)
    tools.append({
        "tool": t,
        "module": t_fork[t],
        "introduced_commit": sha,
        "introduced_date": date,
        "introduced_subject": subj,
        "in_main": t in t_main,
        "dispatcher_case_in_main": t in cases,
        "status": "harvested" if t in t_main else "NOT_HARVESTED",
        "talk_2026_08_29_mapping": TALK.get(t),
        "talk_mapping_curated": True,
    })

# ---- skills ----
sk_status = sh("git","diff","--name-status",MB,FORK,"--",".claude/skills/")
skills = {}
for line in sk_status.splitlines():
    st, path = line.split("\t", 1)
    name = path.split("/")[2]
    skills.setdefault(name, {"name": name, "change": set(), "files": []})
    skills[name]["change"].add(st)
    skills[name]["files"].append(path)
skill_list = []
for name, s in sorted(skills.items()):
    present = os.path.isfile(f".claude/skills/{name}/SKILL.md")
    desc = None
    if present:
        txt = pathlib.Path(f".claude/skills/{name}/SKILL.md").read_text(encoding="utf-8", errors="replace")
        m = re.search(r'^description:\s*(.+)$', txt, re.M)
        if m: desc = m.group(1).strip().strip('"')[:400]
    skill_list.append({
        "skill": name,
        "fork_change": "added" if "A" in s["change"] else "modified",
        "files_in_fork": sorted(s["files"]),
        "in_main": present,
        "status": "harvested" if present else "NOT_HARVESTED",
        "description_in_main": desc,
    })

# ---- domain ----
dom_status = sh("git","diff","--name-status",MB,FORK,"--","domain/")
domain_list = []
for line in dom_status.splitlines():
    st, path = line.split("\t", 1)
    domain_list.append({
        "file": path,
        "fork_change": "added" if st == "A" else "modified",
        "in_main": os.path.isfile(path),
        "status": "harvested" if os.path.isfile(path) else "NOT_HARVESTED",
    })

# ---- other artifacts ----
other = []
for p in ["MCP/Core/SilentFailuresPreprocessor.cs","MCP/Core/TransactionHelper.cs",
          "MCP/Core/WarningSwallower.cs","MCP-Server/scripts/sketch_preview.html",
          "MCP-Server/scripts/sketch_preview_server.cjs",
          "issues/claude-md-wrong-build-output-path.md"]:
    other.append({"file": p, "in_main": os.path.exists(p),
                  "status": "harvested" if os.path.exists(p) else "NOT_HARVESTED"})

commits = []
for line in sh("git","log","--no-merges","--format=%h|%ad|%an|%ae|%s","--date=short",f"{MB}..{FORK}").splitlines():
    h,d,an,ae,s = line.split("|",4)
    commits.append({"sha":h,"date":d,"author_name":an,"author_email":ae,"subject":s})

doc = {
 "$schema_note": "Artifact-level contribution matrix. Companion to docs/fork-audit/contributor-matrix.json (person-level). This file is the REFERENCE TEMPLATE shape for auditing any single contributor's fork against main.",
 "template_version": "1.0",
 "generated_at": datetime.datetime.now().astimezone().isoformat(timespec="seconds"),
 "generated_by": "scripts/gen-contrib-artifact-matrix.py (see docs/fork-audit/README for regeneration)",
 "derivation_note": (
   "Mechanical fields are derived, not typed: tool names come from `name: \"...\"` literals in every "
   "MCP-Server/src/tools/*.ts of each tree; `in_main` is a filesystem/tree lookup against main HEAD; "
   "`dispatcher_case_in_main` greps `case \"<tool>\"` across MCP/Core/CommandExecutor.cs plus globbed "
   "MCP/Core/Commands/*.cs with comments stripped (same rule as verify-qaqc.ps1 check 7-15). "
   "Only fields flagged `talk_mapping_curated` are human judgement."
 ),
 "comparison_basis": {
   "fork_ref": FORK,
   "fork_head": sh("git","rev-parse","--short",FORK).strip(),
   "fork_head_date": sh("git","log","-1","--format=%ad","--date=short",FORK).strip(),
   "main_head": sh("git","rev-parse","--short","main").strip(),
   "merge_base": MB[:7],
   "merge_base_date": sh("git","log","-1","--format=%ad","--date=short",MB).strip(),
   "commits_ahead": len(commits),
 },
 "person": people,
 "commits": commits,
 "summary": {},
 "tools": tools,
 "skills": skill_list,
 "domain_files": domain_list,
 "other_artifacts": other,
}

nt = len(tools); nth = sum(1 for x in tools if x["in_main"])
ns = len(skill_list); nsh = sum(1 for x in skill_list if x["in_main"])
nd = len(domain_list); ndh = sum(1 for x in domain_list if x["in_main"])
doc["summary"] = {
  "tools": {"total": nt, "harvested": nth, "not_harvested": nt-nth,
            "not_harvested_names": [x["tool"] for x in tools if not x["in_main"]]},
  "skills": {"total": ns, "harvested": nsh, "not_harvested": ns-nsh,
             "not_harvested_names": [x["skill"] for x in skill_list if not x["in_main"]]},
  "domain_files": {"total": nd, "harvested": ndh, "not_harvested": nd-ndh,
                   "not_harvested_names": [x["file"] for x in domain_list if not x["in_main"]]},
  "other_artifacts": {"total": len(other),
                      "not_harvested_names": [x["file"] for x in other if not x["in_main"]]},
  "verdict": None,
}

out = "docs/fork-audit/lesleyliuke-artifact-matrix.json"
pathlib.Path(out).write_text(json.dumps(doc, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print("wrote", out)
print(json.dumps(doc["summary"], ensure_ascii=False, indent=2))
