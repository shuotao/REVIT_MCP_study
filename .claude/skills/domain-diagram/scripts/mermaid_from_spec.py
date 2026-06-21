#!/usr/bin/env python3
"""
mermaid_from_spec.py — Deterministic Mermaid generator + structural auditor.

This is the ONLY place allowed to turn a domain flow into Mermaid syntax.
The model's job is to produce the structured spec (nodes + edges); this script
renders it into a GitHub-native ```mermaid fence with consistent shapes,
semantic classDef colors, and <br/> line handling. The model must NOT hand-write
the fence — that is what guarantees consistency across every domain diagram.

Currently supports flowchart specs (the dominant SOP shape). state / sequence
remain hand-templated from the domain cheat-sheet until added here.

Usage:
    python3 mermaid_from_spec.py SPEC.json            # audit + render
    python3 mermaid_from_spec.py SPEC.json --render   # fence only
    python3 mermaid_from_spec.py SPEC.json --audit    # findings only
    cat SPEC.json | python3 mermaid_from_spec.py -     # read spec from stdin

Spec schema (flowchart):
{
  "type": "flowchart",
  "direction": "TD",            # TD | LR | BT | RL  (optional, default TD)
  "title": "...",               # optional
  "nodes": [
    {"id": "start", "label": "開始",   "kind": "start"},
    {"id": "d1",    "label": "決策?",  "kind": "decision"},
    {"id": "a1",    "label": "動作 A", "kind": "action"},
    {"id": "stop",  "label": "結束",   "kind": "end"},
    {"id": "abort", "label": "中止",   "kind": "abort"}
  ],
  "edges": [
    {"from": "start", "to": "d1"},
    {"from": "d1", "to": "a1",   "label": "是"},
    {"from": "d1", "to": "stop", "label": "否"}
  ]
}

kind -> shape:
  start  -> ([...])  stadium   (entry)
  end    -> ([...])  stadium   + green  (normal exit)
  abort  -> ([...])  stadium   + red    (abort/stop exit)
  action -> [...]    rectangle
  decision -> {...}  diamond   + blue
  data   -> [(...)]  cylinder
  io     -> [/.../]  parallelogram
"""

import json
import sys

VALID_KINDS = {"start", "end", "abort", "action", "decision", "data", "io"}
TERMINAL_KINDS = {"end", "abort"}

# Semantic classDef palette. Modest fills so both GitHub light & dark stay legible;
# distinction carries mostly on stroke. Only emitted for nodes that need semantic
# separation (decision / normal-end / abort).
CLASSDEFS = {
    "dec":   "classDef dec fill:#e8f0fe,stroke:#1565c0,stroke-width:1px;",
    "done":  "classDef done fill:#e7f4ea,stroke:#2e7d32,stroke-width:1px;",
    "abort": "classDef abort fill:#fdecea,stroke:#c62828,stroke-width:1px;",
}


def _esc(label: str) -> str:
    """Newlines -> <br/>; always quote so parens/colons/brackets are safe."""
    s = str(label).replace("\\n", "<br/>").replace("\n", "<br/>")
    s = s.replace('"', "&quot;")
    return f'"{s}"'


def _wrap(kind: str, label: str) -> str:
    q = _esc(label)
    if kind == "decision":
        return "{" + q + "}"
    if kind == "data":
        return "[(" + q + ")]"
    if kind == "io":
        return "[/" + q + "/]"
    if kind in ("start", "end", "abort"):
        return "([" + q + "])"
    return "[" + q + "]"  # action / default


def audit(spec: dict) -> list[dict]:
    """Run the deterministic subset of the 6-item flow audit (items 1-4).
    Items 5 (前置缺口) and 6 (原子性) need human judgment and stay manual."""
    findings = []
    nodes = {n["id"]: n for n in spec.get("nodes", [])}
    edges = spec.get("edges", [])
    if not nodes:
        return [{"item": 0, "level": "缺口", "msg": "spec 沒有任何節點"}]

    out = {nid: [] for nid in nodes}
    for e in edges:
        f, t = e.get("from"), e.get("to")
        if f not in nodes:
            findings.append({"item": 2, "level": "缺口", "msg": f"邊指向未定義節點 from={f!r}"})
            continue
        if t not in nodes:
            findings.append({"item": 2, "level": "缺口", "msg": f"邊指向未定義節點 to={t!r}"})
            continue
        out[f].append(e)

    starts = [nid for nid, n in nodes.items() if n.get("kind") == "start"]
    if not starts:
        findings.append({"item": 3, "level": "設計疑慮", "msg": "沒有 kind=start 的起點"})

    # item 2 — 不可達 (BFS from all starts)
    seen = set()
    stack = list(starts)
    while stack:
        cur = stack.pop()
        if cur in seen:
            continue
        seen.add(cur)
        stack.extend(e["to"] for e in out.get(cur, []))
    for nid in nodes:
        if nid not in seen and nodes[nid].get("kind") != "start":
            findings.append({"item": 2, "level": "缺口", "msg": f"節點 {nid!r} 從起點不可達"})

    # item 2/3 — 死路 (non-terminal node with no outgoing edge)
    for nid, n in nodes.items():
        if not out[nid] and n.get("kind") not in TERMINAL_KINDS:
            findings.append({"item": 3, "level": "缺口",
                             "msg": f"節點 {nid!r}(kind={n.get('kind')}) 沒有出邊也不是終點 → 死路"})

    # item 3 — 終點覆蓋 (declared terminals must be reachable)
    for nid, n in nodes.items():
        if n.get("kind") in TERMINAL_KINDS and nid not in seen:
            findings.append({"item": 3, "level": "缺口", "msg": f"宣告的終點 {nid!r} 不可達"})
    if not any(nodes[nid].get("kind") in TERMINAL_KINDS for nid in nodes):
        findings.append({"item": 3, "level": "設計疑慮", "msg": "整張圖沒有任何終點(end/abort)"})

    # item 4 — 決策完備 (decision needs >=2 outgoing, all labeled)
    for nid, n in nodes.items():
        if n.get("kind") == "decision":
            outs = out[nid]
            if len(outs) < 2:
                findings.append({"item": 4, "level": "缺口",
                                 "msg": f"決策 {nid!r} 只有 {len(outs)} 條分支(應 ≥2,是/否都要畫)"})
            if any(not e.get("label") for e in outs):
                findings.append({"item": 4, "level": "設計疑慮",
                                 "msg": f"決策 {nid!r} 有未標示條件的分支(每條分支都該有 label)"})

    # item 1 — 迴圈偵測 (report cycles for MANUAL bounded-exit review)
    WHITE, GRAY, BLACK = 0, 1, 2
    color = {nid: WHITE for nid in nodes}
    cycles = []

    def dfs(u, path):
        color[u] = GRAY
        path.append(u)
        for e in out.get(u, []):
            v = e["to"]
            if color[v] == GRAY:
                i = path.index(v)
                cycles.append(path[i:] + [v])
            elif color[v] == WHITE:
                dfs(v, path)
        path.pop()
        color[u] = BLACK

    for nid in nodes:
        if color[nid] == WHITE:
            dfs(nid, [])
    for cyc in cycles:
        findings.append({"item": 1, "level": "人工確認",
                         "msg": "偵測到迴圈 " + " → ".join(cyc) + " — 請確認退出條件在有限集合上必然觸發"})

    # items 5 & 6 — out of automated scope
    findings.append({"item": 5, "level": "人工", "msg": "前置缺口(baseline/前置條件是否定義)需作者判斷"})
    findings.append({"item": 6, "level": "人工", "msg": "原子性(多步寫入中途崩潰一致性)需作者判斷"})
    return findings


def render(spec: dict) -> str:
    typ = spec.get("type", "flowchart")
    if typ not in ("flowchart", "graph"):
        raise SystemExit(f"render: 目前只支援 flowchart spec,收到 type={typ!r}。"
                         f"state/sequence 請用 domain 速查表手寫模板。")
    direction = spec.get("direction", "TD")
    nodes = spec.get("nodes", [])
    edges = spec.get("edges", [])

    lines = [f"flowchart {direction}"]
    for n in nodes:
        kind = n.get("kind", "action")
        if kind not in VALID_KINDS:
            raise SystemExit(f"未知 kind={kind!r}(node {n.get('id')!r})")
        lines.append(f"  {n['id']}{_wrap(kind, n.get('label', n['id']))}")
    lines.append("")
    for e in edges:
        lbl = e.get("label")
        arrow = f" -- {lbl} --> " if lbl else " --> "
        lines.append(f"  {e['from']}{arrow}{e['to']}")

    # semantic classes
    dec = [n["id"] for n in nodes if n.get("kind") == "decision"]
    done = [n["id"] for n in nodes if n.get("kind") == "end"]
    abort = [n["id"] for n in nodes if n.get("kind") == "abort"]
    if dec or done or abort:
        lines.append("")
        if dec:
            lines.append("  " + CLASSDEFS["dec"])
            lines.append("  class " + ",".join(dec) + " dec")
        if done:
            lines.append("  " + CLASSDEFS["done"])
            lines.append("  class " + ",".join(done) + " done")
        if abort:
            lines.append("  " + CLASSDEFS["abort"])
            lines.append("  class " + ",".join(abort) + " abort")

    body = "\n".join(lines)
    return "```mermaid\n" + body + "\n```"


def fmt_findings(findings: list[dict]) -> str:
    order = {"缺口": 0, "設計疑慮": 1, "人工確認": 2, "人工": 3}
    findings = sorted(findings, key=lambda f: (order.get(f["level"], 9), f["item"]))
    out = ["### 流程健檢結論(腳本自動 + 人工)"]
    for f in findings:
        out.append(f"- [項{f['item']}] **{f['level']}** — {f['msg']}")
    return "\n".join(out)


def main(argv):
    args = [a for a in argv[1:] if not a.startswith("--")]
    flags = {a for a in argv[1:] if a.startswith("--")}
    if not args:
        raise SystemExit(__doc__)
    src = args[0]
    raw = sys.stdin.read() if src == "-" else open(src, encoding="utf-8").read()
    spec = json.loads(raw)

    do_audit = "--audit" in flags or "--render" not in flags
    do_render = "--render" in flags or "--audit" not in flags

    parts = []
    if do_audit:
        parts.append(fmt_findings(audit(spec)))
    if do_render:
        parts.append(render(spec))
    print("\n\n".join(parts))


if __name__ == "__main__":
    main(sys.argv)
