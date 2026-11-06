#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""從 registerRevitTools() 生成 docs/BIM_MCP/reference/tools-index.html。

為什麼要生成而不是手寫：CLAUDE.md 與 QA/QC 7-14 的失敗訊息一直寫「從 registerRevitTools() 重產」，
但這支腳本過去不存在——2026-10-22 第一次逐張比對描述時，222 張卡裡有 45 張的描述已漂成手寫散文。
本頁一律重新生成：工具名、描述、唯讀／寫入／破壞性標記、來源模組全部取自建置後的 registry，
頁面不再有任何一個工具層級的欄位是手打的。

唯一人工策展的是「模組 → 八類」的分類（CATEGORIES）。它沒辦法機械推導：模組名稱說的是程式碼放在哪，
分類說的是使用者拿它做什麼。新增模組沒被歸類時，生成會直接失敗並點名該模組，不會默默漏掉。

用法：
  npm --prefix MCP-Server run build     # 先建置，本腳本讀的是 MCP-Server/build/tools/*.js
  python3 scripts/gen-tools-index.py
  python3 scripts/gen-tools-index.py --check   # 不寫檔；頁面與 registry 不一致時 exit 1
"""
import html
import json
import os
import subprocess
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "docs", "BIM_MCP", "reference", "tools-index.html")

# (字母, 名稱, 表格「涵蓋」欄, 分組標題下的說明, 模組)
CATEGORIES = [
    ("A", "基礎與查詢",
     "讀模型、找元素、問參數，也含少數寫入前置（載入共用參數）。",
     "讀模型、找元素、問參數，多數工作的第一步；也包含少數寫入型前置工具（如載入共用參數檔 load_shared_parameters）。",
     ["base-tools", "document-tools"]),
    ("B", "建模與構件",
     "產生或修改實體構件：牆、柱、樑、帷幕、地形、IFC 同步。",
     "產生或修改實體構件：牆、柱、樑、帷幕、地形、IFC 同步。",
     ["cad-block-placement-tools", "cad-link-tools", "curtain-wall-tools", "dwg-beam-tools",
      "dwg-column-tools", "grading-tools", "grid-tools", "ifc-structural-sync-tools", "opening-type-tools",
      "structure-tools", "wall-tools"]),
    ("C", "房間與空間",
     "房間邊界、編號、面積、走廊、施工架周長。",
     "房間邊界、編號、面積、走廊、施工架周長。",
     ["area-formula-tools", "corridor-analysis-tools", "room-tools", "scaffold-tools"]),
    ("D", "MEP 與碰撞",
     "風管系統、空間代表點、套管開孔、CSA 干涉、梁下淨高與管線淨距檢討。",
     "風管系統、空間代表點、套管開孔、CSA 干涉；也含模型檢討工具組（梁下淨高、管線淨距、模型快照與警告、安全批次編輯）。",
     ["clash-tools", "mep-tools", "model-review-tools"]),
    ("E", "法規檢核",
     "排煙、偵煙、樓梯——有法源、有判準的檢查。",
     "排煙、偵煙、樓梯——有法源、有判準的檢查。",
     ["smoke-detector-tools", "smoke-exhaust-tools", "stair-compliance-tools"]),
    ("F", "視圖與圖紙",
     "建視圖、裁剪、圖紙配置、視埠定位、跨專案複製。",
     "建視圖、裁剪、圖紙配置、視埠定位、跨專案複製。",
     ["camera-manager-tools", "camera-tools", "cross-document-tools", "dependent-view-tools",
      "parallel-section-tools", "scope-box-tools", "sheet-tools", "titleblock-align-tools",
      "view-capture-tools", "view-creation-tools", "view-cropbox-tools", "view-duplicate-tools",
      "view-range-tools", "viewport-position-tools"]),
    ("G", "標註與詳圖",
     "尺寸、詳圖元件、填充區域、圖例、明細表、文字。",
     "尺寸、詳圖元件、填充區域、圖例、明細表、文字。",
     ["detail-component-tools", "detail-copy-tools", "dimension-tools", "dimension-type-tools",
      "door-window-legend-tools", "fill-region-tools", "legend-tools", "legend-view-tools",
      "list-seeds-tools", "numbering-tools", "schedule-tools", "tags-tools", "text-note-tools"]),
    ("H", "視覺化",
     "上色、覆寫、隱藏顯示，也含材質與 Type 建構。",
     "上色、覆寫、隱藏顯示——把分析結果畫回模型上；也涵蓋材質與 Type 建構（含綠建材材質）。",
     ["fill-pattern-tools", "link-display-tools", "material-graphics-tools", "visualization-tools"]),
]

# 由 node 讀建置後的 registry：registerRevitTools() 給名稱／描述／annotations（與 QA/QC 7-14、9-1 同源），
# 各模組檔的匯出陣列只用來回答「這支工具來自哪個模組」。
NODE_DUMP = r"""
import fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
const dir = path.resolve('MCP-Server/build/tools');
const reg = await import(pathToFileURL(path.join(dir, 'index.js')).href);
const moduleOf = {};
for (const f of fs.readdirSync(dir).filter(f => f.endsWith('-tools.js'))) {
  const mod = await import(pathToFileURL(path.join(dir, f)).href);
  for (const v of Object.values(mod)) {
    if (!Array.isArray(v)) continue;
    for (const t of v) {
      if (t && typeof t.name === 'string') {
        (moduleOf[t.name] ??= new Set()).add(f.replace(/\.js$/, ''));
      }
    }
  }
}
const tools = reg.registerRevitTools().map(t => ({
  name: t.name,
  description: t.description,
  readOnly: t.annotations?.readOnlyHint === true,
  destructive: t.annotations?.destructiveHint === true,
  modules: [...(moduleOf[t.name] ?? [])],
}));
process.stdout.write(JSON.stringify(tools));
"""


def load_registry():
    if not os.path.exists(os.path.join(ROOT, "MCP-Server", "build", "tools", "index.js")):
        sys.exit("[FAIL] MCP-Server/build/tools/index.js 不存在——先執行 npm --prefix MCP-Server run build")
    env = dict(os.environ, MCP_PROFILE="full")  # full 是 QA 的真相集，與 verify-qaqc.ps1 一致
    r = subprocess.run(["node", "--input-type=module", "-e", NODE_DUMP],
                       cwd=ROOT, capture_output=True, text=True, encoding="utf-8", env=env)
    if r.returncode != 0:
        sys.exit("[FAIL] 讀 registry 失敗：\n" + r.stderr)
    return json.loads(r.stdout)


def esc(s):
    return html.escape(s, quote=True)


def render(tools):
    problems = []
    names = [t["name"] for t in tools]
    if len(names) != len(set(names)):
        problems.append("registry 有重複工具名")
    mod_to_cat = {}
    for letter, _, _, _, mods in CATEGORIES:
        for m in mods:
            if m in mod_to_cat:
                problems.append(f"模組 {m} 同時被歸進 {mod_to_cat[m]} 與 {letter}")
            mod_to_cat[m] = letter
    by_cat = {c[0]: [] for c in CATEGORIES}
    for t in tools:
        if not isinstance(t["description"], str) or not t["description"].strip():
            problems.append(f"{t['name']}：描述缺漏或非字串")
        if len(t["modules"]) != 1:
            problems.append(f"{t['name']}：對到 {len(t['modules'])} 個模組 {t['modules']}，預期恰好 1 個")
            continue
        m = t["modules"][0]
        if m not in mod_to_cat:
            problems.append(f"{t['name']}：模組 {m} 未歸類——在 CATEGORIES 補上")
            continue
        by_cat[mod_to_cat[m]].append(t)
    used = {t["modules"][0] for t in tools if len(t["modules"]) == 1}
    for m in mod_to_cat:
        if m not in used:
            problems.append(f"CATEGORIES 列了模組 {m}，但它沒有任何註冊工具")
    if problems:
        sys.exit("[FAIL] 無法生成：\n  " + "\n  ".join(problems))

    total = len(tools)
    n_ro = sum(1 for t in tools if t["readOnly"] and not t["destructive"])
    n_de = sum(1 for t in tools if t["destructive"])
    n_wr = total - n_ro - n_de
    destructive = " · ".join(sorted(t["name"] for t in tools if t["destructive"]))
    n_mod = len(mod_to_cat)

    rows = "\n".join(
        f'        <tr><td><strong><a href="#cat-{L}">{L}</a></strong></td><td>{esc(name)}</td>'
        f'<td class="num">{len(by_cat[L])}</td><td>{esc(cover)}</td></tr>'
        for L, name, cover, _, _ in CATEGORIES)

    sections = []
    for L, name, _, blurb, _ in CATEGORIES:
        # data-search-group 放在外層 div：7-14 的 regex 要求 class="cat-header" 緊接 > 才認得標題，
        # 屬性不能放在 class 後面（scripts/verify-qaqc.ps1 7-14 的 cat-header regex）；標題與說明一起隱藏也比較自然。
        out = [f'    <div data-search-group="{L}">',
               f'    <h3 id="cat-{L}" class="cat-header">{L} · {esc(name)}（{len(by_cat[L])} 個）</h3>',
               f'    <p class="cat-blurb">{esc(blurb)}</p>',
               f'    </div>']
        for t in sorted(by_cat[L], key=lambda t: t["name"]):
            if t["destructive"]:
                badge = '<span class="tool-badge destructive">破壞性</span>'
            elif t["readOnly"]:
                badge = '<span class="tool-badge readonly">唯讀</span>'
            else:
                badge = '<span class="tool-badge write">會寫入</span>'
            n = esc(t["name"])
            effect = "destructive" if t["destructive"] else ("readonly" if t["readOnly"] else "write")
            out.append(
                f'    <div class="tool-card" id="{n}" data-tool="{n}" data-search-item data-effect="{effect}" data-search-in="{L}">\n'
                f'      <div>\n'
                f'        <div class="tool-name">{n}</div>\n'
                f'        <div class="tool-flags">{badge}</div>\n'
                f'        <div class="tool-module">{esc(t["modules"][0])}</div>\n'
                f'      </div>\n'
                f'      <div>\n'
                f'        <div class="tool-desc">{esc(t["description"])}</div>\n'
                f'      </div>\n'
                f'    </div>')
        sections.append("\n".join(out))

    return (HEAD
            .replace("{TOTAL}", str(total))
            .replace("{N_RO}", str(n_ro))
            .replace("{N_WR}", str(n_wr))
            .replace("{N_DE}", str(n_de))
            .replace("{DESTRUCTIVE}", destructive)
            .replace("{N_MOD}", str(n_mod))
            .replace("{ROWS}", rows)
            + "\n".join(sections) + "\n" +
            FOOT.replace("{TOTAL}", str(total)))


HEAD = """<!DOCTYPE html>
<html lang="zh-TW">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width, initial-scale=1.0">
<script>try{if(localStorage.getItem("bim-theme")==="light")document.documentElement.setAttribute("data-theme","light")}catch(e){}</script>
<title>Tools 索引（{TOTAL} 個）· BIM_MCP</title>
<meta property="og:title" content="RevitMCP Tools 索引：{TOTAL} 個執行層工具">
<meta property="og:description" content="{TOTAL} 個 MCP 工具 × 唯讀/寫入標註 × 來源模組。Tool 是執行層，方法在 Domain、編排在 Skill。">
<link rel="preconnect" href="https://fonts.googleapis.com">
<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
<link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=JetBrains+Mono:wght@400;500;700&family=Noto+Sans+TC:wght@300;400;600;700;800&display=swap">
<link rel="stylesheet" href="../styles.css">
<link rel="stylesheet" href="../nav.css">
<style>
  .tool-card {
    background: var(--bg-card);
    border: 1px solid var(--border);
    border-radius: var(--radius-md);
    padding: 12px 18px;
    margin-bottom: 8px;
    display: grid;
    grid-template-columns: 300px 1fr;
    gap: 18px;
    align-items: start;
    transition: border-color 0.3s, transform 0.3s;
  }
  .tool-card:hover { border-color: var(--accent-border); transform: translateX(4px); }
  .tool-name {
    font-family: var(--font-tech);
    font-size: 14px;
    color: var(--accent);
    font-weight: 700;
    letter-spacing: 0.2px;
    word-break: break-all;
  }
  .tool-flags { margin-top: 5px; }
  .tool-badge {
    display: inline-block;
    font-family: var(--font-tech);
    font-size: 10px;
    letter-spacing: 1px;
    padding: 2px 7px;
    border-radius: 3px;
    border: 1px solid var(--border);
    color: var(--text-muted);
  }
  .tool-badge.readonly { border-color: var(--accent-border); color: var(--accent); }
  .tool-badge.write { border-color: var(--border); color: var(--text-muted); }
  .tool-badge.destructive { border-color: var(--bad-border); color: var(--bad); }
  .tool-module {
    font-family: var(--font-tech);
    font-size: 10px;
    color: var(--text-faint);
    margin-top: 5px;
    letter-spacing: 0.5px;
  }
  .tool-desc { font-size: 13.5px; line-height: 1.75; color: var(--text-body); }
  .cat-header {
    font-family: var(--font-tech);
    font-size: 13px;
    letter-spacing: 2px;
    color: var(--accent);
    border-bottom: 1px solid var(--accent-border);
    padding-bottom: 6px;
    margin: 40px 0 4px;
  }
  .cat-blurb { font-size: 12.5px; color: var(--text-muted); margin: 0 0 14px; }
  .num { font-family: var(--font-tech); text-align: right; }
  @media (max-width: 720px) {
    .tool-card { grid-template-columns: 1fr; gap: 8px; }
  }
</style>
</head>
<body>

<nav class="bim-topnav">
  <div class="bim-topnav-left">
    <a href="../index.html" class="bim-topnav-home">BIM_MCP</a>
    <span class="bim-topnav-sep">/</span>
    <span class="bim-topnav-section">reference</span>
    <span class="bim-topnav-sep">/</span>
    <span class="bim-topnav-current">Tools 索引</span>
  </div>
  <div class="bim-topnav-actions" data-route-nav="top">
    <a href="https://github.com/shuotao/REVIT_MCP_study" target="_blank" rel="noopener" class="bim-topnav-btn bim-topnav-btn-github" title="View source on GitHub">GitHub</a>
  </div>
</nav>

<header class="bim-hero">
  <div class="bim-hero-eyebrow">TOOLS INDEX <span class="accent">{TOTAL} 個</span></div>
  <h1 class="bim-hero-title">Tools · <span class="accent">執行層</span><br>模型上真正被動到的那一下</h1>
  <p class="bim-hero-sub">Tool 是<strong>執行</strong>。它不決定方法，也不決定順序——方法在 <a href="domain-index.html">Domain</a>，順序在 <a href="skills-index.html">Skill</a>。這一頁把 {TOTAL} 個工具攤開，讓你看得到「有什麼」以及「哪些會動到模型」。</p>
</header>

<section class="bim-section">
  <h2 class="bim-section-title">先看懂三個標記</h2>
  <p>每張卡右上角的標記來自 <code>annotations.ts</code> 集中產生的 <code>annotations</code>，不是這一頁另外標的。</p>
  <ul>
    <li><strong>唯讀</strong>（{N_RO} 個）——設計上不寫入 Revit 模型（<code>export_*</code> 與 <code>capture_view_image</code> 會另外寫出外部或暫存檔案）。</li>
    <li><strong>會寫入</strong>（{N_WR} 個）——會在 Transaction 裡改模型，但不刪除既有內容。可以 Undo，但會進 Revit 的復原堆疊。</li>
    <li><strong>破壞性</strong>（{N_DE} 個）——刪除或不可逆地取代既有內容：<code>{DESTRUCTIVE}</code>。這份名單是白名單，程式碼裡多一個就會被閘門擋下。</li>
  </ul>
  <p style="color:var(--text-muted);font-size:13px;">「唯讀」是工具作者的宣告，不是執行時的沙箱。它告訴你這支工具<em>設計上</em>不寫入，不是保證它<em>不可能</em>寫入。</p>
</section>

<section class="bim-section">
  <h2 class="bim-section-title">八類分工</h2>
  <table>
    <thead>
      <tr><th>類</th><th>名稱</th><th class="num">數量</th><th>涵蓋</th></tr>
    </thead>
    <tbody>
{ROWS}
    </tbody>
  </table>
  <p style="color:var(--text-muted);font-size:13px;">分類是 {N_MOD} 個來源模組的一個分割——每個模組恰好屬於一類，八類加總等於 {TOTAL}。這一頁由 <code>scripts/gen-tools-index.py</code> 從 <code>registerRevitTools()</code> 產生，不是手寫的。</p>
</section>

<section class="bim-section" data-search-root>
  <h2 class="bim-section-title">{TOTAL} 個工具 · 依用途分組</h2>
  <div class="bim-search">
    <label for="tool-search">搜尋工具名稱或描述</label>
    <input type="search" id="tool-search" data-search-input placeholder="例如：採光、牆、sheet">
    <div class="bim-search-chips">
      <button type="button" aria-pressed="false" data-filter-group="effect" data-filter-value="readonly">唯讀</button>
      <button type="button" aria-pressed="false" data-filter-group="effect" data-filter-value="write">會寫入</button>
      <button type="button" aria-pressed="false" data-filter-group="effect" data-filter-value="destructive">破壞性</button>
      <button type="button" data-search-clear>清除</button>
    </div>
    <div class="bim-search-status" data-search-status aria-live="polite"></div>
  </div>

"""

FOOT = """
</section>

<section class="bim-section">
  <h2 class="bim-section-title">標記如何維護</h2>
  <p>卡片上的標記由 <code>MCP-Server/src/tools/annotations.ts</code> 依覆寫表、破壞性名單與名稱前綴集中產生（其餘預設為寫入），不是這一頁手動標的。品質閘門 <code>9-1</code> 檢查每支工具都有標題、<code>readOnlyHint</code> 是布林值、破壞性標記只出現在白名單；<code>9-3</code> 反查 C# handler：宣告唯讀的工具不得 commit Transaction 或刪除元素。</p>
</section>

<footer class="bim-footer">
  <div class="bim-footer-actions" data-route-nav="foot">
    <a href="domain-index.html" class="bim-footer-btn">Domain 索引</a>
    <a href="../index.html" class="bim-footer-btn">回 BIM_MCP 首頁</a>
  </div>
  <p>BIM_MCP · reference / tools-index</p>
  <p style="font-size:11px;color:var(--text-faint);margin-top:6px;">{TOTAL} 個 MCP 工具完整索引，由 scripts/gen-tools-index.py 從 registerRevitTools() 產生；唯讀/寫入標註由 annotations.ts 集中產生。</p>
</footer>

<script src="../shared.js"></script>
<script>initAll();</script>
</body>
</html>
"""


def main():
    page = render(load_registry())
    if "--check" in sys.argv:
        current = open(OUT, encoding="utf-8").read() if os.path.exists(OUT) else ""
        if current != page:
            print("[FAIL] tools-index.html 與 registry 不一致——執行 python3 scripts/gen-tools-index.py 重產")
            sys.exit(1)
        print("[OK] tools-index.html 與 registry 一致")
        return
    with open(OUT, "w", encoding="utf-8", newline="\n") as f:
        f.write(page)
    print(f"[OK] 生成 {os.path.relpath(OUT, ROOT)}")


if __name__ == "__main__":
    main()
