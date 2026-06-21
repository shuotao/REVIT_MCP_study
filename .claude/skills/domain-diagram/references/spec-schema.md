# Flow Spec Schema(flowchart)

模型唯一要產出的東西。**不要手寫 mermaid** — 把這份 JSON 餵給
`scripts/mermaid_from_spec.py`,由腳本確定性地產生 fence 與健檢結論。

```json
{
  "type": "flowchart",
  "direction": "TD",
  "title": "選填,人看的標題,不進圖",
  "nodes": [
    {"id": "start", "label": "開始",   "kind": "start"},
    {"id": "d1",    "label": "決策?",  "kind": "decision"},
    {"id": "a1",    "label": "動作 A", "kind": "action"},
    {"id": "stop",  "label": "正常結束", "kind": "end"},
    {"id": "ab",    "label": "中止",   "kind": "abort"}
  ],
  "edges": [
    {"from": "start", "to": "d1"},
    {"from": "d1", "to": "a1",   "label": "是"},
    {"from": "d1", "to": "stop", "label": "否"}
  ]
}
```

## node.kind 對照(腳本決定形狀與顏色,模型只挑語意)

| kind | 形狀 | 語意色 | 用途 |
|---|---|---|---|
| `start` | `([…])` stadium | 無 | 流程入口 |
| `end` | `([…])` stadium | 綠 | 正常結束 |
| `abort` | `([…])` stadium | 紅 | 中止/異常出口 |
| `action` | `[…]` 方框 | 無 | 一般動作 |
| `decision` | `{…}` 菱形 | 藍 | 決策(必須 ≥2 條 labeled 分支) |
| `data` | `[(…)]` 圓柱 | 無 | 資料/儲存 |
| `io` | `[/…/]` 平行四邊形 | 無 | 輸入/輸出 |

## 規則

- `id` 用英數(mermaid 識別字);中文一律放 `label`。
- `label` 換行寫 `\n` 或 `<br/>`,腳本統一轉 `<br/>`;括號/冒號/引號腳本自動加引號跳脱,不必自己處理。
- `direction`:`TD`(上下,預設)/`LR`(左右)/`BT`/`RL`。
- 每個 `decision` 至少兩條出邊、每條都帶 `label`(否則健檢報項4)。
- 每條路徑要走得到某個 `end` 或 `abort`(否則健檢報死路)。

## 執行

```bash
python3 scripts/mermaid_from_spec.py spec.json            # 健檢 + 產圖
python3 scripts/mermaid_from_spec.py spec.json --render   # 只產 fence
python3 scripts/mermaid_from_spec.py spec.json --audit    # 只跑健檢
```

state / sequence / class **尚未程式化**(見 `programmatization-roadmap.md`),
暫時依 `domain/domain-flow-visualization.md` 速查表手寫模板。
