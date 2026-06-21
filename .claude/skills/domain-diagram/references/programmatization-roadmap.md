# 圖種程式化路線圖(log / lint 接續依據)

這份是 `domain-diagram` 從「模型手寫圖」轉向「腳本確定性產圖」的進度登記表。
**用途**:之後的 `/lint` 與 log 以此為基準,檢查每種圖是否已落到 `mermaid_from_spec.py`,
以及哪些 domain 還在用手寫模板。新增/升級圖種時,**先改這張表**。

## 狀態表

| 圖種 | 狀態 | 產生方式 | 健檢 | 備註 |
|---|---|---|---|---|
| `flowchart` | ✅ 已程式化 | `mermaid_from_spec.py render` | 自動(項1–4)+人工(項5–6) | 9 成 SOP 形狀,優先完成 |
| `stateDiagram-v2` | ☐ 模板 | 手寫(domain 速查表) | 全人工 | dwg 三模式、連線 on/off 等待程式化 |
| `sequenceDiagram` | ☐ 模板 | 手寫(domain 速查表) | 全人工 | AI↔MCP↔Revit 往返 |
| `classDiagram` / `erDiagram` | ☐ 模板 | 手寫(domain 速查表) | 全人工 | 結構/關聯 |
| `mindmap` / `timeline` / `gantt` | ☐ 模板 | 手寫(domain 速查表) | n/a | 階層/時間,健檢不適用 |

圖例:✅ 已程式化(模型禁手寫) ／ ☐ 暫用手寫模板(允許,但須貼 mermaid.live 目視驗證)

## lint 接續時應檢查(待 `/lint` 實作)

1. 標 ✅ 的圖種,domain 內若出現該型態 fence,應可由對應 spec 重現(無手寫漂移)。
2. 每個 `decision` fence 的分支數 ≥2 且都有 label(對應健檢項4)。
3. 安全子集守則:domain fence 不得出現 `architecture-beta` / `block` / `zenuml` 等 GitHub 不渲染型態。
4. 本表狀態欄與實際腳本支援(`mermaid_from_spec.py` 的 `render` 分支)一致。

## 程式化下一步(backlog)

- [ ] `stateDiagram-v2`:spec 加 `states` + `transitions(from,to,trigger)`,render 出 `[*]` 起訖與轉移。
- [ ] `sequenceDiagram`:spec 加 `participants` + `messages(from,to,text,sync/async,return)`。
- [ ] 健檢延伸到 state(不可達狀態、無出口狀態)。
- [ ] push 前語法驗證選項評估(`mmdc` vs 輕量 parser),見 `troubleshooting.md`。
