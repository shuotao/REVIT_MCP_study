#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""從 .claude/skills/*/SKILL.md（Skill 集合的唯一權威）＋ scripts/skills-index-map.json（人工策展的站內文案）
生成 docs/BIM_MCP/reference/skills-index.html。

為什麼要生成：skills-index 是 BIM_MCP 站上「Skill 一個不漏」的那一頁，但它過去是手寫的；
QA/QC 7-10 只能比對卡片的名稱集合，而且在 macOS 上因為字面反斜線路徑讀不到頁面（盲的），
又因為正規式 [a-z0-9-]+ 看不到 GM_* 七張卡。本頁一律由本腳本輸出，閘門 7-18 以 --check 逐位元組比對。

策展（scripts/skills-index-map.json）只放「人寫的字」：
  groups      用途群組 A–G：id、完整標題、速查表用的短標籤
  skills      每個 Skill 的群組、中文描述、觸發關鍵字、對應 Domain（卡片順序＝群組內的 key 順序）
              domains 項目：Domain 檔名（須存在於 domain-index.html 的 id=）、"xxx.html"（站內頁）、
              或 {"to": ..., "label": ...}（顯示文字與目標不同）
  quickOrder  速查表的列順序；每個 Skill 的 quick.purpose / quick.domain 是速查表兩欄
數量（卡片數、群組小計）一律由 map 推導，不手打。

拒絕生成（並點名）：Skill 目錄集合 ≠ map 的 key 集合、domains 項目在 domain-index.html 找不到 id、
群組為空或 map 引用未定義群組、quickOrder 與 skills 不一致。

用法：
  python3 scripts/gen-skills-index.py            # 重產頁面
  python3 scripts/gen-skills-index.py --check    # 不寫檔；頁面與生成結果不一致時 exit 1
"""
import html
import json
import os
import re
import sys

# 非 UTF-8 主控台（例如英文語系 Windows 的 cp1252）輸出中文會 UnicodeEncodeError，
# 讓 7-18 在頁面其實一致時誤判 FAIL；比照 validate_publish_consistency.py 強制 UTF-8，無法 reconfigure 時略過。
for _s in (sys.stdout, sys.stderr):
    try:
        _s.reconfigure(encoding="utf-8")
    except Exception:
        pass

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "docs", "BIM_MCP", "reference", "skills-index.html")
MAP = os.path.join(ROOT, "scripts", "skills-index-map.json")
SKILLS_DIR = os.path.join(ROOT, ".claude", "skills")
REF_DIR = os.path.join(ROOT, "docs", "BIM_MCP", "reference")
DOMAIN_INDEX = os.path.join(REF_DIR, "domain-index.html")


def esc(s):
    return html.escape(s, quote=False)


def load_and_validate():
    with open(MAP, encoding="utf-8") as f:
        m = json.load(f)
    problems = []
    dirs = set()
    if os.path.isdir(SKILLS_DIR):
        for d in os.listdir(SKILLS_DIR):
            if os.path.isfile(os.path.join(SKILLS_DIR, d, "SKILL.md")):
                dirs.add(d)
    else:
        problems.append(".claude/skills 目錄不存在")
    keys = set(m["skills"])
    for d in sorted(dirs - keys):
        problems.append(f"Skill 目錄 {d} 沒有出現在 skills-index-map.json（請補策展資料）")
    for d in sorted(keys - dirs):
        problems.append(f"map 內的 Skill {d} 沒有對應的 .claude/skills/{d}/SKILL.md")
    gids = [g["id"] for g in m["groups"]]
    for g in m["groups"]:
        if not any(s["group"] == g["id"] for s in m["skills"].values()):
            problems.append(f"群組 {g['id']} 是空的")
    for name, s in m["skills"].items():
        if s["group"] not in gids:
            problems.append(f"{name} 指向未定義的群組 {s['group']}")
        if "quick" not in s:
            problems.append(f"{name} 缺 quick（速查表欄位）")
    if sorted(m["quickOrder"]) != sorted(keys) or len(set(m["quickOrder"])) != len(m["quickOrder"]):
        problems.append("quickOrder 與 skills 的 key 集合不一致（缺漏或重複）")
    try:
        with open(DOMAIN_INDEX, encoding="utf-8") as f:
            dom_ids = set(re.findall(r'\bid="([^"]+)"', f.read()))
    except OSError:
        dom_ids = set()
        problems.append("讀不到 domain-index.html")
    for name, s in m["skills"].items():
        for d in s["domains"]:
            to = d["to"] if isinstance(d, dict) else d
            if to.endswith(".html"):
                if not os.path.isfile(os.path.join(REF_DIR, to)):
                    problems.append(f"{name} 的站內頁 {to} 不存在")
            elif to not in dom_ids:
                problems.append(f"{name} 的 domain「{to}」在 domain-index.html 找不到 id")
    if problems:
        sys.exit("[FAIL] 無法生成：\n  " + "\n  ".join(problems))
    return m


HEAD = """<!DOCTYPE html>
<html lang="zh-TW">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width, initial-scale=1.0">
<script>try{if(localStorage.getItem("bim-theme")==="light")document.documentElement.setAttribute("data-theme","light")}catch(e){}</script>
<title>Skills 索引（@@N@@ 個）· BIM_MCP</title>
<meta property="og:title" content="RevitMCP Skills 索引：@@N@@ 個編排層工作流">
<meta property="og:description" content="@@N@@ 個 Skill × 描述 × 觸發關鍵字 × 對應 Domain 表。Skill 是編排（何時/順序），Domain 是知識（法規/SOP）。從 CLAUDE.md 抽出。">
<link rel="preconnect" href="https://fonts.googleapis.com">
<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
<link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=JetBrains+Mono:wght@400;500;700&family=Noto+Sans+TC:wght@300;400;600;700;800&display=swap">
<link rel="stylesheet" href="../styles.css">
<link rel="stylesheet" href="../nav.css">
<style>
  .skill-card {
    background: var(--bg-card);
    border: 1px solid var(--border);
    border-radius: var(--radius-md);
    padding: 18px 22px;
    margin-bottom: var(--gap-sm);
    display: grid;
    grid-template-columns: 239px 1fr;
    gap: 18px;
    align-items: start;
    transition: border-color 0.3s, transform 0.3s;
  }
  .skill-card:hover {
    border-color: var(--accent-border);
    transform: translateX(4px);
  }
  .skill-name {
    font-family: var(--font-tech);
    font-size: 15px;
    color: var(--accent);
    font-weight: 700;
    letter-spacing: 0.3px;
  }
  .skill-cat {
    display: inline-block;
    font-family: var(--font-tech);
    font-size: 9px;
    letter-spacing: 1.5px;
    text-transform: uppercase;
    color: var(--text-faint);
    background: var(--tint-1);
    padding: 2px 8px;
    border-radius: var(--radius-sm);
    margin-top: 6px;
  }
  .skill-desc {
    font-size: 14px;
    color: var(--text-secondary);
    line-height: 1.6;
    margin-bottom: 10px;
  }
  .skill-meta {
    display: flex;
    flex-wrap: wrap;
    gap: 6px;
    margin-top: 8px;
  }
  .trigger-tag {
    font-size: 11px;
    color: var(--warn);
    background: var(--warn-bg);
    border: 1px dashed var(--warn-border);
    padding: 2px 8px;
    border-radius: var(--radius-sm);
    font-family: var(--font-tech);
  }
  .domain-link {
    font-size: 11px;
    color: var(--accent);
    background: var(--accent-bg);
    border: 1px solid var(--accent-border);
    padding: 2px 8px;
    border-radius: var(--radius-sm);
    font-family: var(--font-tech);
    border-bottom: none;
  }
  .domain-link:hover { background: var(--accent-bg-strong); }

  .cat-header {
    grid-column: 1 / -1;
    font-family: var(--font-tech);
    font-size: 11px;
    letter-spacing: 3px;
    text-transform: uppercase;
    color: var(--accent);
    padding: var(--gap-md) 0 var(--gap-xs) 0;
    border-bottom: 1px solid var(--border);
    margin-top: var(--gap-md);
    margin-bottom: 0;
    font-weight: 400;
  }

  @media (max-width: 900px) {
    .skill-card {
      grid-template-columns: 1fr;
      gap: 8px;
    }
  }
</style>
</head>
<body>

<nav class="bim-topnav">
  <div class="bim-topnav-left">
    <a href="../index.html" class="bim-topnav-home">BIM_MCP</a>
    <span class="bim-topnav-sep">/</span>
    <a href="../index.html#reference" style="color:var(--text-secondary);border-bottom:none;">reference</a>
    <span class="bim-topnav-sep">/</span>
    <span class="bim-topnav-current">Skills 索引</span>
  </div>
  <div class="bim-topnav-actions" data-route-nav="top">
    <a href="https://github.com/shuotao/REVIT_MCP_study" target="_blank" rel="noopener" class="bim-topnav-btn bim-topnav-btn-github" title="View source on GitHub"><svg viewBox="0 0 16 16" aria-hidden="true"><path d="M8 0C3.58 0 0 3.58 0 8c0 3.54 2.29 6.53 5.47 7.59.4.07.55-.17.55-.38 0-.19-.01-.82-.01-1.49-2.01.37-2.53-.49-2.69-.94-.09-.23-.48-.94-.82-1.13-.28-.15-.68-.52-.01-.53.63-.01 1.08.58 1.23.82.72 1.21 1.87.87 2.33.66.07-.52.28-.87.51-1.07-1.78-.2-3.64-.89-3.64-3.95 0-.87.31-1.59.82-2.15-.08-.2-.36-1.02.08-2.12 0 0 .67-.21 2.2.82.64-.18 1.32-.27 2-.27.68 0 1.36.09 2 .27 1.53-1.04 2.2-.82 2.2-.82.44 1.1.16 1.92.08 2.12.51.56.82 1.27.82 2.15 0 3.07-1.87 3.75-3.65 3.95.29.25.54.73.54 1.48 0 1.07-.01 1.93-.01 2.2 0 .21.15.46.55.38A8.012 8.012 0 0 0 16 8c0-4.42-3.58-8-8-8z"/></svg>GitHub</a>
    <a href="philosophy-22-propositions.html" class="bim-topnav-btn">22 命題</a>
  </div>
</nav>

<header class="bim-hero">
  <div class="bim-hero-eyebrow">SKILLS INDEX <span class="accent">@@N@@ 個</span></div>
  <h1 class="bim-hero-title">Skills · <span class="accent">編排層</span><br>何時觸發 / 什麼順序</h1>
  <p class="bim-hero-sub">Skills 是<strong>編排</strong>（orchestration），不是知識本身。一個 Skill 規定「<em>什麼關鍵字觸發 / 呼叫哪些工具 / 套用哪個 Domain SOP</em>」。Skill metadata 永遠常駐於 AI Client context；body 與 Domain 被引用時才載入。本頁列 @@N@@ 個 Skill 的描述、觸發關鍵字與對應 Domain。</p>
</header>

<section data-collapse class="bim-section">
  <div class="bim-section-eyebrow">FRAME · 三層架構</div>
  <h2 class="bim-section-title">Skill / Domain / Tool 三者分工</h2>
  <div class="bim-grid bim-grid-3">
    <div class="bim-card">
      <h4>Skill（編排）</h4>
      <p style="font-size:13px;">何時觸發 + 呼叫哪些 tool + 什麼順序。@@N@@ 個。</p>
    </div>
    <div class="bim-card">
      <h4><a href="domain-index.html" style="border-bottom:1px solid var(--accent-border);">Domain（知識）</a></h4>
      <p style="font-size:13px;">法規條文、SOP、計算公式。94 個。被 Skill 引用。</p>
    </div>
    <div class="bim-card">
      <h4>Tool（執行）</h4>
      <p style="font-size:13px;">原子操作（query / create / modify）。239 個 MCP tools。</p>
    </div>
  </div>
  <p class="bim-section-desc" style="margin-top:var(--gap-md);">「<strong>不要把每個 Domain 都升級成 Skill</strong>」——這是本專案的核心架構原則。詳見 <a href="philosophy-22-propositions.html">22 命題</a>。</p>
  <p class="bim-section-desc">完整三層定義見 <a href="architecture-v2.html">三層架構 V2</a>。</p>
</section>

<!-- ============================================ -->
<!-- @@N@@ 個 Skill 列表 -->
<!-- ============================================ -->
<section data-collapse class="bim-section bim-anchor" id="skills" data-search-root>
  <div class="bim-section-eyebrow">CATALOG · @@N@@ 個 Skill</div>
  <h2 class="bim-section-title">@@N@@ 個 Skill · 依用途分組</h2>

  <div class="bim-search">
    <input type="search" data-search-input placeholder="搜尋 Skill：名稱、觸發關鍵字、用途、Domain" aria-label="搜尋 Skill">
    <div class="bim-search-status" data-search-status aria-live="polite"></div>
    <button type="button" class="bim-footer-btn" data-search-clear>清除</button>
  </div>

"""

MID = """</section>

<!-- ============================================ -->
<!-- 一張表（壓縮版） -->
<!-- ============================================ -->
<section data-collapse class="bim-section bim-anchor" id="quick-table">
  <div class="bim-section-eyebrow">QUICK TABLE · 速查表</div>
  <h2 class="bim-section-title">速查：@@N@@ Skill 一覽</h2>
  <p class="bim-section-desc">複製給合作對象 / 列印貼牆。</p>

  <figure class="bim-figure">
    <img src="../images/skills__hero__grains.svg" alt="@@N@@ 個 Skill 依法規、流程、維運、MEP、外部五色排成方塊陣列" width="1200" height="966" loading="lazy">
  <figcaption>@@N@@ 個方塊各代表一個 Skill，依五色分類。</figcaption>
  </figure>
  <p class="bim-section-desc">圖中五色是跨層分類（法規／流程／維運／MEP／外部），與本頁 A–G 用途群組不是一對一；對應：A→法規；B、C、D（非 MEP）、GM_import/inject/query/set→流程；E（非外部）、F（非外部）、GM_update/web/kanban→維運；detect-clashes、mep-settings-curation、set-project-units→MEP；hj-pr-proposal、archicad-skill-adapter→外部。</p>

  <table>
    <thead>
      <tr><th style="width:18%;">Skill</th><th style="width:12%;">分類</th><th style="width:50%;">用途</th><th style="width:20%;">主 Domain</th></tr>
    </thead>
    <tbody>
"""

TAIL = """    </tbody>
  </table>
</section>

<!-- ============================================ -->
<!-- 為什麼 Skill 不該等於 Domain -->
<!-- ============================================ -->
<section data-collapse class="bim-section bim-anchor" id="why-not-merge">
  <div class="bim-section-eyebrow">RATIONALE · 為什麼不每個 Domain 都升級成 Skill</div>
  <h2 class="bim-section-title">@@N@@ Skill vs 94 Domain — 為什麼數量不同？</h2>

  <div class="bim-grid bim-grid-2">
    <div class="bim-card bim-card-bad">
      <h4 style="color:var(--bad);">❌ 反例：把每個 Domain 都升 Skill</h4>
      <p>90 Skill metadata 全部常駐 → AI context window 被吃掉一大塊 → 真正需要時反而找不到。</p>
      <p style="font-size:13px;color:var(--text-muted);">且重複邏輯——防火 SOP 同時被消防、走廊、合規三個 Skill 引用，搬到各自 Skill body 內就重複三次。</p>
    </div>
    <div class="bim-card bim-card-ok">
      <h4 style="color:var(--ok);">✓ 本專案做法</h4>
      <p>Skill = 編排（@@N@@ 個 metadata 常駐）；Domain = 知識（94 markdown，被引用才載入）。</p>
      <p style="font-size:13px;color:var(--text-muted);">知識複用，編排薄層。一個防火 SOP 被三個 Skill 共享，原文只有一份。</p>
    </div>
  </div>

  <div class="how-tech">
    <div class="how-tech-label">原則</div>
    <div class="how-tech-content">Skill 加入門檻高：必須有<strong>跨多 tool 的編排價值</strong>且<strong>觸發頻率高到值得常駐 metadata</strong>。否則就讓它停在 Domain。</div>
    <div class="how-tech-label">TECHNIQUE</div>
    <div class="how-tech-content technique">Orchestration vs Knowledge Separation · Metadata Footprint Control · Knowledge Deduplication</div>
    <div class="how-tech-label">CHECKLIST</div>
    <div class="how-tech-content checklist">
      <ul>
        <li>新 Skill 是否協調 ≥ 2 個 tool？單一 tool 通常不必開 Skill。</li>
        <li>觸發關鍵字是否高頻？冷門關鍵字停在 Domain 即可。</li>
        <li>是否引用既有 Domain？不寫新知識，寫新編排。</li>
      </ul>
    </div>
  </div>
</section>

<footer class="bim-footer">
  <div class="bim-footer-actions" data-route-nav="foot">
    <a href="philosophy-22-propositions.html" class="bim-footer-btn">22 命題</a>
    <a href="../index.html" class="bim-footer-btn">回首頁</a>
  </div>
  <p>BIM_MCP · reference / skills-index</p>
  <p style="font-size:11px;color:var(--text-faint);margin-top:6px;">最後更新 2026-10-23 · 從 CLAUDE.md 抽取 @@N@@ 個 Skill 與其觸發關鍵字、Domain 對應。</p>
</footer>

<script src="../shared.js"></script>
<script>initAll();</script>
</body>
</html>
"""


def render_card(name, s, g):
    L = g["id"]
    out = [
        f'  <div class="skill-card" id="skill-{name}" data-search-item data-search-in="{L}" data-search-keys="{L} {esc(g["title"])}">',
        "    <div>",
        f'      <div class="skill-name">/{name}</div>',
        f'      <div class="skill-cat">{L} · {esc(g["title"])}</div>',
        "    </div>",
        "    <div>",
        f'      <div class="skill-desc">{esc(s["desc"])}</div>',
        '      <div class="skill-meta">',
        "        " + "".join(f'<span class="trigger-tag">{esc(x)}</span>' for x in s["triggers"]),
    ]
    for d in s["domains"]:
        to, label = (d["to"], d["label"]) if isinstance(d, dict) else (d, None)
        if to.endswith(".html"):
            href, label = to, label or to[:-5]
        else:
            href, label = "domain-index.html#" + to, label or to
        out.append(f'        <a href="{href}" class="domain-link">→ {esc(label)}</a>')
    out += ["      </div>", "    </div>", "  </div>"]
    return "\n".join(out)


def render(m):
    n = len(m["skills"])
    gmap = {g["id"]: g for g in m["groups"]}
    parts = []
    for g in m["groups"]:
        names = [k for k, s in m["skills"].items() if s["group"] == g["id"]]
        parts.append(
            f'  <h3 class="cat-header" data-search-group="{g["id"]}">{g["id"]} · {esc(g["title"])}（{len(names)} 個）</h3>'
        )
        for k in names:
            parts.append(render_card(k, m["skills"][k], g))
    body = "\n\n".join(parts) + "\n"
    rows = []
    for k in m["quickOrder"]:
        s = m["skills"][k]
        g = gmap[s["group"]]
        rows.append(
            f'      <tr><td><code>/{k}</code></td><td>{g["id"]} {esc(g["short"])}</td>'
            f'<td>{esc(s["quick"]["purpose"])}</td><td>{esc(s["quick"]["domain"])}</td></tr>'
        )
    page = HEAD + body + MID + "\n".join(rows) + "\n" + TAIL
    return page.replace("@@N@@", str(n))


def main():
    page = render(load_and_validate())
    if "--check" in sys.argv:
        current = open(OUT, encoding="utf-8").read() if os.path.exists(OUT) else ""
        if current != page:
            print("[FAIL] skills-index.html 與生成結果不一致——執行 python3 scripts/gen-skills-index.py 重產（策展資料在 scripts/skills-index-map.json）")
            sys.exit(1)
        print("[OK] skills-index.html 與生成結果一致")
        return
    with open(OUT, "w", encoding="utf-8", newline="\n") as f:
        f.write(page)
    print(f"[OK] 生成 {os.path.relpath(OUT, ROOT)}")


if __name__ == "__main__":
    main()
