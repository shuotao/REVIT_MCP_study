# GitHub 原生 mermaid fence 踩雷集

目標 surface 是 GitHub markdown 內嵌 ` ```mermaid ` fence(不是 mermaid.live、不是 CDN viewer)。
以下是中文 domain 圖在 GitHub 上最常爆的點。flowchart 走腳本後大多自動避開,
但手寫 state/sequence/class 模板時仍要注意。

| 症狀 | 原因 | 解法 |
|---|---|---|
| 整張圖變成純文字、不渲染 | fence 標籤拼錯或前面有空白 | 必須正好是 ` ```mermaid `,fence 不可縮排 |
| `Parse error` 在某節點 | label 含 `()` `{}` `[]` `:` `#` 未跳脱 | label 一律包雙引號:`id["文字(含括號)"]`;腳本已自動處理 |
| 中文節點顯示□或消失 | 用了 `\n` 當換行 | 改 `<br/>`;腳本已自動轉換 |
| `"` 在 label 裡讓解析中斷 | 直引號未跳脱 | 用 `&quot;` 或全形「」;腳本輸出 `&quot;` |
| `architecture-beta` / `block` / `zenuml` 空白 | GitHub 引擎不支援這些型態 | 改安全子集(見 domain 對照表):往返用 `sequenceDiagram` |
| `classDef` 顏色在深色模式看不見 | 寫死高彩度 fill | 用淡 fill + 明確 stroke(腳本的 palette 已調過) |
| subgraph 標題含中文標點報錯 | `subgraph 名稱:` 的冒號 | subgraph 標題也包引號:`subgraph "區段一"` |
| 箭頭 label 中文後接 `-->` 黏住 | `-- 文字-->` 少空白 | 寫 `-- 文字 --> `(腳本已含前後空白) |
| edge 太多圖糊成一團 | 單圖塞太多節點 | 拆子流程 / 改 `LR` / 用 subgraph 分組;或分兩張圖 |
| mindmap 中文根節點不展開 | root 用了形狀括號 | mindmap root 直接寫文字,不加 `[]` |

## 驗證手段(目前)

本機沒有裝 `mmdc`(mermaid-cli),也**刻意不需要** — GitHub 自己會渲染 fence。
要在 push 前確認,最快是:

1. 用 `mermaid_from_spec.py` 產 flowchart → 腳本只吐安全子集,語法穩定。
2. 手寫 state/sequence/class 模板 → 貼到 <https://mermaid.live> 目視確認後再回嵌。
3. push 後在 GitHub 上實際看一眼該 `domain/*.md`(GitHub 引擎才是最終裁判)。

未來若要 push 前自動驗,選項是 `npm i -D @mermaid-js/mermaid-cli` 後 `mmdc -i x.mmd`,
但會拉進 chromium,對「只要 fence 能渲染」的目標而言過重 — 列為 backlog,不預設裝。
