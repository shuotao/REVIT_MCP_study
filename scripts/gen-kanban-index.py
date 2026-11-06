#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""從 tools/kanban/cards/*.json 生成 docs/BIM_MCP/reference/kanban-index.html。

為什麼要生成而不是手寫：看板卡的資料已經有兩份內嵌拷貝（tools/kanban/index.html 與
tools/green-material/GM_kanban.html，兩者僅 localStorage key 不同）。手寫第三份 HTML 會讓
每次改卡都要同步三個地方。本頁一律重新生成，卡片 JSON 是唯一真實來源。

用法：python3 scripts/gen-kanban-index.py
QA/QC 閘門 7-16 會驗證本頁與卡片目錄一致（每張卡恰好一列、無孤兒列、連結目標存在）。
"""
import html
import json
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CARDS_DIR = os.path.join(ROOT, "tools", "kanban", "cards")
OUT = os.path.join(ROOT, "docs", "BIM_MCP", "reference", "kanban-index.html")

GH = "https://github.com/shuotao/REVIT_MCP_study/blob/main"
BOARD = "../../../tools/kanban/index.html"
TALK = "https://goodedunote.web.app/bim-mcp-8/session-1.html"

# 每張卡「產出了什麼」的對照。這一份是人工策展的，因為卡片內文不會逐張明寫它催生了哪份 domain。
# 每個路徑都會在生成時斷言存在——domain 改名或刪除會讓生成直接失敗，不會留死連結。
PRODUCED = {
    "TASK-001":     {"domain": ["domain/GM_catalog.md"]},
    "TASK-002":     {"file": ["assets/green-material-showcase.template.html"]},
    "TASK-003":     {"domain": ["domain/GM_parameter-schema.md"]},
    "TASK-004":     {"skill": ["GM_import"], "file": ["tools/green-material/GM_generate_revit_injection_plan.py"]},
    "TASK-005.1":   {"skill": ["GM_inject"], "domain": ["domain/GM_parameter-schema.md"]},
    "TASK-005.2":   {"skill": ["GM_inject"], "domain": ["domain/GM_parameter-schema.md"]},
    "TASK-005.3":   {"domain": ["domain/GM_parameter-schema.md"]},
    "TASK-005.4":   {"skill": ["GM_inject"], "domain": ["domain/GM_parameter-schema.md"]},
    "TASK-005.5":   {"skill": ["GM_inject", "GM_import"]},
    "TASK-005.6":   {"file": ["tools/green-material/GM_generate_revit_injection_plan.py"]},
    "TASK-005.7":   {"domain": ["domain/GM_rfa-family-injection.md"], "skill": ["GM_inject"]},
    "TASK-005.8":   {"skill": ["GM_inject"], "domain": ["domain/GM_parameter-schema.md"]},
    "TASK-005.10":  {"skill": ["GM_inject"]},
    "TASK-005.11":  {"skill": ["GM_inject"]},
    "TASK-005.12":  {"skill": ["GM_inject"], "domain": ["domain/GM_parameter-schema.md"]},
    "TASK-006":     {"skill": ["GM_query"]},
    "TASK-008":     {"file": [".agents/AGENTS.md", "ai/process/definition-of-done.md"]},
    "TASK-009":     {"skill": ["GM_import", "GM_inject"]},
    "TASK-010":     {"skill": ["GM_inject"]},
    "TASK-011":     {"skill": ["GM_inject"]},
    "TASK-013":     {"skill": ["GM_web"], "file": ["assets/green-material-showcase.template.html"]},
    "TASK-014":     {"file": ["tools/green-material/GM_generate_revit_injection_plan.py"],
                     "domain": ["domain/GM_parameter-schema.md"]},
    "TASK-015":     {"file": ["tools/green-material/GreenMaterial_SharedParams.txt"],
                     "domain": ["domain/GM_parameter-schema.md"]},
}

# 已知被後續 domain 修訂推翻／超出的卡片。這裡刻意人工維護：沒有任何機械方法能判斷
# 「一份活文件是否推翻了一張歷史紀錄」。QA/QC 7-16 會驗證這些標記真的出現在頁面上。
SUPERSEDED = {
    "TASK-005.7": (
        "卡片停在 2026-08-12 的認知。<code>domain/GM_rfa-family-injection.md</code> 已於 2026-08-31 "
        "升到 v1.1：當時記錄的「<code>GreenMaterial_Certified</code> 寫入失敗疑為 YesNo 型別限制」"
        "經追查<strong>並不成立</strong>，真因是 <code>SharedParametersFilename</code> 的生命週期 bug，"
        "修正後實測 0/12 → 12/12。原始觀察已在該 domain 保留為歷史紀錄。"
    ),
}

EPIC_ORDER = ["Epic-1", "Epic-2", "Epic-3", "Epic-4"]


def load_cards():
    cards = []
    for fn in sorted(os.listdir(CARDS_DIR)):
        if fn.endswith(".json"):
            cards.append(json.load(open(os.path.join(CARDS_DIR, fn), encoding="utf-8")))
    cards.sort(key=lambda c: (c.get("order", 0), c["id"]))
    return cards


def card_key(cid):
    """TASK-005.10 要排在 TASK-005.2 之後，不是字典序。"""
    return [int(x) for x in re.findall(r"\d+", cid)]


def extract_tools(content):
    known = ["load_shared_parameters", "set_green_material_type_parameters", "create_single_material_type",
             "create_multi_layer_type", "inject_green_material_into_family", "duplicate_element_type",
             "duplicate_type_only", "set_material_surface_pattern", "get_types_by_category",
             "assign_existing_material", "get_all_materials", "create_material", "create_green_material",
             "create_material_by_domain", "get_element_info"]
    return [t for t in known if t in content]


def extract_ids(content):
    ids = re.findall(r"(?:Element ID|TypeId|Type Id|MaterialId)\s*:?\s*(\d{3,6})", content)
    seen, out = set(), []
    for i in ids:
        if i not in seen:
            seen.add(i)
            out.append(i)
    return out[:6]


def esc(s):
    return html.escape(str(s), quote=True)


def assert_exists(rel):
    if not os.path.exists(os.path.join(ROOT, rel)):
        sys.exit(f"[FAIL] 連結目標不存在，拒絕產出死連結：{rel}")
    return rel


def produced_html(cid):
    spec = PRODUCED.get(cid, {})
    bits = []
    for d in spec.get("domain", []):
        assert_exists(d)
        bits.append(f'<a href="{GH}/{d}" target="_blank" rel="noopener"><code>{esc(os.path.basename(d))}</code></a>')
    for s in spec.get("skill", []):
        rel = f".claude/skills/{s}/SKILL.md"
        assert_exists(rel)
        bits.append(f'<a href="{GH}/{rel}" target="_blank" rel="noopener"><code>/{esc(s)}</code></a>')
    for f in spec.get("file", []):
        assert_exists(f)
        bits.append(f'<a href="{GH}/{f}" target="_blank" rel="noopener"><code>{esc(os.path.basename(f))}</code></a>')
    return "、".join(bits) if bits else '<span class="kb-none">—</span>'


def build():
    cards = load_cards()
    cards.sort(key=lambda c: card_key(c["id"]))
    by_epic = {}
    for c in cards:
        by_epic.setdefault(c.get("epic", "（未分類）"), []).append(c)

    epics = sorted(by_epic, key=lambda e: (EPIC_ORDER.index(e.split(":")[0]) if e.split(":")[0] in EPIC_ORDER else 99, e))
    total = len(cards)
    stages = sorted({c.get("stage") for c in cards})
    dates = sorted(c.get("createdAt", "") for c in cards if c.get("createdAt"))

    rows = []
    sec = 3
    for epic in epics:
        cs = by_epic[epic]
        rows.append(f'''
<section data-collapse class="bim-section bim-anchor" data-cat="workflow" id="{esc(epic.split(':')[0].lower())}">
  <div class="bim-section-eyebrow">SECTION {sec:02d} · {esc(epic.split(':')[0].upper())}</div>
  <h2 class="bim-section-title">{esc(epic)}（{len(cs)} 張）</h2>
  <div class="kb-table-wrap">
  <table class="kb-table">
    <thead><tr><th>卡號</th><th>標題</th><th>風險</th><th>當時做出了什麼</th><th>實測留下的 ID</th></tr></thead>
    <tbody>''')
        for c in cs:
            content = c.get("content", "")
            tools = extract_tools(content)
            ids = extract_ids(content)
            sup = SUPERSEDED.get(c["id"])
            rows.append(f'''
      <tr id="{esc(c['id'])}">
        <td class="kb-id"><a href="{GH}/tools/kanban/cards/{esc(c['id'])}.json" target="_blank" rel="noopener">{esc(c['id'])}</a></td>
        <td class="kb-title">{esc(c['title'])}
          {'<div class="kb-super"><strong>已被後續 domain 修訂超出：</strong>' + sup + '</div>' if sup else ''}
          {'<div class="kb-tools">' + "、".join(f"<code>{esc(t)}</code>" for t in tools) + '</div>' if tools else ''}
        </td>
        <td><span class="kb-risk kb-risk-{esc(c.get('risk','na'))}">{esc(c.get('risk','—'))}</span></td>
        <td class="kb-produced">{produced_html(c['id'])}</td>
        <td class="kb-ids">{"、".join(f"<code>{esc(i)}</code>" for i in ids) if ids else '<span class="kb-none">—</span>'}</td>
      </tr>''')
        rows.append("    </tbody>\n  </table>\n  </div>\n</section>\n")
        sec += 1

    page = f'''<!DOCTYPE html>
<html lang="zh-TW">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width, initial-scale=1.0">
<script>try{{if(localStorage.getItem("bim-theme")==="light")document.documentElement.setAttribute("data-theme","light")}}catch(e){{}}</script>
<title>看板卡索引（{total} 張）· BIM_MCP</title>
<meta property="og:title" content="BIM_MCP 看板卡索引 — 綠建材工具鏈的 {total} 張開發紀錄">
<meta property="og:description" content="林家維（@CWLin0518）開發綠建材 Revit 導入工具鏈時推完的 {total} 張看板卡。每張卡連到它產出的 domain／skill／工具，以及當時的實測記錄。">
<link rel="preconnect" href="https://fonts.googleapis.com">
<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
<link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=JetBrains+Mono:wght@400;500;700&family=Noto+Sans+TC:wght@300;400;600;700;800&display=swap">
<link rel="stylesheet" href="../styles.css">
<link rel="stylesheet" href="../nav.css">
<style>
  .kb-table-wrap{{overflow-x:auto;margin-top:var(--gap-md)}}
  .kb-table{{width:100%;border-collapse:collapse;font-size:13px;min-width:820px}}
  .kb-table th{{text-align:left;padding:10px 12px;border-bottom:2px solid var(--border);color:var(--text-secondary);font-size:12px;letter-spacing:.04em;white-space:nowrap}}
  .kb-table td{{padding:12px;border-bottom:1px solid var(--border);vertical-align:top}}
  .kb-table tr{{scroll-margin-top:130px}}
  .kb-id{{font-family:var(--font-tech);white-space:nowrap;font-size:12px}}
  .kb-title{{min-width:260px;line-height:1.5}}
  .kb-tools{{margin-top:6px}}
  .kb-tools code,.kb-produced code,.kb-ids code{{font-size:11px}}
  .kb-super{{margin-top:8px;padding:8px 10px;border-left:3px solid var(--warn);background:var(--warn-bg-soft);font-size:12px;line-height:1.6;color:var(--text-secondary)}}
  .kb-produced{{min-width:180px;line-height:1.9}}
  .kb-ids{{font-family:var(--font-tech);white-space:nowrap}}
  .kb-none{{color:var(--text-faint)}}
  .kb-risk{{font-family:var(--font-tech);font-size:11px;padding:2px 8px;border-radius:3px;white-space:nowrap}}
  .kb-risk-high{{background:var(--bad-bg-strong);color:var(--bad)}}
  .kb-risk-medium{{background:var(--warn-bg-strong);color:var(--warn)}}
  .kb-risk-low{{background:var(--ok-bg-strong);color:var(--ok)}}
  .kb-warn{{border-left:3px solid var(--warn);background:var(--warn-bg);padding:14px 18px;border-radius:var(--radius-md);margin:var(--gap-md) 0}}
  .kb-flow{{font-family:var(--font-tech);font-size:12px;line-height:2.1;color:var(--text-secondary);background:var(--bg-card);border:1px solid var(--border);border-radius:var(--radius-md);padding:14px 18px;overflow-x:auto}}
  .kb-generated{{font-size:11px;color:var(--text-faint);margin-top:var(--gap-sm)}}
</style>
</head>
<body>

<nav class="bim-topnav">
  <div class="bim-topnav-left">
    <a href="../index.html" class="bim-topnav-home">BIM_MCP</a>
    <span class="bim-topnav-sep">/</span>
    <a href="../index.html#reference" style="color:var(--text-secondary);border-bottom:none;">reference</a>
    <span class="bim-topnav-sep">/</span>
    <span class="bim-topnav-current">看板卡索引</span>
  </div>
  <div class="bim-topnav-actions" data-route-nav="top">
    <a href="{BOARD}" target="_blank" rel="noopener" class="bim-topnav-btn">開啟看板 ↗</a>
  </div>
</nav>

<header class="bim-hero">
  <div class="bim-hero-eyebrow">KANBAN INDEX <span class="accent">{total} 張卡 / {len(epics)} 個 Epic</span></div>
  <h1 class="bim-hero-title">看板卡 · <span class="accent">開發紀錄層</span><br>方法是怎麼被做出來的</h1>
  <p class="bim-hero-sub">
    林家維（<a href="https://github.com/CWLin0518" target="_blank" rel="noopener">@CWLin0518</a>）
    開發綠建材 Revit 導入工具鏈時推完的 <strong>{total} 張看板卡</strong>，全部 <code>stage: done</code>，
    建立於 {esc(dates[0])} ～ {esc(dates[-1])}。
    <strong>Domain 索引回答「有哪些方法」，本頁回答「這些方法是怎麼被做出來的、當初驗在哪個 TypeId 上」。</strong>
    每張卡連到它的 JSON 原始檔、它催生的 domain／skill／工具，以及當時留下的實測 ID。
  </p>
</header>

<section data-collapse class="bim-section bim-anchor" data-cat="frame" id="what">
  <div class="bim-section-eyebrow">SECTION 01 · WHAT</div>
  <h2 class="bim-section-title">什麼是看板卡</h2>
  <p class="bim-section-desc">
    <code>tools/kanban/cards/</code> 底下的 {total} 個 JSON 檔，一張卡一個檔。
    <a href="{BOARD}" target="_blank" rel="noopener">看板頁</a>把它們渲染成欄位視圖。
    這就是林家維在分享中說的「專案看板的方式，就是那個<strong>字卡</strong>」。
  </p>
  <p class="bim-section-desc">
    它跟一般專案管理的卡不同。<code>ai/process/kanban.md</code> 開宗明義：
    <strong>「這個看板追蹤的是『這項任務對 AI agent 來說是否可以安全執行』，不只是單純的任務狀態。」</strong>
    Trello／Jira 的卡問「這件事做到哪了」；這套卡問「這件事夠不夠清楚到可以交給 AI 去動我的 Revit 模型」。
  </p>
  <p class="bim-section-desc">每張卡有兩組布林閘門：</p>
  <ul class="bim-list">
    <li><strong><code>readiness</code>（7 項，進場門檻）</strong>：<code>problem_clear</code>、<code>non_goals_clear</code>、<code>acceptance_testable</code>、<code>files_known</code>、<code>scope_defined</code>、<code>verification_contract</code>、<code>human_approval_recorded</code>。不全為 true，不准交給 agent 動手。</li>
    <li><strong><code>gates</code>（6 項，出場門檻）</strong>：<code>product</code>、<code>ui</code>、<code>architecture</code>、<code>security</code>、<code>test</code>、<code>code_review</code>。不全為 true，不算完成。</li>
    <li><strong><code>dependsOn</code>（硬相依）</strong>：政策明定「列出的卡片全部是『完成』；只要有一張還沒完成，這張卡不能進入『Agent 執行中』」。</li>
  </ul>
  <p class="bim-section-desc" style="margin-top:var(--gap-sm)">推進流程有 12 個欄位，每欄都寫了離場條件：</p>
  <div class="kb-flow">收件匣 → 待釐清 → 待產品核准 → 待 UI Mockup → 待架構規劃 → 待任務卡<br>&nbsp;&nbsp;&nbsp;&nbsp;→ AI 就緒 → Agent 執行中 → 待驗證 → 待審查 → 待人工驗收 → 完成</div>
  <p class="bim-section-desc" style="margin-top:var(--gap-sm)">
    完整規範見 <a href="{GH}/ai/process/kanban.md" target="_blank" rel="noopener"><code>ai/process/kanban.md</code></a>，
    它連 WIP 上限都定了（「若審查欄位滿了，就停止建立新的 AI 工作」）。
  </p>
</section>

<section data-collapse class="bim-section bim-anchor" data-cat="ops" id="timegap">
  <div class="bim-section-eyebrow">SECTION 02 · 讀之前必看</div>
  <h2 class="bim-section-title">卡片是歷史紀錄，不是現行方法</h2>
  <div class="kb-warn">
    <p style="margin:0 0 10px"><strong>這 {total} 張卡全部是 <code>stage: {esc(stages[0]) if len(stages)==1 else "／".join(esc(s) for s in stages)}</code>，建立於 {esc(dates[0])} ～ {esc(dates[-1])}。</strong>
    它們記的是「那一天做了什麼、在哪個 TypeId 上驗過」，<strong>不會再更新</strong>。</p>
    <p style="margin:0 0 10px">而 domain 是<strong>活文件</strong>，會改版。兩者的關係是單向的：
    <strong>卡片是 domain 的出生證明與證據，不是 domain 的內容來源。</strong>
    方法的現況一律以 domain 為準——這是 <code>CLAUDE.md</code> 的既有規則（衝突時 domain 勝）。</p>
    <p style="margin:0">所以真正要注意的不是「卡片改了 domain 沒跟上」（卡片不會再改），而是<strong>時間差</strong>：
    domain 已經前進、卡片停在原地。本頁把已知的情況以黃底標在該列上。</p>
  </div>
  <p class="bim-section-desc">
    另一個時間差的例子不在任何卡片裡：<code>domain/GM_parameter-schema.md</code> 的
    §6「標章效期與資料新鮮度」是 2026-08-31 才加上去的，<strong>{total} 張卡中沒有任何一張提到它</strong>。
  </p>
  <p class="bim-section-desc">
    延伸閱讀：<a href="domain-index.html">Domain 索引</a>（有哪些方法）·
    <a href="skills-index.html">Skills 索引</a>（怎麼編排）·
    <a href="contributors.html">貢獻者</a> ·
    <a href="{TALK}" target="_blank" rel="noopener">林家維當天的分享逐字稿 ↗</a>
  </p>
</section>
{"".join(rows)}
<section data-collapse class="bim-section bim-anchor" data-cat="ref" id="gaps">
  <div class="bim-section-eyebrow">SECTION {sec:02d} · 缺口</div>
  <h2 class="bim-section-title">編號缺口與併卡</h2>
  <p class="bim-section-desc">
    卡號是 TASK-001～006、008～011、013～015，加 TASK-005.1～005.8 與 005.10～005.12。
    <strong>TASK-005.9、TASK-007、TASK-012 在 <code>main</code> 與作者的 fork 兩邊都不存在</strong>；
    另有三張卡標題自帶「[已併入 TASK-005.x]」。
  </p>
  <p class="bim-section-desc">
    這對應到他在分享中自述的「我其實到後面，我其實都自己會有點亂掉了」。
    看板本身收編了，但<strong>「開發到中期卡片爆炸、如何收斂」目前沒有任何 domain 或 skill 回答它</strong>——
    這是這條工具鏈唯一尚未沉澱成方法的一題。
  </p>
</section>

<footer class="bim-footer">
  <div class="bim-footer-actions" data-route-nav="foot">
    <a href="{BOARD}" target="_blank" rel="noopener" class="bim-footer-btn primary">開啟看板 ↗</a>
    <a href="../index.html" class="bim-footer-btn">回 BIM_MCP 首頁</a>
  </div>
  <p>BIM_MCP · reference / kanban-index</p>
  <p class="kb-generated">本頁由 <code>scripts/gen-kanban-index.py</code> 從 <code>tools/kanban/cards/*.json</code> 生成，請勿手動編輯——改卡片 JSON 後重新執行即可。一致性由 QA/QC 檢查 <code>7-16</code> 把關。</p>
</footer>

<script src="../shared.js"></script><script>initAll();</script>
</body>
</html>
'''
    open(OUT, "w", encoding="utf-8").write(page)
    print(f"[OK] 生成 {os.path.relpath(OUT, ROOT)}")
    print(f"     {total} 張卡 / {len(epics)} 個 Epic / {sum(1 for c in cards if c['id'] in SUPERSEDED)} 張標記為已被超出")


if __name__ == "__main__":
    build()
